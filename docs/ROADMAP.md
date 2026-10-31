# Roadmap MikosAI SolidWorks Automation

Status: draft v0.1 · Terakhir diperbarui 2026-10-31
Target v0.1: **12 minggu** untuk tim 2 orang paruh waktu

---

## Filosofi roadmap ini

Satu alur sempurna lebih berharga daripada lima alur setengah jadi. v0.1 hanya mengejar
**satu** hal: bahasa natural menjadi part parametrik yang terverifikasi. Drawing, assembly,
FEA, dan multi-CAD sengaja ditunda — semuanya menumpuk di atas fondasi yang sama, jadi
fondasinya harus benar dulu.

Setiap fase punya **gerbang penerimaan** yang bisa dijawab ya/tidak. Tidak ada fase yang
"hampir selesai". Kalau gerbangnya belum lulus, fase berikutnya belum dimulai.

---

## Pembagian kerja

Dua jalur yang bisa berjalan paralel karena kontraknya jelas.

| | **Jalur Platform** | **Jalur CAD** |
|---|---|---|
| Fokus | Shell add-in, dispatcher, MCP server, provider host, UI | Tool CAD, verifikasi, golden test, ledger |
| Bahasa | C#, TypeScript | C#, SolidWorks API |
| Keahlian | Windows/COM/.NET, web | SolidWorks, engineering |

**Kontrak antara keduanya:** skema tool JSON + `capabilities.yaml`. Keduanya ditulis
bersama di Fase 0 dan setelah itu diperlakukan sebagai API. Jalur Platform bekerja melawan
fake tool executor; Jalur CAD bekerja melawan harness test tanpa UI. Mereka baru benar-benar
bertemu di Fase 3.

Siapa mengambil jalur mana sebaiknya ditentukan oleh siapa yang lebih kuat SolidWorks API —
itu jalur yang lebih sulit dicari penggantinya.

---

## Persiapan per mesin

Tidak ada satu pun dari daftar ini yang bisa dibagi antar anggota tim. Setiap orang
menyiapkannya sendiri di mesinnya.

| Kebutuhan | Lingkup | Catatan |
|---|---|---|
| Windows 10 1809+ | Per mesin | ConPTY butuh versi ini |
| SolidWorks + seat lisensi | Per mesin | Tidak bisa dibagi, tidak bisa headless |
| .NET Framework 4.8 SDK | Per mesin | |
| WebView2 Runtime | Per mesin | |
| `agy` terpasang | Per mesin | Kita tidak mendistribusikannya |
| Login `agy` | **Per akun Google** | Kuota dan rate limit terpisah per orang |
| Registrasi add-in Machine | Per mesin | Butuh UAC sekali |

Yang masuk repo: kode, skema, `capabilities.yaml`, dokumen. Yang tidak boleh masuk repo:
`session.json`, agent-workspace, kredensial, path mesin, nama user Windows. Lihat
`.gitignore`.

### Catatan soal lingkungan pengembangan

Status `verified` di `capabilities.yaml` bertumpu pada satu asumsi: golden test lulus di
SolidWorks yang berperilaku seperti SolidWorks sungguhan. Instalasi yang dimodifikasi
melemahkan asumsi itu dengan dua cara yang nyata:

1. **Registrasi COM, ketersediaan PIA di `api\redist`, dan aktivasi `ISldWorks`** bergantung
   pada komponen yang bisa tersentuh modifikasi. Kalau add-in gagal load, penyebabnya jadi
   ambigu — kita tidak bisa membedakan bug kita dari lingkungan yang rusak. Ingat bahwa
   SolidWorks bisa mengembalikan `LoadAddIn=0` tanpa pernah menginstansiasi `ISwAddin`.
2. **`verified` jadi hanya berarti "jalan di mesin kami"** — bukan klaim yang bisa dipegang
   orang lain.

Karena itu gerbang Fase 5 poin 5 mensyaratkan golden test lulus setidaknya sekali di
instalasi berlisensi bersih sebelum apa pun dirilis ke luar.

Kabar baiknya: **Fase 0 sampai 2 hampir seluruhnya tidak butuh SolidWorks sungguhan.**
Dispatcher, MCP server, provider host, skema tool, DesignPlan, dan seluruh test logika
kontrol berjalan dengan fake. Yang benar-benar menuntut SolidWorks asli adalah Fase 3 ke
atas — jadi ada waktu beberapa minggu untuk mengatur lisensi.

Catatan saat memilih lisensi: **SOLIDWORKS for Makers dilaporkan tidak mendukung add-in
pihak ketiga**, jadi edisi itu tidak bisa dipakai untuk proyek ini walaupun murah. Pastikan
dukungan add-in dan API sebelum membeli apa pun.

---

## Fase 0 — Fondasi (Minggu 1–2)

