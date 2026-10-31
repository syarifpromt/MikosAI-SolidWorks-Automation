# Setup dan uji add-in

Panduan operasional untuk membangun, mendaftarkan, dan memverifikasi
`MikosAI.Addin`. Tidak ada langkah yang butuh Visual Studio.

---

## Kenapa add-in tidak ada di `MikosAI.sln`

Sengaja. `MikosAI.Addin` butuh PIA SolidWorks dari instalasi lokal, jadi kalau ikut di
solution, `dotnet build` dan `dotnet test` akan gagal di mesin tanpa SolidWorks — termasuk
CI nanti.

Akibatnya:

- `dotnet build` dan `dotnet test` **selalu hijau di mesin mana pun** (Core + 171 test)
- Add-in dibangun lewat `scripts/addin.ps1`, yang menuntut path PIA eksplisit

PIA tidak di-vendor ke repo — redistribusinya dibatasi lisensi Dassault.

---

## Sekali per mesin

### 1. Kunci strong name

```powershell
powershell -ExecutionPolicy Bypass -File scripts\New-StrongNameKey.ps1 `
  -Path src\MikosAI.Addin\MikosAI.Addin.snk
```

`sn.exe` ikut Windows SDK atau Visual Studio. Script ini membuat `.snk` lewat API .NET
langsung, jadi tidak butuh keduanya.

Berkas `.snk` **tidak di-commit** (`.gitignore` mengecualikan `*.snk`). Setiap pengembang
membuat kuncinya sendiri untuk pengembangan lokal; rilis resmi memakai satu kunci yang
disimpan terpisah.

Ini hanya menstabilkan identitas assembly — **bukan** code signing Authenticode.

### 2. Cari path PIA

Biasanya:

```
C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist
```

Direktori itu harus memuat `SolidWorks.Interop.sldworks.dll`. Script bisa mendeteksinya
otomatis, tapi **selalu melaporkan path mana yang dipakai** — merujuk PIA dari versi
SolidWorks yang berbeda menghasilkan kegagalan yang membingungkan.

---

## Siklus kerja

```powershell
# 1. Build
powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Build `
  -SolidWorksApiDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'

# 2. Registrasi - WAJIB PowerShell sebagai administrator
powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Register -RegistrationScope Machine

# 3. Buka SolidWorks, pastikan MikosAI tercentang di Tools > Add-Ins

# 4. Verifikasi
powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Probe
```

Di VS Code, keempat langkah ini tersedia sebagai task (`Ctrl+Shift+P` → Run Task):
`addin: buat kunci strong name`, `addin: build`, `addin: status`, `addin: probe`.

### Sebelum rebuild

**SolidWorks mengunci DLL yang sudah dimuat.** Tutup SolidWorks, atau lepas add-in, sebelum
build ulang. Menimpa paksa menghasilkan gejala yang membingungkan dan membuang waktu.

---

## Registrasi Machine vs CurrentUser

| Scope | Yang dilakukan | Cukup untuk SolidWorks? |
|---|---|---|
| `Machine` | HKLM + TLB + kelas COM | **Ya** |
| `CurrentUser` | Hanya aktivasi CLR/COM | **Tidak** |

`CurrentUser` berguna untuk membuktikan assembly bisa diaktifkan, tapi SolidWorks tidak
akan pernah memuat add-in dengan scope itu.

**TLB wajib.** Tanpa TLB terdaftar, SolidWorks bisa memuat DLL dan mengembalikan
`LoadAddIn=0`, lalu **tidak pernah menginstansiasi `ISwAddin`**. Ini jebakan yang sudah
terdokumentasi, dan alasan `-Action Register` selalu memakai `/tlb`.

---

## Membaca hasil probe

Diagnostik ada di `%LOCALAPPDATA%\MikosAI\host-status.json`.

**`ISldWorks.LoadAddIn()` mengembalikan 0 tidak dihitung lulus.** Satu-satunya pernyataan
yang sah bahwa add-in hidup adalah berkas ini.

