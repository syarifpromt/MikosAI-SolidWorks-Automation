<#
.SYNOPSIS
    Verifikasi konektivitas lima sketsa fixture-kontur lewat dispatcher MCP.
    Tidak mengedit model. Sketsa T sengaja tidak valid sebagai profil;
    yang diuji ialah ukuran konektivitas, bukan verdict PASS seluruh sketsa.
#>
[CmdletBinding()]
param([string] $SessionFile, [ValidateRange(1, 20)][int] $Runs = 3)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $SessionFile) { $SessionFile = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json' }
$expected = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot '..\tests\golden\fixture-kontur.json') | ConvertFrom-Json
$session = Get-Content -Raw $SessionFile | ConvertFrom-Json
$statusPath = Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json'
if (Test-Path -LiteralPath $statusPath) {
    $status = Get-Content -Raw $statusPath | ConvertFrom-Json
    Write-Host "Build add-in termuat: $($status.buildCommit); revisi SolidWorks: $($status.solidworksRevision)"
}
$headers = @{ Authorization = "Bearer $($session.token)"; 'Content-Type' = 'application/json' }
$script:rpcId = 0
$script:failed = 0
$script:passed = 0
$script:hypotheses = 0
$script:unexplained = 0
function Property($obj, [string] $name) {
    if ($null -ne $obj -and $obj.PSObject.Properties.Name -contains $name) { return ,($obj.$name) }
    return $null
}
function Rpc([string] $method, [hashtable] $parameters) {
    $script:rpcId++
    $body = @{ jsonrpc = '2.0'; id = $script:rpcId; method = $method; params = $parameters } | ConvertTo-Json -Depth 12
    $response = Invoke-RestMethod -Uri $session.serverUrl -Method Post -Headers $headers -Body $body
    if ($null -ne (Property $response 'error')) { throw ($response.error | ConvertTo-Json -Compress) }
    return $response.result
}
function Tool([string] $name) {
    $result = Rpc 'tools/call' @{ name = $name; arguments = @{} }
    return $result.content[0].text | ConvertFrom-Json
}
function Check([bool] $ok, [string] $message) {
    if ($ok) { $script:passed++; Write-Host "  LULUS $message" -ForegroundColor Green }
    else { $script:failed++; Write-Host "  GAGAL $message" -ForegroundColor Red }
}
$null = Rpc 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'golden-kontur'; version = '1' } }
$doc = Tool 'get_active_document'
if ((Property (Property $doc 'data') 'title') -cne $expected.title) {
    throw "Aktifkan $($expected.title) dan keluar dari Edit Sketch sebelum menjalankan skrip."
}
for ($run = 1; $run -le $Runs; $run++) {
    Write-Host "`n== Fixture kontur, run $run/$Runs" -ForegroundColor Cyan
    $result = Tool 'inspect_sketches'
    $data = Property $result 'data'
    $rows = Property $data 'sketches'
    Check (@($rows).Count -eq 5) 'tepat lima sketsa'
    foreach ($entry in $expected.sketches.PSObject.Properties) {
        $found = @($rows | Where-Object { (Property $_ 'name') -ceq $entry.Name })
        Check ($found.Count -eq 1) "$($entry.Name) ditemukan tepat satu"
        if ($found.Count -ne 1) { continue }
        $row = $found[0]
        foreach ($field in $entry.Value.PSObject.Properties) {
            $value = Property $row $field.Name
            Check ($null -ne $value -and $value -eq $field.Value) "$($entry.Name).$($field.Name) = $value (harapan $($field.Value))"
        }
        foreach ($field in 'nearlyCoincidentEndpointCount') {
            $value = Property $row $field
            Check ($null -ne $value -and $value -eq 0) "$($entry.Name).$field = $value (harapan 0)"
        }
        foreach ($field in 'consumedByFeatures', 'segmentsNotAnalyzed') {
            $value = Property $row $field
            Check ($null -ne $value -and @($value).Count -eq 0) "$($entry.Name).$field = []"
        }
        # Harapan berasal dari resep geometri. Limit belum dibuktikan:
        # laporkan sebagai HIPOTESIS, jangan menaikkan PASS atau exit code.
        $limits = Property $row 'limits'
        $expectedLimits = Property $expected.hypothesizedLimits $entry.Name
        $sameLimits = $null -ne $limits -and $null -ne $expectedLimits
        if ($sameLimits) {
            $actualSorted = @($limits | Sort-Object -CaseSensitive)
            $expectedSorted = @($expectedLimits | Sort-Object -CaseSensitive)
            $sameLimits = $actualSorted.Count -eq $expectedSorted.Count
            for ($i = 0; $sameLimits -and $i -lt $actualSorted.Count; $i++) {
                $sameLimits = $actualSorted[$i] -ceq $expectedSorted[$i]
            }
        }
        $actualText = if ($null -eq $limits) { 'null' } else { ConvertTo-Json -InputObject @($limits) -Compress }
        $expectedText = ConvertTo-Json -InputObject @($expectedLimits) -Compress
        $script:hypotheses++
        if (-not $sameLimits) { $script:unexplained++ }
        $hypothesisState = if ($sameLimits) { 'sesuai dugaan, belum dinyatakan PASS' } else { 'BELUM DIJELASKAN' }
        Write-Host "  HIPOTESIS $($entry.Name).limits = $actualText (harapan geometri $expectedText): $hypothesisState" -ForegroundColor Yellow
        $tol = Property $row 'endpointToleranceMm'
        Check ($null -ne $tol -and $tol -eq 1e-6) "$($entry.Name).endpointToleranceMm = $tol"
    }
    $analysis = Property $data 'analysisLimits'
    Check ($null -ne $analysis -and @($analysis).Count -eq 1 -and $analysis -contains 'undeclared_point_on_segment') 'analysisLimits menyebut kontak tanpa relasi'
    $missing = Property $data 'fieldsNotPopulated'
    Check ($null -ne $missing -and @($missing).Count -eq 0) 'fieldsNotPopulated = []'
}
Write-Host "`nLulus: $script:passed   Gagal: $script:failed"
Write-Host "Hipotesis limits: $script:hypotheses   Belum dijelaskan: $script:unexplained (tidak memengaruhi exit code)"
if ($script:failed -gt 0) { exit 1 }
exit 0
