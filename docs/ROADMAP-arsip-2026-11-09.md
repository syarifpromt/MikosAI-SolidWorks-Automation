# Roadmap MikosAI SolidWorks Automation

Target v0.1: **12 minggu** untuk tim 2 orang paruh waktu
Ringkasan diperbarui 2026-11-07; checklist lama di bawah tetap memuat sisa gerbang.

---

## Status ringkas

| Fase | Isi | Progres |
|---|---|---|
| **0** | Fondasi | **add-in TERBUKTI HIDUP** di SW2025 SP01.1, 7/7 gerbang lulus; sisa 4 item test |
| 1 | MCP server + tool read-only | document_inspection terverifikasi pada part acuan; hitungan lima sketsa fixture SW_PASS 3× dengan lingkup terbatas |
| 2 | Provider host (`agy`) | jalur chat → agy → MCP → jawaban pernah dibuktikan; sisa gerbang tercatat di bawah |
| 3 | Jalur tulis | pilot balok 100×80×10 SW_PASS 3× termasuk simpan–buka ulang; variasi 120×50×12 lulus 1×. Belum diekspos ke AI atau terintegrasi PlanRunner produksi |
| 4 | Pengalaman dua lapis | belum mulai |
| 5 | Pengemasan | belum mulai |

Alur DesignPlan dengan executor palsu sudah diuji. Pilot native pertama
memakai `examples/block-pilot.plan.json` dan `scripts/golden-balok.ps1`;
lihat [pilot-balok.md](pilot-balok.md) untuk bukti yang dituntut dan batasnya.

Tool read-only sudah tersedia lewat MCP. Status tool tulis di ledger belum
dinaikkan; SW_PASS pilot manual terbatas pada alur/ukuran yang diuji, bukan
promosi AI atau bukti seluruh opsi tool. Test otomatis saat run pilot: 382 lulus.

---

## Filosofi

Satu alur sempurna lebih berharga daripada lima alur setengah jadi. v0.1 hanya mengejar
**satu** hal: bahasa natural menjadi part parametrik yang terverifikasi. Drawing, assembly,
FEA, dan multi-CAD sengaja ditunda.

Setiap fase punya **gerbang penerimaan** yang bisa dijawab ya/tidak. Tidak ada fase yang
"hampir selesai". Kalau gerbangnya belum lulus, fase berikutnya belum dimulai.

---

## Pembagian kerja

| | **Jalur Platform** | **Jalur CAD** |
|---|---|---|
| Fokus | Shell add-in, dispatcher, MCP server, provider host, UI | Tool CAD, verifikasi, golden test, ledger |
| Bahasa | C#, TypeScript | C#, SolidWorks API |
| Keahlian | Windows/COM/.NET, web | SolidWorks, engineering |

**Kontrak antara keduanya:** `schemas/tools.json` + `capabilities.yaml`. Keduanya sudah ada
dan diperlakukan sebagai API. Jalur Platform bekerja melawan `FakeToolExecutor`; Jalur CAD
bekerja melawan harness test tanpa UI. Mereka baru benar-benar bertemu di Fase 3.

Siapa mengambil jalur mana sebaiknya ditentukan oleh siapa yang lebih kuat SolidWorks API —
itu jalur yang lebih sulit dicari penggantinya.

---

## Persiapan per mesin

Tidak ada satu pun yang bisa dibagi antar anggota tim.

- [ ] Windows 10 1809+ — ~~ConPTY butuh versi ini~~ ConPTY tidak dipakai; pipe biasa cukup
- [ ] SolidWorks + seat lisensi — per mesin, tidak bisa headless
- [ ] .NET Framework 4.8 SDK
- [ ] `agy` terpasang — per mesin, kita tidak mendistribusikannya
- [ ] Login `agy` — **per akun Google**, kuota dan rate limit terpisah per orang
- [ ] Registrasi add-in Machine — butuh UAC sekali

~~WebView2 Runtime~~ **tidak lagi dibutuhkan.** Panelnya WinForms biasa sejak
WebView2 terbukti menjatuhkan SolidWorks lewat crash native yang tidak lewat CLR.
Itu menghapus satu prasyarat, satu runtime, dan satu DLL native dari setup.
Detailnya di `docs/setup.md`.

Yang masuk repo: kode, skema, `capabilities.yaml`, dokumen.
Yang tidak boleh: `session.json`, agent-workspace, kredensial, path mesin, nama user
Windows. Lihat `.gitignore`.

### Catatan soal lingkungan pengembangan

Status `verified` bertumpu pada satu asumsi: golden test lulus di SolidWorks yang
berperilaku seperti SolidWorks sungguhan. Instalasi yang dimodifikasi melemahkan itu dengan
dua cara nyata:

1. **Registrasi COM, ketersediaan PIA di `api\redist`, dan aktivasi `ISldWorks`** bergantung
   pada komponen yang bisa tersentuh modifikasi. Kalau add-in gagal load, penyebabnya jadi
   ambigu — tidak bisa dibedakan bug kita dari lingkungan yang rusak.
2. **`verified` jadi hanya berarti "jalan di mesin kami"** — bukan klaim yang bisa dipegang
   orang lain.

Karena itu gerbang Fase 5 mensyaratkan golden test lulus setidaknya sekali di instalasi
berlisensi bersih sebelum apa pun dirilis ke luar.

Kabar baiknya: **Fase 0 sampai 2 hampir seluruhnya tidak butuh SolidWorks sungguhan.**
Yang benar-benar menuntutnya adalah Fase 3 ke atas.

Catatan saat memilih lisensi: **SOLIDWORKS for Makers dilaporkan tidak mendukung add-in
pihak ketiga**, jadi edisi itu tidak bisa dipakai walaupun murah. Pastikan dukungan add-in
dan API sebelum membeli apa pun.

---

## Fase 0 — Fondasi (Minggu 1–2)

Tidak ada AI sama sekali di fase ini. Tujuannya: add-in benar-benar hidup di dalam
SolidWorks dan punya kanal thread yang benar.

### Jalur Platform

- [x] Solution C#, target `net48`, `.sln` klasik agar bisa dibuka tooling non-VS
- [x] `Tracing/TraceWriter.cs` — JSONL append-only dengan redaksi
- [x] `Threading/IMainThreadDispatcher.cs` — abstraksi
- [x] **`MainThreadDispatcher` implementasi nyata** lewat `Control.BeginInvoke` — risiko R4 *(sudah lewat compile-check)*
- [x] `MikosAI.Addin` mengimplementasikan `ISwAddin` + `SetAddinCallbackInfo2` *(sudah lewat compile-check)*
- [x] CommandGroup — tab ribbon + menu untuk tampil/sembunyikan panel.
      Toggle-nya memakai `IsActiveTab()`, karena SolidWorks tidak menyediakan
      "apakah panel terlihat"; itu pertanyaan terdekat yang bisa dijawab tanpa
      mengarang. **Tab muncul di ribbon — terbukti 2026-11-05** pada build
      `4442e69`, dengan `commandGroup` HIDUP dan nol galat di `host-status.json`.
      Perilaku toggle-nya sendiri belum dilaporkan, jadi belum diklaim.

      Versi pertama hanya memanggil `CreateCommandGroup2` + `HasToolbar`, dan itu
      TIDAK cukup: tombolnya hanya lahir di menu Tools dan di toolbar klasik yang
      tersembunyi selama CommandManager aktif — yaitu tidak terlihat sama sekali.
      Tab ribbon menuntut `AddCommandTab` → `AddCommandTabBox` → `AddCommands`.
      Add-in melapor `commandGroup: true` dengan jujur sementara penggunanya
      mencari tombol yang tidak ada di tempat yang ia lihat: **diagnostik yang
      benar tapi tidak menjawab pertanyaan orangnya tetap gagal.**
- [x] TaskPane — panel WinForms biasa (WebView2 menjatuhkan SolidWorks, jadi opt-in)
- [x] Penulis diagnostik `host-status.json`
- [x] Script PowerShell build / register / unregister / probe / status (RegAsm 64-bit + TLB)
- [x] **`setup.ps1` satu perintah** — periksa, build, test, registrasi, UAC otomatis
- [x] **`doctor.ps1`** — deteksi lingkungan + saran perbaikan per item
- [x] **`-Action CompileCheck`** — verifikasi kode add-in tanpa SolidWorks lewat stub `ISwAddin`
- [x] Deteksi otomatis path PIA lewat registry CLSID dan kunci Setup per versi
- [x] ~~Generator `.snk`~~ → strong naming dihapus, tidak dibutuhkan `RegAsm /codebase`
- [x] `launch.json` + `tasks.json` untuk VS Code
- [x] Set platform `x64` untuk proyek Addin

### Jalur CAD

- [x] `capabilities.yaml` versi awal, semua entri `not_implemented`
- [x] `Capabilities/CapabilityLedger.cs` — loader, gating, validator
- [x] `schemas/design-plan.schema.json`
- [x] `schemas/tools.json` — 18 tool CAD + 2 orchestration
- [x] `schemas/common.schema.json` — Length, EntityRef, ToolResult, Verification
- [x] `Plan/PlanValidator.cs` — aturan semantik plan
- [x] `Tools/ToolCatalog.cs` — katalog + validasi silang terhadap ledger
- [x] Harness golden test tanpa SolidWorks — `PlanRunner` + `FakeToolExecutor`
- [x] `examples/mounting-plate.plan.json` sebagai target

### Sudah selesai di `MikosAI.Core`

