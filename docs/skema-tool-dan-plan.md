# Keputusan skema tool dan DesignPlan

Status: draft v0.1 · 2026-10-31

File yang dijelaskan dokumen ini:

| File | Isi |
|---|---|
| `schemas/common.schema.json` | Tipe bersama: Length, EntityRef, ToolResult, Verification |
| `schemas/design-plan.schema.json` | Struktur DesignPlan |
| `schemas/tools.json` | Katalog 18 tool CAD + 2 orchestration |
| `examples/mounting-plate.plan.json` | Plan nyata untuk golden test Fase 3 |

**Ini kontrak antara Jalur Platform dan Jalur CAD.** Perubahan di sini adalah percakapan,
bukan commit sepihak.

---

## 1. Satuan: mm di plan, SI di eksekutor

Plan memakai **milimeter dan derajat**, dinyatakan eksplisit di field `units`. API SolidWorks
memakai meter dan radian.

Konversi terjadi di **satu tempat saja**: `ToolExecutor`, tepat sebelum memanggil API.

Alasannya: insinyur berpikir dalam mm, jadi plan yang bisa diedit manusia harus mm. Tapi
konversi yang tersebar adalah sumber bug satuan yang klasik — satu tempat yang lupa dikali
0.001 menghasilkan part 1000x lebih besar, dan itu kelihatan "berhasil" sampai ada yang
melihat bounding box.

Karena itu `units` di skema adalah `const`, bukan `enum`. Tidak ada pilihan, tidak ada
ambiguitas. Kalau nanti butuh inch, itu konversi di lapisan UI, bukan di plan.

## 2. Parameter: referensi saja, bukan ekspresi

Argument boleh berisi `"$plateWidth"` yang merujuk entri di `parameters`. Tapi **tidak ada
aritmetika** — `"$plateWidth / 2 - 15"` tidak didukung.

**Trade-off yang kita terima secara sadar.** Lihat `risks` di
`examples/mounting-plate.plan.json`: posisi lubang di situ ditulis sebagai `±35` dan `±25`,
yang diturunkan dari `plateWidth=100`, `plateDepth=80`, `holeInset=15`. Kalau power user
mengubah `plateWidth` lewat `parameterOverrides`, **posisi lubang tidak ikut berubah.**
Itu keterbatasan nyata dan harus terlihat di UI, bukan disembunyikan.

Kenapa tetap begini untuk v0.1: bahasa ekspresi adalah proyek tersendiri — parser,
presedensi, penanganan error, pencegahan evaluasi tak aman, pesan error yang bisa dimengerti.
Membangunnya di Fase 3 berarti tidak mengirim apa pun di Fase 3.

**Jalan keluar yang sudah terlihat**, urut dari paling murah:

1. **Derived parameters** — entri `parameters` dengan `source: "derived"` plus formula
   terbatas yang dievaluasi runner. Hanya `+ - * /` dan referensi parameter, tanpa fungsi.
2. **Global variables SolidWorks** — pakai equation manager SolidWorks sendiri, sehingga
   relasi hidup di dalam model dan tetap bekerja walaupun pengguna mengedit di SolidWorks
   langsung. Ini sebenarnya jalur yang paling benar secara CAD.

Opsi 2 lebih tepat tapi lebih besar. Diputuskan setelah v0.1 jalan.

## 3. `ok` bukan `verdict` — prinsip inti dikodekan ke skema

`ToolResult` punya dua field yang mudah dikira sama:

```
ok                       -> panggilan terlaksana tanpa exception
verification.verdict     -> tugasnya benar-benar berhasil
```

