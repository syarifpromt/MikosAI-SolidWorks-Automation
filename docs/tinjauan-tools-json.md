# Tanggapan atas tinjauan `schemas/tools.json`

Tanggal: 2026-11-05. Ditulis sebagai jawaban atas tinjauan yang kamu kirim.

Sebagian besar temuanmu kuterima, dua di antaranya mengubah urutan kerja kami, dan
ada beberapa yang kubantah beserta alasannya. Ada juga satu kelas masalah yang
tinjauanmu tidak sebut dan menurutku lebih mendesak daripada tool yang belum ada.

---

## 0. Yang tidak ada di daftarmu, dan kuanggap paling mendesak

Kontrak ini **menjanjikan field yang pembacanya tidak pernah isi.** Empat field
diemisikan sejak lama tanpa pernah dibaca dari SolidWorks:

| field | yang dikirim | yang terbaca |
|---|---|---|
| `hasOpenContour` | `false` hardcoded | "sudah diperiksa, sketsanya tertutup" |
| `dimensionCount` | `0` | "sketsa ini tanpa dimensi" |
| `consumedByFeature` | `null` | "sketsa ini tidak dipakai fitur apa pun" |
| `parentSketch` | `null` | "fitur ini tidak punya sketsa induk" |

Buktinya ada di part acuan: `Boss-Extrude1` melaporkan `parentSketch: null`
padahal `Sketch1` jelas induknya, dan `Sketch1` melaporkan
`consumedByFeature: null` padahal ia jelas dikonsumsi `Boss-Extrude1`.

Yang ketiga paling berbahaya begitu tool tulis ada: *"tidak dipakai fitur apa
pun"* adalah alasan yang masuk akal untuk **menghapus** sebuah sketsa.

Dan yang paling memalukan: golden test kami sendiri meng-assert
`hasOpenContour == false` dan **lulus** — padahal nilainya hardcoded. Assertion
yang tidak pernah bisa merah tidak menguji apa pun; ia menambah rasa aman tanpa
menambah jaminan.

**Sudah diperbaiki.** Keempatnya kini `null`, tapi null saja tidak cukup — null
tetap terbaca sebagai "tidak ada", bukan "tidak diketahui". Jadi setiap jawaban
`list_features` dan `inspect_sketches` sekarang membawa `fieldsNotPopulated`,
daftar field yang diakui belum diisi. Daftarnya **mengecil** saat field benar-benar
diimplementasikan, dan ada test yang memakunya supaya penambahan diam-diam jadi
merah.

Pelajarannya untuk sisa tinjauan ini: **menambah tool lebih mudah daripada menepati
janji tool yang sudah ada.** Itu yang membentuk urutan di bawah.

---

## 1. Yang kuterima apa adanya

### Penemuan entitas — ini blocker sebenarnya

Setuju, dan ini yang paling menentukan. `create_sketch`, `fillet`, `chamfer`,
`hole_wizard`, `linear_pattern` semua menuntut `EntityRef`, sementara satu-satunya
sumber ref adalah `get_selection` yang bergantung pada manusia mengklik. AI yang
harus minta tolong diklikkan bukan otomasi.

`list_reference_geometry`, `find_faces`/`find_edges` dengan query semantik, dan
`create_reference_plane` masuk. Ini juga yang sudah kami tandai sebagai prasyarat
Fase 3 dengan nama lain: *EntityRef resolver*.

### Siklus dokumen

Setuju seluruhnya. `rebuild` paling telanjang: `list_features` melaporkan
`rebuildRequired` dan tidak ada cara menindaklanjutinya. Melaporkan masalah tanpa
menyediakan jalan keluar adalah setengah fitur.

### `set_material`

Setuju, dan kena telak. Kontrak kami ketat soal massa `null` tanpa material, tapi
tanpa cara meng-assign material aturan itu berubah dari penjaga menjadi tembok
buntu.

### Recovery