264 test hijau, dibuild dan dijalankan tanpa SolidWorks dan tanpa Visual Studio:

| Berkas | Isi |
|---|---|
| `Execution/VerificationGate.cs` | Normalisasi dan agregasi putusan |
| `Execution/RecoveryAdvisor.cs` | 11 kelas error, keputusan tanpa eksekusi |
| `Execution/ToolResult.cs` | Envelope, `Ok` dipisah dari `IsTaskSuccessful` |
| `Execution/IToolExecutor.cs` | Kontrak executor + `IAcceptanceEvaluator` |
| `Capabilities/CapabilityLedger.cs` | Pembaca `capabilities.yaml` dan gating |
| `Tools/ToolCatalog.cs` | Katalog tool dan validasi silang |
| `Plan/PlanValidator.cs` | Aturan semantik plan |
| `Plan/PlanRunner.cs` | Orkestrasi step, agregasi, acceptance |
| `Plan/ParameterResolver.cs` | Resolusi `$nama` dan override |
| `Threading/IMainThreadDispatcher.cs` | Abstraksi; implementasi nyata di Addin |
| `Tracing/TraceWriter.cs` | JSONL dengan redaksi, tidak pernah melempar exception |
| `Testing/FakeToolExecutor.cs` | Executor + evaluator palsu, scriptable |
| `Agents/ProviderState.cs` | 6 keadaan provider + aturan apa yang boleh diklaim |
| `Agents/IAgentProvider.cs` | Kontrak provider: command builder + result parser |
| `Agents/ProviderOutcome.cs` | Deteksi provider diam, pembersih ANSI, normalisasi hasil |
| `Tools/ReadOnlyToolShaper.cs` | Aturan sehat/tidaknya pembacaan model |
| `Mcp/McpHttpServer.cs` | HTTP loopback, port ephemeral, bearer token |
| `Mcp/McpRequestHandler.cs` | Protokol JSON-RPC MCP, terpisah dari transport |
| `Mcp/McpToolDescriptor.cs` | Daftar tool MCP dari katalog + ledger |
| `Mcp/AgentWorkspace.cs` | Workspace milik sendiri, penulis `mcp_config.json` |

`MikosAI.Core` sengaja **tidak** punya dependensi SolidWorks. Itu yang membuat Jalur
Platform bisa bekerja penuh tanpa CAD.

### Sisa Fase 0

Add-in sudah terbukti hidup. Yang tersisa hanya pekerjaan test dan kesepakatan:

- [ ] Test: tanpa TLB, add-in tidak terinstansiasi walaupun `LoadAddIn=0`
- [x] Test: setiap metode yang menyentuh API menolak thread selain utama — `AddinThreadGuardTests`
- [x] Tinjau `schemas/tools.json` bersama — tanggapan di
      `docs/tinjauan-tools-json.md`. Tinjauan itu menemukan lubang nyata; meninjau
      balik menemukan yang tidak disebutnya: **kontrak menjanjikan empat field yang
      pembacanya tidak pernah isi**, dan salah satunya membuat assertion golden test
      kami sendiri hampa. Tiga keputusan harus diambil SEBELUM tool tulis ditulis,
      karena ketiganya membentuk tanda tangan tool: kebijakan recovery, sistem
      koordinat `hole_wizard.positions`, dan nasib `fully_define_sketch` setelah
      `add_relation` ada

### Gerbang penerimaan Fase 0

- [x] Add-in diinstansiasi oleh `SLDWORKS.exe` — terbukti, `status: connected`
- [x] Panggilan API SolidWorks bekerja — `RevisionNumber` = `33.1.1`
- [x] Callback terdaftar — `SetAddinCallbackInfo2` sukses setelah `AutoDispatch`
- [x] Objek add-in mengekspos IDispatch — diuji sendiri lewat `Marshal.GetIDispatchForObject`
- [x] `host-status.json` memenuhi semua syarat — **7/7 gerbang lulus di SW2025 SP01.1**
- [ ] Test membuktikan: tanpa TLB, add-in **tidak** terinstansiasi walaupun `LoadAddIn=0`
- [ ] Test membuktikan: handler PMP tanpa `IDispatch` melempar `InvalidCastException`
- [x] `MainThreadDispatcher` menangkap thread utama — terbukti, `dispatcherReady: true`
- [x] Pelanggaran thread menjadi exception tercatat, bukan crash — assert di `SolidWorksApi` dan `SolidWorksReader`
- [x] Test otomatis untuk assert itu — ditemukan lewat refleksi, jadi metode baru yang lupa penjaga langsung merah
- [x] **Sesi penuh selesai bersih** — TaskPane terpasang, MCP hidup, lalu
      SolidWorks ditutup normal dan `DisconnectFromSW` berjalan sampai habis.
      Dibuktikan `status: disconnected` di `host-status.json` dan `crash.log`
      kosong dari exception — bukan sekadar tidak adanya laporan crash.
