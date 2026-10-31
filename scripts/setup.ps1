<#
.SYNOPSIS
    Setup MikosAI satu perintah.

.DESCRIPTION
    Mengerjakan semuanya berurutan dan berhenti dengan pesan jelas kalau ada
    yang kurang:

      1. Periksa lingkungan
      2. Build MikosAI.Core dan jalankan seluruh test
      3. Verifikasi kode add-in kompilabel (tanpa SolidWorks)
      4. Build add-in (kalau SolidWorks terdeteksi)
      5. Registrasi add-in - meminta UAC sekali, otomatis

    Aman dijalankan berulang kali. Langkah yang sudah beres dilewati.

    Kalau SolidWorks tidak terdeteksi, setup TIDAK gagal - langkah 4 dan 5
    dilewati, dan pengembangan MikosAI.Core tetap bisa jalan penuh.

.PARAMETER SolidWorksApiDir
    Timpa hasil deteksi otomatis folder api\redist.

.PARAMETER SkipRegister
    Build saja, jangan registrasi. Menghindari prompt UAC.

.PARAMETER SkipTests
    Lewati 171 test. Tidak disarankan.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
#>
[CmdletBinding()]
param(
    [string] $SolidWorksApiDir,
    [switch] $SkipRegister,
    [switch] $SkipTests,

    # Jalur mundur: kopi swpublished.dll ke folder output alih-alih menanamkan
    # tipe interop. Pakai hanya kalau penanaman bermasalah.
    [switch] $NoEmbedPia,

    # Dipakai internal saat script menjalankan ulang dirinya dengan hak admin.
    [switch] $ElevatedRegisterOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'lib\MikosAI.Env.ps1')

$RepoRoot  = Split-Path -Parent $PSScriptRoot
$Solution  = Join-Path $RepoRoot 'MikosAI.sln'
$AddinProj = Join-Path $RepoRoot 'src\MikosAI.Addin\MikosAI.Addin.csproj'
$AddinDll  = Join-Path $RepoRoot 'src\MikosAI.Addin\bin\Release\net48\MikosAI.Addin.dll'
$AddinTlb  = [System.IO.Path]::ChangeExtension($AddinDll, '.tlb')
$RegAsm    = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'

# Berhenti dengan pesan rapi. `throw` menampilkan jejak exception PowerShell
# yang membingungkan pengguna, dan setup ini harus terasa simpel.
function Stop-Setup {
    param([string] $Pesan, [string] $Petunjuk = '')

    Write-Host ''
    Write-Host 'SETUP BERHENTI' -ForegroundColor Red
    Write-Host '--------------'
    foreach ($baris in ($Pesan -split "`n")) {
        Write-Host "  $($baris.TrimEnd())" -ForegroundColor Red
    }

    if ($Petunjuk) {
        Write-Host ''
        foreach ($baris in ($Petunjuk -split "`n")) {
            Write-Host "  $($baris.TrimEnd())"
        }
    }

    Write-Host ''
    Write-Host '  Untuk diagnosis lengkap:  powershell -File scripts\doctor.ps1'
    Write-Host ''
    exit 1
}

$langkah = 0
function Write-Langkah($teks) {
    $script:langkah++
    Write-Host ''
    Write-Host "[$script:langkah] $teks" -ForegroundColor Cyan
}

function Invoke-Register {
    if (-not (Test-Path -LiteralPath $AddinDll)) {
        throw "Assembly tidak ada: $AddinDll"
    }

    # JANGAN biarkan 2>&1 pada executable native menjadi error yang menghentikan.
    #
    # Di PowerShell 5.1, meredirect stderr sebuah exe membungkus tiap baris jadi
    # ErrorRecord (NativeCommandError). Dengan $ErrorActionPreference='Stop',
    # warning RegAsm yang TIDAK BERBAHAYA ikut menghentikan script - padahal
    # RegAsm keluar dengan kode 0 dan registrasinya berhasil.
    #
    # Jadi: turunkan preference sementara, dan nilai keberhasilan HANYA dari
    # $LASTEXITCODE. ToString() pada ErrorRecord memberi pesannya saja, tanpa
    # jejak posisi yang bikin panik.
    $eapSebelumnya = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $baris = & $RegAsm $AddinDll /codebase /tlb:$AddinTlb 2>&1 | ForEach-Object { $_.ToString() }
        $kode  = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $eapSebelumnya
    }

    foreach ($b in $baris) {
        if ($b -match 'warning RA0000') {
            Write-Host "  (warning wajar, registrasi tetap jalan) $b" -ForegroundColor DarkGray
        }
        else {
            Write-Host "  $b"
        }
    }

    if ($kode -ne 0) {
        $teks = ($baris -join "`n")

        # Kegagalan yang paling mungkin dan paling membingungkan: RegAsm harus
        # MEMUAT assembly kita untuk memanggil [ComRegisterFunction], dan saat
        # itu CLR butuh PIA SolidWorks - yang tidak ada di GAC.
        if ($teks -match 'SolidWorks\.Interop') {
            throw @'
RegAsm tidak bisa memuat PIA SolidWorks.

Assembly add-in dibangun dengan referensi PIA biasa, bukan tipe interop yang
ditanam, jadi CLR mencari swpublished.dll saat RegAsm memuat assembly kita -
dan PIA SolidWorks tidak ada di GAC.

Bangun ulang dengan penanaman tipe interop (sudah default):
  powershell -File scripts\setup.ps1

Atau pakai jalur mundur copy-local:
  powershell -File scripts\setup.ps1 -NoEmbedPia
'@
        }

        throw "RegAsm gagal dengan exit code $kode."
    }
}

