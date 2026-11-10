<# .SYNOPSIS
Mounting plate lewat JALUR PRODUKSI MCP: tools/call create_parametric_block
dengan `holes` -> ParametricBlockFlow -> PlanRunner -> writer -> reopen ->
fresh acceptance.

Ini jalur yang dipakai AI dari chat. golden-plat.ps1 membuktikan writer-nya;
skrip ini membuktikan pintu masuknya - bahwa permintaan dari luar sampai ke
writer yang sama, dengan validasi kontrak yang sama, dan kembali dengan bukti
setelah reopen.

Review manusia tetap wajib; skrip ini MENUNTUT taskSuccessful false, bukan
memaksanya jadi selesai.
#>
[CmdletBinding()]
param([int]$Runs=3,[double]$Width=100,[double]$Height=80,[double]$Depth=10,[double]$Inset=20)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$session=Get-Content -Raw (Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json')|ConvertFrom-Json
$headers=@{Authorization="Bearer $($session.token)";'Content-Type'='application/json'}
$script:rpc=0
function Rpc([string]$method,[hashtable]$parameters) {
    $script:rpc++
    $body=@{jsonrpc='2.0';id=$script:rpc;method=$method;params=$parameters}|ConvertTo-Json -Depth 14
    $r=Invoke-RestMethod -Uri $session.serverUrl -Method Post -Headers $headers -Body $body -TimeoutSec 240
    if($r.PSObject.Properties.Name -contains 'error'){throw ($r.error|ConvertTo-Json -Compress)}
    return $r.result
}
function Call([hashtable]$arguments){$r=Rpc 'tools/call' @{name='create_parametric_block';arguments=$arguments}; return $r.content[0].text|ConvertFrom-Json}

$status=Get-Content -Raw (Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json')|ConvertFrom-Json
Write-Host "PLAT LEWAT MCP $Width x $Height x $Depth mm + 4 lubang M6 counterbore ISO; $Runs run"
Write-Host "Build termuat: $($status.buildCommit), SW $($status.solidworksRevision)"

$null=Rpc 'initialize' @{protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='golden-plat-flow';version='1'}}
$tools=Rpc 'tools/list' @{}
$tool=@($tools.tools | Where-Object { $_.name -eq 'create_parametric_block' })
if($tool.Count -ne 1){throw 'create_parametric_block tidak terekspos - build/catalog terbaru belum termuat.'}
if(-not ($tool[0].inputSchema.properties.PSObject.Properties.Name -contains 'holes')){throw 'Skema tool belum menerima holes - catalog lama termuat.'}
if($tools.tools.name -contains 'hole_wizard' -or $tools.tools.name -contains 'extrude_boss'){throw 'Primitive tulis yang belum teruji ikut terekspos.'}

# --- Penolakan dulu: harus ditolak SEBELUM SolidWorks disentuh -------------
# Lubang di tengah ketebalan, bukan di permukaan atas. Kalau ini lolos, flow
# menerima permintaan yang tidak bisa dipenuhi.
$tolak=Call @{width=$Width;height=$Height;depth=$Depth;operationId=[guid]::NewGuid().ToString('N');
    holes=@{holeType='counterbore';standard='ISO';fastenerSize='M6';positions=@(@{x=50;y=40;z=($Depth/2)})}}
if($tolak.ok -or $tolak.modelChanged -ne $false){throw 'Posisi di luar permukaan atas TIDAK ditolak, atau dilaporkan mengubah model.'}
Write-Host "LULUS penolakan: posisi z=$($Depth/2) ditolak tanpa menyentuh model" -ForegroundColor Green

$tanpaStandar=Call @{width=$Width;height=$Height;depth=$Depth;operationId=[guid]::NewGuid().ToString('N');
    holes=@{holeType='counterbore';fastenerSize='M6';positions=@(@{x=20;y=20;z=$Depth})}}
if($tanpaStandar.ok){throw 'Lubang tanpa standar TIDAK ditolak - ada default yang menyelinap.'}
Write-Host 'LULUS penolakan: tanpa standar ditolak, bukan diberi ISO' -ForegroundColor Green

# --- Run sungguhan -----------------------------------------------------------
$posisi=@(
    @{x=$Inset;y=$Inset;z=$Depth}, @{x=($Width-$Inset);y=$Inset;z=$Depth},
    @{x=$Inset;y=($Height-$Inset);z=$Depth}, @{x=($Width-$Inset);y=($Height-$Inset);z=$Depth})
$root=Join-Path (Split-Path $PSScriptRoot -Parent) ('tests\golden\output\plate-flow\'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $root -Force
$volumeSebelumnya=$null

for($run=1;$run -le $Runs;$run++) {
    $id=[guid]::NewGuid().ToString('N')
    $argumen=@{width=$Width;height=$Height;depth=$Depth;operationId=$id;
        holes=@{holeType='counterbore';standard='ISO';fastenerSize='M6';positions=$posisi}}
    $result=Call $argumen
    [IO.File]::WriteAllText((Join-Path $root "run-$run.json"),($result|ConvertTo-Json -Depth 65),(New-Object Text.UTF8Encoding $false))

    if(-not $result.ok -or $result.verification.verdict -cne 'PASS') {
        Import-Module (Join-Path $PSScriptRoot 'lib\Dialog.psm1') -Force
        Write-DiagnosaMacet -RequestId $id
        throw ($result|ConvertTo-Json -Depth 5)
    }
    if(-not $result.modelChanged -or -not $result.verification.manualReviewRequired -or $result.taskSuccessful){
        throw 'Perubahan/review pilot tidak dilaporkan dengan benar.'
    }

    $m=$result.data.afterReopen
    if($m.bodyCount -ne 1){throw 'Body count bukan satu.'}
    $polos=$Width*$Height*$Depth
    if($m.volumeMm3 -ge $polos -or $m.volumeMm3 -le 0){throw "Volume $($m.volumeMm3) tidak berkurang dari $polos."}

    # Lubang dari data yang dilaporkan flow - yaitu pembacaan SETELAH reopen.
    $h=$result.data.holes
    if(-not $h){throw 'Hasil tidak memuat holes.'}
    if($h.sketchPointCount -ne 4){throw "Titik posisi $($h.sketchPointCount), diharapkan 4."}
    if($h.standard -cne 'ISO' -or $h.fastenerSize -cne 'M6'){throw "Standar/ukuran terbaca '$($h.standard)'/'$($h.fastenerSize)'."}
    $maks=($h.positionDeviationsMm | Measure-Object -Maximum).Maximum
    if($maks -gt 1e-3){throw "Posisi lubang meleset $maks mm."}

    $checks=@($result.data.acceptanceChecks)
    if($checks.Count -ne 5 -or @($checks|Where-Object {$_.Verdict -ne 0}).Count -ne 0){throw 'Fresh acceptance belum lulus seluruhnya.'}

    # Konsistensi antar-run: plat yang sama harus menghasilkan volume yang sama.
    if($null -ne $volumeSebelumnya -and [Math]::Abs($m.volumeMm3-$volumeSebelumnya) -gt 1e-6){
        throw "Volume run $run ($($m.volumeMm3)) berbeda dari run sebelumnya ($volumeSebelumnya)."
    }
    $volumeSebelumnya=$m.volumeMm3

    # Panggilan ulang dengan operationId yang sama HARUS mengembalikan hasil
    # lama, bukan membuat plat kedua.
    $ulang=Call $argumen
    if($ulang.data.runId -cne $result.data.runId -or $ulang.data.documentPath -cne $result.data.documentPath){
        throw 'operationId dieksekusi ulang, bukan dikembalikan dari cache.'
    }

    Write-Host ("LULUS run {0}/{1}: MCP -> plat + 4 lubang, reopen, deviasi maks {2:0.######} mm, volume {3} mm3" -f $run,$Runs,$maks,$m.volumeMm3) -ForegroundColor Green
    Write-Host "  $($result.data.documentPath)"
}

[IO.File]::WriteAllText((Join-Path $root 'manifest.json'),(@{capturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');solidworksRevision=$status.solidworksRevision;
    buildCommit=$status.buildCommit;scope='MCP create_parametric_block dengan holes; pilot review tetap wajib';runs=$Runs;
    width=$Width;height=$Height;depth=$Depth;inset=$Inset}|ConvertTo-Json),(New-Object Text.UTF8Encoding $false))
Write-Host "SW_PASS: $Runs/$Runs plat lewat MCP. Bukti: $root" -ForegroundColor Green
