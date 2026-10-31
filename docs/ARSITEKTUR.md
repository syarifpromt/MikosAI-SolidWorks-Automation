# Arsitektur MikosAI SolidWorks Automation

Status: draft v0.1 · Terakhir diperbarui 2026-10-31

Dokumen ini adalah acuan arsitektur. Kalau kode dan dokumen ini berbeda, salah satunya
bug — perbaiki, jangan dibiarkan.

---

## 1. Tujuan dan non-tujuan

**Tujuan.** AI yang dioperasikan **dari dalam SolidWorks** untuk membangun dan memeriksa
model parametrik, dengan dua lapis pengalaman: mudah dipakai insinyur biasa, tetap bisa
dikendalikan penuh oleh power user.

**Non-tujuan v0.1.**

- Bukan layanan cloud. Semua berjalan lokal di mesin pengguna.
- Bukan pengganti insinyur. Setiap hasil tulis wajib lewat verifikasi, dan kemampuan yang
  belum terverifikasi tidak boleh dipakai tanpa pengawasan.
- Bukan multi-CAD. AutoCAD/Inventor di luar cakupan sampai v0.1 stabil.
- Bukan headless server automation. SolidWorks butuh seat lisensi dan sesi desktop.

---

## 2. Prinsip

1. **AI bebas berpikir, eksekusi harus terkendali.** Model boleh merencanakan apa saja;
   yang menyentuh geometri hanyalah tool yang terdaftar dan terverifikasi.
2. **Tool mengembalikan `success` bukan berarti tugas berhasil.** Keberhasilan hanya
   diakui setelah dibaca ulang dari model.
3. **Status tidak dikenal = `BLOCKED`, bukan `PASS`.** Default selalu aman.
4. **Satu sumber kebenaran kemampuan.** `capabilities.yaml`. Tidak ada registry kedua.
5. **Keputusan pemulihan terpisah dari eksekusi pemulihan.** Sistem memutuskan
   "sebaiknya retry"; yang mengeksekusi adalah lapisan di atasnya, bukan otomatis.
6. **Kecil itu fitur.** Kalau sebuah lapisan tidak membuat output lebih benar atau lebih
   mudah dipakai, lapisan itu tidak dibangun.

---

## 3. Topologi

Inti rancangan: **MCP server berada di dalam proses SolidWorks**, dan AI CLI adalah
subprocess yang dikelola add-in. Ini kebalikan dari pola umum (AI di luar menyetir
SolidWorks dari jauh lewat COM).

```
+-- SLDWORKS.exe -------------------------------------------------+
|                                                                 |
|  MikosAI.Addin   (C# net48, x64, COM-visible, in-process)       |
|                                                                 |
|   +-- Lapisan UI --------------------------------------+        |
|   |  TaskPane  ->  WebView2  ->  MikosAI.Web (chat)    |        |
|   |  CommandGroup (toolbar)                            |        |
|   |  PropertyManagerPage (input terstruktur)           |        |
|   +----------------------------------------------------+        |
|                                                                 |
|   +-- Lapisan kendali ---------------------------------+        |
|   |  MainThreadDispatcher   <- SEMUA panggilan API SW  |        |
|   |  CapabilityLedger       <- capabilities.yaml       |        |
|   |  PlanRunner             <- eksekusi DesignPlan     |        |
|   |  VerificationGate       <- baca ulang model        |        |
|   |  RecoveryAdvisor        <- klasifikasi error       |        |
|   |  Trace                  <- JSONL append-only       |        |
|   +----------------------------------------------------+        |
|                                                                 |
|   +-- Lapisan eksekusi --------------------------------+        |
|   |  ToolRegistry + ToolExecutor                       |        |
|   |   -> SolidWorks PIA langsung (in-process)          |        |
|   +----------------------------------------------------+        |
|                                                                 |
|   +-- Lapisan integrasi ------------------------------+         |
|   |  McpHttpServer   127.0.0.1:<ephemeral>            |         |
|   |  AgentWorkspace  -> tulis .agents/mcp_config.json |         |
|   |  ProviderHost    -> spawn `agy` lewat ConPTY      |         |
|   +---------------------^----------------------------+          |
+-------------------------|---------------------------------------+
                          | MCP (loopback HTTP)
                          v
               Antigravity CLI (`agy`, subprocess headless)
                          |
                          v  hanya untuk beban berat / tanpa CAD
               MikosAI.Sidecar (Python: OCCT, FEA, mesh, PDF readback)
```

