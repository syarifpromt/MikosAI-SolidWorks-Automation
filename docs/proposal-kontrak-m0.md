# Proposal perubahan kontrak — M0 langkah 1

Status: **DISETUJUI dan DITERAPKAN** pada 2026-11-05. Keenam keputusan disetujui
dengan empat amandemen, dan semuanya sudah masuk ke `schemas/tools.json`,
`schemas/common.schema.json`, dan `capabilities.yaml`.

Amandemen yang diterima:

1. **A.2** — `null` wajib disertai alasan (`readBackFailed`), dan di laporan untuk
   manusia `null` diperlakukan **sama dengan `true`**: anggap model berubah,
   periksa snapshot.
2. **A.4** — `change_extent_known` berverdict **FAIL**, bukan WARN. Alasannya
   benar dan lebih kuat daripada usulku: WARN membuat run terlihat lulus dengan
   catatan, padahal ini persis kondisi di mana manusia harus memeriksa snapshot.
3. **B** — lihat koreksi di bawah; premisku salah.
4. **D.1** — validasi **per kombinasi** (`holeType × standard × fastenerType ×
   size`), bukan per field, dan pemetaan nama→enum **di-generate dari refleksi
   PIA**, bukan ditulis tangan.
5. **D.3** — M0 hanya membuka `simple` dan `counterbore`; tipe lain `BLOCKED`
   sampai test urutan parameternya SW_PASS.

## KOREKSI atas B — premisku salah

Aku menulis bahwa `CreatePoint` menerima koordinat model. **Itu tidak ditopang
bukti apa pun.** Aku menyimpulkannya dari *adanya* tiga parameter, dan refleksi
hanya memberi tanda tangan — bukan semantik. Menyimpulkan semantik dari jumlah
parameter adalah bentuk lain dari menebak, yaitu hal yang dilarang briefing ini.

Koreksinya: `ISketchManager.Create*` menerima koordinat dalam **ruang sketsa
aktif**; `Z` ada karena method yang sama dipakai 3D sketch.

Bukti pendukung yang bisa kuperiksa: `ISketch.ModelToSketchTransform` **ada** di
PIA, begitu juga `IMathTransform.Inverse`. Keberadaan transform model→sketsa itu
sendiri adalah argumen kuat — kalau entitas sketsa hidup di koordinat model,
transform itu tidak akan ada gunanya.

Tapi ini tetap **argumen, bukan bukti**. Yang menyelesaikannya adalah test yang
disyaratkan: face atas, face samping, dan face pada offset dari origin — dan
test itu harus pernah terlihat **merah** dengan versi tanpa transformasi. Test di
Front Plane saja tidak dihitung, karena di sana koordinat sketsa kebetulan sama
dengan koordinat model, sehingga transform yang hilang pun akan lulus.

Kontrak `positions` tetap `Vector3D` **koordinat model** — yang berubah bukan
kontraknya, melainkan siapa yang menanggung transformasinya: tool, bukan AI.

Mencakup tiga keputusan: kebijakan recovery (A), sistem koordinat
`hole_wizard.positions` (B), dan nasib `fully_define_sketch` (C). Ditambah
temuan API yang muncul saat memverifikasi B, yang mengubah lebih banyak kontrak
daripada yang kita duga (D).

Semua tanda tangan API di bawah **direfleksikan dari PIA asli** di
`…\api\redist\SolidWorks.Interop.sldworks.dll` pada SolidWorks 2025 SP01.1 —
bukan diambil dari ingatan.

---

## A. Kebijakan recovery

Mengikuti keputusanmu. Satu penyimpangan, ditandai jelas di A.2.

### A.1 Setiap tool tulis membawa `modelChanged`

Ditambahkan ke `ToolResult` di `common.schema.json`, hanya terisi untuk tool
yang `mutates: true`:

```diff
   "ToolResult": {
     "properties": {
       "ok": { "type": "boolean" },
       "capabilityId": { "type": "string" },
+      "modelChanged": {
+        "type": ["boolean", "null"],
+        "description": "Apakah model BERUBAH akibat panggilan ini. Hanya untuk tool mutates=true; null untuk tool read-only. true/false WAJIB hasil pembacaan ulang, bukan asumsi. null berarti tidak bisa ditentukan - lihat A.2."
+      },
       "verification": { "$ref": "#/$defs/VerificationResult" }
     }
   }
```

