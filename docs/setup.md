# Setup

Tidak ada langkah yang butuh Visual Studio atau Windows SDK.

---

## Satu perintah

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
```

Di VS Code: `Ctrl+Shift+P` → **Run Task** → `setup (satu perintah)`.

Script itu mengerjakan semuanya dan berhenti dengan pesan jelas kalau ada yang kurang:

1. Periksa lingkungan
2. Build `MikosAI.Core` dan jalankan seluruh test
3. Verifikasi kode add-in kompilabel — **tanpa** SolidWorks
4. Build add-in, kalau SolidWorks terdeteksi
5. Registrasi add-in — prompt UAC muncul **sekali**, otomatis

Aman dijalankan berulang kali; langkah yang sudah beres dilewati.

### Satu langkah yang tetap manual

Setelah setup, di SolidWorks:

1. **Tools → Add-Ins**
2. Centang **MikosAI** (kolom kiri = aktif sekarang, kanan = aktif otomatis)
3. Verifikasi:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Probe
```

Mencentang add-in tidak diotomasi — itu keputusan pengguna di UI SolidWorks.

---

## Kalau ada yang tidak jalan

```powershell
powershell -ExecutionPolicy Bypass -File scripts\doctor.ps1
```

Read-only, tidak butuh administrator. Melaporkan setiap prasyarat dengan status dan saran
perbaikannya, lalu memberi tahu apa yang **tetap bisa** dikerjakan sekarang.

Contoh keluaran nyata di mesin tanpa SolidWorks:

```
  [OK   ] .NET Framework 4.8       Release 534296
  [OK   ] dotnet SDK               10.0.202
  [OK   ] Windows 10 1809+         build 26300
  [GAGAL] PIA SolidWorks           tidak ditemukan
  [OK   ] WebView2 Runtime         156.0.4314.50
  [GAGAL] Antigravity CLI (agy)    tidak ditemukan
```

Hanya `.NET Framework 4.8` dan `dotnet SDK` yang **memblokir**. Sisanya tidak, dan itu
disengaja.

---

## Tanpa SolidWorks, setup tetap berhasil

Ini penting untuk kolaborasi: kalau salah satu mesin belum punya SolidWorks, setup **tidak
gagal**. Langkah 4 dan 5 dilewati, dan yang berikut tetap jalan penuh:

- `MikosAI.Core` dan seluruh 171 test
- `McpHttpServer`, `PlanRunner`, UI — semuanya di atas `FakeToolExecutor`
- Verifikasi kode add-in lewat compile-check

Jadi Jalur Platform tidak pernah terhalang ketersediaan lisensi CAD.

---

## Keputusan yang membuat setup sesederhana ini

### Add-in ikut di solution, tapi tidak pernah menggagalkannya

`MikosAI.Addin` ada di `MikosAI.sln` supaya terlihat di editor. Tapi build nyatanya butuh
PIA SolidWorks, jadi kalau naif ia akan menggagalkan `dotnet build` dan `dotnet test` di
mesin tanpa CAD — termasuk CI nanti. Itu pernah terjadi.

Jadi csproj-nya mendeteksi PIA sendiri, dan **turun ke mode compile-check** kalau tidak ada:

| Keadaan | Hasil |
|---|---|
| SolidWorks terdeteksi | Build nyata ke `bin\Release
et48\` |
| Tidak terdeteksi | Compile-check ke `bin\compilecheck\` + **warning** |
| `-SolidWorksApiDir` diberikan tapi salah | **Error** — tidak dialihkan diam-diam |

Nama assembly dan folder output mode compile-check sengaja berbeda, jadi build stub **tidak
mungkin** menimpa DLL yang didaftarkan ke SolidWorks.

Efek sampingnya bagus: kode add-in ikut diperiksa tipenya setiap kali `dotnet build`
dijalankan, di mesin mana pun.

### Pemanggilan API early-bound, bukan `dynamic`

Versi pertama memakai `dynamic` supaya kode add-in bisa dikompilasi tanpa PIA. Alasannya
terdengar bagus: tanda tangan API yang salah jadi error terstruktur di `host-status.json`
alih-alih error compiler di mesin orang lain.

**Itu terbukti salah di mesin nyata.** Setiap panggilan gagal dengan HRESULT yang sama:

```
0x8002802B  TYPE_E_ELEMENTNOTFOUND
```

Artinya nama member tidak ditemukan lewat IDispatch — `SetAddinCallbackInfo2`,
`RevisionNumber`, `GetDocumentCount`, semuanya. Interface SolidWorks dideskripsikan oleh
type library dan dipanggil lewat vtable; ia **tidak** bisa dicari namanya lewat IDispatch
seperti yang diasumsikan DLR.

Jadi pemanggilan harus early-bound lewat PIA bertipe. Keuntungan lintas-versi dipertahankan
dengan cara lain — lihat bagian berikutnya.

### Compile-check tanpa SolidWorks

```powershell
powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action CompileCheck
```

Memakai stub `ISwAddin` lokal yang **hanya** ikut dikompilasi di mode ini
(`#if MIKOSAI_PIA_STUB`). Build nyata memakai `swpublished.dll` asli, jadi tidak ada risiko
tanda tangan stub sampai ke runtime.