Tidak ada AI sama sekali di fase ini. Tujuannya satu: add-in benar-benar hidup di dalam
SolidWorks dan punya kanal thread yang benar.

**Jalur Platform**

- Skeleton repo, solution C#, target `net48`/x64, strong name key
- `MikosAI.Addin` mengimplementasikan `ISwAddin`, CommandGroup 3 tombol, TaskPane
- **`MainThreadDispatcher`** — dikerjakan di fase ini, bukan nanti
- WebView2 di TaskPane menampilkan halaman statis
- Script build/register/unregister PowerShell (CurrentUser + Machine, RegAsm 64-bit + TLB)
- Penulis diagnostik `host-status.json`
- `Trace` writer JSONL

**Jalur CAD**

- `capabilities.yaml` versi awal (skema + entri v0.1 berstatus `not_implemented`)
- ~~`CapabilityLedger` loader + validator~~ → `MikosAI.Core/Capabilities/` **selesai**
- ~~Skema JSON untuk `DesignPlan`~~ → `schemas/design-plan.schema.json` **selesai**
- ~~Skema input/output 18 tool v0.1~~ → `schemas/tools.json` **selesai**, perlu ditinjau
  bersama sebelum dianggap final
- ~~Validator DesignPlan, termasuk aturan parameter `assumed` wajib punya `openQuestions`~~
  → `MikosAI.Core/Plan/PlanValidator.cs` **selesai**
- Harness golden test yang bisa jalan tanpa SolidWorks (pakai fake), menjalankan
  `examples/mounting-plate.plan.json`

### Sudah selesai di `MikosAI.Core`

108 test hijau, dibuild dan dijalankan tanpa SolidWorks dan tanpa Visual Studio:

| Berkas | Isi |
|---|---|
| `Execution/VerificationGate.cs` | Normalisasi dan agregasi putusan |
| `Execution/RecoveryAdvisor.cs` | 11 kelas error, keputusan tanpa eksekusi |
| `Execution/ToolResult.cs` | Envelope dengan pemisahan `Ok` dan `IsTaskSuccessful` |
| `Capabilities/CapabilityLedger.cs` | Pembaca `capabilities.yaml` dan gating |
| `Tools/ToolCatalog.cs` | Katalog tool dan validasi silang terhadap ledger |
| `Plan/PlanValidator.cs` | Aturan semantik plan |
| `Threading/IMainThreadDispatcher.cs` | Abstraksi; implementasi nyata di Addin |
| `Tracing/TraceWriter.cs` | JSONL dengan redaksi, tidak pernah melempar exception |

`MikosAI.Core` sengaja **tidak** punya dependensi SolidWorks. Itu yang membuat Jalur
Platform bisa bekerja penuh tanpa CAD, dan membuat seluruh logika keamanan bisa diuji
dalam hitungan detik.

**Gerbang penerimaan Fase 0**

1. Add-in diinstansiasi oleh `SLDWORKS.exe`, `LoadAddIn` mengembalikan 0
2. `host-status.json` memenuhi kelima syarat di `ARSITEKTUR.md` §4.1
3. Test membuktikan: tanpa TLB, add-in **tidak** terinstansiasi walaupun `LoadAddIn=0`
4. Test membuktikan: handler PMP tanpa `IDispatch` melempar `InvalidCastException`
5. `MainThreadDispatcher` punya test yang gagal kalau API SolidWorks dipanggil dari thread lain
6. `capabilities.yaml` tervalidasi dan 18 skema tool disetujui kedua orang

---

## Fase 1 — MCP server + tool read-only (Minggu 3–4)

**Jalur Platform**

- `McpHttpServer` di `127.0.0.1`, port ephemeral, transport streamable HTTP, bearer token
- Token ditulis ke `session.json` ber-ACL
- Daftar tool di-generate dari ledger, difilter `verified`/`pilot`
- `AgentWorkspace`: buat folder sendiri di `%LOCALAPPDATA%`, tulis
  `.agents/mcp_config.json` tiap sesi, jangan sentuh config global pengguna

**Jalur CAD** — 6 tool read-only, aman karena tidak menulis apa pun:

- `get_active_document` · `list_features` · `get_bounding_box`
- `get_mass_properties` · `inspect_sketches` · `get_selection`

**Gerbang penerimaan Fase 1**

1. `agy` bisa melihat daftar tool kita dan memanggilnya, round-trip terbukti
2. Semua 6 tool dieksekusi lewat dispatcher — dibuktikan oleh test, bukan inspeksi mata
3. Tool berstatus `reference_only` **tidak muncul** di daftar MCP
4. Request tanpa bearer token ditolak
5. Server hanya bind loopback — diverifikasi dari mesin lain di jaringan yang sama
6. Konfigurasi global `~/.gemini/config/mcp_config.json` **tidak berubah** setelah add-in
   berjalan — dibuktikan dengan hash sebelum dan sesudah