- [x] `capabilities.yaml` tervalidasi

---

## Fase 1 — MCP server + tool read-only (Minggu 3–4)

### Jalur Platform

- [x] `McpHttpServer` di `127.0.0.1`, port ephemeral, HTTP loopback
- [x] Bearer token per sesi, ditulis ke `session.json` ber-ACL
- [x] Daftar tool di-generate dari `ToolCatalog.ExposableTools()`
- [x] `AgentWorkspace`: folder sendiri di `%LOCALAPPDATA%`, tulis `.agents/mcp_config.json`
      tiap sesi, **jangan sentuh config global pengguna**
- [x] `McpRequestHandler`: JSON-RPC initialize / tools/list / tools/call / ping
- [x] Hubungkan `McpHttpServer` ke add-in di `ConnectToSW`
- [x] TaskPane dengan panel status — WinForms biasa, WebView2 opt-in
- [x] `SolidWorksToolExecutor` — semua pembacaan lewat `MainThreadDispatcher`

### Jalur CAD — 6 tool read-only, aman karena tidak menulis apa pun

- [x] `get_active_document`
- [x] `list_features`
- [x] `get_bounding_box`
- [x] `get_mass_properties`
- [x] `inspect_sketches`
- [x] `get_selection` — mengembalikan `EntityRef` semantik, bukan `Face1`
- [x] Pembentuk `EntityRef` untuk `name`, `normal_and_point`, dan `midpoint`
- [ ] `EntityRef` *resolver* (arah sebaliknya: ref → entitas), untuk tool yang menulis

### Golden test baca — menaikkan `document_inspection` dari `pilot` ke `verified`

- [x] Harness: `scripts/golden-document.ps1` + `tests/golden/plat-referensi.json`.
      Nilai harapannya **diturunkan dari geometri**, bukan disalin dari keluaran
      tool — itu bedanya golden test dengan snapshot. Snapshot hanya menjawab
      "apakah hasilnya sama seperti kemarin", dan kalau pembacaannya sudah salah
      sejak awal ia membekukan kesalahan itu lalu menyebutnya lulus. Kami sudah
      pernah membayar persis itu: `massGrams` yang dikarang dari densitas air
- [x] Harness dibuktikan bisa GAGAL, bukan cuma bisa lulus — 26 lulus/exit 0 saat
      cocok, 1 gagal/exit 1 dengan selisih tepat saat satu nilai dirusak
- [x] Pemeriksaan silang `massa = volume × densitas`, ketiganya dibaca terpisah,
      sehingga satu nilai yang dikarang memecah hubungannya
- [x] Pembacaan identik 3× berturut-turut diuji
- [x] **`tests/golden/plat-referensi.SLDPRT` dibuat dan di-commit** — plat
      100×80×10 mm, Plain Carbon Steel, berpusat di origin
- [x] **Golden test LULUS — 2026-11-05.** 33 pemeriksaan, 0 gagal, empat kali
      berturut-turut, pada add-in build `62d5aa8` yang sama dengan HEAD saat diuji.
      `document_inspection` naik dari `pilot` ke **`verified`**, dan `33.1.1`
      tercatat di `verified_versions`

      Golden test ini membayar dirinya pada percobaan pertama: ia **menolak**
      pembacaan kami sendiri dan membongkar pemetaan `swConstrainedStatus_e`
      yang bergeser satu — sketsa *under-defined* dilaporkan *FullyDefined*,
      yaitu rusak yang terlihat sehat. Acuan yang jawabannya sudah diketahui
      lebih dulu bisa membantah pembacaan; snapshot hanya bisa mengulanginya

Ukurannya sengaja sama dengan basis plat golden Fase 3 (100×80×10), supaya jalur
baca dan jalur tulis nanti diuji di atas geometri yang sama.

### Gerbang penerimaan Fase 1

- [x] Round-trip MCP terbukti lewat klien sungguhan — `scripts\mcp-roundtrip.ps1`:
      initialize, tools/list, 6x tools/call, dan token salah ditolak 401.
      Diuji terhadap `tools\MikosAI.McpHarness` (server produksi, executor palsu):
      9 lulus 0 gagal. Dua uji negatif membuktikan verifikatornya bisa merah.
- [x] Hal yang sama terhadap add-in SUNGGUHAN di dalam SolidWorks — 9 lulus 0 gagal,
      dua kali: dokumen kosong, lalu part dengan solid body. Memeriksa isi jawabannya
      menemukan dua cacat yang gerbang transport TIDAK BISA tangkap — ukuran nol yang
      dikarang, dan massa dari densitas default. Keduanya sudah ditutup test.
