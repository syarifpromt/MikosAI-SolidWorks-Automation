<#
.SYNOPSIS
    Build, registrasi, dan probe add-in MikosAI. Tanpa Visual Studio.

.DESCRIPTION
    Semua langkah di sini memakai dotnet SDK dan RegAsm yang ikut
    .NET Framework. Tidak ada yang butuh Visual Studio atau Windows SDK.

    Action:
      Build        Compile add-in. Butuh -SolidWorksApiDir.
      CompileCheck Verifikasi kode add-in kompilabel TANPA SolidWorks, memakai
                   stub ISwAddin lokal. Outputnya tidak boleh dipakai - ini
                   hanya pemeriksaan tipe.
      Register    Registrasi COM + TLB. Scope Machine butuh administrator.
      Unregister  Lepas registrasi. Pakai path assembly yang sama.
      Probe       Baca host-status.json dan tegakkan gerbang penerimaan.
      Status      Tampilkan apa yang terdaftar di registry.

.PARAMETER SolidWorksApiDir
    Direktori api\redist pada instalasi SolidWorks. Harus diberikan eksplisit
    supaya tidak salah merujuk versi SolidWorks yang berbeda. Biasanya:
    C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist

.PARAMETER RegistrationScope
    Machine menulis HKLM dan mendaftarkan TLB - ini yang diperlukan supaya
    SolidWorks benar-benar memuat add-in. CurrentUser hanya membuktikan
    aktivasi CLR/COM dan TIDAK cukup.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Build `
      -SolidWorksApiDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Register -RegistrationScope Machine
    powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Probe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Build', 'CompileCheck', 'Register', 'Unregister', 'Probe', 'Status')]
    [string] $Action,

    [string] $SolidWorksApiDir,

    [ValidateSet('Machine', 'CurrentUser')]
    [string] $RegistrationScope = 'Machine',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $NoEmbedPia
)

$ErrorActionPreference = 'Stop'

# Deteksi lingkungan bersama. SATU implementasi, dipakai setup.ps1 dan
# doctor.ps1 juga - script ini pernah punya daftar path hardcoded sendiri dan
# gagal total di mesin yang memasang SolidWorks di drive selain C:.
. (Join-Path $PSScriptRoot 'lib\MikosAI.Env.ps1')

$AddinGuid = '{7F2A6C14-9D3B-4E58-B1A7-5C0E9D4F2B63}'

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$AddinProj   = Join-Path $RepoRoot 'src\MikosAI.Addin\MikosAI.Addin.csproj'
$AddinDll    = Join-Path $RepoRoot "src\MikosAI.Addin\bin\$Configuration\net48\MikosAI.Addin.dll"
$AddinTlb    = [System.IO.Path]::ChangeExtension($AddinDll, '.tlb')
$StatusFile  = Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json'
$RegAsm      = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'

function Write-Section($text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

function Test-Administrator {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-RegAsm {
    <#
        Menjalankan RegAsm dan mengembalikan keluarannya sebagai string biasa.

        JANGAN biarkan 2>&1 pada executable native menjadi error yang
        menghentikan. Di PowerShell 5.1, meredirect stderr sebuah exe membungkus
        tiap baris jadi ErrorRecord (NativeCommandError). Dengan
        $ErrorActionPreference='Stop', warning RegAsm yang TIDAK BERBAHAYA ikut
        menghentikan script - padahal RegAsm keluar dengan kode 0 dan
        registrasinya berhasil.

        Keberhasilan dinilai HANYA dari $LASTEXITCODE.
    #>
    param([string[]] $Argumen)

    $eapSebelumnya = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $baris = & $RegAsm @Argumen 2>&1 | ForEach-Object { $_.ToString() }
        $kode  = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $eapSebelumnya
    }

    foreach ($b in $baris) {
        if ($b -match 'warning RA0000') {
            Write-Host "  (warning wajar, registrasi tetap jalan)" -ForegroundColor DarkGray
            Write-Host "  $b" -ForegroundColor DarkGray
        }
        else {
            Write-Host "  $b"
        }
    }

    if ($kode -ne 0) {
        if (($baris -join "`n") -match 'SolidWorks\.Interop') {
            Write-Host ''
            Write-Host 'RegAsm tidak bisa memuat PIA SolidWorks.' -ForegroundColor Red
            Write-Host 'Bangun ulang: powershell -File scripts\setup.ps1' -ForegroundColor Yellow
            Write-Host 'Atau jalur mundur: powershell -File scripts\setup.ps1 -NoEmbedPia' -ForegroundColor Yellow
        }
        throw "RegAsm gagal dengan exit code $kode."
    }

    return $baris
}

