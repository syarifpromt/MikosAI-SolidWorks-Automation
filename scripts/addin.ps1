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

function Resolve-SolidWorksApiDir {
    if ($SolidWorksApiDir) {
        if (-not (Test-Path -LiteralPath $SolidWorksApiDir)) {
            throw "SolidWorksApiDir tidak ada: $SolidWorksApiDir"
        }
        return $SolidWorksApiDir
    }

    # Tebak dari lokasi umum, tapi TETAP laporkan apa yang dipakai - merujuk
    # PIA dari versi SolidWorks yang salah menghasilkan kegagalan yang
    # membingungkan.
    $candidates = @(
        'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'
        'C:\Program Files\SolidWorks Corp\SolidWorks\api\redist'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'SolidWorks.Interop.sldworks.dll')) {
            Write-Host "SolidWorksApiDir tidak diberikan, memakai hasil deteksi:" -ForegroundColor Yellow
            Write-Host "  $candidate" -ForegroundColor Yellow
            return $candidate
        }
    }

    throw @"
SolidWorksApiDir tidak diberikan dan tidak terdeteksi otomatis.

Berikan eksplisit:
  -SolidWorksApiDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'

Direktori itu harus memuat SolidWorks.Interop.sldworks.dll.
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
        $keluaran = & $RegAsm $AddinDll /codebase /tlb:$AddinTlb 2>&1
        $keluaran | ForEach-Object { Write-Host $_ }

        if (($keluaran | Out-String) -match 'SolidWorks\.Interop') {
            Write-Host ''
            Write-Host 'RegAsm tidak bisa memuat PIA SolidWorks.' -ForegroundColor Red
            Write-Host 'Bangun ulang: powershell -File scripts\setup.ps1' -ForegroundColor Yellow
            Write-Host 'Atau jalur mundur: powershell -File scripts\setup.ps1 -NoEmbedPia' -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "CurrentUser hanya membuktikan aktivasi CLR/COM." -ForegroundColor Yellow
        Write-Host "SolidWorks TIDAK akan memuat add-in dengan scope ini." -ForegroundColor Yellow
        & $RegAsm $AddinDll /codebase
    }

    if ($LASTEXITCODE -ne 0) {
        throw "RegAsm gagal dengan exit code $LASTEXITCODE."
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

    & $RegAsm $AddinDll /unregister /tlb:$AddinTlb
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
        Write-Host 'Penyebab paling sering, urut dari yang paling mungkin:'
        Write-Host '  1. Registrasi Machine belum dilakukan (CurrentUser tidak cukup)'
        Write-Host '  2. TLB belum terdaftar - SolidWorks memuat DLL tapi tidak instansiasi ISwAddin'
        Write-Host '  3. Add-in belum dicentang di SolidWorks: Tools > Add-Ins'
        Write-Host '  4. SolidWorks belum dibuka setelah registrasi'
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

    Write-Section 'Gerbang penerimaan'

    $gates = @(
        @{ Nama = 'status == connected';        Lulus = ($status.status -eq 'connected') }
        @{ Nama = 'callbackRegistered';          Lulus = [bool]$status.callbackRegistered }
        @{ Nama = 'dispatcherReady';             Lulus = [bool]$status.dispatcherReady }
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