- [x] **`agy` memanggil tool kita sendiri** — terbukti: `agy` memanggil
      `get_active_document` di dalam SolidWorks yang berjalan dan melaporkan
      `Part1.SLDPRT`, `part`, `Default`, `33.1.1` apa adanya. 10 lulus 0 gagal.
      Butuh aturan izin `mcp(mikosai-solidworks/*)` — lihat bagian 6
      `docs/integrasi-antigravity.md`, yang dugaannya sempat salah soal ini.
- [x] Semua 6 tool dieksekusi lewat dispatcher — dibuktikan test, bukan inspeksi mata
- [x] Tool berstatus `reference_only` **tidak muncul** di daftar MCP
- [x] Request tanpa bearer token ditolak
- [x] Server hanya bind loopback — ~~butuh mesin lain di jaringan~~ **tidak**: kalau
      listener-nya bind `0.0.0.0`, ia menerima koneksi ke IP LAN mesin ini sendiri.
      Jadi menembak IP sendiri sudah membedakan `127.0.0.1` dari `0.0.0.0`.
      Diuji terhadap 3 alamat non-loopback sungguhan, dan satu test terpisah
      membuktikan **detektornya tidak hampa**: sebuah listener sengaja dibind ke
      `0.0.0.0` dan detektor yang sama HARUS menemukannya. Yang tetap tidak
      terjangkau cuma firewall dan NAT — dan itu bukan properti kode kita.
- [x] ~~Config global `~/.gemini/config/mcp_config.json` **tidak berubah** setelah add-in
      berjalan~~ — **gerbang ini salah, dan sempat tercentang keliru.**

      Add-in MEMANG mengubahnya, dengan sengaja: pendaftaran MCP lewat `agy mcp add`,
      sesuai keputusan di `docs/integrasi-antigravity.md` bagian 3. Gerbangnya ditulis
      waktu rancangannya masih menulis `.agents/mcp_config.json` sendiri.

      Test yang membuatnya tercentang menguji `AgentWorkspace.Prepare`, dan untuk itu ia
      benar — tapi `Prepare` bukan satu-satunya yang berjalan. Pelajarannya: test hijau
      hanya membuktikan apa yang diuji, bukan apa yang tertulis di gerbangnya.

      Gerbang yang benar ada di bawah.
- [x] Satu-satunya perubahan add-in pada config global adalah entry `mikosai-solidworks`
      lewat `agy mcp add`, dan entry itu dihapus saat disconnect lewat `agy mcp remove`
- [x] `AgentWorkspace.Prepare` sendiri tidak menyentuh config global — hash sebelum
      dan sesudah
- [x] **Token tidak lagi ditulis ke berkas tanpa pembaca** — `.agents/mcp_config.json`
      terbukti DIABAIKAN `agy`, jadi penulisannya dihapus bersama `BuildMcpConfig`,
      `WriteMcpConfig`, dan `McpConfigPath`. Di dalam workspace kita, token sekarang
      hanya ada di `session.json` — yang punya pembaca dan ACL khusus pengguna.
      Dijaga test yang mencari token di SELURUH berkas workspace, jadi salinan baru
      langsung merah. Lihat catatan item 6 di `docs/integrasi-antigravity.md`.
- [x] URL non-loopback ditolak sebelum ditulis ke konfigurasi AI — jaring pengamannya
      sudah ada di kode sejak awal, sampai sekarang tidak ada yang membuktikannya jalan
- [x] Token sesi tidak pernah berulang dan panjangnya cukup — 200 iterasi

---

## Fase 2 — Provider host (Minggu 5–6)

Fase paling berisiko. **Baca `docs/integrasi-antigravity.md` seluruhnya sebelum mulai.**

### Verifikasi `agy` nyata — kerjakan SEBELUM menulis kode

- [x] 1. Versi `agy` terpasang dan flag apa yang ada di `--help`
- [x] 2. Apakah bug silent non-TTY masih ada di versi itu
- [x] 3. Nama field sebenarnya untuk isi jawaban di envelope `json`
- [x] 4. Apakah `stream-json` didukung, dan bentuk event-nya
- [x] 5. Apakah entry `serverUrl` mendukung custom header untuk bearer token
- [x] 6. ~~Apakah `.agents/mcp_config.json` dibaca relatif ke cwd~~ — **TIDAK, diabaikan
      sepenuhnya.** Pendaftaran global lewat `agy mcp add`. Penulisan berkas itu sudah
      dihapus beserta salinan token di dalamnya
- [x] 7. Rate limit — satu permintaan ~4 detik, ~6.8k token input; batas belum diuji
- [ ] 8. ToS: orkestrasi eksternal dengan pemakaian berbasis akun — **risiko legal, bukan teknis**

### Jalur Platform