function Resolve-SolidWorksApiDir {
    <#
        Mendelegasikan ke Get-SolidWorksApiDir di lib/MikosAI.Env.ps1.

        Versi sebelumnya punya daftar path hardcoded sendiri, hanya
        C:\Program Files - jadi gagal total di mesin yang memasang SolidWorks di
        drive lain. Dua implementasi deteksi untuk satu kebutuhan, satu benar
        satu salah; itulah yang terjadi.

        Deteksi berbasis registry di lib adalah satu-satunya yang dipakai.
    #>
    if ($SolidWorksApiDir) {
        if (-not (Test-Path -LiteralPath $SolidWorksApiDir)) {
            throw "SolidWorksApiDir tidak ada: $SolidWorksApiDir"
        }
        return $SolidWorksApiDir
    }

    $terdeteksi = Get-SolidWorksApiDir

    if ($terdeteksi) {
        Write-Host "SolidWorksApiDir tidak diberikan, memakai hasil deteksi:" -ForegroundColor Yellow
        Write-Host "  $terdeteksi" -ForegroundColor Yellow
        return $terdeteksi
    }

    throw @"
SolidWorks tidak terdeteksi.

Jalankan scripts\doctor.ps1 untuk melihat detailnya, atau berikan path eksplisit:
  -SolidWorksApiDir '<folder yang memuat SolidWorks.Interop.sldworks.dll>'
"@
}


# ====================================================================
# CompileCheck - verifikasi tipe tanpa SolidWorks
# ====================================================================
if ($Action -eq 'CompileCheck') {
    Write-Section 'Compile-check add-in (stub ISwAddin, tanpa PIA)'
    Write-Host 'Ini hanya pemeriksaan tipe. Output di bin\compilecheck TIDAK boleh dipakai.'
    Write-Host ''

    & dotnet build $AddinProj -c $Configuration -p:UsePiaStub=true --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Compile-check gagal dengan exit code $LASTEXITCODE. Ini murni masalah kode, bukan lingkungan."
    }

    Write-Host ''
    Write-Host 'Kode add-in benar secara tipe.' -ForegroundColor Green
    Write-Host 'Build nyata tetap butuh PIA: -Action Build'
    exit 0
}

# ====================================================================
# Build
# ====================================================================
if ($Action -eq 'Build') {
    $apiDir = Resolve-SolidWorksApiDir

    # SolidWorks mengunci DLL yang sudah dimuat. Diperiksa lebih dulu supaya
    # pesannya jelas, bukan terkubur di belasan baris retry MSBuild.
    $sw = Test-SolidWorksRunning
    if (-not $sw.Lulus) {
        throw @"
SolidWorks sedang berjalan ($($sw.Nilai)).

Ia mengunci MikosAI.Addin.dll dan MikosAI.Core.dll yang sudah dimuat, jadi build
akan gagal dengan "file is locked by SolidWorks".

Tutup SolidWorks sepenuhnya, lalu jalankan ulang perintah ini.
"@
    }

    Write-Section "Build add-in ($Configuration, net48, x64)"
    Write-Host "PIA: $apiDir"

    $embed = 'true'
    if ($NoEmbedPia) { $embed = 'false' }

    & dotnet build $AddinProj -c $Configuration -p:SolidWorksApiDir="$apiDir" -p:EmbedPia=$embed --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build gagal dengan exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $AddinDll)) {
        throw "Build melaporkan sukses tapi $AddinDll tidak ada."
    }

    Write-Host ''
    Write-Host "Output: $AddinDll" -ForegroundColor Green
    Write-Host "Berikutnya: -Action Register -RegistrationScope Machine (butuh administrator)."
    exit 0
}