### Kenapa in-process

| Aspek | Out-of-process (COM) | In-process (pilihan kita) |
|---|---|---|
| Marshalling COM | Rapuh, sering gagal VARIANT/SAFEARRAY | Tidak ada |
| Latensi per panggilan | Milidetik sampai puluhan ms | Mikrodetik |
| Event SolidWorks | Tidak praktis | Native |
| PropertyManagerPage | Tidak bisa | Bisa |
| Konteks seleksi pengguna | Harus di-polling | Langsung tersedia |
| Interface khusus in-process | Terblokir | Tersedia |
| Risiko utama | Koneksi gagal | Crash menjatuhkan SolidWorks |

Risiko terakhir itu nyata, dan itulah alasan `MainThreadDispatcher` dan
`VerificationGate` wajib ada sejak awal, bukan ditambahkan nanti.

---

## 4. Komponen

### 4.1 MikosAI.Addin (C#, net48, x64)

Target `net48` dipilih sengaja. Add-in SolidWorks di-load sebagai COM in-process ke dalam
CLR yang sudah dijalankan SolidWorks, dan `net48` adalah jalur yang paling teruji untuk
itu. Kebutuhan .NET modern ditempatkan di proses terpisah, bukan di dalam add-in.

Entry point mengimplementasikan `ISwAddin`:

- `ConnectToSW(object app, int cookie)` — simpan `ISldWorks`, panggil
  `SetAddinCallbackInfo2`, **tangkap thread utama di sini** (lihat 4.2), bangun
  CommandGroup dan TaskPane, tulis diagnostik.
- `DisconnectFromSW()` — hentikan ProviderHost, tutup McpHttpServer, lepas TaskPane,
  hapus CommandGroup, release COM.

**Gerbang penerimaan.** `ISldWorks.LoadAddIn()` mengembalikan `0` **tidak** dihitung lulus.
Add-in dianggap benar-benar hidup hanya jika file diagnostik
`%LOCALAPPDATA%\MikosAI\host-status.json` memenuhi semuanya:

1. `status == "connected"`
2. `callbackRegistered == true`
3. `commandGroup`, `taskPane`, `mcpServer` ketiganya `true`
4. setelah membuat part sementara, hitungan event `fileNew` dan dokumen aktif bertambah;
   setelah `CloseDoc`, `GetDocumentCount() == 0`
5. `errors` kosong

Dua jebakan yang sudah terdokumentasi di repo referensi dan harus kita jadikan test:

- Tanpa TLB terdaftar, SolidWorks bisa memuat DLL dan mengembalikan `LoadAddIn=0`
  **tanpa** pernah menginstansiasi `ISwAddin`.
- Handler `PropertyManagerPage` yang hanya menyediakan `IUnknown` tanpa `IDispatch` membuat
  `ICreatePropertyManagerPage` melempar `InvalidCastException`.

### 4.2 MainThreadDispatcher

Komponen paling penting dan paling mudah diabaikan.

API SolidWorks **tidak thread-safe** dan harus dipanggil dari thread STA utama yang
diberikan SolidWorks. Dalam arsitektur kita ada tiga sumber panggilan konkuren:

- thread `HttpListener` dari McpHttpServer,
- thread pembaca pipe dari ProviderHost,
- callback WebView2 dari UI.

