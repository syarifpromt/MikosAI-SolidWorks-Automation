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

### Add-in tidak ada di `MikosAI.sln`

Kalau ikut di solution, `dotnet build` dan `dotnet test` akan gagal di mesin tanpa
SolidWorks — termasuk CI nanti. Add-in dibangun lewat `scripts/addin.ps1`.

Akibatnya `dotnet build` dan `dotnet test` **selalu hijau di mesin mana pun**.

### Permukaan PIA dikecilkan ke satu interface

Satu-satunya tipe PIA yang dipakai saat kompilasi adalah `ISwAddin`. Semua panggilan
SolidWorks lain late-bound lewat `SolidWorksApi` (`dynamic` — SolidWorks mendukung
IDispatch).

Tanda tangan API yang berbeda antarversi jadi **error terstruktur di `host-status.json`**,
bukan error compiler di mesin kalian. Yang kita tukar: "build gagal di mesinmu" menjadi
"satu baris error di berkas diagnostik" — dan yang kedua jauh lebih mudah dilaporkan dan
diperbaiki.

Pendaftaran callback bahkan punya fallback: `SetAddinCallbackInfo2` dicoba lebih dulu, lalu
`SetAddinCallbackInfo` versi lama.

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
