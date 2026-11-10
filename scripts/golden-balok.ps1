<#
.SYNOPSIS
    Membuat balok parametrik pada part BARU melalui acceptance harness in-process.
    Bukan bypass gating MCP/AI; tool tulis belum diekspos sebelum bukti native.
    Menyimpan part baru, menutup hanya part milik run, membuka ulang dan assert.
#>
[CmdletBinding()]
param(
    [ValidateRange(0.001,10000)][double] $Width=100,
    [ValidateRange(0.001,10000)][double] $Height=80,
    [ValidateRange(0.001,10000)][double] $Depth=10,
    [ValidateRange(1,10)][int] $Runs=3,
    [string] $TemplatePath,
    [ValidateRange(15,600)][int] $TimeoutSeconds=180
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $env:LOCALAPPDATA 'MikosAI\block-pilot'
$null=New-Item -ItemType Directory -Path $folder -Force
$requestPath=Join-Path $folder 'request.json'
$processingPath=Join-Path $folder 'processing.json'
$status=Get-Content -Raw -LiteralPath (Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json') | ConvertFrom-Json
if ($status.status -ne 'connected' -or -not (Get-Process -Id $status.processId -ErrorAction SilentlyContinue)) {
    throw 'Buka SolidWorks dengan build pilot terbaru dan MikosAI aktif lebih dulu.'
}
Write-Host "PILOT NATIVE balok $Width x $Height x $Depth mm; $Runs run"
Write-Host "Build termuat: $($status.buildCommit), $($status.buildTime), SW $($status.solidworksRevision)"
$runRoot=Join-Path $repo ('tests\golden\output\block-pilot\'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $runRoot -Force
for($run=1; $run -le $Runs; $run++) {
    if ((Test-Path -LiteralPath $requestPath) -or (Test-Path -LiteralPath $processingPath)) {
        throw "Request/run lain masih ada di $folder. Periksa hasilnya; tidak menimpa atau retry."
    }
    $plan=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repo 'examples\block-pilot.plan.json') | ConvertFrom-Json
    $plan.parameters.width.value=$Width
    $plan.parameters.height.value=$Height
    $plan.parameters.depth.value=$Depth
    # Nama dokumen harus unik juga antar-direktori: CloseDoc memilih lewat nama.
    $output=Join-Path $runRoot ("block-"+(Split-Path $runRoot -Leaf)+"-$run.SLDPRT")
    $plan.document.savePath=$output
    $plan.document.template=$TemplatePath
    $plan.title="Balok parametrik $Width x $Height x $Depth mm"
    $planJson=$plan | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText((Join-Path $runRoot "block-$run.plan.json"),$planJson,(New-Object Text.UTF8Encoding $false))
    $id=[guid]::NewGuid().ToString('N')
    $request=@{requestId=$id;expiresAtUtc=[DateTimeOffset]::UtcNow.AddMinutes(5).ToString('O');outputPath=$output;templatePath=$TemplatePath;plan=$plan}
    $temporary=Join-Path $folder ("request-"+$id+'.tmp')
    [IO.File]::WriteAllText($temporary,($request|ConvertTo-Json -Depth 35),(New-Object Text.UTF8Encoding $false))
    Move-Item -LiteralPath $temporary -Destination $requestPath
    $resultPath=Join-Path $folder ("result-"+$id+'.json')
    Write-Host "Run $run/${Runs}: menunggu dispatcher SolidWorks..."
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(-not (Test-Path -LiteralPath $resultPath)) {
        if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
            # Jangan hapus/retry request: ia mungkin sudah sedang berjalan.
            # MENYEBUT apa yang menahan, bukan menyuruh orang menebak.
            # Deteksi harus DARI LUAR proses: dialog modal memblokir thread
            # yang seharusnya melapor, jadi add-in tidak akan pernah bisa
            # menceritakannya sendiri.
            Import-Module (Join-Path $PSScriptRoot 'lib\Dialog.psm1') -Force
            Write-Host ''
            Write-Host 'Run tidak memberi hasil. Diagnosa:' -ForegroundColor Yellow
            Write-DiagnosaMacet -RequestId $id
            Write-Host ''
            throw "Belum ada hasil dalam $TimeoutSeconds detik. Request $id tetap untuk ditinjau; jangan kirim ulang."
        }
        Start-Sleep -Milliseconds 250
    }
    $response=Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    Copy-Item -LiteralPath $resultPath -Destination (Join-Path $runRoot "block-$run.result.json")
    if ($response.requestId -cne $id -or -not $response.ok) {
        Write-Host "GAGAL: $($response.error)"
        if ($response.PSObject.Properties.Name -contains 'steps') {
            $response.steps | Select-Object tool,modelChanged,error | Format-Table | Out-Host
        }
        throw "Run $run berhenti. Hasil di $runRoot; part gagal tetap tersedia untuk inspeksi."
    }
    if ($response.data.status -cne 'SW_PASS' -or -not (Test-Path -LiteralPath $output)) { throw 'Envelope hasil tidak membuktikan part tersimpan.' }
    $measure=$response.data.afterReopen
    $size=@(($measure.max.X-$measure.min.X),($measure.max.Y-$measure.min.Y),($measure.max.Z-$measure.min.Z))
    $expectedSize=@($Width,$Height,$Depth)
    for($axis=0;$axis -lt 3;$axis++) {
        if ([Math]::Abs($size[$axis]-$expectedSize[$axis]) -gt 1e-6) { throw 'Ukuran reopen tidak sesuai parameter skrip.' }
    }
    if ($measure.bodyCount -ne 1 -or [Math]::Abs($measure.volumeMm3-$Width*$Height*$Depth) -gt 1e-3) { throw 'Body/volume reopen tidak sesuai geometri.' }
    $profile=@($measure.profileDimensions | Sort-Object valueMm)
    $expectedProfile=@($Width,$Height | Sort-Object)
    if ($profile.Count -ne 2) { throw 'Jumlah dimensi profil reopen bukan dua.' }
    for($dim=0;$dim -lt 2;$dim++) {
        if ($profile[$dim].drivenState -ne 2 -or [Math]::Abs($profile[$dim].valueMm-$expectedProfile[$dim]) -gt 1e-6) { throw 'Dimensi driving profil tidak sesuai.' }
    }
    $depthDims=@($measure.depthDimensions | Where-Object { $_.name.EndsWith('@'+$response.data.featureName,[StringComparison]::Ordinal) })
    if ($depthDims.Count -ne 1 -or $depthDims[0].drivenState -ne 2 -or [Math]::Abs($depthDims[0].valueMm-$Depth) -gt 1e-6) { throw 'Dimensi depth milik fitur extrude tidak sesuai.' }
    # SolidWorks mempertahankan handle tulis ketika part dibuka ulang.
    # Buka stream baca dengan sharing tulis, tanpa mengubah isi berkas.
    $stream=[IO.File]::Open($output,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        $hasher=[Security.Cryptography.SHA256]::Create()
        try { $sha=[BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','') }
        finally { $hasher.Dispose() }
    }
    finally { $stream.Dispose() }
    Write-Host "LULUS run $run : simpan-tutup-buka ulang; volume $($response.data.afterReopen.volumeMm3) mm3" -ForegroundColor Green
    Write-Host "  $output"
    [IO.File]::WriteAllText((Join-Path $runRoot "block-$run.sha256"),$sha,(New-Object Text.UTF8Encoding $false))
}
Write-Host "SW_PASS: $Runs/$Runs balok pilot $Width x $Height x $Depth mm, termasuk baca ulang setelah reopen." -ForegroundColor Green
Write-Host "Artefak: $runRoot"
