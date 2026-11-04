<#
.SYNOPSIS
    Golden test untuk capability document_inspection.

.DESCRIPTION
    Membaca part acuan lewat MCP tiga kali berturut-turut dan membandingkannya
    dengan nilai yang DITURUNKAN DARI GEOMETRI di tests/golden/plat-referensi.json.

    KENAPA INI ADA.

    document_inspection berstatus `pilot` karena satu part yang terbaca benar
    bukan bukti. Yang dibutuhkan untuk menaikkannya ke `verified`:

      1. nilainya cocok dengan hitungan yang berdiri sendiri, bukan dengan
         pembacaan sebelumnya
      2. cocok TIGA KALI berturut-turut, supaya tidak bergantung pada keadaan
         yang berubah antarpanggilan

    Skrip ini menjawab keduanya, dan menolak menyatakan lulus kalau salah satu
    tidak terpenuhi.

.PARAMETER SessionFile
    Berkas sesi MCP. Default: %LOCALAPPDATA%\MikosAI\agent-workspace\session.json

.PARAMETER Harapan
    Berkas nilai harapan. Default: tests\golden\plat-referensi.json
#>
[CmdletBinding()]
param(
    [string] $SessionFile,
    [string] $Harapan
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$akarRepo = Split-Path $PSScriptRoot -Parent

if (-not $SessionFile) {
    $SessionFile = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json'
}
if (-not $Harapan) {
    $Harapan = Join-Path $akarRepo 'tests\golden\plat-referensi.json'
}

$script:lulus = 0
$script:gagal = 0

function Write-Judul([string] $teks) {
    Write-Host ''
    Write-Host "== $teks" -ForegroundColor Cyan
}

function Write-Lulus([string] $teks) {
    $script:lulus++
    Write-Host "  LULUS  $teks" -ForegroundColor Green
}

function Write-Gagal([string] $teks) {
    $script:gagal++
    Write-Host "  GAGAL  $teks" -ForegroundColor Red
}

# StrictMode melempar untuk properti yang tidak ada, dan "tidak ada" adalah
# jawaban yang sah di sini - tool boleh mengembalikan null untuk ukuran yang
# memang tidak bisa diukur.
function Get-Properti($objek, [string] $nama) {
    if ($null -eq $objek) { return $null }
    if ($objek.PSObject.Properties.Name -contains $nama) { return $objek.$nama }
    return $null
}

# ---------------------------------------------------------------------------
# Pembanding
#
# Dipisah dari pemanggilan supaya pesan gagalnya seragam: nilai yang
# diharapkan, yang diterima, dan SELISIHNYA. Tanpa selisih, orang tidak bisa
# membedakan "salah hitung" dari "beda pembulatan".
# ---------------------------------------------------------------------------

function Bandingkan-Angka([string] $label, $diterima, [double] $diharapkan, [double] $toleransi) {
    if ($null -eq $diterima) {
        Write-Gagal "$label : null, diharapkan $diharapkan"
        return
    }

    $nilai = [double]$diterima
    $selisih = [Math]::Abs($nilai - $diharapkan)

    if ($selisih -le $toleransi) {
        Write-Lulus ("{0} = {1}" -f $label, $nilai)
    }
    else {
        Write-Gagal ("{0} : diharapkan {1}, diterima {2}, selisih {3} (toleransi {4})" -f
            $label, $diharapkan, $nilai, $selisih, $toleransi)
    }
}

function Bandingkan-Teks([string] $label, $diterima, [string] $diharapkan) {
    if ([string]$diterima -ceq $diharapkan) {
        Write-Lulus ("{0} = '{1}'" -f $label, $diterima)
    }
    else {
        Write-Gagal ("{0} : diharapkan '{1}', diterima '{2}'" -f $label, $diharapkan, $diterima)
    }
}

function Bandingkan-Vektor([string] $label, $diterima, $diharapkan, [double] $toleransi) {
    foreach ($sumbu in 'x', 'y', 'z') {
        Bandingkan-Angka "$label.$sumbu" (Get-Properti $diterima $sumbu) ([double]$diharapkan.$sumbu) $toleransi
    }
}

# ---------------------------------------------------------------------------
# Klien MCP
# ---------------------------------------------------------------------------

$script:idRpc = 0

function Invoke-Tool([string] $nama) {
    $script:idRpc++

    $badan = @{
        jsonrpc = '2.0'
        id      = $script:idRpc
        method  = 'tools/call'
        params  = @{ name = $nama; arguments = @{} }
    } | ConvertTo-Json -Depth 10

    $jawaban = Invoke-RestMethod -Uri $script:endpoint -Method Post -Headers $script:header -Body $badan

    $teks = $jawaban.result.content[0].text
    return $teks | ConvertFrom-Json
}

# ---------------------------------------------------------------------------
# Mulai
# ---------------------------------------------------------------------------

Write-Host ''
Write-Host 'GOLDEN TEST - document_inspection' -ForegroundColor White
Write-Host '=================================='

if (-not (Test-Path $Harapan)) {
    Write-Host "Berkas harapan tidak ditemukan: $Harapan" -ForegroundColor Red
    exit 1
}

$h = Get-Content $Harapan -Raw | ConvertFrom-Json
$tol = $h.toleransi

if (-not (Test-Path $SessionFile)) {
    Write-Host ''
    Write-Host "Berkas sesi MCP tidak ada: $SessionFile" -ForegroundColor Red
    Write-Host 'Buka SolidWorks dengan add-in MikosAI terpasang lebih dulu.' -ForegroundColor Yellow
    exit 1
}

$sesi = Get-Content $SessionFile -Raw | ConvertFrom-Json
$script:endpoint = $sesi.serverUrl
$script:header = @{
    Authorization  = "Bearer $($sesi.token)"
    'Content-Type' = 'application/json'
}

Write-Host "  endpoint : $script:endpoint"
Write-Host "  harapan  : $Harapan"

# initialize wajib mendahului tools/call.
$script:idRpc++
$init = @{
    jsonrpc = '2.0'
    id      = $script:idRpc
    method  = 'initialize'
    params  = @{
        protocolVersion = '2025-06-18'
        capabilities    = @{}
        clientInfo      = @{ name = 'golden-document'; version = '1' }
    }
} | ConvertTo-Json -Depth 10

try {
    $null = Invoke-RestMethod -Uri $script:endpoint -Method Post -Headers $script:header -Body $init
}
catch {
    Write-Host ''
    Write-Host "Tidak bisa menghubungi server MCP: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'SolidWorks mungkin sudah ditutup sejak berkas sesi ditulis.' -ForegroundColor Yellow
    exit 1
}

# --- Part yang benar? ------------------------------------------------------
#
# Diperiksa SEBELUM apa pun yang lain. Menjalankan golden test terhadap part
# yang salah menghasilkan daftar kegagalan panjang yang semuanya menyesatkan.

Write-Judul 'Dokumen aktif'

$dok = Invoke-Tool 'get_active_document'
$dataDok = Get-Properti $dok 'data'
$judulDiterima = [string](Get-Properti $dataDok 'title')

if ($judulDiterima -ne $h.part.judul) {
    Write-Host ''
    Write-Host "  Part yang aktif: '$judulDiterima'" -ForegroundColor Red
    Write-Host "  Yang dibutuhkan: '$($h.part.judul)'" -ForegroundColor Red
    Write-Host ''
    Write-Host '  Buka part acuannya di SolidWorks, lalu ulangi.' -ForegroundColor Yellow
    Write-Host '  Belum punya? Lihat tests\golden\CARA-MEMBUAT-PART.md' -ForegroundColor Yellow
    exit 1
}

Bandingkan-Teks 'judul'       $judulDiterima                              $h.part.judul
Bandingkan-Teks 'tipe'        (Get-Properti $dataDok 'type')              $h.part.tipe
Bandingkan-Teks 'konfigurasi' (Get-Properti $dataDok 'activeConfiguration') $h.part.konfigurasi

# --- Bounding box ----------------------------------------------------------

Write-Judul 'Bounding box'

$bb = Invoke-Tool 'get_bounding_box'
$dataBb = Get-Properti $bb 'data'

Bandingkan-Vektor 'min'  (Get-Properti $dataBb 'min')  $h.boundingBox.min  $tol.panjangMm
Bandingkan-Vektor 'max'  (Get-Properti $dataBb 'max')  $h.boundingBox.max  $tol.panjangMm
Bandingkan-Vektor 'size' (Get-Properti $dataBb 'size') $h.boundingBox.size $tol.panjangMm
Bandingkan-Angka  'bodyCount' (Get-Properti $dataBb 'bodyCount') ([double]$h.boundingBox.bodyCount) 0

# --- Mass properties -------------------------------------------------------

Write-Judul 'Mass properties'

$mp = Invoke-Tool 'get_mass_properties'
$dataMp = Get-Properti $mp 'data'

Bandingkan-Angka 'volumeMm3'      (Get-Properti $dataMp 'volumeMm3')      ([double]$h.massProperties.volumeMm3)      $tol.volumeMm3
Bandingkan-Angka 'massGrams'      (Get-Properti $dataMp 'massGrams')      ([double]$h.massProperties.massGrams)      $tol.massaGram
Bandingkan-Angka 'surfaceAreaMm2' (Get-Properti $dataMp 'surfaceAreaMm2') ([double]$h.massProperties.surfaceAreaMm2) $tol.luasMm2
Bandingkan-Angka 'densityKgM3'    (Get-Properti $dataMp 'densityKgM3')    ([double]$h.massProperties.densityKgM3)    $tol.densitasKgM3
Bandingkan-Vektor 'centerOfMass'  (Get-Properti $dataMp 'centerOfMass')   $h.massProperties.centerOfMass            $tol.panjangMm
Bandingkan-Teks  'materialName'   (Get-Properti $dataMp 'materialName')   $h.part.material

# PEMERIKSAAN SILANG: massa harus konsisten dengan volume x densitas.
#
# Ini yang paling berharga di seluruh berkas. Ketiganya dibaca TERPISAH dari
# SolidWorks, jadi kalau salah satunya dikarang, hubungan ini pecah. Persis
# kelas bug yang pernah kami punya: massa dihitung dari densitas default air
# padahal tidak ada material yang ditetapkan.
Write-Judul 'Pemeriksaan silang (massa = volume x densitas)'

$v = Get-Properti $dataMp 'volumeMm3'
$d = Get-Properti $dataMp 'densityKgM3'
$m = Get-Properti $dataMp 'massGrams'

if ($null -eq $v -or $null -eq $d -or $null -eq $m) {
    Write-Gagal 'salah satu dari volume/densitas/massa null - hubungan tidak bisa diperiksa'
}
else {
    # mm3 -> m3 (1e-9), kg -> g (1e3)
    $massaDihitung = [double]$v * 1e-9 * [double]$d * 1e3
    Bandingkan-Angka 'massa dari volume x densitas' $m $massaDihitung $tol.massaGram
}

# --- Fitur -----------------------------------------------------------------

Write-Judul 'Fitur'

$ft = Invoke-Tool 'list_features'
$dataFt = Get-Properti $ft 'data'
$daftar = Get-Properti $dataFt 'features'

foreach ($wajib in $h.fitur.harusAda) {
    $cocok = $daftar | Where-Object { $_.name -ceq $wajib.name }

    if (-not $cocok) {
        Write-Gagal "fitur '$($wajib.name)' tidak ada"
    }
    elseif ($cocok.typeName -cne $wajib.typeName) {
        Write-Gagal "fitur '$($wajib.name)' bertipe '$($cocok.typeName)', diharapkan '$($wajib.typeName)'"
    }
    else {
        Write-Lulus "fitur '$($wajib.name)' ($($wajib.typeName))"
    }
}

Bandingkan-Angka 'suppressedCount' (Get-Properti $dataFt 'suppressedCount') ([double]$h.fitur.suppressedCount) 0
Bandingkan-Angka 'errorCount'      (Get-Properti $dataFt 'errorCount')      ([double]$h.fitur.errorCount)      0

# --- Sketsa ----------------------------------------------------------------
#
# Di sinilah golden test membayar dirinya sendiri. Pemetaan
# swConstrainedStatus_e kami bergeser satu, sehingga sketsa yang fully defined
# dilaporkan OverDefined - dan, jauh lebih berbahaya, sketsa under-defined
# dilaporkan FullyDefined. Tidak ada yang bisa menangkapnya tanpa acuan yang
# jawabannya sudah diketahui lebih dulu.

Write-Judul 'Sketsa'

$sk = Invoke-Tool 'inspect_sketches'
$dataSk = Get-Properti $sk 'data'
$daftarSk = @(Get-Properti $dataSk 'sketches')

Bandingkan-Angka 'sketchCount'       (Get-Properti $dataSk 'sketchCount')       ([double]$h.sketsa.sketchCount)       0
Bandingkan-Angka 'fullyDefinedCount' (Get-Properti $dataSk 'fullyDefinedCount') ([double]$h.sketsa.fullyDefinedCount) 0

if ($daftarSk.Count -lt 1) {
    Write-Gagal 'tidak ada sketsa terbaca'
}
else {
    $s1 = $daftarSk[0]

    Bandingkan-Teks 'constrainedStatus' (Get-Properti $s1 'constrainedStatus') $h.sketsa.constrainedStatus

    $jumlahEntitas = Get-Properti $s1 'entityCount'
    $minimal = [int]$h.sketsa.entityCountMinimal

    if ($null -ne $jumlahEntitas -and [int]$jumlahEntitas -ge $minimal) {
        Write-Lulus "entityCount = $jumlahEntitas (minimal $minimal untuk persegi tertutup)"
    }
    else {
        Write-Gagal "entityCount : $jumlahEntitas, diharapkan minimal $minimal"
    }

    $kontur = Get-Properti $s1 'hasOpenContour'
    if ([bool]$kontur -eq [bool]$h.sketsa.hasOpenContour) {
        Write-Lulus "hasOpenContour = $kontur"
    }
    else {
        Write-Gagal "hasOpenContour : diharapkan $($h.sketsa.hasOpenContour), diterima $kontur"
    }
}

$verdictSk = Get-Properti (Get-Properti $sk 'verification') 'verdict'
if ($verdictSk -ceq 'PASS') {
    Write-Lulus 'verdict inspect_sketches = PASS'
}
else {
    Write-Gagal "verdict inspect_sketches = $verdictSk, diharapkan PASS"
}

# --- get_selection ---------------------------------------------------------
#
# BATAS YANG DISEBUTKAN, BUKAN DISEMBUNYIKAN.
#
# get_selection bergantung pada apa yang sedang DIPILIH pengguna, dan part
# acuan tidak bisa membawa seleksi. Jadi yang diuji di sini hanya bahwa
# jalurnya hidup dan amplopnya utuh - bukan isinya.
#
# Menuliskannya begini supaya tidak ada yang mengira capability ini sudah
# terbukti seluruhnya. Lihat capabilities.yaml.

Write-Judul 'Seleksi (hanya uji jalur, isinya bergantung pengguna)'

$sel = Invoke-Tool 'get_selection'
$cekSel = Get-Properti (Get-Properti $sel 'verification') 'checks'
$bacaSel = @($cekSel) | Where-Object { $_.id -ceq 'selection_read' }

if ($bacaSel -and $bacaSel.verdict -ceq 'PASS') {
    Write-Lulus 'selection_read = PASS (jalur hidup)'
}
else {
    Write-Gagal 'selection_read tidak PASS - jalur get_selection bermasalah'
}

# --- Pengulangan -----------------------------------------------------------
#
# Sekali benar bisa kebetulan. Yang diuji di sini: pembacaan tidak bergantung
# pada keadaan yang berubah antarpanggilan.

$ulang = [int]$h.pengulangan.jumlah

Write-Judul "Pengulangan ($ulang kali, harus identik)"

$sidikJari = @()

for ($i = 1; $i -le $ulang; $i++) {
    $b = Invoke-Tool 'get_bounding_box'
    $p = Invoke-Tool 'get_mass_properties'

    # Dibandingkan sebagai JSON, bukan field per field: kalau ada field yang
    # goyah dan kita tidak memikirkannya, perbandingan per field akan
    # melewatkannya justru karena kita tidak memikirkannya.
    $sidikJari += (
        ((Get-Properti $b 'data') | ConvertTo-Json -Depth 10 -Compress) + '|' +
        ((Get-Properti $p 'data') | ConvertTo-Json -Depth 10 -Compress)
    )
}

# @() WAJIB. Select-Object -Unique mengembalikan STRING TUNGGAL kalau hanya ada
# satu nilai unik, bukan array berisi satu - dan di bawah StrictMode, .Count
# pada string melempar.
#
# Yang membuatnya berbahaya: itu persis terjadi pada jalur BERHASIL. Jadi
# skripnya meledak tepat ketika semua perbandingan lulus, dan lulus akan
# terlihat seperti galat.
$unik = @($sidikJari | Select-Object -Unique)

if ($unik.Count -eq 1) {
    Write-Lulus "$ulang pembacaan identik"
}
else {
    Write-Gagal "pembacaan BERBEDA antarpanggilan ($($unik.Count) varian dari $ulang)"
    for ($i = 0; $i -lt $sidikJari.Count; $i++) {
        Write-Host "    [$($i + 1)] $($sidikJari[$i])" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------

Write-Host ''
Write-Host '=================================='
Write-Host "Lulus: $script:lulus   Gagal: $script:gagal"

if ($script:gagal -eq 0) {
    Write-Host ''
    Write-Host 'Golden test LULUS.' -ForegroundColor Green
    Write-Host 'document_inspection layak dinaikkan dari pilot ke verified.' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host 'Golden test GAGAL. document_inspection tetap pilot.' -ForegroundColor Red
exit 1