# ====================================================================
# Mode khusus: dijalankan ulang dengan hak admin hanya untuk registrasi
# ====================================================================
if ($ElevatedRegisterOnly) {
    Write-Host 'Registrasi add-in (mode administrator)...' -ForegroundColor Cyan
    try {
        Invoke-Register
        Write-Host ''
        Write-Host 'Registrasi berhasil.' -ForegroundColor Green
        exit 0
    }
    catch {
        Write-Host ''
        Write-Host "Registrasi GAGAL: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ''
        Write-Host 'Tekan Enter untuk menutup jendela ini...'
        [void][System.Console]::ReadLine()
        exit 1
    }
}

Write-Host ''
Write-Host 'Setup MikosAI' -ForegroundColor Cyan
Write-Host '============='

# Tampilkan commit yang sedang dibangun, dan peringatkan kalau branch lokal
# tertinggal dari origin. Dua kali kami menguji kode lama karena git pull
# terlewat, dan gejalanya menyesatkan - error runtime dari bug yang sudah
# diperbaiki.
try {
    $commit  = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
    $subjek  = (& git -C $RepoRoot log -1 --format=%s 2>$null)

    if ($commit) {
        Write-Host ''
        Write-Host "Commit: $($commit.Trim())  $subjek"

        $tertinggal = (& git -C $RepoRoot rev-list --count "HEAD..origin/main" 2>$null)
        if ($tertinggal -and ([int] $tertinggal) -gt 0) {
            Write-Host ''
            Write-Host "PERINGATAN: branch lokal tertinggal $tertinggal commit dari origin/main." -ForegroundColor Yellow
            Write-Host '            Kalau tidak sengaja, hentikan dan jalankan:' -ForegroundColor Yellow
            Write-Host '              git pull origin main' -ForegroundColor Yellow
            Write-Host '            (perbandingan ini memakai ref origin yang tersimpan, jalankan git fetch kalau ragu)'
        }
    }
}
catch {
    # Bukan repo git, atau git tidak ada. Bukan masalah.
}

# ====================================================================
# 1. Lingkungan
# ====================================================================
Write-Langkah 'Memeriksa lingkungan'

$checks = Get-AllChecks
Write-CheckTable -Checks $checks

$blockers = @($checks | Where-Object { -not $_.Lulus -and $_.Blocker })
if ($blockers.Count -gt 0) {
    Write-Host ''
    Write-Host 'Setup tidak bisa lanjut.' -ForegroundColor Red
    foreach ($item in $blockers) {
        Write-Host ''
        Write-Host "  $($item.Nama)" -ForegroundColor Red
        Write-Host "  $($item.Saran)"
    }
    Write-Host ''
    Write-Host 'Perbaiki lalu jalankan ulang setup.ps1.'
    Write-Host ''
    exit 1
}

# Tentukan path PIA.
$apiDir = $SolidWorksApiDir
if ([string]::IsNullOrWhiteSpace($apiDir)) {
    $apiDir = Get-SolidWorksApiDir
}

$punyaSolidWorks = -not [string]::IsNullOrWhiteSpace($apiDir)

# ====================================================================
# 2. Core + test
# ====================================================================
Write-Langkah 'Build MikosAI.Core dan jalankan test'

& dotnet build $Solution -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Stop-Setup 'Build MikosAI.Core gagal.' `
        "Ini tidak butuh SolidWorks sama sekali, jadi kegagalan di sini berarti masalah toolchain.`nPeriksa error compiler di atas."
}
Write-Host '  Build OK.' -ForegroundColor Green