# ====================================================================
# Register
# ====================================================================
if ($Action -eq 'Register') {
    if (-not (Test-Path -LiteralPath $AddinDll)) {
        throw "Assembly belum ada: $AddinDll`nJalankan -Action Build lebih dulu."
    }

    if (-not (Test-Path -LiteralPath $RegAsm)) {
        throw "RegAsm tidak ditemukan: $RegAsm`nPastikan .NET Framework 4.x terpasang."
    }

    if ($RegistrationScope -eq 'Machine' -and -not (Test-Administrator)) {
        throw @"
Registrasi Machine butuh hak administrator - ia menulis HKLM.

Buka PowerShell sebagai administrator, lalu jalankan ulang perintah yang sama.
"@
    }

    Write-Section "Registrasi ($RegistrationScope)"

    if ($RegistrationScope -eq 'Machine') {
        # /codebase supaya CLR tahu lokasi DLL tanpa GAC.
        # /tlb WAJIB. Tanpa TLB, SolidWorks bisa memuat DLL dan mengembalikan
        # LoadAddIn=0, lalu TIDAK PERNAH menginstansiasi ISwAddin.
        $baris = Invoke-RegAsm @($AddinDll, '/codebase', "/tlb:$AddinTlb")
    }
    else {
        Write-Host "CurrentUser hanya membuktikan aktivasi CLR/COM." -ForegroundColor Yellow
        Write-Host "SolidWorks TIDAK akan memuat add-in dengan scope ini." -ForegroundColor Yellow
        $baris = Invoke-RegAsm @($AddinDll, '/codebase')
    }

    Write-Host ''
    Write-Host 'Terdaftar.' -ForegroundColor Green
    Write-Host 'Berikutnya: buka SolidWorks, lalu jalankan -Action Probe.'
    exit 0
}

# ====================================================================
# Unregister
# ====================================================================
if ($Action -eq 'Unregister') {
    if (-not (Test-Path -LiteralPath $AddinDll)) {
        throw @"
Assembly tidak ada: $AddinDll

Unregister harus memakai path assembly yang SAMA dengan saat register.
Kalau assembly sudah terhapus, bersihkan registry manual:
  HKLM\SOFTWARE\SolidWorks\Addins\$AddinGuid
  HKCU\SOFTWARE\SolidWorks\AddInsStartup\$AddinGuid
"@
    }

    if ($RegistrationScope -eq 'Machine' -and -not (Test-Administrator)) {
        throw 'Unregister scope Machine butuh hak administrator.'
    }

    Write-Section 'Lepas registrasi'

    $baris = Invoke-RegAsm @($AddinDll, '/unregister', "/tlb:$AddinTlb")
    Write-Host ''
    Write-Host 'Selesai.' -ForegroundColor Green
    Write-Host 'Tutup SolidWorks sebelum rebuild - SolidWorks mengunci DLL yang sudah dimuat.'
    exit 0
}

# ====================================================================
# Status
# ====================================================================
if ($Action -eq 'Status') {
    Write-Section 'Registry'

    $hklm = "HKLM:\SOFTWARE\SolidWorks\Addins\$AddinGuid"
    $hkcu = "HKCU:\SOFTWARE\SolidWorks\AddInsStartup\$AddinGuid"

    foreach ($path in @($hklm, $hkcu)) {
        if (Test-Path -LiteralPath $path) {
            Write-Host "ADA    $path" -ForegroundColor Green
        }
        else {
            Write-Host "TIDAK  $path" -ForegroundColor Yellow
        }
    }

    Write-Section 'Artefak'
    foreach ($path in @($AddinDll, $AddinTlb, $StatusFile)) {
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path
            Write-Host ("ADA    {0}  ({1} byte, {2})" -f $path, $item.Length, $item.LastWriteTime) -ForegroundColor Green
        }
        else {
            Write-Host "TIDAK  $path" -ForegroundColor Yellow
        }
    }

    Write-Section 'Proses SolidWorks'
    $processes = Get-Process -Name 'SLDWORKS' -ErrorAction SilentlyContinue
    if ($processes) {
        $processes | ForEach-Object { Write-Host "Berjalan, PID $($_.Id)" }
    }
    else {
        Write-Host 'Tidak berjalan.'
    }

    exit 0
}

