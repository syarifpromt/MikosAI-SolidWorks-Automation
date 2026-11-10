# Bedah MecAgent Copilot v2.13.0

Tanggal: 2026-11-10.

## Lingkup dan batasnya

Yang dibaca: **struktur paket, nama assembly, manifest dependensi,
`runtimeconfig`, dan nama tipe di metadata .NET.** Semuanya metadata yang
memang ada di artefak terdistribusi.

Yang **tidak** dilakukan: menjalankan installer, mendekompilasi IL menjadi
sumber, atau menyalin kode, aset, maupun string milik mereka ke repo ini.

Yang diadopsi **gagasan arsitekturnya**, dan setiap adopsi di bawah ditulis
ulang dari nol dengan API publik Windows/SolidWorks. Installer-nya tidak
di-commit; `.gitignore` sudah menutupnya.

Alasan membedah: mereka memecahkan masalah yang sama dengan kita, dan kesulitan
yang sama akan muncul di tempat yang sama.

---

## 1. Bentuknya

| | |
|---|---|
| Installer | Squirrel.Windows, 335 MB |
| Aplikasi | Electron (`mecagent-app`) |
| Add-in in-process | `MecAgent.SolidWorks.Host.dll` — **net45** |
| Proses terpisah | `MecAgent.SolidWorks.Runner.exe` — **.NET 10, self-contained** |
| Penghubung | `MecAgent.SolidWorks.Protocol.dll` — ada di **kedua** sisi |

Ini yang paling menentukan: **dua proses, bukan satu.** Shim tipis net45 hidup
di dalam `SLDWORKS.exe`; seluruh logika berat hidup di proses lain dengan .NET
modern.

Transportnya **JSON lewat stdio** — metadata `Protocol.dll` memuat
`<Stdout>` dan `<ReturnJson>`, bukan pipe bernama atau TCP. Pola yang sama
dengan jalur provider kita ke `agy`.

## 2. Kosakata protokolnya

Dari nama tipe di `Protocol.dll`:

```
Permintaan : Bind, Compile, PrepareCell, PrepareMacro, RunCell, RunPrepared,
             RunProgram, DiscardPrepared, CaptureViews, Progress, Cancel, Shutdown
Pesan      : Bound, Compiled, Prepared, Ready, Result, Event, ViewsCaptured
Catatan    : CompileRecord, ExecutionRecord, PreparedRecord, DialogRecord
Konsep     : ExecutionStage, ProgramKind, PreparedKind, Cell, CellEffect,
             CellRepair, ComFault, FingerprintChange
```

Empat hal menonjol:

- **`FingerprintChange`** — mereka melacak perubahan model lewat sidik jari.
  Setara dengan `modelChanged` kita yang memakai snapshot sebelum/sesudah.
- **`DialogRecord`** — dialog modal adalah warga kelas satu di protokolnya.
- **`ComFault`** — kegagalan COM dibedakan dari kegagalan lain.
- **`Cell` / `CellEffect` / `CellRepair`** — pekerjaan dimodelkan sebagai sel
  bergaya notebook, lengkap dengan perbaikan.

## 3. Strategi inti mereka: Roslyn

`deps.json` runner memuat `Microsoft.CodeAnalysis.CSharp.Scripting`.

Artinya: **AI menulis C#, Roslyn mengompilasi dan menjalankannya terhadap API
SolidWorks.** Itu sebabnya ada `ScriptApi.dll`, `CompileRequest`, dan
`CompileError`.

**Kita sengaja tidak mengikuti ini**, dan pembedahan ini justru menguatkan
alasannya. Briefing proyek melarang `run_api_script`/VBA bebas, dan seluruh
model ledger kemampuan kita bergantung pada permukaan tool yang **tetap dan
terverifikasi**. Kode yang dikarang saat runtime tidak bisa diverifikasi lebih
dulu, tidak bisa dipetakan ke capability, dan tidak bisa dijanjikan apa-apa
soal apa yang akan disentuhnya.

Keduanya pilihan yang sah. Milik mereka lebih luwes; milik kita bisa
dipertanggungjawabkan. Yang tidak boleh terjadi adalah mengambil keluwesan itu
sambil tetap mengaku punya jaminan.

