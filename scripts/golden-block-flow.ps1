<# .SYNOPSIS
Uji jalur produksi MCP -> PlanRunner -> writer -> reopen -> fresh acceptance.
Status pilot/review manusia tetap dipertahankan, bukan dipaksa menjadi selesai.
#>
[CmdletBinding()]
param([int]$Runs=3,[double]$Width=120,[double]$Height=50,[double]$Depth=12)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$session=Get-Content -Raw (Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json')|ConvertFrom-Json
$headers=@{Authorization="Bearer $($session.token)";'Content-Type'='application/json'}
$script:blockRpcId=0
function Rpc([string]$method,[hashtable]$parameters) {
    $script:blockRpcId++
    $body=@{jsonrpc='2.0';id=$script:blockRpcId;method=$method;params=$parameters}|ConvertTo-Json -Depth 12
    $r=Invoke-RestMethod -Uri $session.serverUrl -Method Post -Headers $headers -Body $body -TimeoutSec 180
    if($r.PSObject.Properties.Name -contains 'error'){throw ($r.error|ConvertTo-Json -Compress)}
    return $r.result
}
function Call([hashtable]$arguments){$r=Rpc 'tools/call' @{name='create_parametric_block';arguments=$arguments}; return $r.content[0].text|ConvertFrom-Json}
$null=Rpc 'initialize' @{protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='golden-block-flow';version='1'}}
$tools=Rpc 'tools/list' @{}
if(-not ($tools.tools.name -contains 'create_parametric_block')){throw 'Build/catalog terbaru belum termuat.'}
if($tools.tools.name -contains 'extrude_boss' -or $tools.tools.name -contains 'add_rectangle'){throw 'Primitive tulis yang belum teruji ikut terekspos.'}
$root=Join-Path (Split-Path $PSScriptRoot -Parent) ('tests\golden\output\block-flow\'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $root -Force
for($run=1;$run -le $Runs;$run++) {
    $id=[guid]::NewGuid().ToString('N')
    $argsBlock=@{width=$Width;height=$Height;depth=$Depth;operationId=$id}
    $result=Call $argsBlock
    [IO.File]::WriteAllText((Join-Path $root "run-$run.json"),($result|ConvertTo-Json -Depth 65),(New-Object Text.UTF8Encoding $false))
    if(-not $result.ok -or $result.verification.verdict -cne 'PASS') {throw ($result|ConvertTo-Json -Depth 4)}
    if(-not $result.modelChanged -or -not $result.verification.manualReviewRequired -or $result.taskSuccessful){throw 'Perubahan/review pilot tidak dilaporkan dengan benar.'}
    $m=$result.data.afterReopen
    if($m.bodyCount -ne 1 -or [Math]::Abs($m.volumeMm3-$Width*$Height*$Depth)-gt 1e-3){throw 'Body/volume tidak cocok.'}
    foreach($entry in @(@('X',$Width),@('Y',$Height),@('Z',$Depth))) {
        if([Math]::Abs(($m.max.($entry[0])-$m.min.($entry[0]))-$entry[1])-gt 1e-6){throw 'Ukuran reopen tidak cocok.'}
    }
    $checks=@($result.data.acceptanceChecks)
    if($checks.Count -ne 5 -or @($checks|Where-Object {$_.Verdict -ne 0}).Count -ne 0){throw 'Fresh acceptance belum lulus seluruhnya.'}
    $repeated=Call $argsBlock
    if($repeated.data.runId -cne $result.data.runId -or $repeated.data.documentPath -cne $result.data.documentPath){throw 'operationId dieksekusi ulang, bukan dikembalikan dari cache.'}
    Write-Host "LULUS run $run/$Runs; PlanRunner + reopen + 5 fresh checks; review tetap diperlukan" -ForegroundColor Green
    Write-Host "  $($result.data.documentPath)"
}
$status=Get-Content -Raw (Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json')|ConvertFrom-Json
[IO.File]::WriteAllText((Join-Path $root 'manifest.json'),(@{capturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');solidworksRevision=$status.solidworksRevision;buildCommit=$status.buildCommit;buildTime=$status.buildTime;loadedDllSha256=(Get-FileHash -LiteralPath $status.addinAssembly).Hash;scope='production MCP/PlanRunner geometry; pilot review remains required';runs=$Runs;width=$Width;height=$Height;depth=$Depth}|ConvertTo-Json),(New-Object Text.UTF8Encoding $false))
Write-Host "Bukti: $root"