- [x] ~~ConPTY~~ → **tidak diperlukan**, pipe biasa cukup di 1.4.11
- [x] `ProviderHost` menjalankan proses `agy` sungguhan
- [x] Pembersih ANSI / spinner / repaint carriage-return (dipertahankan, murah)
- [x] Flag probing dijalankan saat startup
- [x] Parser `stream-json` dan `json` — diuji terhadap payload nyata
- [x] **Deteksi `provider_silent`** — exit 0 + stdout kosong adalah kegagalan
- [x] State machine provider dan tampilannya di UI
- [x] UI chat: kirim prompt, tampilkan hasil
- [x] Progres bertahap di chat lewat `text_delta`

### Jalur CAD

- [x] `VerificationGate` + agregasi `BLOCKED > FAIL > WARN > PASS`
- [x] `RecoveryAdvisor` + klasifikasi error
- [x] Golden test deterministik dengan fake handler, tanpa SolidWorks, tanpa jaringan

### Gerbang penerimaan Fase 2

- [x] Chat di TaskPane → `agy` → MCP → tool read-only → jawaban muncul di UI
      — **terbukti 2026-11-04.** Diketik di panel: "dokumen apa yang aktif sekarang?",
      dijawab `Part1.SLDPRT` / `part` / `Default` / `33.1.1` / `isDirty: false`, cocok
      dengan apa yang dibaca `mcp-roundtrip.ps1` langsung dari tool. Empat bug berurutan
      harus dilewati lebih dulu, dan **keempatnya melapor berhasil sambil gagal**:
      aturan izin MCP tidak pernah dikirim; stderr dibuang justru pada exit code 0;
      pembersih keluaran menghancurkan teks CRLF. Yang terakhir dan termahal: probe
      membaca `--help` dari stdout sementara `agy` menulisnya ke stderr
- [ ] ~~Bug silent non-TTY terbukti tertangani — ada test yang gagal kalau ConPTY
      dilepas~~ **DIBATALKAN: premisnya salah.** Item verifikasi 2 membuktikan pipe
      biasa cukup; ConPTY tidak dibutuhkan. Kekosongan yang kami kira bug TTY sebenarnya
      empat hal di atas. Nasihat "pakai ConPTY" sudah dihapus dari pesan galat, dan test
      yang MENUNTUT nasihat itu ada sudah dibalik — ia mengabadikan dugaan yang sudah
      terbantah. Lihat docs/integrasi-antigravity.md bagian 4
- [ ] Keadaan belum terautentikasi memberi panduan jelas, bukan error mentah
- [x] **7 dari 8** item verifikasi terjawab dan terdokumentasi. Yang tersisa hanya item 8
      (ToS), yang risiko legal — bukan teknis, dan bukan milik kode untuk menjawabnya
- [x] Status tidak dikenal mendarat di `BLOCKED`, dibuktikan test
- [x] `RecoveryAdvisor` tidak pernah mengusulkan `retry` untuk error tidak dikenal

---

## Fase 3 — Jalur tulis (Minggu 7–9)

Fase terberat dan paling bernilai. Di sini kedua jalur bertemu.

### Jalur CAD — 12 tool tulis

Sketsa:

- [ ] `create_sketch`
- [ ] `add_rectangle`
- [ ] `add_circle`
- [ ] `add_slot`
- [ ] `fully_define_sketch`
- [ ] `close_sketch`

Fitur:

- [ ] `extrude_boss` — **tulis test khusus urutan parameter `FeatureExtrusion2/3`**
- [ ] `extrude_cut` — verifikasi WAJIB memeriksa volume berkurang
- [ ] `fillet`
- [ ] `chamfer`
- [ ] `hole_wizard` — `BLOCKED` kalau `standard`/`fastenerSize` tidak diberikan
- [ ] `linear_pattern`

Verifikasi:

- [ ] Fitur ada di FeatureManager tree dengan nama yang diharapkan
- [ ] `EditRebuild3` sukses, tanpa rebuild error
- [ ] Status constraint lewat `GetConstrainedStatus` — **bukan** `Fix` massal
- [ ] `MassProperties` volume bukan nol
- [ ] **Simpan, tutup, buka ulang, assert ulang dimensi kunci** — paling sering dilewati
- [ ] Setiap tool `EntitySelector` mengembalikan jumlah entitas yang benar-benar terpilih

### Jalur Platform

- [x] `PlanRunner` eksekusi step demi step dengan trace
- [x] Validasi plan terhadap skema **sebelum** eksekusi
- [ ] `IAcceptanceEvaluator` implementasi nyata
- [ ] Prompt sistem yang mengarahkan `agy` menghasilkan DesignPlan, bukan narasi
- [ ] Panel Evidence di UI

### Gerbang penerimaan Fase 3

- [ ] Golden test **"mounting plate 100×80×10, 4 lubang M6 counterbore"** lulus
      **3 kali berturut-turut** di SolidWorks nyata ← *definisi "v0.1 bekerja"*
