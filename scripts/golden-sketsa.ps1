<#
.SYNOPSIS
    Test fixture-konsumen: konsumen vs dependent, dan tiga ember dimensi.

.DESCRIPTION
    Berbeda dari golden-document.ps1, tidak semua yang diperiksa di sini
    diturunkan dari geometri. Sebagian adalah HIPOTESIS tentang perilaku API
    yang belum pernah kami amati:

      - apakah IFeature.GetParents() mengembalikan node sketsa
      - apakah GetChildren() memuat sketsa yang sekadar punya relasi
      - apakah IDimension.DrivenState terisi untuk dimensi SKETSA

    Hipotesis yang salah BUKAN kegagalan skrip ini - ia hasil yang dicari.
    Karena itu keluarannya memisahkan dua hal dengan tegas:

      GAGAL      = geometri atau invariant yang memang harus benar
      HIPOTESIS  = dugaan yang ternyata tidak terbukti, dilaporkan tanpa
                   membuat exit code merah

    Mencampur keduanya akan membuat run ini terlihat gagal padahal ia justru
    berhasil menjawab pertanyaannya.

.PARAMETER SessionFile
    Berkas sesi MCP. Default: %LOCALAPPDATA%\MikosAI\agent-workspace\session.json
#>
[CmdletBinding()]
param(
    [string] $SessionFile,
    [string] $Harapan
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$akarRepo = Split-Path $PSScriptRoot -Parent
if (-not $SessionFile) { $SessionFile = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json' }
if (-not $Harapan) { $Harapan = Join-Path $akarRepo 'tests\golden\fixture-konsumen.json' }

$script:lulus = 0
$script:gagal = 0
$script:hipotesisPatah = 0

function Write-Judul([string] $t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Write-Lulus([string] $t) { $script:lulus++; Write-Host "  LULUS      $t" -ForegroundColor Green }
function Write-Gagal([string] $t) { $script:gagal++; Write-Host "  GAGAL      $t" -ForegroundColor Red }

# Hipotesis yang patah TIDAK menaikkan $gagal. Lihat catatan di atas.
function Write-Patah([string] $t) {
    $script:hipotesisPatah++
    Write-Host "  HIPOTESIS  $t" -ForegroundColor Yellow
}

function Get-Properti($o, [string] $n) {
    if ($null -eq $o) { return $null }
    # Jangan enumerate array: [] harus tetap array kosong, bukan menjadi null.
    if ($o.PSObject.Properties.Name -contains $n) { return ,($o.$n) }
    return $null
}

function Bandingkan-Angka([string] $label, $diterima, [double] $diharapkan, [double] $tol) {
    if ($null -eq $diterima) { Write-Gagal "$label : null, diharapkan $diharapkan"; return }
    $selisih = [Math]::Abs([double]$diterima - $diharapkan)
    if ($selisih -le $tol) { Write-Lulus ("{0} = {1}" -f $label, $diterima) }
    else { Write-Gagal ("{0} : diharapkan {1}, diterima {2}, selisih {3}" -f $label, $diharapkan, $diterima, $selisih) }
}

function Bandingkan-Teks([string] $label, $diterima, [string] $diharapkan) {
    if ([string]$diterima -ceq $diharapkan) { Write-Lulus ("{0} = '{1}'" -f $label, $diterima) }
    else { Write-Gagal ("{0} : diharapkan '{1}', diterima '{2}'" -f $label, $diharapkan, $diterima) }
}

# --- Klien MCP -------------------------------------------------------------

$script:idRpc = 0

function Invoke-Tool([string] $nama) {
    $script:idRpc++
    $badan = @{
        jsonrpc = '2.0'; id = $script:idRpc; method = 'tools/call'
        params  = @{ name = $nama; arguments = @{} }
    } | ConvertTo-Json -Depth 10

    $jawaban = Invoke-RestMethod -Uri $script:endpoint -Method Post -Headers $script:header -Body $badan
    return $jawaban.result.content[0].text | ConvertFrom-Json
}

# --- Mulai -----------------------------------------------------------------

Write-Host ''
Write-Host 'GOLDEN TEST - konsumen, dependent, dan dimensi' -ForegroundColor White
Write-Host '=============================================='

if (-not (Test-Path $Harapan)) { Write-Host "Berkas harapan hilang: $Harapan" -ForegroundColor Red; exit 1 }
$h = Get-Content $Harapan -Raw | ConvertFrom-Json

if (-not (Test-Path $SessionFile)) {
    Write-Host "Berkas sesi MCP tidak ada: $SessionFile" -ForegroundColor Red
    Write-Host 'Buka SolidWorks dengan add-in MikosAI terpasang lebih dulu.' -ForegroundColor Yellow
    exit 1
}

$sesi = Get-Content $SessionFile -Raw | ConvertFrom-Json
$script:endpoint = $sesi.serverUrl
$script:header = @{ Authorization = "Bearer $($sesi.token)"; 'Content-Type' = 'application/json' }

Write-Host "  endpoint : $script:endpoint"
Write-Host "  harapan  : $Harapan"

# Build yang TERMUAT, bukan HEAD. Klaim SW_PASS batal kalau keduanya berbeda,
# jadi angkanya dicetak di sini supaya laporan tidak perlu mengingat-ingat.
$statusPath = Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json'
if (Test-Path $statusPath) {
    $st = Get-Content $statusPath -Raw | ConvertFrom-Json
    Write-Host "  build add-in TERMUAT : $(Get-Properti $st 'buildCommit')"
    Write-Host "  revisi SolidWorks    : $(Get-Properti $st 'solidworksRevision')"
}

$script:idRpc++
$init = @{
    jsonrpc = '2.0'; id = $script:idRpc; method = 'initialize'
    params  = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'golden-sketsa'; version = '1' } }
} | ConvertTo-Json -Depth 10

try { $null = Invoke-RestMethod -Uri $script:endpoint -Method Post -Headers $script:header -Body $init }
catch {
    Write-Host "Tidak bisa menghubungi server MCP: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# --- Part yang benar? ------------------------------------------------------

Write-Judul 'Dokumen aktif'
$dok = Invoke-Tool 'get_active_document'
$judul = [string](Get-Properti (Get-Properti $dok 'data') 'title')

if ($judul -ne $h.part.judul) {
    Write-Host ''
    Write-Host "  Part aktif      : '$judul'" -ForegroundColor Red
    Write-Host "  Yang dibutuhkan : '$($h.part.judul)'" -ForegroundColor Red
    Write-Host '  Lihat tests\golden\CARA-MEMBUAT-FIXTURE-SKETSA.md' -ForegroundColor Yellow
    exit 1
}
Write-Lulus "judul = '$judul'"

# --- Geometri: harus benar, bukan hipotesis --------------------------------

Write-Judul 'Body (diturunkan dari geometri)'
$mp = Invoke-Tool 'get_mass_properties'
$bb = Invoke-Tool 'get_bounding_box'

Bandingkan-Angka 'volumeMm3' (Get-Properti (Get-Properti $mp 'data') 'volumeMm3') ([double]$h.body.volumeMm3) $h.toleransi.volumeMm3
$size = Get-Properti (Get-Properti $bb 'data') 'size'
foreach ($sumbu in 'x', 'y', 'z') {
    Bandingkan-Angka "size.$sumbu" (Get-Properti $size $sumbu) ([double]$h.body.size.$sumbu) $h.toleransi.panjangMm
}
Bandingkan-Angka 'bodyCount' (Get-Properti (Get-Properti $bb 'data') 'bodyCount') ([double]$h.body.bodyCount) 0
foreach ($batas in 'min', 'max') {
    $harapanBatas = Get-Properti $h.body $batas
    if ($null -eq $harapanBatas) { continue }
    $diterimaBatas = Get-Properti (Get-Properti $bb 'data') $batas
    foreach ($sumbu in 'x', 'y', 'z') {
        Bandingkan-Angka "$batas.$sumbu" (Get-Properti $diterimaBatas $sumbu) ([double]$harapanBatas.$sumbu) $h.toleransi.panjangMm
    }
}

# --- Sketsa ----------------------------------------------------------------

$sk = Invoke-Tool 'inspect_sketches'
$dataSk = Get-Properti $sk 'data'
# Get-Properti sudah mempertahankan array sebagai satu nilai. @(...)
# pada pemanggilan fungsi justru membungkusnya menjadi array bertingkat.
$semua = Get-Properti $dataSk 'sketches'

function Cari-Sketsa([string] $nama) {
    $hasil = @($semua | Where-Object { (Get-Properti $_ 'name') -ceq $nama })
    if ($hasil.Count -ne 1) {
        Write-Gagal "sketsa '$nama': ditemukan $($hasil.Count), diharapkan tepat satu"
        return $null
    }
    return $hasil[0]
}

function Periksa-Dimensi($sketsa, $harapan, [string] $label) {
    Bandingkan-Angka "$label.drivingDimensionCount"   (Get-Properti $sketsa 'drivingDimensionCount')   ([double]$harapan.drivingDimensionCount) 0
    Bandingkan-Angka "$label.drivenDimensionCount"    (Get-Properti $sketsa 'drivenDimensionCount')    ([double]$harapan.drivenDimensionCount) 0
    Bandingkan-Angka "$label.unknownDrivenStateCount" (Get-Properti $sketsa 'unknownDrivenStateCount') ([double]$harapan.unknownDrivenStateCount) 0
    Bandingkan-Angka "$label.dimensionCountRead"      (Get-Properti $sketsa 'dimensionCountRead')      ([double]$harapan.dimensionCountRead) 0

    # INVARIANT. Diperiksa di sini juga, bukan hanya di produk: kalau ketiga
    # ember dan totalnya tidak cocok, ada dimensi yang lenyap antara dibaca dan
    # diklasifikasi - dan kehilangan seperti itu tidak muncul di angka mana pun.
    foreach ($field in 'drivingDimensionCount', 'drivenDimensionCount', 'unknownDrivenStateCount', 'dimensionCountRead') {
        if ($null -eq (Get-Properti $sketsa $field)) {
            Write-Gagal "$label invariant tidak bisa diperiksa: $field tidak terbaca"
            return
        }
    }
    $jumlah = [int](Get-Properti $sketsa 'drivingDimensionCount') +
              [int](Get-Properti $sketsa 'drivenDimensionCount') +
              [int](Get-Properti $sketsa 'unknownDrivenStateCount')
    $total = [int](Get-Properti $sketsa 'dimensionCountRead')

    if ($jumlah -eq $total) { Write-Lulus "$label invariant: driving+driven+unknown = $total" }
    else { Write-Gagal "$label invariant PECAH: $jumlah vs dimensionCountRead $total" }
}

Write-Judul 'Sketch1 - dipakai DUA fitur'
$s1 = Cari-Sketsa 'Sketch1'
if ($s1) {
    Bandingkan-Teks 'Sketch1.constrainedStatus' (Get-Properti $s1 'constrainedStatus') $h.sketch1.constrainedStatus

    $konsumen = Get-Properti $s1 'consumedByFeatures'
    foreach ($f in $h.sketch1.consumedByFeatures) {
        if ($konsumen -contains $f) { Write-Lulus "consumedByFeatures memuat '$f'" }
        else { Write-Gagal "consumedByFeatures TIDAK memuat '$f'" }
    }
    foreach ($f in $h.sketch1.consumedByFeaturesTidakMemuat) {
        if ($konsumen -contains $f) { Write-Gagal "consumedByFeatures memuat '$f' - seharusnya TIDAK" }
        else { Write-Lulus "consumedByFeatures tidak memuat '$f'" }
    }

    Periksa-Dimensi $s1 $h.sketch1 'Sketch1'

    # --- HIPOTESIS: isi dependents ---------------------------------------
    Write-Judul 'HIPOTESIS - isi dependents'
    $dep = Get-Properti $s1 'dependents'
    $namaDep = @($dep | ForEach-Object { Get-Properti $_ 'name' })

    foreach ($f in $h.sketch1.dependentsMemuat) {
        if ($namaDep -contains $f) { Write-Lulus "dependents memuat '$f'" }
        else { Write-Patah "dependents TIDAK memuat '$f' - dugaan kami salah, dan itu jawaban yang kami cari" }
    }
}

Write-Judul 'Sketch2 - tidak dipakai fitur apa pun'
$s2 = Cari-Sketsa 'Sketch2'
if ($s2) {
    Bandingkan-Teks 'Sketch2.constrainedStatus' (Get-Properti $s2 'constrainedStatus') $h.sketch2.constrainedStatus

    $konsumen2 = Get-Properti $s2 'consumedByFeatures'
    if ($null -eq $konsumen2) {
        Write-Gagal 'Sketch2.consumedByFeatures null - seharusnya daftar KOSONG, bukan tidak diketahui'
    }
    elseif (@($konsumen2).Count -eq 0) {
        Write-Lulus 'Sketch2.consumedByFeatures = [] (kosong, bukan null)'
    }
    else {
        Write-Gagal "Sketch2.consumedByFeatures berisi $(@($konsumen2) -join ', ') - seharusnya kosong"
    }

    Periksa-Dimensi $s2 $h.sketch2 'Sketch2'
}

# --- Sensus tipe: dilaporkan, bukan di-assert ------------------------------

Write-Judul 'SENSUS typeName (dilaporkan untuk dibaca, bukan di-assert)'
Write-Host '  Daftar putih tipe konsumen diisi DARI SINI - dengan bukti, bukan ingatan.' -ForegroundColor Gray
Write-Host ''

foreach ($sketsa in $semua) {
    $dep = Get-Properti $sketsa 'dependents'
    if ($null -eq $dep -or @($dep).Count -eq 0) { continue }
    Write-Host "  $($sketsa.name):" -ForegroundColor Gray
    foreach ($d in $dep) {
        $tn = $d.typeName
        if (-not $tn) { $tn = '(typeName tidak terbaca)' }
        Write-Host ("     {0,-20} {1}" -f $d.name, $tn)
    }
}

$fieldKosong = Get-Properti $dataSk 'fieldsNotPopulated'
if ($null -ne $fieldKosong -and @($fieldKosong).Count -gt 0) {
    Write-Host ''
    Write-Host "  fieldsNotPopulated: $($fieldKosong -join ', ')" -ForegroundColor Gray
}

# ---------------------------------------------------------------------------

Write-Host ''
Write-Host '=============================================='
Write-Host "Lulus: $script:lulus   Gagal: $script:gagal   Hipotesis patah: $script:hipotesisPatah"

if ($script:hipotesisPatah -gt 0) {
    Write-Host ''
    Write-Host 'Ada hipotesis yang tidak terbukti. Itu BUKAN kegagalan - kontraknya' -ForegroundColor Yellow
    Write-Host 'yang menyesuaikan diri dengan bukti, bukan sebaliknya.' -ForegroundColor Yellow
}

if ($script:gagal -eq 0) {
    Write-Host ''
    Write-Host 'Geometri dan invariant benar.' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host 'Ada yang memang gagal - bukan sekadar hipotesis yang patah.' -ForegroundColor Red
exit 1