Ketiganya **tidak boleh** menyentuh `ISldWorks` langsung.

Implementasi: pada `ConnectToSW`, buat satu `Control` WinForms (host TaskPane sudah
memberi ini). Control itu terikat ke thread utama, jadi `Control.BeginInvoke` menjadi kanal
marshalling kita.

```csharp
// Satu-satunya pintu ke API SolidWorks.
public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken ct);
```

Aturan yang ditegakkan lewat review: **tidak ada satu pun pemanggilan tipe
`SolidWorks.Interop.*` di luar delegate yang dijalankan dispatcher.** Pelanggaran aturan ini
menghasilkan crash acak yang tidak bisa direproduksi. Retrofit-nya sangat mahal — karena
itu ini ada di Fase 0, bukan nanti.

### 4.3 CapabilityLedger

Membaca `capabilities.yaml`, satu-satunya sumber kebenaran. Status per kemampuan:

| Status | Arti | Diekspos ke AI? |
|---|---|---|
| `verified` | Lulus golden test di SolidWorks nyata, versi tercatat | Ya |
| `pilot` | Jalan, tapi butuh review manusia | Ya, ditandai `review_required` |
| `reference_only` | Ada dokumentasi, belum ada implementasi teruji | **Tidak** |
| `not_implemented` | Belum ada | **Tidak** |

Ini mekanisme utama "mudah dipakai tapi tetap aman": AI secara struktural **tidak bisa**
memanggil hal yang belum kita uji, karena tool-nya tidak pernah muncul di daftar MCP.

Promosi ke `verified` butuh golden test lulus di instalasi nyata, versi SolidWorks dicatat,
dan tiga kali lulus berturut-turut.

### 4.4 DesignPlan dan PlanRunner

Artefak yang bisa diedit pengguna **bukan** makro, tapi **DesignPlan** — JSON parametrik
bervalidasi skema.

```
Bahasa natural
   |  (agy)
   v
DesignPlan (JSON, divalidasi skema)
   |  (PlanRunner -> ToolExecutor -> MainThreadDispatcher)
   v
Geometri SolidWorks
   |  (VerificationGate)
   v
Evidence + Trace
```

Kenapa plan, bukan makro: plan bisa dieksekusi ulang, divalidasi **sebelum** dijalankan,
di-diff antarversi, dan parameternya bisa diubah tanpa membaca kode. Makro hanya bisa
dijalankan dan dibaca.

Inilah lapis "overpower". Pengguna biasa tidak pernah melihatnya. Power user membuka panel
plan, mengubah `thickness: 10` jadi `12`, jalankan ulang — tanpa prompt ulang ke AI, tanpa
biaya token, dan hasilnya deterministik.

### 4.5 ToolRegistry dan ToolExecutor

Sekitar 18 tool untuk v0.1 (lihat `capabilities.yaml`). Setiap tool:

- punya skema input/output JSON,
- punya id kemampuan yang merujuk ledger,
- dieksekusi **hanya** lewat `MainThreadDispatcher`,
- mengembalikan hasil terstruktur, tidak pernah string bebas.

**Referensi entitas semantik.** `Face1` / `Edge1` / koordinat layar rapuh dan akan rusak
begitu ada fitur disisipkan. Tool kita memilih entitas lewat kriteria stabil — normal dan
centroid permukaan, atau persistent reference ID lewat
`IModelDoc2.Extension.GetPersistReference3` — bukan nama indeks. Ini bagian yang secara
jujur sulit dan punya jalur kerja sendiri di roadmap.

### 4.6 VerificationGate

Menerjemahkan hasil pemeriksaan ke satu bahasa, lalu mengagregasi:

```
BLOCKED > FAIL > WARN > PASS
```

Normalisasi: `pass` / `warn` / `review_required` menjadi `PASS` / `WARN`; `fail` / `failed`
menjadi `FAIL`; `blocked` menjadi `BLOCKED`; **apa pun yang tidak dikenal menjadi
`BLOCKED`**.