---

## 4. Yang DIADOPSI sekarang

### Diagnosa macet dari luar proses — `scripts/lib/Dialog.psm1`

`DialogRecord` mereka menunjuk sesuatu yang kita lewatkan, dan alasannya lebih
dalam daripada "mereka punya fitur itu":

**Add-in kita TIDAK BISA melaporkan dialog modal.** Dialog memblokir thread
utama SolidWorks — thread yang sama yang menjalankan writer kita dan yang akan
menulis laporannya. Selama dialog terbuka, tidak ada satu baris pun yang bisa
kita tulis.

Pengamat yang ikut terblokir tidak bisa mengamati. Itu kemungkinan besar
**alasan runner mereka terpisah sejak awal.**

Sebelumnya skrip golden kita berbunyi:

> *"Periksa apakah build terbaru termuat atau ada dialog modal."*

Benar sebagai hipotesis, tidak berguna sebagai diagnosis — orang tetap harus
menoleh ke layar.

Sekarang, saat run tidak memberi hasil, skrip:

1. memeriksa apakah `SLDWORKS.exe` masih hidup;
2. kalau mati, menunjuk `swxJRNL.swj` **dan mencetak stack trace native
   terakhirnya** — jalur yang baru saja kita gali manual untuk menemukan
   `auFeatureDataObject_c::disconnectFromCoreObject`;
3. kalau hidup, menyebutkan jendela yang mungkin memblokir beserta kelas dan
   judulnya.

Daftar dialog yang kosong dilaporkan sebagai **"tidak ada yang kami temukan"**,
bukan "tidak ada dialog" — dialog yang digambar sendiri tanpa kelas `#32770`
memang tidak terlihat oleh pemeriksaan ini, dan ketiadaannya tidak boleh dibaca
sebagai jaminan.

---

## 5. Yang LAYAK DIPERTIMBANGKAN, tapi BUKAN sekarang

### Runner di proses terpisah

Keuntungannya nyata untuk kita:

- `MikosAI.Core` tidak lagi terpenjara di **net48**. Seluruh perbaikan G17
  `System.Text.Json` yang harus kita tulis sendiri lahir dari batasan itu.
- Kegagalan terkelola di runner tidak menjatuhkan SolidWorks.

Tapi **bukan obat untuk crash yang baru kita alami.** `AccessSelections`
mematikan SolidWorks dari dalam `sldapiu` — kode SolidWorks sendiri. Memanggil
dari proses lain lewat COM tetap akan menjatuhkan server yang sama. Yang
terlindungi hanya proses kita.

Dan biayanya besar: seluruh jalur eksekusi harus dipecah. Kita **satu tool**
dari gerbang v0.1. Ini pekerjaan sesudahnya.

### `ComFault` sebagai jenis error tersendiri

`ErrorKind` kita punya `Transient`, `InvalidArgument`, `EnvironmentMissing`,
`ToolUnavailable`. Kegagalan COM saat ini melebur ke dalamnya. Memisahkannya
murah dan berguna: COM fault berarti **lapisan API yang rusak**, bukan rencana
yang salah — dan keduanya menuntut tindakan berbeda.

Ditunda karena menyentuh kontrak, dan kontrak diubah lewat proposal.

---

## 6. Yang ternyata SUDAH kita punya

- **Pemisahan compile dari run.** `validate_design_plan` dan `run_design_plan`
  kita sudah memisahkan "periksa rencana" dari "sentuh model", sama seperti
  `CompileRequest` mendahului `RunRequest` di sana.
- **Deteksi perubahan model.** `FingerprintChange` mereka setara dengan
  `modelChanged` kita yang berbasis snapshot — termasuk keadaan "tidak
  diketahui", yang kita wajibkan disertai `readBackFailed`.
- **Progres bertahap dan pembatalan.** Panel kita sudah menerima `text_delta`;
  `ProgressRequest`/`CancelRequest` mereka sepadan.

Itu bukan kebetulan: masalahnya memang sama, jadi bentuk jawabannya mirip.
