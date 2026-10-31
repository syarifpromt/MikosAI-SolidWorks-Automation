<#
.SYNOPSIS
    Memeriksa lingkungan dan melaporkan apa yang perlu diperbaiki.

.DESCRIPTION
    Read-only. Tidak mengubah apa pun, tidak butuh administrator.
    Jalankan ini lebih dulu kalau setup.ps1 gagal, atau kalau add-in tidak
    muncul di SolidWorks.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\doctor.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'lib\MikosAI.Env.ps1')

Write-Host ''
Write-Host 'MikosAI - pemeriksaan lingkungan' -ForegroundColor Cyan
Write-Host '================================'
Write-Host ''

$checks = Get-AllChecks
Write-CheckTable -Checks $checks

$blockers = @($checks | Where-Object { -not $_.Lulus -and $_.Blocker })
$warnings = @($checks | Where-Object { -not $_.Lulus -and -not $_.Blocker })

if ($blockers.Count -gt 0) {
    Write-Host ''
    Write-Host 'HARUS DIPERBAIKI' -ForegroundColor Red
    Write-Host '----------------'
    foreach ($item in $blockers) {
        Write-Host ''
        Write-Host "  $($item.Nama)" -ForegroundColor Red
        Write-Host "  $($item.Saran)"
    }
}

if ($warnings.Count -gt 0) {
    Write-Host ''
    Write-Host 'BELUM SIAP, TAPI TIDAK MEMBLOKIR' -ForegroundColor Yellow
    Write-Host '--------------------------------'
    foreach ($item in $warnings) {
        Write-Host ''
        Write-Host "  $($item.Nama)" -ForegroundColor Yellow
        foreach ($baris in ($item.Saran -split "`n")) {
            Write-Host "  $($baris.TrimEnd())"
        }
    }
}

Write-Host ''
Write-Host 'APA YANG BISA DIKERJAKAN SEKARANG' -ForegroundColor Cyan
Write-Host '---------------------------------'

$pia = $checks | Where-Object { $_.Nama -eq 'PIA SolidWorks' }
$sdk = $checks | Where-Object { $_.Nama -eq 'dotnet SDK' }

if ($sdk.Lulus) {
    Write-Host '  OK    MikosAI.Core + 171 test - tidak butuh SolidWorks' -ForegroundColor Green
    Write-Host '        dotnet test'
}
else {
    Write-Host '  TIDAK Butuh dotnet SDK lebih dulu.' -ForegroundColor Red
}

if ($pia.Lulus) {
    Write-Host '  OK    Build add-in' -ForegroundColor Green
    Write-Host '        powershell -File scripts\setup.ps1'
}
else {
    Write-Host '  TIDAK Build add-in - SolidWorks tidak terdeteksi.' -ForegroundColor Yellow
    Write-Host '        Verifikasi kode add-in tetap bisa tanpa SolidWorks:'
    Write-Host '        powershell -File scripts\addin.ps1 -Action CompileCheck'
}

Write-Host ''
if ($blockers.Count -eq 0) {
    Write-Host 'Tidak ada blocker. Lanjut: powershell -File scripts\setup.ps1' -ForegroundColor Green
    exit 0
}

Write-Host "$($blockers.Count) blocker harus diperbaiki lebih dulu." -ForegroundColor Red
exit 1
