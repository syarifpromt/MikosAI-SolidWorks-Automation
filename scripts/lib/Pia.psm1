<#
    Refleksi tanda tangan PIA SolidWorks.

    KENAPA MODUL INI ADA.

    Dua kesalahan kontrak lahir dari hal yang sama: BUKAN dari refleksinya,
    melainkan dari cara keluarannya dicetak.

      1. CreateCommandGroup2 - parameter `Errors` dicetak sebagai `out` karena
         IsOut true, padahal IsIn JUGA true. [In][Out] menuntut `ref` di C#.
         Stub memakai `out`, compile-check lulus, build nyata memberi CS1620.

      2. GetNextDisplayDimension - dicetak sebagai `()` karena skripnya
         meng-hardcode tanda kurung kosong. Parameter `Object DispIn` tidak
         pernah terlihat. Stub tanpa parameter, compile-check lulus, build
         nyata memberi CS7036.

    Keduanya lolos compile-check lalu ditangkap build nyata - yaitu mode
    kegagalan stub yang paling berbahaya, karena ia memberi keyakinan palsu
    tepat di lapisan yang dibuat untuk memberi keyakinan.

    Menambal kasus per kasus akan menyisakan kesalahan ketiga. Jadi yang
    diperbaiki FORMATNYA: satu fungsi yang mencetak tanda tangan LENGKAP -
    return type, setiap parameter, tipenya, dan modifier ref/out yang
    ditentukan dari IsIn DAN IsOut, bukan dari IsOut saja.

    Diuji terhadap kedua kasus di atas oleh scripts/uji-pia.ps1.
#>

Set-StrictMode -Version Latest

function Get-PiaPath {
    <#
        Menemukan PIA dari registry, bukan dari path yang di-hardcode.
        Mesin pengguna memasang SolidWorks di drive yang berbeda-beda.
    #>
    [CmdletBinding()]
    param()

    $clsid = (Get-ItemProperty 'HKLM:\SOFTWARE\Classes\SldWorks.Application\CLSID' -ErrorAction SilentlyContinue).'(default)'
    if (-not $clsid) { return $null }

    $ls = (Get-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid\LocalServer32" -ErrorAction SilentlyContinue).'(default)'
    if (-not $ls) { return $null }

    $exe = ($ls -replace '"', '').Trim()
    $pia = Join-Path (Split-Path $exe -Parent) 'api\redist\SolidWorks.Interop.sldworks.dll'

    if (Test-Path $pia) { return $pia }
    return $null
}

function Get-ModifierParameter {
    <#
        Modifier C# untuk sebuah parameter.

        INI INTI DARI SELURUH MODUL.

        IsOut saja TIDAK CUKUP. Parameter [In][Out] melaporkan IsOut = true,
        tapi C# menuntut `ref` - bukan `out`. `out` hanya benar kalau
        parameternya keluar SAJA.

        Urutan pemeriksaan di bawah sengaja eksplisit, bukan diringkas jadi
        satu ekspresi, supaya alasannya terbaca di tempat ia berlaku.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Parameter)

    if (-not $Parameter.ParameterType.IsByRef) { return '' }

    if ($Parameter.IsOut -and -not $Parameter.IsIn) { return 'out' }

    # Termasuk [In][Out] dan ByRef tanpa atribut sama sekali.
    return 'ref'
}

function Format-TandaTangan {
    <#
        Tanda tangan LENGKAP sebuah method, dalam bentuk yang bisa dibandingkan
        apa adanya.

        Tidak ada bagian yang di-hardcode. Tanda kurung selalu diisi dari
        daftar parameter yang sebenarnya - termasuk ketika daftarnya memang
        kosong, yang justru menjadi informasi alih-alih asumsi.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [System.Reflection.MethodInfo] $Method)

    $bagian = @()

    foreach ($p in $Method.GetParameters()) {
        $mod = Get-ModifierParameter -Parameter $p
        $tipe = $p.ParameterType.ToString().TrimEnd('&').Replace('System.', '')

        if ($mod) { $bagian += "$mod $tipe $($p.Name)" }
        else { $bagian += "$tipe $($p.Name)" }
    }

    $ret = $Method.ReturnType.ToString().Replace('System.', '')

    return "$ret $($Method.Name)($($bagian -join ', '))"
}

function Get-TandaTanganPia {
    <#
        Tanda tangan setiap method sebuah interface PIA yang cocok dengan pola.

        .EXAMPLE
            Get-TandaTanganPia -Interface ICommandManager -Pola 'CreateCommandGroup2'
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Interface,
        [string] $Pola = '.',
        [string] $PiaPath,
        [string] $Namespace = 'SolidWorks.Interop.sldworks'
    )

    if (-not $PiaPath) { $PiaPath = Get-PiaPath }
    if (-not $PiaPath) { throw 'PIA SolidWorks tidak ditemukan lewat registry.' }

    $asm = [System.Reflection.Assembly]::LoadFrom($PiaPath)
    $tipe = $asm.GetType("$Namespace.$Interface")

    if (-not $tipe) { throw "Interface tidak ada di PIA: $Namespace.$Interface" }

    $hasil = @()
    foreach ($m in $tipe.GetMethods()) {
        if ($m.Name -notmatch $Pola) { continue }
        $hasil += Format-TandaTangan -Method $m
    }

    return $hasil
}

Export-ModuleMember -Function Get-PiaPath, Get-ModifierParameter, Format-TandaTangan, Get-TandaTanganPia