- [ ] Setiap fitur terverifikasi lewat baca-ulang setelah simpan-buka ulang
- [ ] Sketsa fully defined lewat dimensi desain, **bukan** `Fix` massal
- [ ] Tidak ada tool yang memakai `Face1`/`Edge1`/koordinat layar
- [ ] Operasi gagal meninggalkan model dalam keadaan yang bisa dipulihkan pengguna
- [ ] Tidak ada crash SolidWorks dalam 50 eksekusi plan berturut-turut ← *definisi "layak dipakai"*

---

## Fase 4 — Pengalaman dua lapis (Minggu 10–11)

"Mudah dipakai" dikerjakan sebagai pekerjaan sungguhan, bukan sisa waktu.

### Lapis 1 — mudah

- [ ] Tombol preset
- [ ] Contoh prompt
- [ ] Pesan error yang menjelaskan tindakan manusia, bukan kode internal
- [x] Onboarding: deteksi `agy` belum terpasang atau belum login, lalu pandu

### Lapis 2 — overpower

- [ ] Editor DesignPlan + jalankan ulang
- [ ] Trace viewer
- [ ] Panel Evidence
- [ ] Override pemilihan model dan backend

### Lain-lain

- [ ] Tema terang/gelap mengikuti SolidWorks
- [ ] Perilaku saat SolidWorks sibuk / dokumen tidak ada / lisensi tidak aktif

### Gerbang penerimaan Fase 4

- [ ] Orang yang belum pernah melihat proyek ini membuat part lewat Lapis 1 tanpa dibantu
- [ ] Mengubah satu parameter di DesignPlan dan menjalankan ulang **tidak memanggil AI**
- [ ] Setiap keadaan `BLOCKED` punya pesan yang menyebut tindakan konkret
- [ ] Tidak ada jargon internal (`capability_gap`, `provider_silent`) terlihat di Lapis 1

---

## Fase 5 — Pengemasan (Minggu 12)

- [ ] Installer (registrasi Machine + UAC sekali)
- [ ] Code signing Authenticode untuk DLL dan installer
- [ ] `capabilities.yaml` di-tag versi, matriks versi SolidWorks yang diuji
- [ ] README pemakaian + panduan troubleshooting
- [ ] Checklist rilis + script verifikasi

### Gerbang penerimaan Fase 5

- [ ] Install bersih di mesin yang belum pernah dipakai development berhasil
- [ ] Uninstall bersih, tidak meninggalkan registry atau file
- [ ] Config global `agy` milik pengguna tidak tersentuh sebelum maupun sesudah uninstall
- [ ] Matriks versi menyatakan dengan jujur apa yang diuji dan apa yang tidak
- [ ] **Golden test Fase 3 lulus setidaknya sekali di instalasi SolidWorks berlisensi dan
      bersih** — tanpa ini, `verified_versions` tidak boleh diisi

---

## Setelah v0.1

Urutan mengikuti nilai dibagi risiko, bukan keinginan.

- [ ] **1. Pengerasan referensi entitas semantik** — fondasi semua hal lanjutan; `Face1` akan
      jadi utang teknis terbesar
- [ ] **2. Otomasi drawing** — nilai bisnis tertinggi, tapi API drawing rapuh antarversi;
      butuh matriks versi sendiri
- [ ] **3. Assembly dan mate** — butuh referensi semantik yang sudah kuat
- [ ] **4. Sidecar Python** (OCCT, mesh, baca ulang PDF) — verifikasi drawing lewat PDF
      sangat berharga
- [ ] **5. Provider kedua** (Claude Code / Gemini CLI) — membuktikan abstraksi provider
      benar-benar bekerja
- [ ] **6. Ekspresi parameter** — derived parameters, atau global variables SolidWorks
      (lebih benar secara CAD). Lihat `skema-tool-dan-plan.md` §2
- [ ] **7. FEA preflight** — butuh solver eksternal, selalu `pilot`, selalu butuh review
- [ ] **8. Multi-CAD** — hanya kalau v0.1 benar-benar stabil

---

## Registry risiko