`PASS` di tingkat atas tidak boleh menutupi `FAIL` pada check yang `required`.

Untuk part modeling v0.1, verifikasi minimum setelah setiap operasi tulis:

1. fitur ada di FeatureManager tree dengan nama yang diharapkan,
2. `EditRebuild3` sukses dan tidak ada rebuild error,
3. status constraint sketsa dibaca lewat `GetConstrainedStatus` — bukan di-`Fix` massal,
4. `MassProperties` mengembalikan volume bukan nol,
5. **simpan, tutup, buka ulang**, lalu assert ulang nilai dimensi kunci lewat
   `IDimension.SystemValue`.

Poin 5 yang paling sering dilewati dan paling sering menangkap bug.

### 4.7 RecoveryAdvisor

Mengklasifikasi error ke salah satu dari: `transient`, `invalid_argument`,
`environment_missing`, `tool_unavailable`, `verification_failed`, `user_action_required`,
`policy_blocked`, `capability_gap`, `provider_silent`, `fatal`.

Lalu **mengusulkan** tindakan: `retry` (maksimum 2), `replan`, `user_action_required`,
`block`, `fail`. Advisor **tidak mengeksekusi**. Tidak yakin berarti `block` atau `fail`,
tidak pernah default ke `retry`. Ini yang mencegah AI mengulang-ulang operasi yang merusak
model.

`provider_silent` adalah kelas error khusus kita — lihat `docs/integrasi-antigravity.md`.

### 4.8 McpHttpServer

`HttpListener` di `127.0.0.1` dengan **port ephemeral** — ditentukan OS saat startup, bukan
angka tetap.

- Transport MCP streamable HTTP.
- Bearer token dibuat per sesi, ditulis ke `%LOCALAPPDATA%\MikosAI\session.json` dengan ACL
  hanya untuk pengguna saat ini.
- Hanya bind loopback. Tidak pernah `0.0.0.0`.
- Daftar tool adalah ledger yang difilter ke `verified` dan `pilot`.

Port ephemeral dimungkinkan karena `AgentWorkspace` (4.9) menulis konfigurasi MCP setiap
sesi, jadi tidak ada file statis yang perlu mencocokkan angka port. Keuntungan tambahan: dua
instance SolidWorks bisa berjalan bersamaan tanpa bentrok port.

### 4.9 AgentWorkspace

Add-in **memiliki direktori kerjanya sendiri** dan tidak pernah menyentuh konfigurasi
pribadi pengguna.

```
%LOCALAPPDATA%\MikosAI\agent-workspace\
└── .agents\
    └── mcp_config.json      <- ditulis ulang setiap ConnectToSW
```

Dibuat saat `ConnectToSW`, berisi URL dan token server MCP sesi ini, lalu `ProviderHost`
men-spawn `agy` dengan **cwd diarahkan ke folder ini** sehingga Antigravity membaca
konfigurasi per-proyek dari situ.

Kenapa bukan konfigurasi global `~/.gemini/config/mcp_config.json`:

- Konfigurasi pribadi pengguna tidak pernah dimodifikasi atau ditimpa.
- Tidak bentrok dengan MCP server lain yang pengguna pakai untuk pekerjaan sendiri.
- Port dan token selalu benar karena ditulis ulang per sesi.
- Uninstall bersih: hapus satu folder.

**Verifikasi terbuka.** Perlu dikonfirmasi bahwa `agy` benar-benar membaca
`.agents/mcp_config.json` relatif ke cwd, dan apakah konfigurasi per-proyek menggantikan
atau digabung dengan yang global. Kalau ternyata hanya konfigurasi global yang dibaca,
fallback-nya adalah menulis ke sana dengan merge non-destruktif — baca dulu, tambahkan entri
kita, tulis kembali, jangan pernah menimpa entri milik pengguna. Lihat
`docs/integrasi-antigravity.md` §7 item 6.