# ====================================================================
# Probe - gerbang penerimaan Fase 0
# ====================================================================
if ($Action -eq 'Probe') {
    Write-Section 'Probe diagnostik add-in'

    if (-not (Test-Path -LiteralPath $StatusFile)) {
        Write-Host "host-status.json tidak ada: $StatusFile" -ForegroundColor Red
        Write-Host ''
        Write-Host 'Artinya add-in belum pernah menjalankan ConnectToSW.' -ForegroundColor Red
        Write-Host ''
        Write-Host 'Periksa berurutan:' -ForegroundColor Yellow
        Write-Host '  1. Sudah buka SolidWorks SETELAH registrasi?'
        Write-Host '  2. MikosAI MUNCUL di Tools > Add-Ins? Kalau tidak muncul sama sekali,'
        Write-Host '     masalahnya registrasi atau TLB - bukan kode add-in.'
        Write-Host '  3. Sudah dicentang di situ?'

        # Dependensi yang hilang adalah penyebab paling membingungkan: SolidWorks
        # melaporkan sukses, add-in tidak pernah terinstansiasi, tidak ada pesan
        # apa pun. Jadi diperiksa di sini, bukan dibiarkan jadi misteri.
        $folder = Split-Path -Parent $AddinDll
        if (Test-Path -LiteralPath $folder) {
            Write-Host ''
            Write-Host 'Dependensi di folder add-in:' -ForegroundColor Yellow

            $wajib = @('MikosAI.Addin.dll', 'MikosAI.Core.dll', 'System.Text.Json.dll', 'YamlDotNet.dll')
            $hilang = 0

            foreach ($nama in $wajib) {
                $jalur = Join-Path $folder $nama
                if (Test-Path -LiteralPath $jalur) {
                    Write-Host "  ADA    $nama" -ForegroundColor Green
                }
                else {
                    Write-Host "  TIDAK  $nama" -ForegroundColor Red
                    $hilang++
                }
            }

            if ($hilang -gt 0) {
                Write-Host ''
                Write-Host "$hilang dependensi hilang dari folder output." -ForegroundColor Red
                Write-Host 'Bangun ulang: powershell -File scripts\setup.ps1'
            }

            $dll = @(Get-ChildItem -LiteralPath $folder -Filter *.dll -ErrorAction SilentlyContinue)
            Write-Host ''
            Write-Host "  Total $($dll.Count) DLL di folder output."
        }

        Write-Host ''
        Write-Host 'Jalankan -Action Status untuk melihat apa yang sudah terdaftar.'
        exit 1
    }

    $status = Get-Content -LiteralPath $StatusFile -Raw | ConvertFrom-Json

    Write-Host "Berkas   : $StatusFile"
    Write-Host "Ditulis  : $($status.writtenAt)"
    Write-Host "Status   : $($status.status)"
    Write-Host "Revisi SW: $($status.solidworksRevision)"
    Write-Host "Assembly : $($status.addinAssembly)"
    Write-Host "PID      : $($status.processId)"

    # Deteksi DLL usang. Dua kali kami mendiagnosis error runtime dari kode yang
    # sudah diperbaiki, karena yang dimuat SolidWorks ternyata build lama.
    $commitDll = $null
    if ($status.PSObject.Properties.Name -contains 'buildCommit') {
        $commitDll = $status.buildCommit
    }

    $commitRepo = $null
    try {
        $commitRepo = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
        if ($commitRepo) { $commitRepo = $commitRepo.Trim() }
    }
    catch {
        $commitRepo = $null
    }

    Write-Host "Commit   : $commitDll (build DLL)"

    if ($status.PSObject.Properties.Name -contains 'featureFlags' -and $status.featureFlags) {
        Write-Host "Fitur    : $($status.featureFlags)"
    }

    # Petunjuk terkuat saat SolidWorks mati: langkah yang sedang BERJALAN,
    # bukan yang terakhir selesai.
    if ($status.PSObject.Properties.Name -contains 'lastActivity' -and $status.lastActivity) {
        $warnaJejak = 'Gray'
        if ($status.lastActivity -ne 'connect:selesai' -and $status.lastActivity -ne 'latar:selesai') {
            $warnaJejak = 'Yellow'
        }
        Write-Host "Aktivitas terakhir: $($status.lastActivity)" -ForegroundColor $warnaJejak
    }

    if ($commitDll -and $commitRepo) {
        if ($commitDll -eq $commitRepo) {
            Write-Host "           sama dengan HEAD repo - DLL ini mutakhir" -ForegroundColor Green
        }
        else {
            Write-Host ''
            Write-Host 'DLL YANG BERJALAN BUKAN KODE TERBARU' -ForegroundColor Red
            Write-Host '------------------------------------'
            Write-Host "  DLL dibangun dari : $commitDll"
            Write-Host "  HEAD repo sekarang: $commitRepo"
            Write-Host ''
            Write-Host '  Hasil di bawah TIDAK mencerminkan kode terbaru.' -ForegroundColor Red
            Write-Host '  Tutup SolidWorks, lalu:' -ForegroundColor Yellow
            Write-Host '    git pull origin main'
            Write-Host '    powershell -File scripts\setup.ps1'
            Write-Host '  lalu buka SolidWorks dan probe ulang.'
            Write-Host ''
        }
    }
    elseif (-not $commitDll) {
        Write-Host '           (DLL dibangun sebelum cap commit ada - rebuild untuk mengaktifkannya)' -ForegroundColor Yellow
    }

    Write-Section 'Gerbang penerimaan'

    $gates = @(
        @{ Nama = 'status == connected';        Lulus = ($status.status -eq 'connected') }
        @{ Nama = 'callbackRegistered';          Lulus = [bool]$status.callbackRegistered }
        @{ Nama = 'dispatcherReady';             Lulus = [bool]$status.dispatcherReady }
        @{ Nama = 'addinExposesIDispatch';       Lulus = [bool]$status.addinExposesIDispatch }
        @{ Nama = 'mainThreadId terisi';         Lulus = ($status.mainThreadId -gt 0) }
        @{ Nama = 'revisi SolidWorks terbaca';   Lulus = -not [string]::IsNullOrWhiteSpace($status.solidworksRevision) }
        @{ Nama = 'errors kosong';               Lulus = ($null -eq $status.errors -or $status.errors.Count -eq 0) }
    )

    $gagal = 0
    foreach ($gate in $gates) {
        if ($gate.Lulus) {
            Write-Host "  LULUS  $($gate.Nama)" -ForegroundColor Green
        }
        else {
            Write-Host "  GAGAL  $($gate.Nama)" -ForegroundColor Red
            $gagal++
        }
    }

    if ($status.errors -and $status.errors.Count -gt 0) {
        Write-Section 'Error yang tercatat'
        foreach ($err in $status.errors) {
            Write-Host "  $err" -ForegroundColor Red
        }
    }

    if ($status.PSObject.Properties.Name -contains 'assemblyResolveLog' -and
        $status.assemblyResolveLog -and $status.assemblyResolveLog.Count -gt 0) {
        Write-Section 'Resolusi dependensi'
        Write-Host '  Dependensi add-in yang dimuat lewat handler kita, bukan oleh CLR.'
        Write-Host '  Itu wajar: /codebase membuat CLR mencari dependensi di folder'
        Write-Host '  SLDWORKS.exe, bukan folder kita.'
        Write-Host ''
        foreach ($baris in $status.assemblyResolveLog) {
            $warna = 'Gray'
            if ($baris -match '^TIDAK ADA|^GAGAL') { $warna = 'Red' }
            Write-Host "  $baris" -ForegroundColor $warna
        }
    }

    Write-Section 'Belum diuji di tahap ini'
    Write-Host '  commandGroup, taskPane, mcpServer - belum diimplementasikan, wajar false.'
    Write-Host '  Hitungan event file_new - butuh langganan event, menyusul.'

    Write-Host ''
    if ($gagal -eq 0) {
        Write-Host 'Add-in hidup. Fondasi Tahap 1 terbukti.' -ForegroundColor Green
        exit 0
    }

    Write-Host "$gagal gerbang gagal. Jangan lanjut ke Fase 1 sebelum ini bersih." -ForegroundColor Red
    exit 1
}