if ($SkipTests) {
    Write-Host '  Test dilewati (-SkipTests).' -ForegroundColor Yellow
}
else {
    & dotnet test $Solution -c Release --nologo -v q --no-build
    if ($LASTEXITCODE -ne 0) {
        Stop-Setup 'Ada test yang gagal.' `
            "Jangan lanjut sebelum semuanya hijau. Laporkan nama test yang merah."
    }
    Write-Host '  Semua test hijau.' -ForegroundColor Green
}

# ====================================================================
# 3. Verifikasi kode add-in kompilabel
# ====================================================================
Write-Langkah 'Verifikasi kode add-in (tanpa SolidWorks)'

& dotnet build $AddinProj -c Release -p:UsePiaStub=true --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Stop-Setup 'Kode add-in tidak kompilabel bahkan dengan stub.' `
        "Ini murni masalah kode, bukan lingkungan mesinmu.`nLaporkan error compiler di atas - itu cepat diperbaiki."
}
Write-Host '  Kode add-in benar secara tipe.' -ForegroundColor Green

# ====================================================================
# 4. Build add-in
# ====================================================================
if (-not $punyaSolidWorks) {
    Write-Langkah 'Build add-in - DILEWATI'
    Write-Host '  SolidWorks tidak terdeteksi.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Ini BUKAN kegagalan setup. Yang tetap bisa dikerjakan:' -ForegroundColor Yellow
    Write-Host '    - MikosAI.Core dan seluruh testnya'
    Write-Host '    - McpHttpServer, PlanRunner, UI - semuanya di atas FakeToolExecutor'
    Write-Host '    - Verifikasi kode add-in lewat CompileCheck'
    Write-Host ''
    Write-Host '  Kalau SolidWorks sebenarnya terpasang, berikan path-nya:'
    Write-Host "    -SolidWorksApiDir '<folder yang memuat SolidWorks.Interop.swpublished.dll>'"
    Write-Host ''
    Write-Host 'Setup selesai sejauh yang bisa dilakukan di mesin ini.' -ForegroundColor Green
    exit 0
}

Write-Langkah 'Build add-in'
Write-Host "  PIA: $apiDir"

$embed = 'true'
if ($NoEmbedPia) { $embed = 'false' }

if ($NoEmbedPia) {
    Write-Host '  Mode: copy-local PIA (-NoEmbedPia)' -ForegroundColor Yellow
}
else {
    Write-Host '  Mode: tipe interop ditanam (tanpa dependensi runtime)'
}

& dotnet build $AddinProj -c Release -p:SolidWorksApiDir="$apiDir" -p:EmbedPia=$embed --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Stop-Setup 'Build add-in gagal padahal compile-check lulus.' `
        ("Artinya masalahnya di referensi PIA, BUKAN di kode kita. Periksa:`n" +
         "  - Apakah folder ini benar-benar api\redist instalasi SolidWorks?`n" +
         "      $apiDir`n" +
         "  - Apakah SolidWorks.Interop.swpublished.dll ada di situ?")
}

if (-not (Test-Path -LiteralPath $AddinDll)) {
    Stop-Setup 'Build melaporkan sukses tapi assembly tidak ada.' "Diharapkan di: $AddinDll"
}
Write-Host '  Build OK.' -ForegroundColor Green

# ====================================================================
# 5. Registrasi
# ====================================================================
if ($SkipRegister) {
    Write-Langkah 'Registrasi - DILEWATI (-SkipRegister)'
}
else {
    Write-Langkah 'Registrasi add-in'

    if (-not (Test-Path -LiteralPath $RegAsm)) {
        Stop-Setup 'RegAsm tidak ditemukan.' `
            ("Diharapkan di: $RegAsm`nPastikan .NET Framework 4.x terpasang.")
    }

    $admin = Test-IsAdministrator

    if ($admin.Lulus) {
        Invoke-Register
        Write-Host '  Terdaftar.' -ForegroundColor Green
    }
    else {
        Write-Host '  Butuh hak administrator - akan muncul prompt UAC sekali.' -ForegroundColor Yellow

        $argumen = @(
            '-NoProfile'
            '-ExecutionPolicy'; 'Bypass'
            '-File'; "`"$PSCommandPath`""
            '-ElevatedRegisterOnly'
        )
        if ($NoEmbedPia) { $argumen += '-NoEmbedPia' }

        $proses = Start-Process -FilePath 'powershell' -ArgumentList $argumen `
            -Verb RunAs -Wait -PassThru -ErrorAction Stop

        if ($proses.ExitCode -ne 0) {
            Stop-Setup 'Registrasi gagal di proses terelevasi.' `
                ("Kalau prompt UAC kamu tolak, jalankan ulang setup.ps1.`n`n" +
                 "Kalau UAC disetujui tapi tetap gagal, coba manual:`n" +
                 "  buka PowerShell sebagai administrator, lalu jalankan`n" +
                 "  powershell -File scripts\addin.ps1 -Action Register -RegistrationScope Machine")
        }

        Write-Host '  Terdaftar.' -ForegroundColor Green
    }
}

# ====================================================================
# Selesai
# ====================================================================
Write-Host ''
Write-Host 'SETUP SELESAI' -ForegroundColor Green
Write-Host '============='
Write-Host ''
Write-Host 'Satu langkah terakhir harus dilakukan manual di SolidWorks:' -ForegroundColor Cyan
Write-Host ''
Write-Host '  1. Buka SolidWorks'
Write-Host '  2. Tools > Add-Ins'
Write-Host '  3. Centang MikosAI (kolom kiri untuk aktif sekarang, kanan untuk aktif otomatis)'
Write-Host '  4. Jalankan:  powershell -File scripts\addin.ps1 -Action Probe'
Write-Host ''
Write-Host 'Probe itu yang menentukan apakah add-in benar-benar hidup.'
Write-Host 'LoadAddIn=0 TIDAK dihitung lulus - hanya host-status.json yang dihitung.'
Write-Host ''
Write-Host 'Kalau ada yang tidak jalan:  powershell -File scripts\doctor.ps1'