Fitur yang gagal atau error **tidak dihapus otomatis**. Tool melaporkan
`featureName` sebenarnya beserta statusnya, dan `run_design_plan` berhenti.

### A.2 PENYIMPANGAN: `modelChanged` tiga keadaan, bukan dua

Kamu menulis `modelChanged: boolean`. Aku mengusulkan `boolean|null`, dan ini
satu-satunya tempat aku menyimpang dari keputusanmu.

Alasannya: `modelChanged` **wajib hasil pembacaan ulang**, bukan asumsi — itu
mengikuti aturanmu sendiri bahwa status sukses API bukan bukti. Tapi ada
keadaan di mana pembacaan ulang itu sendiri tidak bisa dilakukan: panggilan
melempar di tengah, dokumen jadi tidak responsif, atau pembacaan volume gagal.

Di keadaan itu, `false` adalah **klaim yang tidak diperiksa**, dan klaimnya
berbahaya: "model tidak berubah" adalah alasan paling masuk akal bagi manusia
untuk **tidak** memulihkan dari snapshot. Kalau ternyata model berubah, dia
kehilangan pekerjaannya karena laporan kita.

`null` = "tidak bisa dipastikan, perlakukan seolah berubah". Ini aturan yang
sama persis dengan `densityAssigned` tiga keadaan, dan dengan ukuran yang tidak
ada dilaporkan `null` bukan nol. Dua keadaan memaksa kita berbohong di keadaan
ketiga.

Konsekuensi di `run_design_plan`: satu saja step dengan `modelChanged: null`
membuat `firstChangedStep` ikut tidak pasti, dan itu harus terlihat — lihat
A.4.

### A.3 Snapshot pasif sebelum step pertama

```diff
   "run_design_plan": {
     "outputData": {
       "runId": "string",
+      "snapshotPath": "string|null",
       "stepsCompleted": "integer",
+      "lastGoodStep": "string|null",
+      "firstChangedStep": "string|null",
+      "failedStep": "string|null",
       "stepResults": "array of ToolResult",
       "finalVerdict": "PASS|WARN|FAIL|BLOCKED"
     }
   }
```

Realisasinya — **direfleksikan, bukan ditebak**:

```
IModelDocExtension.SaveAs3(String Name, Int32 Version, Int32 Options,
    Object ExportData, Object AdvancedSaveAsOptions,
    ref Int32 Errors, ref Int32 Warnings)   -> Boolean
```

`Options` = `swSaveAsOptions_Copy` (2) `|` `swSaveAsOptions_Silent` (1) = **3**.
Nilai diambil dari `swSaveAsOptions_e` di swconst asli. Catat bahwa
`swSaveAsOptions_CopyAndOpen` bernilai 512 dan **bukan** yang kita mau — itu
justru membuka salinannya.

`Errors` dan `Warnings` adalah `ref`, bukan `out`. Ini bukan kerewelan: kami
baru saja membayar kesalahan yang persis sama di `CreateCommandGroup2`, di mana
refleksi melaporkan `IsOut=true` sementara `IsIn` juga true, dan C# menuntut
`ref`.

Lokasi snapshot:

```
%LOCALAPPDATA%\MikosAI\snapshots\<runId>\<judul-dokumen>.SLDPRT
```

Dokumen yang belum pernah disimpan tetap di-snapshot; namanya diambil dari
judul dokumen, bukan dari path yang memang belum ada.

**Snapshot gagal → plan TIDAK dijalankan, verdict `BLOCKED`.** Tidak ada
kompromi di sini: menjalankan plan tanpa jaring berarti kegagalan di step 5
meninggalkan model setengah jadi tanpa jalan pulang.

`snapshotPath` dikembalikan **juga saat plan berhasil**, supaya manusia tahu
ada berkas yang bisa dibuang.

### A.4 Saat gagal

Berhenti. Tidak memulihkan apa pun. Melaporkan:

