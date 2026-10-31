# ADR 0002 — MCP server di-host di dalam add-in

Tanggal: 2026-10-31 · Status: diterima

## Konteks

AI CLI perlu memanggil operasi CAD kita. Pola umum: MCP server adalah proses terpisah yang
dispawn oleh AI client lewat stdio, lalu server itu menghubungi CAD lewat COM.

Pola itu yang dipakai repo referensi (`mcp-server/server.py`, 65 tool, 3889 baris).

## Keputusan

MCP server **di-host di dalam add-in**, di dalam proses SolidWorks, sebagai HTTP listener di
`127.0.0.1` dengan **port ephemeral**. AI CLI dikonfigurasi menghubunginya lewat field
`serverUrl`, bukan menjalankannya lewat `command`/`args`.

Add-in memiliki direktori kerja sendiri (`AgentWorkspace`) dan menulis
`.agents/mcp_config.json` di situ setiap sesi, lalu men-spawn `agy` dengan cwd ke folder
tersebut. Konfigurasi global pengguna tidak pernah disentuh.

## Alasan

Konsekuensi langsung dari ADR 0001. Kalau eksekusi CAD ada di dalam add-in, maka tool MCP
harus bisa menjangkau add-in. Dua pilihan: server di luar yang menghubungi add-in lewat IPC,
atau server di dalam add-in.

Server di dalam add-in menghapus satu lapisan IPC sepenuhnya — tool handler memanggil
`ToolExecutor` lewat pemanggilan fungsi biasa, bukan lewat protokol.

Antigravity mendukung MCP server remote lewat `serverUrl`, jadi pola ini memang didukung
client-nya. Ini terverifikasi saat riset.

## Konsekuensi

**Port ephemeral, dimungkinkan oleh AgentWorkspace.** Pertimbangan awal memilih port tetap
`8765`, dengan alasan `mcp_config.json` harus stabil antar sesi. Alasan itu gugur begitu
kita memutuskan add-in menulis konfigurasinya sendiri setiap sesi: tidak ada file statis
yang perlu mencocokkan angka port, jadi port bisa diserahkan ke OS. Itu lebih aman, dan
memungkinkan dua instance SolidWorks berjalan bersamaan tanpa bentrok.

**Konfigurasi milik kita, bukan milik pengguna.** Menulis ke
`~/.gemini/config/mcp_config.json` berarti memodifikasi file pribadi pengguna — berisiko
menimpa entri MCP server lain yang mereka pakai untuk pekerjaan sendiri, dan membuat
uninstall kotor. Dengan `AgentWorkspace` kita hanya memiliki satu folder di
`%LOCALAPPDATA%`, dan membersihkannya berarti menghapus folder itu.

**Keamanan jadi tanggung jawab kita.** HTTP listener di mesin pengguna:

- Hanya bind `127.0.0.1`. Tidak pernah `0.0.0.0`. Diverifikasi dari mesin lain di jaringan.
- Bearer token per sesi, ditulis ke `%LOCALAPPDATA%\MikosAI\session.json` dengan ACL hanya
  untuk pengguna saat ini.
- Hanya tool berstatus `verified`/`pilot` yang pernah terdaftar.

**Verifikasi terbuka:** apakah `agy` membaca `.agents/mcp_config.json` relatif ke cwd yang
kita tentukan, dan apakah konfigurasi per-proyek menggantikan atau digabung dengan yang
global. Kalau hanya konfigurasi global yang dibaca, fallback-nya adalah merge
non-destruktif — baca, tambahkan entri kita, tulis kembali, jangan pernah menimpa entri
pengguna.

Dan: apakah entry `serverUrl` Antigravity mendukung custom header untuk
bearer token belum terkonfirmasi. Kalau tidak, fallback-nya token di query string (dapat
diterima untuk loopback-only) atau pembuktian kepemilikan lewat file sesi ber-ACL. Ini item
5 di daftar verifikasi `docs/integrasi-antigravity.md` §7.

**Server mati ketika SolidWorks tutup.** Ini benar dan memang diinginkan — tool CAD tidak
punya arti tanpa SolidWorks berjalan. AI harus mendapat pesan yang jelas, bukan timeout.

## Alternatif yang ditolak

**Server stdio terpisah yang dispawn `agy`.** Butuh IPC tambahan ke add-in, dan `agy` akan
mengelola siklus hidup proses yang sebenarnya bergantung pada SolidWorks. Lapisan ekstra
tanpa manfaat.

**Named pipe atau COM alih-alih HTTP loopback.** Lebih cepat secara teori, tapi MCP punya
transport HTTP standar yang sudah didukung client. Memakai transport non-standar berarti
kita kehilangan kompatibilitas dengan provider lain — padahal abstraksi provider adalah
ADR 0003.
