<#
.SYNOPSIS
    Menumbuhkan daftar putih tipe konsumen dengan bukti, lewat fixture-fitur.

.DESCRIPTION
    Hari ini daftar putih hanya memuat Extrusion dan ICE. Setiap part yang
    memakai revolve atau hole wizard akan mengibarkan `unclassified_child` -
    disengaja, tapi tidak boleh berkibar selamanya.

    Syarat masuk daftar tetap DUA bukti:
      1. typeName teramati dari model nyata
      2. fitur itu terbukti memakai sketsa sebagai profil, dengan efek yang
         terverifikasi

    Skrip ini menyediakan keduanya, dan memisahkan dengan tegas apa yang
    DI-ASSERT dari apa yang hanya DILAPORKAN:

      GAGAL  = geometri yang bisa dihitung dengan pensil, atau invariant
      SENSUS = nama tipe, yang memang belum kami ketahui

    Meng-assert nama tipe yang belum pernah dilihat berarti menebak lalu
    menamainya test.

.PARAMETER Tahap2
    Sertakan kalau hole wizard sudah ditambahkan ke fixture.
#>
[CmdletBinding()]
param(
    [string] $SessionFile,
    [string] $Harapan,
    [switch] $Tahap2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$akar = Split-Path $PSScriptRoot -Parent
if (-not $SessionFile) { $SessionFile = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json' }
if (-not $Harapan) { $Harapan = Join-Path $akar 'tests\golden\fixture-fitur.json' }

$script:lulus = 0
$script:gagal = 0

function Write-Judul([string] $t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Write-Lulus([string] $t) { $script:lulus++; Write-Host "  LULUS  $t" -ForegroundColor Green }
function Write-Gagal([string] $t) { $script:gagal++; Write-Host "  GAGAL  $t" -ForegroundColor Red }

function Get-Properti($o, [string] $n) {
    if ($null -eq $o) { return $null }
    if ($o.PSObject.Properties.Name -contains $n) { return $o.$n }
    return $null
}

$script:idRpc = 0
function Invoke-Tool([string] $nama) {
    $script:idRpc++
    $badan = @{
        jsonrpc = '2.0'; id = $script:idRpc; method = 'tools/call'
        params  = @{ name = $nama; arguments = @{} }
    } | ConvertTo-Json -Depth 10
    $j = Invoke-RestMethod -Uri $script:endpoint -Method Post -Headers $script:header -Body $badan
    return $j.result.content[0].text | ConvertFrom-Json
}

Write-Host ''
Write-Host 'GOLDEN - sensus tipe konsumen' -ForegroundColor White
Write-Host '============================='

if (-not (Test-Path $Harapan)) { Write-Host "Harapan hilang: $Harapan" -ForegroundColor Red; exit 1 }
$h = Get-Content $Harapan -Raw | ConvertFrom-Json

if (-not (Test-Path $SessionFile)) {
    Write-Host "Sesi MCP tidak ada: $SessionFile" -ForegroundColor Red
    Write-Host 'Buka SolidWorks dengan add-in MikosAI lebih dulu.' -ForegroundColor Yellow
    exit 1
}

$sesi = Get-Content $SessionFile -Raw | ConvertFrom-Json
$script:endpoint = $sesi.serverUrl
$script:header = @{ Authorization = "Bearer $($sesi.token)"; 'Content-Type' = 'application/json' }

# Build yang TERMUAT, bukan HEAD. Klaim batal kalau keduanya berbeda, jadi
# angkanya dicetak supaya laporan tidak perlu mengingat-ingat.
$statusPath = Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json'
if (Test-Path $statusPath) {
    $st = Get-Content $statusPath -Raw | ConvertFrom-Json
    Write-Host "  build add-in TERMUAT : $(Get-Properti $st 'buildCommit')"
    Write-Host "  revisi SolidWorks    : $(Get-Properti $st 'solidworksRevision')"
}

$script:idRpc++
$init = @{
    jsonrpc = '2.0'; id = $script:idRpc; method = 'initialize'
    params  = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'golden-fitur'; version = '1' } }
} | ConvertTo-Json -Depth 10
try { $null = Invoke-RestMethod -Uri $script:endpoint -Method Post -Headers $script:header -Body $init }
catch { Write-Host "Server MCP tidak bisa dihubungi: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }

# --- Part yang benar? ------------------------------------------------------

Write-Judul 'Dokumen aktif'
$dok = Invoke-Tool 'get_active_document'
$judul = [string](Get-Properti (Get-Properti $dok 'data') 'title')

if ($judul -ne $h.part.judul) {
    Write-Host ''
    Write-Host "  Part aktif      : '$judul'" -ForegroundColor Red
    Write-Host "  Yang dibutuhkan : '$($h.part.judul)'" -ForegroundColor Red
    Write-Host '  Lihat tests\golden\CARA-MEMBUAT-FIXTURE-FITUR.md' -ForegroundColor Yellow
    exit 1
}
Write-Lulus "judul = '$judul'"

# --- Geometri: yang bisa dihitung dengan pensil ----------------------------

$mp = Invoke-Tool 'get_mass_properties'
$bb = Invoke-Tool 'get_bounding_box'
$volume = Get-Properti (Get-Properti $mp 'data') 'volumeMm3'
$bodies = Get-Properti (Get-Properti $bb 'data') 'bodyCount'

if ($Tahap2) {
    Write-Judul 'Tahap 2 - hole wizard (hanya ARAH volume yang dipatok)'

    $batas = [double]$h.tahap2.volumeHarusBerkurangDari
    if ($null -eq $volume) {
        Write-Gagal 'volumeMm3 null'
    }
    elseif ([double]$volume -lt $batas) {
        Write-Lulus "volume $volume < $batas (lubang tembus memang membuang material)"
    }
    else {
        Write-Gagal "volume $volume TIDAK berkurang dari $batas - lubangnya tidak memotong apa pun"
    }

    if ([int]$bodies -eq [int]$h.tahap2.bodyCount) { Write-Lulus "bodyCount = $bodies" }
    else { Write-Gagal "bodyCount = $bodies, diharapkan $($h.tahap2.bodyCount)" }
}
else {
    Write-Judul 'Tahap 1 - balok + revolve (angka eksak)'

    $diharapkan = [double]$h.tahap1.volumeMm3
    $tol = [double]$h.toleransi.volumeMm3
    if ($null -eq $volume) {
        Write-Gagal 'volumeMm3 null'
    }
    else {
        $selisih = [Math]::Abs([double]$volume - $diharapkan)
        if ($selisih -le $tol) { Write-Lulus "volumeMm3 = $volume (48000 balok + 6283.185307 silinder)" }
        else { Write-Gagal "volumeMm3 : diharapkan $diharapkan, diterima $volume, selisih $selisih" }
    }

    if ([int]$bodies -eq [int]$h.tahap1.bodyCount) { Write-Lulus "bodyCount = $bodies" }
    else { Write-Gagal "bodyCount = $bodies, diharapkan $($h.tahap1.bodyCount) - silinder seharusnya TIDAK menyentuh balok" }
}

# --- Konsumen: peran, bukan nama -------------------------------------------

Write-Judul 'Peran - sketsa dikonsumsi fitur yang benar'

$sk = Invoke-Tool 'inspect_sketches'
$dataSk = Get-Properti $sk 'data'
$semua = @(Get-Properti $dataSk 'sketches')

foreach ($nama in $h.tahap1.sketsaKonsumen.PSObject.Properties.Name) {
    $s = $semua | Where-Object { $_.name -ceq $nama }
    if (-not $s) { Write-Gagal "sketsa '$nama' tidak ada"; continue }

    $konsumen = @(Get-Properti $s 'consumedByFeatures')
    foreach ($f in $h.tahap1.sketsaKonsumen.$nama) {
        if ($konsumen -contains $f) {
            Write-Lulus "$nama dikonsumsi '$f'"
        }
        else {
            # Ini BISA gagal karena tipe fiturnya belum ada di daftar putih -
            # dan itu justru keadaan yang kita datang untuk perbaiki. Pesannya
            # menyebutkan kemungkinan itu supaya tidak disalahartikan sebagai
            # kerusakan pembaca.
            Write-Gagal "$nama TIDAK melaporkan '$f' sebagai konsumen - kemungkinan tipenya belum ada di daftar putih; lihat sensus di bawah"
        }
    }
}

# --- SENSUS: dilaporkan, TIDAK di-assert -----------------------------------

Write-Judul 'SENSUS typeName - dilaporkan untuk dibaca, bukan di-assert'
Write-Host '  Meng-assert nama yang belum pernah dilihat berarti menebak lalu' -ForegroundColor Gray
Write-Host '  menamainya test. Daftar putih diisi DARI SINI.' -ForegroundColor Gray
Write-Host ''

$dikenal = @()
$belumDikenal = @()

foreach ($s in $semua) {
    $dep = @(Get-Properti $s 'dependents')
    if ($dep.Count -eq 0) { continue }
    Write-Host "  $($s.name):" -ForegroundColor Gray
    $konsumen = @(Get-Properti $s 'consumedByFeatures')
    foreach ($d in $dep) {
        $tn = $d.typeName
        if (-not $tn) { $tn = '(typeName tidak terbaca)' }
        $tanda = if ($konsumen -contains $d.name) { 'KONSUMEN' } else { '-' }
        Write-Host ("     {0,-34} {1,-18} {2}" -f $d.name, $tn, $tanda)
        if ($konsumen -contains $d.name) { $dikenal += $tn } else { $belumDikenal += $tn }
    }
}

Write-Host ''
$baru = $belumDikenal | Sort-Object -Unique
if ($baru.Count -gt 0) {
    Write-Host '  Tipe di luar daftar putih (kandidat, perlu bukti peran):' -ForegroundColor Yellow
    $baru | ForEach-Object { Write-Host "     $_" -ForegroundColor Yellow }
}

$limitSemua = @()
foreach ($s in $semua) { $limitSemua += @(Get-Properti $s 'limits') }
$limitUnik = $limitSemua | Sort-Object -Unique
if ($limitUnik.Count -gt 0) {
    Write-Host ''
    Write-Host "  limits yang berkibar: $($limitUnik -join ', ')" -ForegroundColor Gray
}

# ---------------------------------------------------------------------------

Write-Host ''
Write-Host '============================='
Write-Host "Lulus: $script:lulus   Gagal: $script:gagal"

if ($script:gagal -eq 0) {
    Write-Host ''
    Write-Host 'Geometri dan peran benar. Pakai sensus di atas untuk mengisi' -ForegroundColor Green
    Write-Host 'TipeKonsumenSketsa - satu entri per tipe, menyebut fixture dan build.' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host 'Ada yang gagal. Kalau kegagalannya soal konsumen yang tidak terlaporkan,' -ForegroundColor Yellow
Write-Host 'periksa sensus: kemungkinan tipenya memang belum ada di daftar putih,' -ForegroundColor Yellow
Write-Host 'dan itu yang harus diperbaiki - bukan pembacanya.' -ForegroundColor Yellow
exit 1