| field | arti | kalau tidak diketahui |
|---|---|---|
| `lastGoodStep` | step terakhir yang verdict-nya PASS | `null` kalau belum ada |
| `firstChangedStep` | step pertama yang `modelChanged` **true** | `null` kalau model belum berubah |
| `failedStep` | step yang gagal | selalu terisi saat gagal |
| `snapshotPath` | jaring pengaman | `null` mustahil — plan BLOCKED kalau snapshot gagal |

Tambahan dari A.2: kalau ada step dengan `modelChanged: null` **sebelum**
`firstChangedStep` yang diketahui, maka `firstChangedStep` ikut jadi tidak
pasti. Diusulkan check verifikasi `change_extent_known` ber-verdict **WARN**
yang menyebut step mana yang tidak bisa dipastikan — supaya manusia tidak
membaca `firstChangedStep: step7` sebagai jaminan bahwa step 1-6 aman.

### A.5 Pemulihan adalah keputusan manusia

Tidak ada tool restore di M0. Kalau nanti diusulkan, ia dipanggil eksplisit,
dan menurutku ia harus **membuka snapshot sebagai dokumen terpisah**, bukan
menimpa dokumen kerja — menimpa adalah operasi yang bisa gagal saat memulihkan
dari kegagalan, yaitu keadaan terburuk yang mungkin.

---

## B. `hole_wizard.positions` — pertanyaannya berubah

Pertanyaan awalnya: *"face tidak punya sistem koordinat 2D, origin dan arah
sumbunya dari mana?"*

Jawabannya ternyata bukan memilih konvensi, karena **API Hole Wizard tidak
menerima posisi sama sekali.** Direfleksikan:

```
Feature HoleWizard5(Int32 GenericHoleType, Int32 StandardIndex,
    Int32 FastenerTypeIndex, String SSize, Int16 EndType,
    Double Diameter, Double Depth, Double Length,
    Double Value1 … Double Value12, String ThreadClass,
    Boolean RevDir, Boolean FeatureScope, Boolean AutoSelect,
    Boolean AssemblyFeatureScope, Boolean AutoSelectComponents,
    Boolean PropagateFeatureToParts)
```

Tidak ada satu pun parameter koordinat. Posisi lubang di SolidWorks hidup di
**sketsa posisi**, dan ditaruh lewat:

```
SketchPoint ISketchManager.CreatePoint(Double X, Double Y, Double Z)
```

Tiga koordinat, bukan dua.

### Usulan

`positions` menjadi titik **3D dalam koordinat model**, bukan `Point2D`:

```diff
   "positions": {
     "type": "array",
     "minItems": 1,
-    "items": { "$ref": "common.schema.json#/$defs/Point2D" },
-    "description": "Posisi pada permukaan, dalam mm."
+    "items": { "$ref": "common.schema.json#/$defs/Vector3D" },
+    "description": "Posisi lubang dalam KOORDINAT MODEL, mm. Setiap titik wajib berada pada permukaan yang dirujuk `face`, dalam toleransi. Bukan koordinat 2D pada face: face planar tidak punya origin maupun arah sumbu bawaan, dan SolidWorks sendiri tidak menerima posisi lewat HoleWizard5 - posisi ditaruh sebagai titik sketsa lewat CreatePoint(x,y,z), yang memang tiga koordinat."
   }
```

Kenapa koordinat model, bukan mengarang frame lokal:

1. **Memetakan satu-ke-satu ke `CreatePoint`.** Tidak ada lapisan konversi yang
   bisa salah arah sumbu.
2. **AI sudah punya angkanya.** `get_bounding_box` mengembalikan koordinat
   model, jadi "4 lubang berjarak 20 mm dari tiap tepi" bisa dihitung langsung.
3. **`actualPositions` jadi bermakna.** Pembacaan ulang dan permintaan berada di
   kerangka yang sama, jadi perbandingannya aritmetika — bukan terjemahan.
4. Frame lokal apa pun yang kita karang akan jadi konvensi tak tertulis yang
   harus dihafal, dan itu sumber bug yang kamu antisipasi.

Ditambah check verifikasi baru, **required, FAIL kalau gagal**:

- `positions_on_face` — setiap titik berada pada face yang dirujuk dalam
  toleransi. Titik yang melayang di luar face adalah permintaan yang tidak bisa
  dipenuhi, dan lebih baik ditolak di depan daripada menghasilkan lubang di
  tempat lain.

