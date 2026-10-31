# Bedah repo referensi: `wzyn20051216/solidworks-automation-skill`

Dibedah 2026-10-31 · commit HEAD `bfb12f7` · Lisensi MIT

Dokumen ini ada supaya kita berdua punya pemahaman yang sama tentang apa yang kita pelajari,
apa yang kita ambil, dan apa yang sengaja kita tolak. Tanpa ini, keputusan di
`docs/adr/` akan terlihat sewenang-wenang.

---

## 1. Profil

| | |
|---|---|
| Ukuran | 591 file, ~14 MB |
| Lisensi | **MIT** — legal untuk kita pakai ulang dengan atribusi |
| Bahasa dokumen | Mandarin, seluruhnya |
| Aktivitas | Aktif; commit terakhir yang terlihat 2026-10-27 |
| Test | 200+ file test |
| Tool MCP | 65 |
| Target verifikasi | SolidWorks 2024 & 2026, AutoCAD 2024 |

Ini bukan proyek mainan. Ini pekerjaan serius dari tim yang jelas punya akses ke SolidWorks
nyata dan melakukan regresi di mesin sungguhan.

## 2. Topologi mereka

```
Codex / Claude Code / Gemini CLI / OpenCode    <- AI hidup DI LUAR
   | subprocess headless: -p prompt --output-format json
   v
3 entry sejajar: SKILL.md | MCP server (65 tool) | CAD Studio desktop
   v
Execution Core (scripts/core/)   <- lapisan keandalan
   v
Backend Router (capabilities.yaml)
   v
Python COM out-of-process -> SolidWorks
```

Komponen Execution Core: `state.py`, `capability.py`, `trace.py`, `verification.py`,
`recovery.py`, `execution.py`. Dokumentasinya di
`docs/architecture/execution-core.md` adalah file paling berharga di seluruh repo.

## 3. Temuan paling menentukan untuk kita

**Add-in C# mereka bukan tempat AI-nya.**

`dotnet/CadStudio.SolidWorks.AddinHost/` memang lengkap secara teknis: `ISwAddin`,
`SetAddinCallbackInfo2`, event aplikasi, CommandGroup 3 perintah, TaskPane WinForms,
dan implementasi penuh 37 callback `IPropertyManagerPage2Handler9`. Mereka bahkan punya
bukti regresi di SolidWorks 2026 SP1.1.

Tapi `TaskPaneControl.cs` isinya hanya: satu label status, satu tombol
"buka PropertyManagerPage", satu tombol "refresh diagnostik". **Tidak ada chat. Tidak ada
AI.** Add-in itu panel diagnostik, bukan antarmuka.

Seluruh eksekusi CAD tetap terjadi di Python, di luar proses, lewat COM.

**Artinya:** "AI dioperasikan di dalam SolidWorks" adalah celah yang belum mereka isi.
Diferensiasi kita struktural, bukan kosmetik. Dan add-in host mereka adalah bukti bahwa
fondasi teknisnya bisa bekerja — mereka sudah membuktikan bagian yang sulit.

## 4. Lima ide yang kita ambil

### 4.1 `capabilities.yaml` sebagai satu sumber kebenaran

583 baris. Status per kemampuan: `verified` / `pilot` / `reference_only` /
`not_implemented`. Skill, MCP, queue, dan UI semuanya membaca file yang sama.

Kenapa berharga: ini mencegah AI mengklaim bisa melakukan sesuatu yang belum pernah diuji —
secara struktural, bukan lewat prompt. Kita mengambil ini hampir utuh.

### 4.2 "Tool return success bukan berarti task success"

Aturan emas mereka, dan diterapkan sungguhan. Setelah operasi, model dibaca ulang: simpan
dan buka ulang, verifikasi sketsa dikonsumsi fitur target, verifikasi dimensi kunci masih
bisa diubah. Untuk drawing, mereka bahkan membaca ulang PDF hasil export dengan PyMuPDF
untuk memverifikasi batas teks dimensi yang sebenarnya.

Itu tingkat kerasnya verifikasi yang pantas dicontoh.

### 4.3 Agregasi verifikasi `BLOCKED > FAIL > WARN > PASS`

Dengan satu detail penting: status tidak dikenal **turun jadi `BLOCKED`**, bukan naik jadi
`PASS`. Dan `PASS` di tingkat atas tidak boleh menutupi `FAIL` pada check yang `required`.

### 4.4 Recovery Decision bukan Recovery Execution

`classify_error` memetakan 10 jenis error ke keputusan (`retry` / `fallback_backend` /
`replan` / `user_action_required` / `block` / `fail`) — tapi **tidak mengeksekusinya**.
Default aman: tidak yakin berarti `block`, tidak pernah `retry`.