Gerbang yang ditegakkan `-Action Probe` di tahap ini:

- [ ] `status == "connected"`
- [ ] `callbackRegistered == true`
- [ ] `dispatcherReady == true`
- [ ] `mainThreadId` terisi
- [ ] revisi SolidWorks terbaca
- [ ] `errors` kosong

Yang **wajar masih `false`** di Tahap 1: `commandGroup`, `taskPane`, `mcpServer`. Ketiganya
belum diimplementasikan.

### Kalau `host-status.json` tidak ada

Artinya `ConnectToSW` belum pernah berjalan. Penyebab urut dari yang paling mungkin:

1. Registrasi Machine belum dilakukan (`CurrentUser` tidak cukup)
2. TLB belum terdaftar
3. Add-in belum dicentang di **Tools → Add-Ins**
4. SolidWorks belum dibuka setelah registrasi

`-Action Status` menunjukkan apa yang sudah terdaftar dan artefak apa yang ada.

---

## Soal debugging

Step-debug add-in net48 di VS Code punya batasan nyata — risiko R12 di `ROADMAP.md`.
C# Dev Kit tidak mendukung .NET Framework; ekstensi C# dasar punya `type: "clr"` dengan
attach ke proses tapi dukungannya belum terkonfirmasi di versi terkini; debugger ReSharper
mendukungnya tapi tidak bisa aktif bareng C# Dev Kit.

Konfigurasi attach sudah disiapkan di `.vscode/launch.json`, tapi **jalur utama kita adalah
diagnostik**, bukan step-debugging:

1. `host-status.json` — task `addin: probe`
2. Trace JSONL di `%LOCALAPPDATA%\MikosAI\trace\`
3. 171 test `MikosAI.Core` yang jalan ~1 detik tanpa SolidWorks

Itu bukan tambalan karena tidak punya Visual Studio. Debugging add-in CAD menyakitkan apa
pun editornya — SolidWorks mengunci DLL, jadi setiap iterasi butuh restart SolidWorks. Orang
yang serius mengerjakan add-in SolidWorks jarang step-debug; mereka mengandalkan
diagnostik. Itu sebabnya diagnostik kita ditulis lengkap dengan sengaja.

Pintu darurat: sisipkan `System.Diagnostics.Debugger.Launch()` di kode add-in. Itu
memunculkan dialog JIT debugger yang bisa menarik debugger apa pun yang terdaftar di
sistem.

---

## Yang ada di Tahap 1, dan yang belum

Add-in versi ini **sengaja minimal**. Tujuannya satu: menjawab pertanyaan paling berisiko
yang belum terjawab — apakah add-in benar-benar diinstansiasi `SLDWORKS.exe` di lingkungan
kalian, dan apakah thread utamanya bisa ditangkap.

**Ada:**

- `ISwAddin.ConnectToSW` / `DisconnectFromSW`
- `SetAddinCallbackInfo2`
- `MainThreadDispatcher` lewat `Control.BeginInvoke`
- Penulis `host-status.json`
- Registrasi/unregistrasi COM

**Belum ada, dan itu disengaja:** CommandGroup, TaskPane, WebView2,
PropertyManagerPage, MCP server. Semuanya menyentuh permukaan API yang jauh lebih luas.
Menambahkannya sekarang hanya memperbanyak kemungkinan penyebab kalau gagal.

### Catatan: kode ini belum pernah dikompilasi

`MikosAI.Addin` ditulis tanpa akses ke PIA SolidWorks, jadi **belum pernah lewat compiler.**
Kemungkinan ada ketidakcocokan tanda tangan API — paling mungkin pada
`SetAddinCallbackInfo2`, `RevisionNumber`, atau `GetDocumentCount`.

Semua pemanggilan API dibungkus `try/catch` per langkah justru untuk ini: kalau satu
gagal, langkah lain tetap jalan dan `host-status.json` menyebut persis yang mana. Build
pertama di mesin kalian adalah uji nyata pertamanya — laporkan error compiler apa pun,
itu cepat diperbaiki.
