<#
    Deteksi lingkungan bersama, dipakai setup.ps1, doctor.ps1, dan addin.ps1.

    Setiap fungsi Test-* mengembalikan objek dengan bentuk sama:

        Nama      nama pemeriksaan
        Lulus     $true / $false
        Nilai     apa yang ditemukan
        Blocker   $true kalau setup tidak mungkin lanjut tanpa ini
        Saran     apa yang harus dilakukan manusia kalau gagal

    Bentuk seragam itu membuat doctor bisa mencetak tabel dan setup bisa
    memutuskan berhenti, tanpa logika khusus per pemeriksaan.
#>

Set-StrictMode -Version Latest

$script:AddinGuid = '{7F2A6C14-9D3B-4E58-B1A7-5C0E9D4F2B63}'

function New-CheckResult {
    param(
        [string] $Nama,
        [bool]   $Lulus,
        [string] $Nilai = '',
        [bool]   $Blocker = $false,
        [string] $Saran = ''
    )

    return [pscustomobject]@{
        Nama    = $Nama
        Lulus   = $Lulus
        Nilai   = $Nilai
        Blocker = $Blocker
        Saran   = $Saran
    }
}

function Test-WindowsVersion {
    # ConPTY butuh Windows 10 1809 (build 17763). ProviderHost akan
    # memerlukannya di Fase 2 untuk mengatasi bug stdout non-TTY pada agy.
    $build = [int][System.Environment]::OSVersion.Version.Build
    $lulus = $build -ge 17763

    $saran = ''
    if (-not $lulus) {
        $saran = 'ConPTY butuh Windows 10 1809+ (build 17763). Perbarui Windows.'
    }

    return New-CheckResult -Nama 'Windows 10 1809+' -Lulus $lulus `
        -Nilai "build $build" -Blocker $false -Saran $saran
}

function Test-DotNetFramework48 {
    $lulus   = $false
    $release = 0

    try {
        $key = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop
        $release = [int] $key.Release
        # 528040 = .NET Framework 4.8
        $lulus = $release -ge 528040
    }
    catch {
        $lulus = $false
    }

    $saran = ''
    if (-not $lulus) {
        $saran = 'Pasang .NET Framework 4.8 Developer Pack dari situs Microsoft.'
    }

    return New-CheckResult -Nama '.NET Framework 4.8' -Lulus $lulus `
        -Nilai "Release $release" -Blocker $true -Saran $saran
}

function Test-DotNetSdk {
    $versi = $null
    try {
        $versi = (& dotnet --version 2>$null)
    }
    catch {
        $versi = $null
    }

    $lulus = -not [string]::IsNullOrWhiteSpace($versi)

    $saran = ''
    if (-not $lulus) {
        $saran = 'Pasang .NET SDK dari https://dotnet.microsoft.com/download lalu buka terminal baru.'
    }

    $nilai = 'tidak ditemukan'
    if ($lulus) { $nilai = $versi.Trim() }

    return New-CheckResult -Nama 'dotnet SDK' -Lulus $lulus `
        -Nilai $nilai -Blocker $true -Saran $saran
}

function Get-SolidWorksApiDir {
    <#
        Mencari direktori api\redist. Dicoba berurutan dari yang paling andal:

          1. Registry CLSID SldWorks.Application -> LocalServer32 -> exe
          2. Registry Setup per versi -> "SolidWorks Folder"
          3. Path umum

        Mengembalikan $null kalau tidak ditemukan.
    #>

    $kandidat = New-Object System.Collections.Generic.List[string]

    # 1. Dari ProgID. Paling andal karena tidak bergantung nomor versi.
    try {
        $clsid = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Classes\SldWorks.Application\CLSID' -ErrorAction Stop).'(default)'
        if ($clsid) {
            $server = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Classes\CLSID\$clsid\LocalServer32" -ErrorAction Stop).'(default)'
            if ($server) {
                $exe = $server.Trim('"')
                $dir = [System.IO.Path]::GetDirectoryName($exe)
                if ($dir) {
                    $kandidat.Add((Join-Path $dir 'api\redist'))
                }
            }
        }
    }
    catch {
        # Lanjut ke cara berikutnya.
    }

    # 2. Kunci Setup per versi.
    try {
        $root = 'HKLM:\SOFTWARE\SolidWorks'
        if (Test-Path -LiteralPath $root) {
            foreach ($child in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
                $setup = Join-Path $child.PSPath 'Setup'
                if (Test-Path -LiteralPath $setup) {
                    try {
                        $folder = (Get-ItemProperty -LiteralPath $setup -ErrorAction Stop).'SolidWorks Folder'
                        if ($folder) {
                            $kandidat.Add((Join-Path $folder 'api\redist'))
                        }
                    }
                    catch {
                        # Kunci ini tidak punya nilai itu.
                    }
                }
            }
        }
    }
    catch {
        # Lanjut.
    }

    # 3. Path umum, termasuk variasi kapitalisasi lama.
    $kandidat.Add('C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist')
    $kandidat.Add('C:\Program Files\SolidWorks Corp\SolidWorks\api\redist')

    foreach ($path in $kandidat) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $probe = Join-Path $path 'SolidWorks.Interop.swpublished.dll'
        if (Test-Path -LiteralPath $probe) {
            return (Resolve-Path -LiteralPath $path).Path
        }
    }

    return $null
}

function Test-SolidWorksPia {
    $dir   = Get-SolidWorksApiDir
    $lulus = $null -ne $dir

    $saran = ''
    if (-not $lulus) {
        $saran = @'
SolidWorks tidak terdeteksi, atau folder api\redist tidak ada.

Kalau SolidWorks terpasang, cari sendiri folder yang memuat
SolidWorks.Interop.swpublished.dll lalu berikan lewat -SolidWorksApiDir.

Tanpa ini, add-in tidak bisa dibangun - tapi MikosAI.Core dan seluruh
testnya tetap jalan, jadi pengembangan Jalur Platform tidak terhalang.
'@
    }

    $nilai = 'tidak ditemukan'
    if ($lulus) { $nilai = $dir }

    return New-CheckResult -Nama 'PIA SolidWorks' -Lulus $lulus `
        -Nilai $nilai -Blocker $false -Saran $saran
}

