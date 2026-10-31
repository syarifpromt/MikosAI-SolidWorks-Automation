<#
.SYNOPSIS
    Membuat berkas .snk tanpa sn.exe.

.DESCRIPTION
    sn.exe ikut Windows SDK atau Visual Studio. Di mesin yang hanya punya
    .NET Framework dan dotnet SDK, berkas itu tidak ada.

    Berkas .snk sebenarnya hanyalah CSP private key blob mentah, dan itu bisa
    dibuat lewat API .NET langsung.

    Penandatanganan strong name hanya menstabilkan identitas assembly. Ini
    BUKAN code signing Authenticode - distribusi tetap butuh penandatanganan
    di pipeline rilis.

.PARAMETER Path
    Berkas .snk yang akan dibuat.

.PARAMETER KeySize
    Ukuran kunci RSA. Default 2048. sn.exe secara historis memakai 1024.

.PARAMETER Force
    Timpa berkas yang sudah ada.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\New-StrongNameKey.ps1 `
      -Path src\MikosAI.Addin\MikosAI.Addin.snk
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [ValidateSet(1024, 2048, 4096)]
    [int] $KeySize = 2048,

    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$resolved = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::Combine((Get-Location).Path, $Path))

if ((Test-Path -LiteralPath $resolved) -and -not $Force) {
    Write-Host "Berkas sudah ada, dibiarkan: $resolved"
    Write-Host "Pakai -Force kalau memang mau diganti."
    Write-Host ""
    Write-Host "PERINGATAN: mengganti kunci mengubah identitas assembly."
    Write-Host "Add-in yang sudah terdaftar harus di-unregister lebih dulu."
    exit 0
}

$directory = [System.IO.Path]::GetDirectoryName($resolved)
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

# RSACryptoServiceProvider memakai CAPI, jadi ExportCspBlob tersedia.
# RSACng tidak punya method itu.
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider($KeySize)
try {
    $blob = $rsa.ExportCspBlob($true)
    [System.IO.File]::WriteAllBytes($resolved, $blob)
}
finally {
    $rsa.Dispose()
}

$info = Get-Item -LiteralPath $resolved

Write-Host "Kunci strong name dibuat."
Write-Host "  Berkas : $resolved"
Write-Host "  Ukuran : $($info.Length) byte (RSA $KeySize)"
Write-Host ""
Write-Host "JANGAN commit berkas ini - .gitignore sudah mengecualikan *.snk."
Write-Host "Setiap pengembang membuat kuncinya sendiri untuk pengembangan lokal."
Write-Host "Rilis resmi memakai satu kunci yang disimpan terpisah."