Setuju, **dan ini kunaikkan di atas daftar tool.** Pertanyaanmu — *"model
di-rollback, atau dibiarkan?"* — bukan permintaan tool, melainkan **keputusan
kebijakan** yang membentuk tanda tangan setiap tool tulis. Menjawabnya setelah 12
tool ditulis berarti menulis ulang 12 tool.

Posisi awalku: **tidak ada rollback otomatis.** Alasannya, rollback itu sendiri
operasi yang bisa gagal, dan gagal saat memulihkan dari kegagalan adalah keadaan
terburuk yang mungkin — pengguna kehilangan pekerjaan karena kode yang bermaksud
menyelamatkannya. Gantinya: `run_design_plan` melapor persis sampai step berapa
model berubah, dan menyerahkan keputusan ke manusia dengan `undo` eksplisit.
Silakan bantah.

### Celah kontrak yang kamu tunjuk

- **`hole_wizard.positions` tanpa sistem koordinat** — benar, dan ini memang akan
  jadi sumber bug terbesar. Satu permukaan planar tidak punya origin maupun arah
  sumbu bawaan. Harus eksplisit: origin dan sumbu U/V diturunkan dari apa.
- **Required bersyarat** — setuju, pindah ke `if`/`then` atau `oneOf`. Aturan yang
  hanya hidup di `description` adalah aturan yang diserahkan ke AI untuk dipatuhi
  secara sukarela.
- **`linear_pattern` hanya lewat `edge`** — benar. Tambah opsi `axis`/`plane`.
- **`volumeMm3After` tidak nullable** — benar, dan itu tidak konsisten dengan
  aturan kami sendiri ("null, bukan nol"). Diperbaiki bersama tool-nya ditulis.
- **`list_capabilities`** — setuju, murah dan menghapus satu kelas tebakan.

---

## 2. Yang kuterima dengan koreksi

### Units dokumen

Setuju field-nya ditambahkan, tapi **alasannya bukan yang kamu kira.**

API SolidWorks mengembalikan SI — meter dan kilogram — **terlepas dari satuan
dokumen.** Konversi kami ke mm berangkat dari meter, jadi keluaran kami tidak
pernah salah karena dokumennya inch. Risiko yang kamu khawatirkan tidak ada di
jalur baca.

Yang benar-benar butuh satuan dokumen adalah **jalur tulis**: nilai dimensi yang
kita setel, dan apa yang dilihat manusia di layar saat memverifikasi. Jadi
field-nya tetap masuk — sebagai konteks untuk menulis, bukan sebagai koreksi untuk
membaca.

### `capture_view` / screenshot

Diterima **sebagai alat bantu manusia, bukan sebagai bukti.**

"LLM bisa verifikasi visual" justru jebakan yang paling dihindari proyek ini.
Gambar yang terlihat benar adalah bentuk paling meyakinkan dari "melapor berhasil
sambil gagal" — dan kami sudah membayar kelas itu empat kali dalam satu minggu.
Sebuah plat yang lubangnya meleset 3 mm terlihat sempurna di screenshot.

Verifikasi tetap milik angka: volume berkurang, posisi lubang dibaca ulang, sketsa
fully defined. Screenshot boleh ada, ditandai **tidak boleh dipakai sebagai
evidence** di ledger, dan tidak boleh menaikkan verdict apa pun.

---

## 3. Yang kubantah

### Fitur dasar yang "hilang" bukan P0

`revolve`, `shell`, `mirror`, `circular_pattern` — semuanya akan dibutuhkan, tapi
menaruhnya di P0 berarti menambah empat permukaan API yang belum terverifikasi
sebelum **satu pun** jalur tulis terbukti bekerja ujung ke ujung.

Golden test Fase 3 kami adalah *mounting plate 100×80×10 dengan 4 lubang M6
counterbore*. Yang dibutuhkan untuk menutupnya: sketsa persegi, extrude, hole
wizard, linear pattern. Itu saja. Setelah rantai itu hijau, menambah `revolve`
jadi pekerjaan satu tool — bukan taruhan.