| # | Risiko | Dampak | Mitigasi | Status |
|---|---|---|---|---|
| R1 | `agy` diam ketika stdout bukan TTY | ~~Kritis~~ → **rendah** | **Diuji 1.4.11: bug TIDAK ADA.** ConPTY tidak diperlukan; deteksi `provider_silent` dipertahankan sebagai jaring pengaman | ✅ **terjawab** |
| R2 | Flag `agy` berubah antarrilis | Tinggi | Probing `--help` saat startup; pin versi teruji | ⬜ |
| R3 | ToS: orkestrasi eksternal dengan akun subscription | Tinggi, **legal** | Abstraksi provider; jalur API key; cek ToS sebelum distribusi | ⬜ |
| R4 | API SolidWorks tidak thread-safe | **Kritis** — crash acak tak bisa direproduksi | `MainThreadDispatcher` + **assert di setiap pemanggilan API**; aturan review terbukti TIDAK cukup | ✅ ditegakkan kode |
| R5 | Crash add-in menjatuhkan SolidWorks | Tinggi | Guard di setiap operasi; saklar fitur untuk mempersempit | ✅ **TERJADI 2026-11-01, selesai 2026-11-04** — penyebabnya WebView2 (lihat R20). Menunda inisialisasi, mencabut polling, dan menutup re-entrancy COM semuanya TIDAK menolong; yang menyelesaikannya adalah membuang WebView2. Sesi penuh kini selesai bersih. |
| R6 | Referensi `Face1`/`Edge1` rusak saat fitur disisipkan | Tinggi | Referensi semantik; dilarang lewat skema | Skema melarangnya ✅ |
| R7 | SolidWorks mengunci DLL yang dimuat | Sedang | `UnloadAddIn` sebelum rebuild; didokumentasikan | ✅ terdokumentasi |
| R8 | Rate limit terasa saat pemakaian interaktif | Sedang | DesignPlan bisa dijalankan ulang tanpa AI | Override parameter ✅; satu permintaan ~4 detik, ~6.8k token input |
| R9 | Drift versi SolidWorks (2024/2025/2026) | Sedang | Matriks versi di ledger; `verified` selalu menyebut versi | Ledger siap ✅ |
| R10 | Redistribusi PIA dibatasi lisensi | Sedang | Rujuk instalasi lokal, jangan vendor ke repo | ✅ terdokumentasi |
| R11 | Dua orang, cakupan membengkak | Tinggi | Gerbang penerimaan per fase; "kecil itu fitur" | ✅ |
| R12 | Tanpa Visual Studio, step-debug net48 terbatas | Sedang | Diagnostik-first (`host-status.json`, trace); Core tanpa dependensi SolidWorks diuji cepat | Core ✅, diagnostik ✅ |
| R13 | Kode add-in tidak bisa diverifikasi tanpa SolidWorks | Tinggi | Stub PIA compile-check (`-Action CompileCheck`) | ✅ |
| R14 | Dependensi add-in `/codebase` dicari di folder SLDWORKS.exe, bukan folder kita | **Kritis** — add-in diam-diam tidak terinstansiasi, tanpa pesan error | `AssemblyResolver` dipasang di static ctor; `HostDiagnostics` nol dependensi eksternal | ✅ terbukti |
| R15 | API SolidWorks tidak bisa dipanggil lewat `dynamic`/IDispatch | Tinggi — semua panggilan gagal `0x8002802B` | PIA bertipe wajib; `EmbedInteropTypes` mempertahankan keuntungan lintas-versi | ✅ **selesai, `RevisionNumber` terbaca** |
| R16 | Kelas add-in tanpa IDispatch → `SetAddinCallbackInfo2` gagal `InvalidCastException` | Tinggi — callback SolidWorks tidak bisa didaftarkan | `ClassInterfaceType.AutoDispatch`; uji `Marshal.GetIDispatchForObject` sendiri + gerbang probe | ✅ **selesai, terbukti** |
| R18 | `AssemblyResolve` global dimintai dependensi add-in LAIN | Sedang — bisa menyerahkan assembly salah ke add-in lain | Permintaan yang bukan dari folder kami ditolak lebih dulu | ✅ |
| R20 | **WebView2 menjatuhkan SolidWorks** — crash native, tidak tertangkap CLR | **Kritis** | Panel WinForms biasa jadi default; WebView2 opt-in lewat `MIKOSAI_ENABLE_WEBVIEW2` | ✅ dihindari |
| R19 | `agy` terpasang tapi tidak di PATH | Tinggi — deteksi gagal padahal provider siap | Cari `%USERPROFILE%\.geminiingy.exe` langsung, bukan hanya PATH | ✅ **ditemukan di mesin nyata** |
| R17 | Menguji DLL usang karena `git pull` terlewat | Sedang — mendiagnosis bug yang sudah diperbaiki | Commit dicap ke metadata assembly; probe membandingkan dengan HEAD | ✅ |

R1, R3, R4 yang paling perlu diawasi. R1 dan R4 bisa diselesaikan dengan kode. R3 tidak —
itu keputusan, bukan bug.

---

## Yang sengaja tidak kita bangun

Pelajaran dari membedah repo referensi — mereka membangun semua ini sebelum punya pengguna.

- Lapisan attestation/governance dengan trust root dan drift checking
- File evidence SHA-256 yang di-commit ke repo
- Aplikasi desktop terpisah (TaskPane adalah satu-satunya UI)
- Dua framework UI yang bersaing
- Test untuk test runner
- Dukungan multi-CAD sebelum satu CAD benar-benar bekerja

Kalau nanti kita butuh salah satunya, kita bangun saat itu — dengan alasan yang nyata.
