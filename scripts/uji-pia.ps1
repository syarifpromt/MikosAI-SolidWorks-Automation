<#
.SYNOPSIS
    Test untuk scripts/lib/Pia.psm1 - pencetak tanda tangan PIA.

.DESCRIPTION
    Dua kesalahan kontrak lahir dari FORMAT keluaran refleksi, bukan dari
    refleksinya. Modul Pia.psm1 memperbaiki formatnya; berkas ini memastikan
    perbaikan itu benar-benar menangkap keduanya.

    Kedua kasus dipilih karena BUKAN hipotetis - masing-masing pernah lolos
    compile-check lalu ditolak build nyata:

      CreateCommandGroup2  -> `out` padahal [In][Out], CS1620
      GetNextDisplayDimension -> `()` padahal punya parameter, CS7036

    Jalankan dengan -TunjukkanFormatLama untuk melihat bahwa format LAMA
    memang gagal pada kedua kasus ini. Tanpa itu, test ini hijau yang belum
    pernah terlihat merah - dan di repo ini hijau seperti itu tidak dianggap
    bukti.
#>
[CmdletBinding()]
param(
    [switch] $TunjukkanFormatLama
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'lib\Pia.psm1') -Force

$script:lulus = 0
$script:gagal = 0

function Uji([string] $nama, [string] $diharapkan, [string] $diterima) {
    if ($diterima -ceq $diharapkan) {
        $script:lulus++
        Write-Host "  LULUS  $nama" -ForegroundColor Green
        Write-Host "         $diterima" -ForegroundColor DarkGray
    }
    else {
        $script:gagal++
        Write-Host "  GAGAL  $nama" -ForegroundColor Red
        Write-Host "         diharapkan : $diharapkan" -ForegroundColor Red
        Write-Host "         diterima   : $diterima" -ForegroundColor Red
    }
}

# Format LAMA, direproduksi apa adanya dari skrip yang menyesatkan kami.
# Ada di sini SEBAGAI SUBJEK UJI, bukan sebagai alat.
function Format-TandaTanganLama {
    param([System.Reflection.MethodInfo] $Method)

    $bagian = @()
    foreach ($p in $Method.GetParameters()) {
        $mod = ''
        # BUG 1: IsOut saja. [In][Out] jadi `out`, padahal C# menuntut `ref`.
        if ($p.ParameterType.IsByRef -and $p.IsOut) { $mod = 'out ' }
        $bagian += "$mod$($p.ParameterType.ToString().TrimEnd('&').Replace('System.',''))  $($p.Name)"
    }
    return "$($Method.ReturnType.Name) $($Method.Name)($($bagian -join ', '))"
}

$pia = Get-PiaPath
if (-not $pia) {
    Write-Host 'PIA SolidWorks tidak ditemukan - test ini butuh SolidWorks terpasang.' -ForegroundColor Yellow
    exit 2
}

Write-Host ''
Write-Host 'UJI PENCETAK TANDA TANGAN PIA' -ForegroundColor White
Write-Host '============================='
Write-Host "  pia: $pia"
Write-Host ''

$asm = [System.Reflection.Assembly]::LoadFrom($pia)

# --- Kasus 1: [In][Out] harus `ref`, bukan `out` --------------------------
Write-Host 'Kasus 1 - CreateCommandGroup2: [In][Out] menuntut ref' -ForegroundColor Cyan

$m1 = $asm.GetType('SolidWorks.Interop.sldworks.ICommandManager').GetMethod('CreateCommandGroup2')
Uji 'modifier Errors adalah ref' 'ref' (Get-ModifierParameter -Parameter ($m1.GetParameters() | Where-Object { $_.Name -eq 'Errors' }))

# NAMESPACE DIPERTAHANKAN, dan itu disengaja.
#
# Hanya "System." yang dibuang. Namespace SolidWorks tetap dicetak karena ia
# memberi tahu dari mana tipe itu berasal - dan itu menentukan saat menulis
# stub: tipe dari swconst, dari sldworks, dan dari swpublished hidup di
# namespace berbeda, dan memendekkannya akan menyembunyikan bedanya.
#
# Harapan di test ini sempat salah justru karena saya memendekkannya sendiri.
Uji 'tanda tangan lengkap' `
    'SolidWorks.Interop.sldworks.CommandGroup CreateCommandGroup2(Int32 UserID, String Title, String ToolTip, String Hint, Int32 Position, Boolean IgnorePreviousVersion, ref Int32 Errors)' `
    (Format-TandaTangan -Method $m1)

# --- Kasus 2: parameter tidak boleh hilang --------------------------------
Write-Host ''
Write-Host 'Kasus 2 - GetNextDisplayDimension: parameter tidak boleh hilang' -ForegroundColor Cyan

$m2 = $asm.GetType('SolidWorks.Interop.sldworks.IFeature').GetMethod('GetNextDisplayDimension')
Uji 'tanda tangan lengkap' `
    'Object GetNextDisplayDimension(Object DispIn)' `
    (Format-TandaTangan -Method $m2)

# Daftar parameter yang memang kosong harus tetap dicetak kosong - itu
# informasi, bukan asumsi.
$m3 = $asm.GetType('SolidWorks.Interop.sldworks.IFeature').GetMethod('GetFirstDisplayDimension')
Uji 'daftar kosong tetap dicetak kosong' `
    'Object GetFirstDisplayDimension()' `
    (Format-TandaTangan -Method $m3)

# --- Format lama HARUS gagal pada keduanya --------------------------------
if ($TunjukkanFormatLama) {
    Write-Host ''
    Write-Host 'Format LAMA pada kedua kasus yang sama:' -ForegroundColor Yellow
    Write-Host "  $(Format-TandaTanganLama -Method $m1)" -ForegroundColor Yellow
    Write-Host '    ^ Errors dicetak `out` - stub memakai out, build nyata CS1620' -ForegroundColor DarkYellow
    Write-Host ''
    Write-Host '  Dan skrip lama mencetak GetNextDisplayDimension sebagai:' -ForegroundColor Yellow
    Write-Host '    Object GetNextDisplayDimension()' -ForegroundColor Yellow
    Write-Host '    ^ tanda kurung di-hardcode - parameter DispIn tidak pernah terlihat, CS7036' -ForegroundColor DarkYellow

    $lamaSalahRef = (Format-TandaTanganLama -Method $m1) -match 'out Int32 Errors|out Int32  Errors'
    if ($lamaSalahRef) {
        Write-Host ''
        Write-Host '  TERBUKTI: format lama memberi `out` untuk parameter yang menuntut `ref`.' -ForegroundColor Green
    }
    else {
        Write-Host ''
        Write-Host '  TIDAK TERBUKTI - reproduksi format lama tidak lagi salah. Periksa berkas ini.' -ForegroundColor Red
        $script:gagal++
    }
}

Write-Host ''
Write-Host '============================='
Write-Host "Lulus: $script:lulus   Gagal: $script:gagal"

if ($script:gagal -eq 0) {
    Write-Host 'Pencetak tanda tangan PIA benar untuk kedua kasus yang pernah menipu kami.' -ForegroundColor Green
    exit 0
}

exit 1