Gunanya: kode add-in bisa diverifikasi benar secara tipe di mesin tanpa CAD. Tanpa ini,
satu-satunya cara tahu kode add-in kompilabel adalah mencobanya di mesin bermesin
SolidWorks — umpan balik lambat, tepat di langkah yang ingin kita buat simpel.

### Tipe interop ditanam, bukan direferensi

`EmbedInteropTypes=true` (NoPIA) menanamkan definisi `ISwAddin` ke dalam assembly kita,
sehingga **tidak ada dependensi assembly saat runtime**.

Ini memperbaiki dua hal:

**Pertama, registrasi.** RegAsm harus *memuat* assembly kita untuk memanggil
`[ComRegisterFunction]`. Dengan referensi biasa, CLR butuh `swpublished.dll` saat itu — dan
PIA SolidWorks **tidak ada di GAC**, jadi registrasi gagal:

```
RegAsm : error RA0000 : Could not load file or assembly
'SolidWorks.Interop.swpublished, Version=33.1.1.1 ...'
```

**Kedua, dan ini yang lebih penting untuk kolaborasi:** referensi biasa mengikat versi PIA
secara keras. `Version=33.1.1.1` itu SolidWorks 2025. Add-in yang dibangun di SW2025 lalu
dipakai di SW2026 akan memuat dua versi assembly yang sama ke satu proses. Type equivalence
pada interop yang ditanam membuat interface-nya **menyatu** dengan apa pun yang dimuat
SolidWorks — jadi dua laptop dengan versi berbeda tidak masalah.

