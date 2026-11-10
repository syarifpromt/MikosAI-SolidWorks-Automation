<#
    Mendeteksi dialog modal SolidWorks DARI LUAR PROSES.

    KENAPA DARI LUAR, DAN KENAPA INI PENTING.

    Add-in kami TIDAK BISA melaporkan dialog modal. Dialog itu memblokir thread
    utama SolidWorks - yaitu thread yang sama yang menjalankan writer kami dan
    yang akan menulis laporannya. Selama dialog terbuka, tidak ada satu baris
    pun yang bisa kami tulis.

    Akibatnya skrip golden kami selama ini hanya MENEBAK: "Periksa apakah build
    terbaru termuat atau ada dialog modal." Tebakan itu benar sebagai hipotesis
    dan tidak berguna sebagai diagnosis - orang tetap harus menoleh ke layar.

    Gagasan ini diadopsi dari MecAgent Copilot, yang menjadikan DialogRecord
    bagian dari protokolnya. Pola itu juga menjelaskan kenapa mereka memisahkan
    runner ke proses lain: pengamat yang ikut terblokir tidak bisa mengamati.

    Yang diadopsi GAGASANNYA, bukan kodenya. Implementasi di bawah memakai
    Win32 biasa.
#>

Set-StrictMode -Version Latest

if (-not ('MikosAI.Win32Jendela' -as [type])) {
    Add-Type -Namespace MikosAI -Name Win32Jendela -MemberDefinition @'
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, System.IntPtr lParam);

    public delegate bool EnumWindowsProc(System.IntPtr hWnd, System.IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(System.IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(System.IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(System.IntPtr hWnd, System.Text.StringBuilder s, int n);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(System.IntPtr hWnd, System.Text.StringBuilder s, int n);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern System.IntPtr GetWindow(System.IntPtr hWnd, uint uCmd);
'@
}

function Get-DialogSolidWorks {
    <#
    .SYNOPSIS
        Jendela milik SLDWORKS yang terlihat seperti dialog yang memblokir.

    .DESCRIPTION
        Mengembalikan daftar kosong kalau tidak ada - dan daftar kosong berarti
        "tidak ada yang kami temukan", BUKAN "tidak ada dialog". Dialog yang
        digambar sendiri oleh SolidWorks tanpa kelas #32770 tidak akan terlihat
        di sini, dan itu disebutkan supaya ketiadaannya tidak dibaca sebagai
        jaminan.
    #>
    [CmdletBinding()]
    param()

    $sw = Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue
    if (-not $sw) { return @() }

    $pidSw = @($sw | ForEach-Object { [uint32]$_.Id })
    $hasil = New-Object System.Collections.ArrayList

    $callback = [MikosAI.Win32Jendela+EnumWindowsProc] {
        param([IntPtr] $hWnd, [IntPtr] $lParam)

        $pid32 = [uint32]0
        [void][MikosAI.Win32Jendela]::GetWindowThreadProcessId($hWnd, [ref] $pid32)
        if ($pidSw -notcontains $pid32) { return $true }
        if (-not [MikosAI.Win32Jendela]::IsWindowVisible($hWnd)) { return $true }

        $kelas = New-Object System.Text.StringBuilder 256
        [void][MikosAI.Win32Jendela]::GetClassNameW($hWnd, $kelas, 256)

        $judul = New-Object System.Text.StringBuilder 512
        [void][MikosAI.Win32Jendela]::GetWindowTextW($hWnd, $judul, 512)

        # GW_OWNER = 4. Dialog modal dimiliki jendela lain; jendela utama tidak.
        $pemilik = [MikosAI.Win32Jendela]::GetWindow($hWnd, 4)

        $kelasTeks = $kelas.ToString()
        $judulTeks = $judul.ToString()

        # #32770 adalah kelas dialog standar Windows. Jendela SolidWorks yang
        # punya PEMILIK dan judul juga dicatat, karena sebagian dialognya
        # memakai kelas sendiri.
        $mencurigakan = ($kelasTeks -eq '#32770') -or
                        ($pemilik -ne [IntPtr]::Zero -and $judulTeks.Length -gt 0)

        if ($mencurigakan) {
            [void]$hasil.Add([pscustomobject]@{
                Handle    = $hWnd
                ProcessId = $pid32
                Kelas     = $kelasTeks
                Judul     = $judulTeks
                Aktif     = [MikosAI.Win32Jendela]::IsWindowEnabled($hWnd)
                Dimiliki  = ($pemilik -ne [IntPtr]::Zero)
            })
        }
        return $true
    }

    [void][MikosAI.Win32Jendela]::EnumWindows($callback, [IntPtr]::Zero)
    return @($hasil)
}

function Write-DiagnosaMacet {
    <#
    .SYNOPSIS
        Dipanggil saat run tidak memberi hasil: menyebut APA yang menahan,
        bukan menyuruh orang menebak.
    #>
    [CmdletBinding()]
    param([string] $RequestId)

    $sw = Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue

    if (-not $sw) {
        Write-Host '  SLDWORKS.exe TIDAK BERJALAN - prosesnya mati, bukan menggantung.' -ForegroundColor Red
        Write-Host '  Crash native tidak meninggalkan jejak di crash.log kami maupun Event Log.' -ForegroundColor Yellow
        $swj = Join-Path $env:APPDATA 'SolidWorks\SOLIDWORKS 2025\swxJRNL.swj'
        if (Test-Path $swj) {
            Write-Host "  Jurnal SolidWorks merekam panggilan API terakhir beserta stack trace native:" -ForegroundColor Yellow
            Write-Host "    $swj"
            $jejak = Get-Content $swj -Tail 40 | Where-Object { $_ -match 'stack trace|sldapiu|SLDWORKS:' }
            if ($jejak) {
                Write-Host '  Stack trace terakhir:' -ForegroundColor Yellow
                $jejak | Select-Object -Last 6 | ForEach-Object { Write-Host "    $_" }
            }
        }
        return
    }

    Write-Host "  SLDWORKS.exe berjalan (PID $($sw.Id -join ', ')), responding=$($sw.Responding)"

    $dialog = @(Get-DialogSolidWorks)
    if ($dialog.Count -eq 0) {
        Write-Host '  Tidak ada dialog yang kami temukan.' -ForegroundColor Yellow
        Write-Host '  Itu BUKAN jaminan tidak ada: dialog yang digambar sendiri tanpa' -ForegroundColor Yellow
        Write-Host '  kelas #32770 tidak terlihat oleh pemeriksaan ini.' -ForegroundColor Yellow
    }
    else {
        Write-Host "  $($dialog.Count) jendela yang mungkin memblokir:" -ForegroundColor Red
        foreach ($d in $dialog) {
            Write-Host ("    [{0}] '{1}'  aktif={2} dimiliki={3}" -f $d.Kelas, $d.Judul, $d.Aktif, $d.Dimiliki) -ForegroundColor Red
        }
    }
}

Export-ModuleMember -Function Get-DialogSolidWorks, Write-DiagnosaMacet