Ini bukan redundansi. Ini prinsip nomor 2 proyek ini ("tool mengembalikan success bukan
berarti tugas berhasil") yang dipaksa masuk ke bentuk data, supaya tidak bisa diabaikan.

`extrude_cut` yang mengembalikan `ok: true` tapi volumenya tidak berkurang adalah cut yang
tidak memotong apa pun. Dalam skema ini itu terekspresikan: `ok: true`,
`verification.verdict: "FAIL"`.

Dan `verification.manualReviewRequired: true` memblokir penyelesaian **walaupun** verdict
`PASS`.

## 4. Referensi entitas: empat strategi, satu dilarang

`EntityRef` punya empat bentuk:

| `by` | Untuk | Stabilitas |
|---|---|---|
| `name` | bidang standar, fitur, sketsa | Stabil kalau nama tidak diubah |
| `normal_and_point` | permukaan | Cukup stabil; bisa salah target kalau ada permukaan sejajar berdekatan |
| `midpoint` | tepi | Paling rapuh dari ketiganya — selalu verifikasi jumlah entitas terpilih |
| `persist_id` | apa saja | Paling stabil. **Belum dipakai di v0.1** |

Yang **tidak ada** di skema: `Face1`, `Edge1`, dan koordinat layar. Mereka tidak bisa
diekspresikan, jadi tidak bisa dipakai. Itu penegakan struktural, bukan imbauan.

`get_selection` mengembalikan `EntityRef` semantik yang langsung bisa dipakai tool lain —
jadi alur "pengguna klik permukaan, lalu minta AI membuat lubang di situ" bekerja tanpa
pernah menyentuh nama indeks.

**Konsekuensi untuk Jalur CAD:** setiap tool yang menerima `EntitySelector` **wajib**
mengembalikan jumlah entitas yang benar-benar terpilih (`filletedEdgeCount`,
`chamferedEdgeCount`), dan verifikasi membandingkannya dengan jumlah yang diminta. Fillet
yang mengenai 3 dari 4 tepi adalah kegagalan yang tidak dilaporkan API.

## 5. Konvensi yang mudah salah

Dikumpulkan di satu tempat karena semuanya sudah pernah memakan waktu orang lain:

**Diameter, bukan radius.** `add_circle` dan `hole_wizard` menerima diameter. Gambar teknik
memakai diameter, jadi plan juga.

**`instances` termasuk fitur aslinya.** Konvensi SolidWorks. Empat lubang berarti
`instances: 4`, bukan 3.

**`start`/`end` slot adalah pusat busur ujung**, bukan tepi luar. Panjang keseluruhan slot
sama dengan `jarak(start, end) + width`. Ini sumber kesalahan yang sering, jadi UI harus
menunjukkannya.

**Nama fitur ditentukan SolidWorks, bukan kita.** `create_sketch` mengembalikan
`sketchName` yang **sebenarnya** — selalu pakai itu untuk step berikutnya, jangan asumsikan
nama yang diminta dipakai.

**Nama fitur Hole Wizard berbeda antarversi dan antarbahasa UI.** `"CBORE for M6 Hex Head
Bolt1"` di SW2026 Inggris bisa berbeda di tempat lain. Jangan jadikan `expect.featureName`
sebagai check yang `required`.

**Urutan parameter `FeatureExtrusion2/3`.** Daftar parameternya panjang, tipenya mirip, dan
urutannya berbeda antarversi. Tertukar dua argumen menghasilkan geometri yang salah tanpa
error. Tulis test khusus urutan parameter — repo referensi punya
`test_extrude_cut_param_order.py` tepat untuk alasan ini.

## 6. "Jangan menebak" dibuat struktural

Dua field yang bekerja berpasangan:

- `parameters.*.source` dengan nilai `"assumed"` menandai bahwa AI menebak,
- `openQuestions[]` mencatat apa yang tidak diketahui.

**Aturannya:** setiap parameter `assumed` wajib punya entri `openQuestions` yang
merujuknya lewat `affectsParameters`. Validator menegakkan ini.

Dan `hole_wizard` mengembalikan `BLOCKED` kalau `standard` atau `fastenerSize` tidak
diberikan untuk tipe yang membutuhkannya — **bukan** memilih standar default. Tipe lubang,
datum, dan toleransi tidak boleh ditebak dari geometri.

Di contoh `mounting-plate.plan.json`, `cornerRadius: 5` ditandai `assumed` dan muncul di
`openQuestions` dengan `blocking: false` — artinya plan boleh jalan, tapi pengguna melihat
bahwa fillet itu bukan permintaannya.

## 7. `onFailure` tidak punya opsi "continue"

Hanya `abort` atau `ask_user`.

Melanjutkan di atas step yang gagal menghasilkan model yang salah secara diam-diam — dan
model yang salah secara diam-diam adalah hasil terburuk yang bisa diberikan alat seperti ini.
Lebih baik berhenti dengan jelas.

## 8. `acceptance` terpisah dari `expect`

- `expect` pada step: diperiksa segera setelah step itu.
- `acceptance` pada plan: diperiksa setelah **semua** step selesai **dan dokumen dibuka
  ulang**.

Pemisahan ini penting. `a5` di contoh (`dimension_equals` pada `D1@Boss-Extrude1`) hanya
bermakna setelah simpan-tutup-buka ulang — itu yang membuktikan dimensi benar-benar
tersimpan di model, bukan hanya ada di memori sesi.

---

## Langkah berikutnya per jalur

**Jalur Platform** bisa mulai sekarang tanpa SolidWorks:

- `CapabilityLedger` loader + validator untuk `capabilities.yaml`
- Validator DesignPlan terhadap `design-plan.schema.json`, termasuk aturan
  `assumed` → `openQuestions`
- Generator daftar tool MCP dari `tools.json` difilter ledger
- Fake `ToolExecutor` yang mengembalikan `ToolResult` valid, supaya `PlanRunner` dan UI bisa
  dikembangkan penuh tanpa CAD

**Jalur CAD** bisa mulai dari yang read-only:

- Implementasi 6 tool Fase 1 lewat `MainThreadDispatcher`
- `EntityRef` resolver untuk `by: "name"` dan `by: "normal_and_point"`
- Harness golden test yang menjalankan `examples/mounting-plate.plan.json`, awalnya dengan
  semua step `not_implemented`, lalu dihidupkan satu per satu

Golden test itu sudah bisa ditulis **sekarang** dan akan merah sampai Fase 3 selesai. Itu
bentuk yang benar: target yang terlihat sejak awal, bukan dikarang di akhir.