Jalur mundur kalau penanaman bermasalah:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -NoEmbedPia
```

Itu mengopi `swpublished.dll` ke folder output. Juga sah — folder sumbernya memang bernama
`redist`, dan Dassault menyediakannya untuk didistribusikan bersama add-in.

### Tanpa strong naming

`RegAsm /codebase` merekam **path** assembly, bukan identitas strong name, jadi SolidWorks
tidak membutuhkannya. Dan strong name bukan code signing Authenticode.

Menambahkannya justru merugikan: assembly strong-named tidak boleh mereferensi yang tidak
(CS8002), jadi `MikosAI.Core` harus ikut ditandatangani — dan itu membuat `dotnet build`
bergantung pada kunci yang digenerate dan tidak di-commit, sehingga **clone baru gagal
build**.

`scripts/New-StrongNameKey.ps1` tetap ada untuk kebutuhan rilis nanti, dan tidak memerlukan
`sn.exe`.

### Deteksi PIA otomatis

`Get-SolidWorksApiDir` mencoba berurutan dari yang paling andal:

1. Registry `SldWorks.Application` → CLSID → `LocalServer32` → path exe → `api\redist`
   — paling andal karena tidak bergantung nomor versi
2. `HKLM\SOFTWARE\SolidWorks\<versi>\Setup` → `SolidWorks Folder`
3. Path umum, termasuk variasi kapitalisasi lama

Selalu melaporkan path mana yang dipakai — merujuk PIA dari versi SolidWorks yang berbeda
menghasilkan kegagalan yang membingungkan.

---

## Perintah lain

| Perintah | Guna |
|---|---|
| `doctor.ps1` | Periksa lingkungan, read-only |
| `addin.ps1 -Action Status` | Apa yang terdaftar di registry, artefak apa yang ada |
| `addin.ps1 -Action Probe` | Baca `host-status.json`, tegakkan gerbang penerimaan |
| `addin.ps1 -Action CompileCheck` | Verifikasi tipe tanpa SolidWorks |
| `addin.ps1 -Action Unregister` | Lepas registrasi (butuh administrator) |
| `setup.ps1 -SkipRegister` | Build saja, hindari prompt UAC |
| `setup.ps1 -SolidWorksApiDir '<path>'` | Timpa hasil deteksi otomatis |
| `setup.ps1 -NoEmbedPia` | Jalur mundur: kopi PIA alih-alih menanam tipe interop |

### Warning RegAsm yang wajar muncul

```
RegAsm : warning RA0000 : Registering an unsigned assembly with /codebase ...
Please give your assembly a strong name and re-register it.
```

Ini **warning, bukan error**, dan registrasi tetap berhasil. Script menampilkannya dengan
label `(warning wajar, registrasi tetap jalan)` supaya tidak disalahartikan.

> **Catatan untuk yang mengubah script:** jangan memakai `2>&1` pada executable native tanpa
> pengaman. Di PowerShell 5.1 itu membungkus tiap baris stderr jadi `ErrorRecord`
> (`NativeCommandError`), dan dengan `$ErrorActionPreference = 'Stop'` warning yang tidak
> berbahaya pun menghentikan script — padahal exe-nya keluar dengan kode 0. Pernah terjadi
> dan membuat registrasi yang berhasil terlihat gagal. Pola yang benar ada di
> `Invoke-RegAsm`: turunkan preference sementara, lalu nilai keberhasilan **hanya** dari
> `$LASTEXITCODE`. `/codebase` diperlukan karena
assembly kita tidak ada di GAC — tanpa itu, CLR tidak akan menemukannya saat SolidWorks
memuat add-in. Add-in .NET SolidWorks umumnya didaftarkan begini.

Kalau nanti warning itu benar-benar menimbulkan masalah pemuatan, jalan keluarnya adalah
strong naming dengan kunci yang di-commit ke repo — strong name bukan mekanisme keamanan,
jadi itu praktik yang lazim. Belum dilakukan karena belum ada buktinya diperlukan.

**Sebelum rebuild:** SolidWorks mengunci DLL yang sudah dimuat. Tutup SolidWorks atau lepas
add-in lebih dulu. Menimpa paksa menghasilkan gejala yang membingungkan.

---

## Membaca hasil probe

Diagnostik ada di `%LOCALAPPDATA%\MikosAI\host-status.json`.

**`ISldWorks.LoadAddIn()` mengembalikan 0 tidak dihitung lulus.** SolidWorks bisa memuat DLL
dan melaporkan sukses tanpa pernah menginstansiasi `ISwAddin` — biasanya karena TLB belum
terdaftar. Satu-satunya pernyataan yang sah bahwa add-in hidup adalah berkas ini.

Gerbang yang ditegakkan di tahap ini:

- [ ] `status == "connected"`
- [ ] `callbackRegistered == true`
- [ ] `dispatcherReady == true`
- [ ] `mainThreadId` terisi
- [ ] revisi SolidWorks terbaca
- [ ] `errors` kosong

Yang **wajar masih `false`**: `commandGroup`, `taskPane`, `mcpServer` — ketiganya belum
diimplementasikan di Tahap 1.

### Kalau `host-status.json` tidak ada

`ConnectToSW` belum pernah berjalan. Penyebab urut dari yang paling mungkin:

1. Registrasi Machine belum dilakukan (`CurrentUser` tidak cukup)
2. TLB belum terdaftar
3. Add-in belum dicentang di **Tools → Add-Ins**
4. SolidWorks belum dibuka setelah registrasi

`-Action Status` menunjukkan apa yang sudah terdaftar.

---

## Soal debugging

Step-debug add-in net48 di VS Code punya batasan nyata — risiko R12 di `ROADMAP.md`.
C# Dev Kit tidak mendukung .NET Framework; ekstensi C# dasar punya `type: "clr"` dengan
attach ke proses tapi dukungannya belum terkonfirmasi di versi terkini; debugger ReSharper
mendukungnya tapi tidak bisa aktif bareng C# Dev Kit.

Konfigurasi attach sudah disiapkan di `.vscode/launch.json`, tapi **jalur utama kita adalah
diagnostik**:

1. `host-status.json` — task `addin: probe`
2. Trace JSONL di `%LOCALAPPDATA%\MikosAI\trace\`
3. 171 test `MikosAI.Core` yang jalan ~1 detik tanpa SolidWorks

Itu bukan tambalan karena tidak punya Visual Studio. Debugging add-in CAD menyakitkan apa
pun editornya — SolidWorks mengunci DLL, jadi setiap iterasi butuh restart SolidWorks. Orang
yang serius mengerjakan add-in SolidWorks jarang step-debug; mereka mengandalkan
diagnostik.

Pintu darurat: sisipkan `System.Diagnostics.Debugger.Launch()` di kode add-in. Itu
memunculkan dialog JIT debugger yang bisa menarik debugger apa pun yang terdaftar di
sistem.

---

## Yang ada di Tahap 1, dan yang belum

Add-in versi ini **sengaja minimal**. Tujuannya satu: menjawab pertanyaan paling berisiko
yang belum terjawab — apakah add-in benar-benar diinstansiasi `SLDWORKS.exe` di lingkungan
kalian, dan apakah thread utamanya bisa ditangkap.

**Ada:** `ISwAddin.ConnectToSW` / `DisconnectFromSW`, pendaftaran callback dengan fallback,
`MainThreadDispatcher` lewat `Control.BeginInvoke`, penulis `host-status.json`,
registrasi/unregistrasi COM.

**Belum ada, dan itu disengaja:** CommandGroup, TaskPane, WebView2, PropertyManagerPage,
MCP server. Semuanya menyentuh permukaan API yang jauh lebih luas. Menambahkannya sekarang
hanya memperbanyak kemungkinan penyebab kalau gagal.

Kode add-in **sudah lewat compile-check**, jadi ia benar secara tipe. Yang belum terbukti
adalah perilaku runtime-nya di SolidWorks nyata — dan itu hanya bisa dibuktikan di mesin
kalian.

---

## Mempersempit crash SolidWorks

Kalau SolidWorks mati setelah add-in dimuat, jangan menebak subsistem mana yang bersalah.
Matikan satu per satu.

Semua subsistem dikendalikan variabel lingkungan tingkat **User** — harus User, bukan
proses, karena SolidWorks diluncurkan dari Explorer dan hanya mewarisi variabel User atau
Machine. Tidak perlu rebuild.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\crash-bisect.ps1 -Mode SafeMode
```

