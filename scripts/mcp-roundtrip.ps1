<#
.SYNOPSIS
    Memverifikasi round-trip MCP terhadap add-in yang SUNGGUHAN berjalan di
    dalam SolidWorks.

.DESCRIPTION
    Gerbang penerimaan Fase 1 menuntut bukti bahwa daftar tool kita bisa dilihat
    dan dipanggil dari luar. Sampai sekarang buktinya hanya "server-nya bind dan
    host-status.json bilang mcpServer: true" - itu membuktikan socket-nya
    terbuka, BUKAN bahwa protokolnya benar.

    Script ini memanggil servernya sebagai klien sungguhan:

      1. initialize   - protokol disepakati
      2. tools/list   - daftar tool terbaca
      3. tools/call   - keenam tool read-only dipanggil satu per satu
      4. token salah  - WAJIB ditolak 401

    Langkah 4 sama pentingnya dengan tiga yang pertama. Server yang menerima
    token apa pun bukan "lulus dengan catatan" - ia lubang, dan ia hidup di
    dalam proses SolidWorks milik pengguna.

    Tidak ada model AI yang dipanggil di sini, jadi tidak ada kuota yang
    terpakai. Untuk round-trip lewat agy, pakai -Agy.

.PARAMETER Agy
    Setelah HTTP lulus, minta `agy` memanggil salah satu tool kita.
    INI MEMAKAI KUOTA akun Google yang login di agy.

.PARAMETER TimeoutSec
    Batas waktu per request. Default 30 detik: pembacaan model di SolidWorks
    berjalan di thread utama dan bisa menunggu operasi lain selesai.

.PARAMETER SessionFile
    Berkas sesi alternatif. Default: berkas yang ditulis add-in.

    Ada supaya script ini sendiri bisa diuji - lihat scripts\mcp-harness.ps1,
    yang menyalakan server MCP sungguhan tanpa SolidWorks dan menulis sesi ke
    berkas sementara. Tanpa ini, satu-satunya cara menguji script verifikasi
    adalah menjalankan hal yang sedang diverifikasi.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\mcp-roundtrip.ps1

.NOTES
    SolidWorks harus sedang BERJALAN dengan add-in MikosAI aktif, dan sebaiknya
    ada satu part terbuka - beberapa tool mengembalikan WARN pada dokumen kosong,
    dan itu hasil yang sah, bukan kegagalan.
#>
[CmdletBinding()]
param(
    [switch] $Agy,
    [int]    $TimeoutSec = 30,
    [string] $SessionFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\lib\MikosAI.Env.ps1"

if (-not $SessionFile) {
    $SessionFile = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace\session.json'
}

$script:Lulus = 0
$script:Gagal = 0

function Write-Langkah {
    param([string] $Nama)
    Write-Host ''
    Write-Host "-- $Nama" -ForegroundColor Cyan
}

function Write-Lulus {
    param([string] $Pesan)
    $script:Lulus++
    Write-Host "   LULUS  $Pesan" -ForegroundColor Green
}

function Write-Gagal {
    param([string] $Pesan)
    $script:Gagal++
    Write-Host "   GAGAL  $Pesan" -ForegroundColor Red
}

# ---------------------------------------------------------------------------
# Sesi
# ---------------------------------------------------------------------------

Write-Host 'MikosAI - round-trip MCP' -ForegroundColor White
Write-Host '========================'

if (-not (Test-Path -LiteralPath $SessionFile)) {
    Write-Host ''
    Write-Host "session.json tidak ada di $SessionFile" -ForegroundColor Red
    Write-Host 'Artinya server MCP belum pernah berjalan di mesin ini.' -ForegroundColor Yellow
    Write-Host 'Buka SolidWorks dengan add-in MikosAI aktif, lalu jalankan ulang.'
    exit 1
}

$sesi = Get-Content -LiteralPath $SessionFile -Raw | ConvertFrom-Json

Write-Host ''
Write-Host "  Endpoint : $($sesi.serverUrl)"
Write-Host "  Sesi     : $($sesi.createdAt)"
Write-Host "  PID      : $($sesi.pid)"

# session.json tertinggal setelah SolidWorks ditutup. Menembaknya begitu saja
# menghasilkan connection-refused yang membingungkan, jadi periksa prosesnya
# dulu dan katakan apa yang sebenarnya terjadi.
$proses = Get-Process -Id $sesi.pid -ErrorAction SilentlyContinue

if (-not $proses) {
    Write-Host ''
    Write-Host "  Proses $($sesi.pid) sudah tidak berjalan." -ForegroundColor Red
    Write-Host '  session.json ini sisa dari sesi sebelumnya.' -ForegroundColor Yellow
    Write-Host '  Buka SolidWorks, tunggu panel MikosAI muncul, lalu jalankan ulang.'
    exit 1
}

Write-Host "  Proses   : $($proses.ProcessName) (hidup)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Klien JSON-RPC
# ---------------------------------------------------------------------------

Add-Type -AssemblyName System.Net.Http | Out-Null

$script:Klien = [System.Net.Http.HttpClient]::new()
$script:Klien.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)