Harga yang kita bayar: AI harus tahu posisi face di ruang model. Itu wajar — ia
memang harus memanggil `find_faces` lebih dulu, dan itu sudah ada di urutan M0
langkah 5.

---

## C. `fully_define_sketch` — diubah jadi diagnostik

Direfleksikan:

```
Int32 ISketchManager.FullyDefineSketch(Boolean EntitiesToFullyDefine,
    Boolean UseRelations, Int32 RelationsToApply, Boolean UseDimensions,
    Int32 HorizontalDimScheme, Object HorizontalDatumDisp,
    Int32 VerticalDimScheme, Object VerticalDatumDisp,
    Int32 HorizontalDimPlacement, Int32 VerticalDimPlacement)
```

Perhatikan `HorizontalDimScheme`, `VerticalDimScheme`, dan kedua `DatumDisp`.
Fungsi ini **memilih skema dimensi dan datum**. Itu bukan formalitas: skema
datum menentukan bagaimana part berubah ketika parameter diubah. Memilihnya
otomatis berarti mengambil keputusan desain, lalu menyajikan hasilnya sebagai
sekadar "sketsa sudah fully defined".

Begitu `add_relation` ada, tool ini juga jadi jalan belakang ke larangan Fix
massal: relasi yang ditambahkan otomatis mencapai hasil yang sama, hanya lebih
sopan.

Dan bahayanya sunyi: sketsa yang fully defined **lewat dimensi yang dipilih
mesin** lolos setiap verifikasi kita — `constrainedStatus` hijau, `verdict`
PASS — sementara partnya salah dengan cara yang tidak ada satu pun check kita
tangkap. Itu kelas "melapor berhasil sambil gagal" yang sudah kita bayar empat
kali minggu ini.

### Usulan

Hapus tool `fully_define_sketch`, ganti dengan `diagnose_sketch`, **`mutates:
false`**:

```diff
-    {
-      "name": "fully_define_sketch",
-      "capabilityId": "sketch_authoring",
-      "mutates": true,
-      "description": "Coba definisikan penuh sketsa…"
-    },
+    {
+      "name": "diagnose_sketch",
+      "capabilityId": "sketch_authoring",
+      "phase": 3,
+      "mutates": false,
+      "description": "Kenapa sebuah sketsa belum fully defined, dan apa yang kurang. TIDAK mengubah apa pun.",
+      "inputSchema": {
+        "type": "object",
+        "additionalProperties": false,
+        "properties": { "sketchName": { "type": "string" } }
+      },
+      "outputData": {
+        "constrainedStatus": "string",
+        "fullyDefined": "boolean",
+        "underDefinedEntities": "array of {entityId, missing}",
+        "degreesOfFreedom": "integer|null"
+      },
+      "notes": "Menggantikan fully_define_sketch, yang MENULIS. ISketchManager.FullyDefineSketch menerima HorizontalDimScheme, VerticalDimScheme, dan datum display - ia MEMILIH skema dimensi dan datum, dan itu keputusan desain yang menentukan bagaimana part berubah saat parameter diubah. Sketsa yang fully defined lewat dimensi pilihan mesin lolos SELURUH verifikasi kami sementara partnya salah. Yang dibutuhkan AI bukan 'buat hijau', melainkan 'apa yang kurang' - lalu planner menambahkan add_dimension eksplisit, atau mengembalikan openQuestions kalau dimensinya memang tidak pernah disebut pengguna."
+    },
```

Ini **mengurangi** satu permukaan tulis, bukan menambah, dan menutup loop
dengan aturan "jangan mengarang nilai engineering": dimensi yang tidak disebut
pengguna menjadi `openQuestions`, bukan tebakan mesin.

Kalau kamu tidak setuju menghapusnya, alternatif yang bisa kuterima:
pertahankan dengan `UseDimensions: false` dipaksa, whitelist tipe relasi, dan
wajib melaporkan **setiap** relasi yang ditambahkan. Aku tetap menganggap ini
lebih lemah, karena relasi otomatis pun memilih skema.

---

## D. Temuan yang belum masuk daftar keputusan

