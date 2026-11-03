<#
.SYNOPSIS
    Mempersempit penyebab crash SolidWorks dengan mematikan subsistem.

.DESCRIPTION
    SolidWorks mati dengan Fault Module kosong, dan host-status.json
    menunjukkan ConnectToSW selesai sempurna tanpa error. Artinya crash terjadi
    SETELAH itu, di salah satu subsistem yang berjalan di latar.

    Script ini menyalakan atau mematikan subsistem lewat variabel lingkungan
    tingkat User - harus User, bukan proses, karena SolidWorks diluncurkan dari
    Explorer dan hanya mewarisi variabel User atau Machine.

    Tidak perlu rebuild. Ubah mode, buka ulang SolidWorks, jalankan probe.

.PARAMETER Mode
    SafeMode    Matikan SEMUANYA. Hanya ConnectToSW, dispatcher, diagnostik.
    NoTaskPane  Matikan TaskPane dan WebView2.
    NoMcp       Matikan server MCP.
    NoProvider  Matikan deteksi provider dan subprocess agy.
    Normal      Nyalakan semuanya kembali.
    Status      Tampilkan mode saat ini.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\crash-bisect.ps1 -Mode SafeMode
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SafeMode', 'NoTaskPane', 'NoMcp', 'NoProvider', 'Normal', 'Status')]
    [string] $Mode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Variabel = @(
    'MIKOSAI_SAFE_MODE'
    'MIKOSAI_DISABLE_TASKPANE'
    'MIKOSAI_DISABLE_MCP'
    'MIKOSAI_DISABLE_PROVIDER'
)

function Set-Saklar {
    param([string[]] $Nyalakan)

    foreach ($nama in $Variabel) {
        $nilai = $null
        if ($Nyalakan -contains $nama) { $nilai = '1' }

        # Tingkat User, supaya diwarisi SLDWORKS.exe yang diluncurkan Explorer.
        [Environment]::SetEnvironmentVariable($nama, $nilai, 'User')
    }
}

function Show-Status {
    Write-Host ''
    Write-Host 'Saklar fitur saat ini (tingkat User):' -ForegroundColor Cyan

    $adaYangMati = $false

    foreach ($nama in $Variabel) {
        $nilai = [Environment]::GetEnvironmentVariable($nama, 'User')

        if ([string]::IsNullOrWhiteSpace($nilai)) {
            Write-Host ("  {0,-28} tidak diset" -f $nama) -ForegroundColor DarkGray
        }
        else {
            Write-Host ("  {0,-28} {1}" -f $nama, $nilai) -ForegroundColor Yellow
            $adaYangMati = $true
        }
    }

    Write-Host ''
    if ($adaYangMati) {
        Write-Host 'Sebagian subsistem DIMATIKAN.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'Semua subsistem aktif (normal).' -ForegroundColor Green
    }
}

if ($Mode -eq 'Status') {
    Show-Status
    exit 0
}

switch ($Mode) {
    'SafeMode'   { Set-Saklar @('MIKOSAI_SAFE_MODE') }
    'NoTaskPane' { Set-Saklar @('MIKOSAI_DISABLE_TASKPANE') }
    'NoMcp'      { Set-Saklar @('MIKOSAI_DISABLE_MCP') }
    'NoProvider' { Set-Saklar @('MIKOSAI_DISABLE_PROVIDER') }
    'Normal'     { Set-Saklar @() }
}

Write-Host ''
Write-Host "Mode diset: $Mode" -ForegroundColor Green
Show-Status

Write-Host ''
Write-Host 'LANGKAH BERIKUTNYA' -ForegroundColor Cyan
Write-Host '------------------'
Write-Host '  1. TUTUP SolidWorks sepenuhnya kalau masih terbuka.'
Write-Host '  2. Buka SolidWorks lagi (variabel baru hanya terbaca saat proses dimulai).'
Write-Host '  3. Tunggu 30 detik, atau sampai crash.'
Write-Host '  4. Jalankan:  powershell -File scripts\addin.ps1 -Action Probe'
Write-Host ''
Write-Host '  Yang dicari di output probe: baris "Aktivitas terakhir".'
Write-Host '  Itu menunjuk langkah yang sedang berjalan saat proses berhenti.'
Write-Host ''

if ($Mode -ne 'Normal') {
    Write-Host 'Jangan lupa kembalikan ke Normal setelah selesai menguji:' -ForegroundColor Yellow
    Write-Host '  powershell -File scripts\crash-bisect.ps1 -Mode Normal' -ForegroundColor Yellow
    Write-Host ''
}