function Get-Properti {
    <#
        Membaca properti yang mungkin tidak ada.

        Set-StrictMode -Version Latest membuat $obj.tidakAda MELEMPAR, bukan
        mengembalikan $null. Itu perilaku yang diinginkan untuk salah tulis nama
        variabel, tapi jawaban JSON-RPC memang tidak punya field 'error' kalau
        ia sukses - dan 'result' kalau ia gagal. Mengeceknya langsung membuat
        script ini mati pada jawaban yang justru benar.
    #>
    param($Objek, [string] $Nama)

    if ($null -eq $Objek) { return $null }
    if (-not ($Objek.PSObject.Properties.Name -contains $Nama)) { return $null }

    return $Objek.$Nama
}

function Invoke-Rpc {
    <#
        Mengembalikan hashtable: Status, Body (objek hasil parse atau $null), Raw.
        Tidak melempar pada status non-200 - status itu sendiri bagian dari yang
        diuji.
    #>
    param(
        [string] $Metode,
        [hashtable] $Parameter = @{},
        [string] $Token,
        [int] $Id = 1
    )

    $muatan = @{
        jsonrpc = '2.0'
        id      = $Id
        method  = $Metode
        params  = $Parameter
    } | ConvertTo-Json -Depth 10 -Compress

    $isi = [System.Net.Http.StringContent]::new($muatan, [System.Text.Encoding]::UTF8, 'application/json')

    $permintaan = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $sesi.serverUrl)
    $permintaan.Content = $isi
    $permintaan.Headers.Add('Authorization', "Bearer $Token")

    try {
        $jawaban = $script:Klien.SendAsync($permintaan).GetAwaiter().GetResult()
        $teks = $jawaban.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        $objek = $null
        if ($teks) {
            try { $objek = $teks | ConvertFrom-Json } catch { $objek = $null }
        }

        return @{
            Status = [int] $jawaban.StatusCode
            Body   = $objek
            Raw    = $teks
        }
    }
    catch {
        return @{
            Status = -1
            Body   = $null
            Raw    = $_.Exception.Message
        }
    }
}

# ---------------------------------------------------------------------------
# 1. initialize
# ---------------------------------------------------------------------------

Write-Langkah 'initialize'

$r = Invoke-Rpc -Metode 'initialize' -Token $sesi.token -Parameter @{
    protocolVersion = '2024-11-05'
    capabilities    = @{}
    clientInfo      = @{ name = 'mcp-roundtrip.ps1'; version = '1.0' }
}

if ($r.Status -ne 200) {
    Write-Gagal "status $($r.Status) - $($r.Raw)"
}
else {
    $hasil = Get-Properti $r.Body 'result'

    if ($null -eq $hasil) {
        Write-Gagal "jawaban tanpa field result: $($r.Raw)"
    }
    else {
        $info = Get-Properti $hasil 'serverInfo'
        Write-Lulus "protokol $(Get-Properti $hasil 'protocolVersion'), server $(Get-Properti $info 'name') $(Get-Properti $info 'version')"
    }
}

# ---------------------------------------------------------------------------
# 2. tools/list
# ---------------------------------------------------------------------------

Write-Langkah 'tools/list'

$r = Invoke-Rpc -Metode 'tools/list' -Token $sesi.token -Id 2
$namaTool = @()

$hasilDaftar = Get-Properti $r.Body 'result'

if ($r.Status -ne 200 -or $null -eq $hasilDaftar) {
    Write-Gagal "status $($r.Status) - $($r.Raw)"
}
else {
    $namaTool = @((Get-Properti $hasilDaftar 'tools') | ForEach-Object { $_.name })

    if ($namaTool.Count -eq 0) {
        # Daftar kosong itu SAH menurut desainnya - gating capabilities.yaml
        # memang bisa menghasilkan nol tool. Tapi di sini ia berarti tidak ada
        # yang bisa diuji, jadi tetap dihitung gagal.
        Write-Gagal 'daftar tool kosong - periksa status di capabilities.yaml'
    }
    else {
        Write-Lulus "$($namaTool.Count) tool terbaca: $($namaTool -join ', ')"
    }
}

# ---------------------------------------------------------------------------
# 3. tools/call
# ---------------------------------------------------------------------------

Write-Langkah 'tools/call'

