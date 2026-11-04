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

    # JANGAN menamai ini $agy.
    #
    # Nama variabel PowerShell TIDAK peka huruf besar-kecil, jadi $agy dan $Agy
    # adalah variabel yang SAMA - dan $Agy adalah parameter [switch] di atas.
    # Menugaskan string ke sana membuat PowerShell menegakkan tipe parameternya
    # dan melempar ArgumentTransformationMetadataException.
    #
    # Pesannya tidak menyebut nama variabel mana pun, hanya "Cannot convert
    # value System.String to type SwitchParameter", dan ia dilaporkan atas nama
    # SCRIPT-nya - bukan atas nama barisnya. Jadi gejalanya menunjuk ke mana
    # saja kecuali ke penyebabnya.
    $agyExe = Get-AgyPath

    if (-not $agyExe) {
        Write-Gagal 'agy tidak ditemukan - lihat doctor.ps1'
    }
    else {
        $kerja = Join-Path $env:LOCALAPPDATA 'MikosAI\agent-workspace'

        Write-Host "   agy      : $agyExe"
        Write-Host "   workspace: $kerja"

        # Dijalankan DI DALAM workspace, karena di situlah
        # .agents\mcp_config.json yang ditulis add-in berada.
        Push-Location $kerja
        try {
            $permintaan = 'Pakai tool get_active_document dan laporkan hasilnya apa adanya. ' +
                          'Jangan mengarang nilai apa pun.'

            # IZIN HARUS DIBERIKAN EKSPLISIT DI MODE HEADLESS.
            #
            # Percobaan pertama gagal dengan:
            #
            #   "a tool required the mcp permission that headless mode cannot
            #    prompt for, so it was auto-denied"
            #   "denied_actions":[{"action":"mcp","display_name":"CallMcpTool"}]
            #
            # Itu BUKAN kegagalan server kita - agy sudah menemukan server kita
            # dan mencoba memanggil tool-nya. Yang memblokir sistem izin agy
            # sendiri, yang di mode -p tidak bisa menampilkan prompt.
            #
            # BUKAN --dangerously-skip-permissions. Opsi itu menyetujui SEMUA
            # tool, termasuk perintah shell, dan memakainya di script verifikasi
            # berarti verifikasinya dijalankan dengan izin yang jauh lebih luas
            # daripada pemakaian sehari-hari - menguji keadaan yang bukan
            # keadaan sebenarnya.
            #
            # --settings bersifat SESI SAJA dan tidak pernah disimpan, jadi
            # ~/.gemini/settings.json milik pengguna tidak tersentuh. Itu
            # mempertahankan gerbang isolasi config global.
            #
            # DITULIS KE BERKAS, BUKAN DITERUSKAN INLINE.
            #
            # Percobaan inline gagal dengan "invalid character 'p' looking for
            # beginning of object key string": PowerShell 5.1 melepas tanda kutip
            # ganda saat meneruskan argumen ke exe native, jadi {"permissions":...}
            # tiba sebagai {permissions:...}. Itu bisa ditambal dengan escape
            # berlapis, tapi escape berlapis adalah hal yang rusak diam-diam saat
            # diedit nanti.
            #
            # --settings juga menerima path berkas, yang menghapus seluruh
            # masalah kutip-mengutip.
            $berkasIzin = Join-Path ([System.IO.Path]::GetTempPath()) "mikosai-izin-$PID.json"

            # TANPA BOM, dan itu bukan detail.
            #
            # Set-Content -Encoding utf8 di PowerShell 5.1 menulis UTF-8 DENGAN
            # BOM, dan parser JSON agy menolaknya:
            #
            #   invalid character '﻿' looking for beginning of value
            #
            # UTF8Encoding($false) adalah satu-satunya cara ringkas untuk menulis
            # UTF-8 tanpa BOM di PowerShell 5.1.
            # SINTAKSNYA mcp(<server>/<tool>), BUKAN mcp(<server>).
            #
            # Percobaan dengan mcp(mikosai-solidworks) diterima sebagai JSON yang
            # sah tapi TIDAK cocok dengan apa pun, jadi tool-nya tetap ditolak -
            # gagal diam-diam, tanpa keluhan soal aturannya.
            #
            # Bentuk yang benar diambil dari string di dalam agy.exe sendiri:
            #
            #   mcp(<server_name>/<tool_name>)
            #   mcp(chrome_devtools/*)
            #   mcp(*)
            #
            # Dipakai lingkup server kita saja, bukan mcp(*): yang diuji adalah
            # tool kita, dan memberi izin lebih luas daripada yang diuji membuat
            # hasilnya tidak mewakili pemakaian sehari-hari.
            [System.IO.File]::WriteAllText(
                $berkasIzin,
                '{"permissions":{"allow":["mcp(mikosai-solidworks/*)"]}}',
                (New-Object System.Text.UTF8Encoding $false))

            # TANPA 2>&1, dan itu disengaja.
            #
            # Di PowerShell 5.1, mengarahkan stderr exe native ke dalam pipeline
            # membungkus SETIAP baris jadi ErrorRecord NativeCommandError - yang
            # lalu dicetak sebagai blok error berhalaman, lengkap dengan stack
            # trace, walaupun perintahnya sukses. Keluarannya jadi lebih sulit
            # dibaca daripada kalau tidak ditangkap sama sekali.
            #
            # `--output-format json` menulis hasilnya ke STDOUT, jadi itu saja
            # yang ditangkap. stderr dibiarkan mengalir ke layar: di situ agy
            # menulis kemajuannya, dan saat gagal itulah yang ingin dilihat.
            #
            # $ErrorActionPreference tetap dilonggarkan supaya stderr tidak
            # menggagalkan perintah yang sebenarnya berhasil.
            $simpan = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            $keluaran = (& $agyExe --output-format json --settings $berkasIzin -p $permintaan) | Out-String
            $kode = $LASTEXITCODE
            $ErrorActionPreference = $simpan

            # Out-String pada keluaran kosong mengembalikan string kosong, bukan
            # null - tapi jangan bergantung pada itu di bawah Set-StrictMode.
            if (-not $keluaran) { $keluaran = '' }

            # status: SUCCESS BUKAN berarti berhasil untuk tujuan kita.
            #
            # Percobaan pertama mengembalikan status SUCCESS, exit code 0, DAN
            # response kosong - karena tool-nya ditolak di lapisan izin. Menilai
            # dari status atau exit code saja akan melaporkan itu sebagai lulus.
            #
            # denied_actions adalah bukti yang bisa dibaca mesin, jadi itu yang
            # diperiksa.
            $jawabanJson = $null
            if ($keluaran.Trim()) {
                try { $jawabanJson = $keluaran | ConvertFrom-Json } catch { $jawabanJson = $null }
            }

            $ditolak = @()
            if ($jawabanJson -and ($jawabanJson.PSObject.Properties.Name -contains 'denied_actions')) {
                $ditolak = @($jawabanJson.denied_actions)
            }

            $jawaban = ''
            if ($jawabanJson -and ($jawabanJson.PSObject.Properties.Name -contains 'response')) {
                $jawaban = [string] $jawabanJson.response
            }

            if ($kode -ne 0) {
                Write-Gagal "agy keluar dengan kode $kode (pesannya di stderr, di atas)"
                Write-Host $keluaran.Trim() -ForegroundColor DarkGray
            }
            elseif ($ditolak.Count -gt 0) {
                # Dibedakan dengan sengaja: agy MENEMUKAN server kita dan
                # MENCOBA memanggil tool-nya. Yang gagal lapisan izinnya, bukan
                # integrasi kita - dan itu diagnosis yang sama sekali berbeda.
                $nama = ($ditolak | ForEach-Object { $_.action }) -join ', '
                Write-Gagal "agy mencoba memanggil tool kita tapi izinnya ditolak: $nama"
                Write-Host '   Integrasinya BEKERJA - yang kurang aturan izin. Periksa $izin di script ini.' -ForegroundColor Yellow
            }
            elseif ($jawaban -match 'get_active_document' -or $keluaran -match 'get_active_document') {
                Write-Lulus 'agy memanggil tool kita dan menyebutnya di jawabannya'
                Write-Host "   $($jawaban.Trim())" -ForegroundColor DarkGray
            }
            elseif (-not $jawaban) {
                Write-Gagal 'agy jalan tanpa error tapi tidak menghasilkan jawaban apa pun'
                Write-Host $keluaran.Trim() -ForegroundColor DarkGray
            }
            else {
                # agy menjawab tapi tidak menyentuh tool kita. Penyebab paling
                # mungkin: .agents\mcp_config.json tidak dibaca.
                Write-Gagal 'agy menjawab tapi tidak terlihat memanggil tool kita'
                Write-Host "   Periksa: $(Join-Path $kerja '.agents\mcp_config.json')" -ForegroundColor Yellow
                Write-Host "   $($jawaban.Trim())" -ForegroundColor DarkGray
            }
        }
        finally {
            Pop-Location
            Remove-Item -LiteralPath $berkasIzin -ErrorAction SilentlyContinue
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