`circular_pattern` yang paling kuat alasannya dari keempatnya (bolt circle memang
sangat umum), tapi ia tetap menunggu giliran setelah `linear_pattern` terbukti,
karena keduanya berbagi masalah yang sama soal arah dan penghitungan instance.

### Primitive sketsa: setuju arahnya, bantah cakupannya

`add_line`, `add_arc`, `add_relation`, `add_dimension` memang dibutuhkan untuk
profil L-bracket. Tapi `sketch_fillet` dan `offset_entities` tidak dibutuhkan
sampai ada profil yang menuntutnya.

Yang lebih penting: begitu `add_relation` ada, **`fully_define_sketch` jadi
berbahaya.** Tool itu sudah dilarang memakai Fix massal, dan menambah relasi
otomatis membuka jalan belakang ke pelanggaran yang sama. Perlu dipikirkan
bersama sebelum ditulis, bukan sesudah.

---

## 4. Pertanyaanmu soal Fase 2

Bukan kelalaian, dan bukan fase edit parameter.

Fase 2 adalah **provider host** — menjalankan `agy`, mendaftarkan server MCP,
panel chat. Tidak ada satu pun tool CAD di dalamnya, jadi wajar katalog ini loncat
dari `phase: 1` ke `phase: 3`. Fase 2 sudah ditutup 2026-11-05: chat di panel
memanggil tool MCP di dalam SolidWorks dan melaporkan hasilnya apa adanya.

Edit parameter yang kamu maksud adalah **Lapis 2**, dan itu memang belum punya
tool sendiri — `parameterOverrides` di `run_design_plan` hanya menjalankan ulang
plan, tidak menyentuh model yang sudah ada. Temuanmu soal
`list_dimensions`/`set_dimension_value` benar dan itu lubang yang nyata.

---

## 5. Urutan yang kuusulkan

Berbeda dari urutanmu di satu hal: **keputusan mendahului tool.**

**Tahap A — keputusan, bukan kode**

1. Kebijakan recovery: rollback otomatis atau tidak (posisiku di atas)
2. Sistem koordinat `hole_wizard.positions`: origin dan sumbu U/V dari apa
3. Nasib `fully_define_sketch` setelah `add_relation` ada

Ketiganya membentuk tanda tangan tool. Menjawabnya belakangan berarti menulis ulang.

**Tahap B — menepati janji yang sudah dibuat**

4. Isi empat field di `fieldsNotPopulated`, atau hapus dari kontrak
5. Required bersyarat jadi `if`/`then`
6. `volumeMm3After` nullable

**Tahap C — membuka jalan tulis**

7. Penemuan entitas: `list_reference_geometry`, `find_faces`/`find_edges`,
   `create_reference_plane`
8. Siklus dokumen: `create_document`, `save_document`, `rebuild`
9. `set_material`

**Tahap D — rantai golden Fase 3, sesedikit mungkin**

10. Sketsa → extrude → hole wizard → linear pattern, sampai *mounting plate*
    hijau ujung ke ujung

**Tahap E — setelah ada yang terbukti**

11. `revolve`, `shell`, `mirror`, `circular_pattern`
12. `measure`, `list_dimensions`/`set_dimension_value`, `get_run_trace`,
    `list_capabilities`
13. Assembly dan drawing

Satu catatan soal assembly dan drawing: kamu benar bahwa `get_active_document`
sudah menerima `assembly|drawing` sementara dukungannya belum ada. Itu sudah
ditandai di `capabilities.yaml` — `document_inspection` berstatus `verified`
**hanya untuk part prismatik sederhana di SW2025 SP01.1**, dan assembly,
permukaan, serta konfigurasi ganda disebut eksplisit sebagai belum pernah diuji.
`verified` hanya berguna selama artinya tidak dilebarkan diam-diam.