function Test-WebView2Runtime {
    # TaskPane akan memakai WebView2 di tahap berikutnya.
    $guid  = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    $paths = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$guid"
        "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\$guid"
        "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\$guid"
    )

    $versi = $null
    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            try {
                $versi = (Get-ItemProperty -LiteralPath $path -ErrorAction Stop).pv
                if ($versi) { break }
            }
            catch {
                # Coba path berikutnya.
            }
        }
    }

    $lulus = -not [string]::IsNullOrWhiteSpace($versi)

    $saran = ''
    if (-not $lulus) {
        $saran = 'Pasang WebView2 Evergreen Runtime dari Microsoft. Belum wajib di Tahap 1.'
    }

    $nilai = 'tidak ditemukan'
    if ($lulus) { $nilai = $versi }

    return New-CheckResult -Nama 'WebView2 Runtime' -Lulus $lulus `
        -Nilai $nilai -Blocker $false -Saran $saran
}

function Test-AntigravityCli {
    $versi = $null
    try {
        $cmd = Get-Command 'agy' -ErrorAction Stop
        if ($cmd) {
            $versi = (& agy --version 2>$null | Select-Object -First 1)
        }
    }
    catch {
        $versi = $null
    }

    $lulus = -not [string]::IsNullOrWhiteSpace($versi)

    $saran = ''
    if (-not $lulus) {
        $saran = 'Pasang Antigravity CLI (agy) lalu login sekali secara interaktif. Belum wajib di Tahap 1.'
    }

    $nilai = 'tidak ditemukan'
    if ($lulus) { $nilai = $versi.Trim() }

    return New-CheckResult -Nama 'Antigravity CLI (agy)' -Lulus $lulus `
        -Nilai $nilai -Blocker $false -Saran $saran
}

function Test-AddinRegistered {
    $hklm  = "HKLM:\SOFTWARE\SolidWorks\Addins\$script:AddinGuid"
    $lulus = Test-Path -LiteralPath $hklm

    $saran = ''
    if (-not $lulus) {
        $saran = 'Jalankan setup.ps1, atau addin.ps1 -Action Register (butuh administrator).'
    }

    $nilai = 'belum terdaftar'
    if ($lulus) { $nilai = 'terdaftar (HKLM)' }

    return New-CheckResult -Nama 'Registrasi add-in' -Lulus $lulus `
        -Nilai $nilai -Blocker $false -Saran $saran
}

function Test-AddinConnected {
    $statusFile = Join-Path $env:LOCALAPPDATA 'MikosAI\host-status.json'

    if (-not (Test-Path -LiteralPath $statusFile)) {
        return New-CheckResult -Nama 'Add-in pernah connect' -Lulus $false `
            -Nilai 'host-status.json belum ada' -Blocker $false `
            -Saran 'Buka SolidWorks dan centang MikosAI di Tools > Add-Ins, lalu jalankan addin.ps1 -Action Probe.'
    }

    try {
        $status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
        $lulus  = ($status.status -eq 'connected') -or ($status.status -eq 'disconnected')

        return New-CheckResult -Nama 'Add-in pernah connect' -Lulus $lulus `
            -Nilai "status=$($status.status), SW=$($status.solidworksRevision)" -Blocker $false `
            -Saran 'Jalankan addin.ps1 -Action Probe untuk detailnya.'
    }
    catch {
        return New-CheckResult -Nama 'Add-in pernah connect' -Lulus $false `
            -Nilai 'host-status.json tidak bisa dibaca' -Blocker $false `
            -Saran 'Hapus berkas itu lalu buka ulang SolidWorks.'
    }
}

function Test-IsAdministrator {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $lulus     = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    $nilai = 'pengguna biasa'
    if ($lulus) { $nilai = 'administrator' }

    return New-CheckResult -Nama 'Hak administrator' -Lulus $lulus `
        -Nilai $nilai -Blocker $false `
        -Saran 'Registrasi add-in butuh administrator. setup.ps1 akan meminta UAC sekali.'
}

function Get-AllChecks {
    return @(
        (Test-DotNetFramework48)
        (Test-DotNetSdk)
        (Test-WindowsVersion)
        (Test-SolidWorksPia)
        (Test-WebView2Runtime)
        (Test-AntigravityCli)
        (Test-AddinRegistered)
        (Test-AddinConnected)
    )
}

function Write-CheckTable {
    param([object[]] $Checks)

    foreach ($check in $Checks) {
        $tanda = 'GAGAL'
        $warna = 'Yellow'

        if ($check.Lulus) {
            $tanda = 'OK   '
            $warna = 'Green'
        }
        elseif ($check.Blocker) {
            $tanda = 'BLOK '
            $warna = 'Red'
        }

        Write-Host ("  [{0}] {1,-24} {2}" -f $tanda, $check.Nama, $check.Nilai) -ForegroundColor $warna
    }
}