foreach ($tool in $namaTool) {
    $r = Invoke-Rpc -Metode 'tools/call' -Token $sesi.token -Id 3 -Parameter @{
        name      = $tool
        arguments = @{}
    }

    if ($r.Status -ne 200) {
        Write-Gagal "$tool - status $($r.Status): $($r.Raw)"
        continue
    }

    $rpcError = Get-Properti $r.Body 'error'
    if ($null -ne $rpcError) {
        Write-Gagal "$tool - error JSON-RPC: $(Get-Properti $rpcError 'message')"
        continue
    }

    $hasil = Get-Properti $r.Body 'result'
    if ($null -eq $hasil) {
        Write-Gagal "$tool - jawaban tanpa result: $($r.Raw)"
        continue
    }

    # isError=true BUKAN kegagalan script ini.
    #
    # Ia dipasang dari keberhasilan TUGAS, bukan keberhasilan transport. Pada
    # dokumen kosong, get_bounding_box memang seharusnya melapor tidak berhasil.
    # Yang diuji di sini adalah round-trip-nya, dan jawaban terstruktur yang
    # jujur soal kegagalannya justru bukti bahwa round-trip-nya bekerja.
    $isError = [bool] (Get-Properti $hasil 'isError')
    $isi = Get-Properti $hasil 'content'
    $teks = ($isi | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text

    if (-not $teks) {
        Write-Gagal "$tool - result tanpa isi teks"
        continue
    }

    $panjang = $teks.Length
    if ($isError) {
        Write-Lulus "$tool - round-trip OK, tugas melapor gagal (sah), $panjang bita"
    }
    else {
        Write-Lulus "$tool - round-trip OK, tugas berhasil, $panjang bita"
    }
}

# ---------------------------------------------------------------------------
# 4. Token salah WAJIB ditolak
# ---------------------------------------------------------------------------

Write-Langkah 'penolakan token salah'

$r = Invoke-Rpc -Metode 'tools/list' -Token 'token-yang-jelas-salah' -Id 4

if ($r.Status -eq 401) {
    Write-Lulus 'token salah ditolak 401'
}
elseif ($r.Status -eq -1) {
    # Server tidak terhubung sama sekali. Itu kegagalan, tapi BUKAN kegagalan
    # otentikasi - menyebutnya lubang keamanan akan mengirim orang mencari
    # masalah yang tidak ada.
    Write-Gagal "tidak bisa menghubungi server: $($r.Raw)"
}
else {
    Write-Gagal "token salah menghasilkan status $($r.Status), seharusnya 401 - ini lubang keamanan, bukan catatan kecil"
}

# ---------------------------------------------------------------------------
# 5. agy (opsional, memakai kuota)
# ---------------------------------------------------------------------------

if ($Agy) {
    Write-Langkah 'round-trip lewat agy (memakai kuota)'

    $agy = Get-AgyPath

    if (-not $agy) {
        Write-Gagal 'agy tidak ditemukan - lihat doctor.ps1'
    }
    else {
        $kerja = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace'

        Write-Host "   agy      : $agy"
        Write-Host "   workspace: $kerja"

        # Dijalankan DI DALAM workspace, karena di situlah
        # .agents\mcp_config.json yang ditulis add-in berada.
        Push-Location $kerja
        try {
            $permintaan = 'Pakai tool get_active_document dan laporkan hasilnya apa adanya. ' +
                          'Jangan mengarang nilai apa pun.'

            # $ErrorActionPreference dilonggarkan: agy menulis kemajuan ke stderr,
            # dan di PowerShell 5.1 itu cukup untuk menggagalkan perintah yang
            # sebenarnya sukses.
            $simpan = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            $keluaran = & $agy --output-format json -p $permintaan 2>&1 | Out-String
            $kode = $LASTEXITCODE
            $ErrorActionPreference = $simpan

            if ($kode -ne 0) {
                Write-Gagal "agy keluar dengan kode $kode"
                Write-Host $keluaran -ForegroundColor DarkGray
            }
            elseif ($keluaran -match 'get_active_document') {
                Write-Lulus 'agy memanggil tool kita dan menyebutnya di jawabannya'
                Write-Host ($keluaran.Trim() | Select-Object -First 1) -ForegroundColor DarkGray
            }
            else {
                Write-Gagal 'agy jalan tapi tidak terlihat memanggil tool kita'
                Write-Host $keluaran -ForegroundColor DarkGray
            }
        }
        finally {
            Pop-Location
        }
    }
}
else {
    Write-Host ''
    Write-Host 'Round-trip lewat agy dilewati. Tambahkan -Agy untuk mengujinya.' -ForegroundColor DarkGray
    Write-Host 'Catatan: itu memakai kuota akun Google yang login di agy.' -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------

$script:Klien.Dispose()

Write-Host ''
Write-Host '========================'
Write-Host "Lulus: $script:Lulus   Gagal: $script:Gagal"

if ($script:Gagal -eq 0) {
    Write-Host 'Round-trip MCP terbukti.' -ForegroundColor Green
    exit 0
}

Write-Host 'Round-trip MCP BELUM terbukti.' -ForegroundColor Red
exit 1
