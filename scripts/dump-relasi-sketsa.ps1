<#
.SYNOPSIS
    Dump relasi tiga sketsa fixture melalui reader in-process dan dispatcher.
    Bukan tool MCP baru dan tidak mengubah geometri. Perlu build diagnostik terbaru.
#>
[CmdletBinding()]
param([string] $SessionFile, [string] $OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $SessionFile) { $SessionFile = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\tests\golden\evidence\relasi-kontur' }
$session = Get-Content -Raw -LiteralPath $SessionFile | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($session.token)"; 'Content-Type' = 'application/json' }
$script:rpcId = 0
function Rpc([string] $method, [hashtable] $parameters) {
    $script:rpcId++
    $body = @{ jsonrpc='2.0'; id=$script:rpcId; method=$method; params=$parameters } | ConvertTo-Json -Depth 12
    $response = Invoke-RestMethod -Uri $session.serverUrl -Method Post -Headers $headers -Body $body
    if ($response.PSObject.Properties.Name -contains 'error') { throw ($response.error | ConvertTo-Json -Compress) }
    return $response.result
}
$null = Rpc 'initialize' @{protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='dump-relasi-sketsa';version='1'}}
$doc = Rpc 'tools/call' @{name='get_active_document';arguments=@{}}
$docData = $doc.content[0].text | ConvertFrom-Json
if ($docData.data.title -cne 'fixture-kontur.SLDPRT') { throw 'Aktifkan fixture-kontur.SLDPRT dan keluar dari Edit Sketch.' }
$folder = Join-Path $env:LOCALAPPDATA 'MikosAI\diagnostics\sketch-relations'
$null = New-Item -ItemType Directory -Path $folder -Force
$requestPath = Join-Path $folder 'request.json'
if (Test-Path -LiteralPath $requestPath) { throw "Request diagnostik sudah ada: $requestPath. Periksa sebelum menggantinya." }
$requestId = [guid]::NewGuid().ToString('N')
$names = @('SketchT', 'SketchKotak', 'SketchDuaLoop')
$request = @{requestId=$requestId;documentTitle='fixture-kontur.SLDPRT';sketchNames=$names;expiresAtUtc=[DateTimeOffset]::UtcNow.AddMinutes(5).ToString('O')}
[IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
try {
    for ($i=0; $i -lt $names.Count; $i++) {
        $null = Rpc 'tools/call' @{name='inspect_sketches';arguments=@{sketchName=$names[$i]}}
        $capturePath = Join-Path $folder "sketch-$i.json"
        if (-not (Test-Path -LiteralPath $capturePath)) {
            throw 'Dump belum ditulis. Build yang termuat belum mendukung diagnostik: tutup SolidWorks, jalankan setup.ps1, lalu buka kembali fixture.'
        }
        $capture = Get-Content -Raw -LiteralPath $capturePath | ConvertFrom-Json
        if ($capture.requestId -cne $requestId) { throw 'Dump berasal dari request lama. Muat ulang build diagnostik dan periksa host-status.json.' }
        $destination = Join-Path $OutputDirectory ($names[$i] + '.json')
        Copy-Item -LiteralPath $capturePath -Destination $destination -Force
        Write-Host "DUMP $($names[$i]): relasi $(@($capture.relations).Count)/$($capture.relationCountReported), kandidat $(@($capture.unexplainedCandidates).Count), errors $(@($capture.errors).Count)"
        Write-Host "  $([IO.Path]::GetFullPath($destination))"
        if (@($capture.errors).Count -gt 0 -or @($capture.relations).Count -ne $capture.relationCountReported) {
            throw 'Dump parsial: periksa errors. Jangan menyimpulkan asal limit dari capture yang tidak lengkap.'
        }
    }
    $status = Get-Content -Raw -LiteralPath (Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json') | ConvertFrom-Json
    $manifest = @{requestId=$requestId;capturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');solidworksRevision=$status.solidworksRevision;buildCommit=$status.buildCommit;buildTime=$status.buildTime;loadedDllSha256=(Get-FileHash -LiteralPath $status.addinAssembly -Algorithm SHA256).Hash;note='Dump diagnostik; bukan PASS dan bukan baseline harapan.'}
    [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
}
finally {
    # Hanya hapus request milik run ini; tidak ada penghapusan rekursif.
    if (Test-Path -LiteralPath $requestPath) {
        $currentRequest = Get-Content -Raw -LiteralPath $requestPath | ConvertFrom-Json
        if ($currentRequest.requestId -ceq $requestId) { Remove-Item -LiteralPath $requestPath }
    }
}