Lalu **tutup SolidWorks sepenuhnya**, buka lagi, tunggu 30 detik, dan jalankan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Probe
```

### Urutan yang disarankan

| Langkah | Mode | Kalau TETAP crash | Kalau TIDAK crash |
|---|---|---|---|
| 1 | `SafeMode` | Penyebabnya bukan ketiga subsistem — lihat `lastActivity` | Lanjut langkah 2 |
| 2 | `NoTaskPane` | Bukan WebView2 | **WebView2 tersangkanya** |
| 3 | `NoProvider` | Bukan subprocess agy | **agy tersangkanya** |
| 4 | `NoMcp` | Bukan server MCP | **Server MCP tersangkanya** |

Setelah selesai:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\crash-bisect.ps1 -Mode Normal
```

### Membaca `Aktivitas terakhir`

Probe menampilkan baris **`Aktivitas terakhir`**. Itu langkah yang sedang **berjalan**,
bukan yang terakhir selesai — ditulis ke disk tepat saat dimulai, jadi kalau proses mati
setelahnya, nilai itulah yang tersimpan.

| Nilai | Artinya |
|---|---|
| `connect:selesai` | `ConnectToSW` beres; crash terjadi sesudahnya |
| `connect:taskpane-mulai` | Mati saat membangun TaskPane |
| `webview:ensure-core` | Mati saat menginisialisasi WebView2 |
| `webview:navigasi` | Mati saat memuat halaman UI |
| `latar:probe-agy-mulai` | Mati saat menjalankan `agy --version` atau `--help` |
| `latar:daftar-mcp-mulai` | Mati saat `agy mcp add` |
| `latar:kirim-status` | Mati saat mengirim status ke panel |
| `latar:selesai` | Semua latar beres; crash terjadi lebih jauh lagi |

Kalau nilainya `connect:selesai` **dan** mode `SafeMode` aktif, berarti penyebabnya bukan
WebView2, bukan MCP, bukan agy — dan itu informasi yang sama berharganya, karena
menyingkirkan tiga tersangka sekaligus.