### 4.10 ProviderHost

Menjalankan AI CLI sebagai subprocess. **Wajib lewat ConPTY**, bukan pipe biasa — alasan
teknisnya dan cara deteksi kegagalannya ada di `docs/integrasi-antigravity.md`. Jangan
implementasikan bagian ini tanpa membaca dokumen itu lebih dulu.

Status provider dibedakan ketat, dan UI tidak boleh menyamakannya:

```
not_installed -> installed -> auth_failed
                           -> auth_unverified
                           -> auth_verified -> execution_verified
```

CLI ada di PATH bukan bukti model bisa dipanggil. Provider melaporkan sukses bukan bukti
geometri SolidWorks benar.

### 4.11 Trace

JSONL append-only di `%LOCALAPPDATA%\MikosAI\trace\`. Event: `run.started`, `step.started`,
`tool.called`, `tool.succeeded`, `tool.failed`, `verification.*`, `recovery.*`,
`run.completed` / `run.failed` / `run.blocked`.

Hanya ringkasan argumen dan hasil — **tidak** menyimpan prompt penuh, API key, token, atau
path absolut yang tidak perlu. Ada fungsi redaksi sebagai jaring pengaman.

Kegagalan menulis trace tidak boleh menggagalkan eksekusi; observabilitas adalah jalur
samping.

Pemisahan tanggung jawab: **Trace adalah riwayat proses, Evidence adalah fakta hasil
akhir.** Jangan dicampur.

---

## 5. Pengalaman dua lapis

Target "mudah dipakai tapi tetap overpower" dipenuhi dengan **dua lapis**, bukan satu
kompromi di tengah.

**Lapis 1 — mudah.** Chat bahasa natural di TaskPane, plus tombol preset. Pengguna tidak
pernah melihat JSON, tidak pernah melihat nama tool. Kalau sesuatu terblokir, pesannya
menjelaskan apa yang harus dilakukan manusia, bukan melempar kode error.

**Lapis 2 — overpower.** Opsional, di balik tab:

- panel DesignPlan yang bisa diedit dan dijalankan ulang,
- panel Evidence: apa yang diperiksa, apa hasilnya, kenapa `BLOCKED`,
- Trace viewer,
- override pemilihan model dan backend.

Kuncinya: kekuatan itu **opsional dan progresif**. Repo referensi yang kami bedah hanya
punya lapis 2 — itu sebabnya terasa berat dipakai.

---

## 6. Batasan yang diketahui

- SolidWorks mengunci DLL yang sudah dimuat. Jalankan `UnloadAddIn` atau tutup SolidWorks
  sebelum rebuild. Jangan timpa paksa.
- `FileCloseNotify` tingkat aplikasi hanya terpicu setelah `CloseAndReopen` pada drawing.
  Untuk part dan assembly, periksa jumlah dokumen atau berlangganan event dokumen.
- Layout CommandManager masuk ke konfigurasi pengguna. GUID, command group ID, dan command
  user ID harus stabil setelah rilis pertama.
- Penandatanganan strong name menstabilkan identitas assembly, tapi **bukan** code signing
  Authenticode. Distribusi butuh penandatanganan di pipeline rilis.
- PIA SolidWorks dirujuk dari instalasi lokal, tidak di-vendor ke repo — redistribusinya
  dibatasi lisensi.
- SolidWorks butuh seat lisensi dan sesi desktop interaktif.

---

## 7. Dokumen terkait

- `docs/integrasi-antigravity.md` — integrasi `agy`, termasuk bug non-TTY yang kritis
- `docs/ROADMAP.md` — fase, gerbang penerimaan, pembagian kerja
- `docs/bedah-repo-referensi.md` — hasil bedah repo yang menjadi acuan
- `docs/adr/` — catatan keputusan arsitektur
- `capabilities.yaml` — ledger kemampuan