---

## Fase 2 — Provider host (Minggu 5–6)

Fase paling berisiko. **Baca `docs/integrasi-antigravity.md` seluruhnya sebelum mulai.**

**Jalur Platform**

- Jawab seluruh 8 item di daftar verifikasi terbuka dengan `agy` nyata — **ini tugas
  pertama fase ini, sebelum menulis kode**
- `ProviderHost` dengan **ConPTY** (atau sidecar `pywinpty` sementara)
- Pembersih ANSI/spinner/repaint
- Flag probing: `--version` + `--help` saat startup
- Parser `stream-json`, dengan fallback ke `json` lalu `text`
- Deteksi `provider_silent` — exit 0 + stdout kosong adalah **kegagalan**
- State machine provider dan tampilannya di UI
- UI chat: kirim prompt, tampilkan progres bertahap, tampilkan hasil

**Jalur CAD**

- `VerificationGate` + agregasi `BLOCKED > FAIL > WARN > PASS`
- `RecoveryAdvisor` + 10 klasifikasi error
- Golden test deterministik dengan fake handler (tanpa SolidWorks, tanpa jaringan)

**Gerbang penerimaan Fase 2**

1. Chat di TaskPane → `agy` → MCP → tool read-only → jawaban muncul di UI
2. Bug silent non-TTY terbukti tertangani — ada test yang gagal kalau ConPTY dilepas
3. Keadaan belum terautentikasi memberi panduan jelas, bukan error mentah
4. Semua 8 item verifikasi terbuka terjawab dan terdokumentasi di repo
5. Status yang tidak dikenal mendarat di `BLOCKED`, dibuktikan test
6. `RecoveryAdvisor` tidak pernah mengusulkan `retry` untuk error yang tidak dikenal

---

## Fase 3 — Jalur tulis (Minggu 7–9)

Fase terberat dan paling bernilai. Di sini kedua jalur bertemu.

**Jalur CAD** — 12 tool tulis:

- Sketsa: `create_sketch` · `add_rectangle` · `add_circle` · `add_slot` ·
  `fully_define_sketch` · `close_sketch`
- Fitur: `extrude_boss` · `extrude_cut` · `fillet` · `chamfer` · `hole_wizard` ·
  `linear_pattern`
- Verifikasi baca-ulang: rebuild, status constraint, volume, **simpan-tutup-buka ulang**
- Referensi entitas semantik — mulai dari normal+centroid, bukan `Face1`

**Jalur Platform**

- `PlanRunner`: eksekusi DesignPlan langkah demi langkah dengan trace
- Validasi plan terhadap skema **sebelum** eksekusi
- Prompt sistem yang mengarahkan `agy` menghasilkan DesignPlan, bukan narasi
- Panel Evidence di UI

**Gerbang penerimaan Fase 3**

1. Golden test **"mounting plate 100×80×10, 4 lubang M6 counterbore"** lulus
   **3 kali berturut-turut** di SolidWorks nyata
2. Setiap fitur yang dibuat terverifikasi lewat baca-ulang setelah simpan-buka ulang
3. Sketsa fully defined lewat dimensi desain, **bukan** `Fix` massal
4. Tidak ada tool yang memakai `Face1`/`Edge1`/koordinat layar
5. Operasi yang gagal meninggalkan model dalam keadaan yang bisa dipulihkan pengguna
6. Tidak ada crash SolidWorks dalam 50 kali eksekusi plan berturut-turut

Poin 1 adalah definisi "v0.1 bekerja". Poin 6 adalah definisi "v0.1 layak dipakai".

---

## Fase 4 — Pengalaman dua lapis (Minggu 10–11)

Di sini "mudah dipakai" dikerjakan sebagai pekerjaan sungguhan, bukan sisa waktu.

- **Lapis 1:** tombol preset, contoh prompt, pesan error yang menjelaskan tindakan manusia
- **Lapis 2:** editor DesignPlan + jalankan ulang, Trace viewer, override model
- Tema terang/gelap mengikuti SolidWorks
- Perilaku saat SolidWorks sibuk / dokumen tidak ada / lisensi tidak aktif
- Onboarding: deteksi `agy` belum terpasang atau belum login, lalu pandu

**Gerbang penerimaan Fase 4**

1. Orang yang belum pernah melihat proyek ini membuat part lewat Lapis 1 tanpa dibantu
2. Mengubah satu parameter di DesignPlan dan menjalankan ulang **tidak memanggil AI**
3. Setiap keadaan `BLOCKED` punya pesan yang menyebut tindakan konkret
4. Tidak ada jargon internal (`capability_gap`, `provider_silent`) terlihat di Lapis 1

---