Ini yang mencegah AI mengulang-ulang operasi yang merusak model. Sederhana, dan sangat
tepat.

### 4.5 Golden Workflow + eval deterministik

12 workflow menghasilkan 43 skenario (12 nominal + 31 robustness), dijalankan dengan fake
handler dan fake reviewer. Jumlah skenario diturunkan dari data sumber, tidak di-hardcode
di test.

Untuk tim 2 orang ini penyelamat waktu: logika kontrol bisa diuji tanpa membuka SolidWorks.

### Bonus: state machine provider

`references/agent-provider-architecture.md` membedakan dengan ketat:
`not_installed` / `installed` / `auth_failed` / `auth_unverified` / `auth_verified` /
`execution_verified`, dan menyatakan tegas bahwa CLI ada di PATH, environment variable ada,
atau konfigurasi provider ada **tidak satu pun** membuktikan request model bisa jalan.

Kita ambil ini utuh — lihat `ARSITEKTUR.md` §4.10.

## 5. Empat hal yang kita tolak

### 5.1 Dua UI desktop yang bersaing

Ada `apps/desktop/` (PySide6) **dan** `apps/workbench-ui/` (Tauri + React + TypeScript,
lengkap dengan wallpaper video dan DXF viewport). Keduanya aktif, keduanya punya test,
keduanya punya screenshot preview.

Untuk tim 2 orang ini mematikan. Kita punya **satu** UI: WebView2 di dalam TaskPane.

### 5.2 Upacara tata kelola sebelum ada pengguna

`governance-ruleset-attestation.json`, `governance_trust_root.cjs`,
`check_governance_drift.cjs`, `capture_governance_attestation.cjs`,
`governance-bypass-remediation.md`, plus file bukti SHA-256 yang di-commit ke repo
(`p0-2-1-independent-seal-verification.json` dan kawan-kawan).

Ini infrastruktur kepercayaan level enterprise untuk proyek yang belum punya pengguna
eksternal. Kita tunda sampai ada yang benar-benar menuntutnya.

### 5.3 Rasio test terhadap fitur yang ekstrem

200+ file test untuk 65 tool, termasuk `test_eval_runner.py`,
`test_eval_metrics.py`, `test_public_evidence_integrity.py` — test untuk test runner mereka
sendiri.

Kita tetap menulis test dengan serius, tapi yang diuji adalah **perilaku CAD dan logika
kontrol**, bukan infrastruktur test kita sendiri.

### 5.4 Monolingual Mandarin

Seluruh dokumen, komentar kode, dan pesan commit berbahasa Mandarin. Ini menutup kontribusi
luar sepenuhnya, dan juga membuat repo ini sulit kita pelajari.

Kita pakai bahasa Indonesia untuk dokumen, Inggris untuk identifier kode.

## 6. Kesimpulan

**Arsitektur mereka pintar, eksekusinya kembung.**

Lapisan keandalan mereka adalah hasil pemikiran nyata tentang kenapa otomasi CAD gagal —
dan solusinya tepat. Tapi mereka membangun aplikasi desktop, lapisan governance, dan
dukungan multi-CAD sebelum satu alur pun benar-benar mudah dipakai.

Strategi kita: ambil lapisan keandalannya, buang upacaranya, dan belanjakan selisih
waktunya untuk hal yang belum mereka kerjakan — pengalaman pemakaian di dalam SolidWorks.

## 7. Catatan reuse

Lisensi MIT berarti kita boleh memakai ulang kode mereka. Kita memilih **clean-room dengan
pinjaman selektif** (lihat `docs/adr/0004-clean-room-dan-reuse.md`): konsep dan rancangan
diadopsi, dan kalau ada file tertentu yang di-port, itu dilakukan secara sadar dan dicatat
di `ATTRIBUTION.md`.

Yang paling layak dibaca ulang saat implementasi:

| File mereka | Untuk |
|---|---|
| `docs/architecture/execution-core.md` | Rancangan lapisan keandalan |
| `references/agent-provider-architecture.md` | State machine provider |
| `references/solidworks-addin-host.md` | Gerbang penerimaan add-in dan jebakan TLB/IDispatch |
| `capabilities.yaml` | Bentuk ledger kemampuan |
| `scripts/core/recovery.py` | Klasifikasi error |
| `subskills/solidworks-vibecad/schemas/design_plan.schema.json` | Pembanding untuk skema DesignPlan kita |
| `references/complex-hole-features.md` | Pelajaran lapangan soal hole feature |
| `scripts/sw_entity_reference.py` | Pendekatan mereka menghindari `Face1`/`Edge1` |