Muncul saat memverifikasi B. Semuanya menyentuh kontrak `hole_wizard` yang
sudah ada.

### D.1 `standard` dan `fastenerSize` bukan string bebas

Kontrak kita menerima `standard: "ISO"` dan `fastenerSize: "M6"` sebagai
string. API-nya:

- `StandardIndex` adalah **Int32**, dan ada enum aslinya: `swWzdHoleStandards_e`
  — 19 nilai, `swStandardISO = 8`, `swStandardDIN = 4`,
  `swStandardAnsiMetric = 1`, dan seterusnya.
- `FastenerTypeIndex` juga **Int32**, dengan `swWzdHoleStandardFastenerTypes_e`
  — **404 nilai**.
- `SSize` memang **String**, jadi `"M6"` boleh tetap string.

Kabar baiknya: pemetaannya bukan indeks basis data yang kita tebak, melainkan
enum bernama. Kabar buruknya: kombinasi standard × fastener type ada ratusan,
dan yang tidak valid tidak akan ketahuan sampai runtime.

Usulku: `standard` tetap string di kontrak, tapi **divalidasi terhadap
whitelist yang diturunkan dari enum**, dan nilai di luar whitelist menghasilkan
`BLOCKED` + `openQuestions` — bukan tebakan ke ISO. Butuh persetujuanmu karena
ini mempersempit input yang sebelumnya bebas.

### D.2 Dua belas `Value1…Value12`

`HoleWizard5` punya 12 parameter `Double` bernama `Value1` sampai `Value12`
yang artinya **bergantung pada tipe lubang**. Ini persis perangkap urutan
parameter yang kamu peringatkan, dan ia lebih buruk daripada di
`FeatureExtrusion` karena namanya tidak memberi petunjuk apa pun.

Konsekuensi untuk Definition of Done: `hole_wizard` **wajib** punya test urutan
parameter per tipe lubang — `counterbore` dan `countersink` tidak bisa diwakili
satu test.

### D.3 `HoleWizard5` menambah `Length` yang tidak ada di `HoleWizard4`

Tanda tangannya berbeda antarversi: `HoleWizard5` menyisipkan `Double Length`
setelah `Depth`. Kalau kita memanggil lewat tipe yang ditanam
(`EmbedInteropTypes`), versi yang dipakai ditentukan PIA mesin pengguna.

Usulku: panggil **`HoleWizard5` saja**, dan kalau tidak tersedia, kembalikan
kegagalan yang menyebutkan versi — bukan jatuh ke `HoleWizard4` dengan urutan
parameter yang bergeser. Fallback diam-diam di sini akan menghasilkan lubang
dengan kedalaman yang salah, dan itu lolos sebagai sukses.

---

## Yang kuminta diputuskan

1. **A.2** — `modelChanged` tiga keadaan (`boolean|null`) alih-alih dua. Ini
   penyimpangan dari keputusanmu; aku butuh ya/tidak.
2. **A.4** — check `change_extent_known` ber-verdict WARN saat ada step yang
   `modelChanged`-nya tidak pasti.
3. **B** — `positions` jadi `Vector3D` koordinat model. Ini **breaking change**
   pada skema yang sudah dipakai Jalur Platform.
4. **C** — hapus `fully_define_sketch`, ganti `diagnose_sketch` yang read-only.
   Ini mengurangi permukaan tulis.
5. **D.1** — `standard` divalidasi terhadap whitelist dari
   `swWzdHoleStandards_e`, di luar itu `BLOCKED`.
6. **D.3** — hanya `HoleWizard5`, tanpa fallback ke `HoleWizard4`.

Setelah disetujui, aku lanjut ke M0 langkah 2 (menutup empat field
`fieldsNotPopulated`) dan langkah 3 (test satuan jalur tulis di dokumen mm dan
inch).

Satu hal yang perlu kamu siapkan untuk langkah 3: aku butuh **dokumen baru
bersatuan inch**. Aku tidak boleh menutup SolidWorks sendiri, dan aku belum
punya `create_document`, jadi dokumen itu harus kamu buat — atau kamu izinkan
aku menambahkan `create_document` lebih dulu, yang memang ada di langkah 4.