## Fase 5 — Pengemasan (Minggu 12)

- Installer (registrasi Machine + UAC sekali)
- Code signing Authenticode untuk DLL dan installer
- `capabilities.yaml` di-tag versi, matriks versi SolidWorks yang diuji
- README pemakaian, panduan troubleshooting
- Checklist rilis + script verifikasi

**Gerbang penerimaan Fase 5**

1. Install bersih di mesin yang belum pernah dipakai development berhasil
2. Uninstall bersih, tidak meninggalkan registry atau file
3. Konfigurasi global `agy` milik pengguna tidak tersentuh sebelum maupun sesudah uninstall
4. Matriks versi menyatakan dengan jujur apa yang diuji dan apa yang tidak
5. **Golden test Fase 3 lulus setidaknya sekali di instalasi SolidWorks berlisensi dan
   bersih.** Tanpa ini, `verified_versions` di ledger tidak boleh diisi — lihat
   "Catatan soal lingkungan pengembangan" di bawah

---

## Setelah v0.1

Urutannya mengikuti nilai dibagi risiko, bukan keinginan.

| Prioritas | Pekerjaan | Catatan |
|---|---|---|
| 1 | Pengerasan referensi entitas semantik | Fondasi semua hal lanjutan; `Face1` akan jadi utang teknis terbesar |
| 2 | Otomasi drawing | Nilai bisnis tertinggi, tapi API drawing rapuh antarversi — butuh matriks versi |
| 3 | Assembly dan mate | Butuh referensi semantik yang sudah kuat |
| 4 | Sidecar Python (OCCT, mesh, PDF readback) | Verifikasi drawing lewat baca ulang PDF sangat berharga |
| 5 | Provider kedua (Claude Code / Gemini CLI) | Membuktikan abstraksi provider benar-benar bekerja |
| 6 | FEA preflight | Butuh solver eksternal, selalu `pilot`, selalu butuh review insinyur |
| 7 | Multi-CAD | Hanya kalau v0.1 benar-benar stabil |

---

## Registry risiko

| # | Risiko | Dampak | Mitigasi |
|---|---|---|---|
| R1 | `agy` diam ketika stdout bukan TTY | **Kritis** — kegagalan menyerupai sukses | ConPTY; kelas error `provider_silent`; test yang gagal tanpa ConPTY |
| R2 | Flag `agy` berubah antarrilis | Tinggi | Probing `--help` saat startup; pin versi teruji di ledger |
| R3 | ToS: orkestrasi eksternal dengan akun subscription belum jelas | Tinggi, **legal bukan teknis** | Abstraksi provider; jalur API key; cek ToS sebelum distribusi |
| R4 | API SolidWorks tidak thread-safe | **Kritis** — crash acak tak bisa direproduksi | `MainThreadDispatcher` di Fase 0; aturan review; test |
| R5 | Crash add-in menjatuhkan SolidWorks | Tinggi | Tidak ada panggilan API tanpa guard; operasi dibungkus try/catch; verifikasi sebelum klaim sukses |
| R6 | Referensi `Face1`/`Edge1` rusak saat fitur disisipkan | Tinggi | Referensi semantik sejak Fase 3; dilarang lewat review |
| R7 | SolidWorks mengunci DLL yang dimuat | Sedang | `UnloadAddIn` sebelum rebuild; didokumentasikan di panduan dev |
| R8 | Rate limit free tier terasa saat pemakaian interaktif | Sedang | DesignPlan bisa dijalankan ulang tanpa AI; preset tanpa AI; provider bisa ditukar |
| R9 | Drift versi SolidWorks (2024/2025/2026) | Sedang | Matriks versi di ledger; status `verified` selalu menyebut versi |
| R10 | Redistribusi PIA dibatasi lisensi | Sedang | Rujuk instalasi lokal, jangan vendor ke repo |
| R11 | Dua orang, cakupan membengkak | Tinggi | Gerbang penerimaan per fase; "kecil itu fitur"; tunda tanpa rasa bersalah |

R1, R3, R4 yang paling perlu diawasi. R1 dan R4 bisa diselesaikan dengan kode. R3 tidak —
itu keputusan, bukan bug.

---

## Yang sengaja tidak kita bangun

Diambil dari pelajaran membedah repo referensi — mereka membangun semua ini sebelum punya
pengguna, dan itu memperlambat mereka.

- Lapisan attestation/governance dengan trust root dan drift checking
- File evidence SHA-256 yang di-commit ke repo
- Aplikasi desktop terpisah (TaskPane adalah satu-satunya UI)
- Dua framework UI yang bersaing
- Test untuk test runner
- Dukungan multi-CAD sebelum satu CAD benar-benar bekerja

Kalau nanti kita butuh salah satunya, kita bangun saat itu — dengan alasan yang nyata.
