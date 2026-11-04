# Proposal kontrak — M0 langkah 2: bentuk empat field `fieldsNotPopulated`

Status: **DISETUJUI dan DITERAPKAN** 2026-11-05, dengan tiga koreksi.

Koreksi yang diterima, dan semuanya memperbaiki proposalku:

1. **`children` bukan konsumen, `parents` bukan sketsa.** Aku menyamakan
   "children" dengan "konsumen" tanpa dasar. Dipisah jadi dua field:
   `consumedByFeatures` (hanya profil/path/posisi) dan `dependents` (seluruh
   children tanpa filter). `delete_feature` nanti memakai `dependents` - melihat
   satu konsumen padahal ada dependent lain berarti menghapus sesuatu yang masih
   dipakai. `parentSketches` difilter per tipe; plane dan fitur tidak ikut.
   Kalau test membuktikan `GetChildren()` ternyata konsumen saja, satu field
   cukup dan kontraknya disederhanakan.
2. **Invariant dimensi.** Ditambah `dimensionCountRead` supaya
   `driving + driven + unknown = dimensionCountRead` bisa diperiksa dari luar.
   `unknownDrivenStateCount > 0` menghasilkan minimal WARN.
3. **`hasOpenContour` dihapus sepenuhnya**, tanpa boolean turunan - lebih tegas
   daripada usulku yang masih menawarkan opsi mempertahankannya. Ditambah
   `branchPointCount`, karena T-junction tidak punya ujung bebas tapi tetap
   bukan profil yang sah. Dan hipotesis identitas `SketchPoint`: kalau ujung yang
   tersambung berbagi objek yang sama, konektivitas dihitung EKSAK tanpa
   toleransi, dan toleransi hanya untuk `nearlyCoincidentEndpointCount`.
   `ISketchPoint.GetID()` ada, jadi hipotesis itu bisa diuji.

Keempat usulanmu kuterima. Dua di antaranya punya alasan yang lebih kuat
daripada yang kamu ajukan, satu kuusulkan berubah lebih jauh dari usulanmu, dan
ada satu batas yang harus disebut sebelum kita menjanjikan apa pun lagi.

Semua tanda tangan direfleksikan dari PIA asli SolidWorks 2025 SP01.1.

---

## 1. `parentSketch` → `parentSketches: string[]` — DITERIMA

Alasanmu: loft dan sweep di M1 memakai lebih dari satu sketsa, jadi bentuk
tunggal akan jadi breaking change kedua.

Ada alasan **yang berlaku sekarang**, bukan nanti:

```
Object IFeature.GetParents()      -> array of Feature
```

API-nya **sudah jamak**. Bentuk tunggal berarti kita harus *membuang* data yang
sudah dikembalikan SolidWorks agar muat ke bentuk yang lebih sempit. Membuang
data untuk memuat bentuk adalah cara kita nanti menemukan bahwa bentuknya salah
— dan saat itu datanya sudah tidak ada untuk membuktikannya.

Jadi bukan "antisipasi M1", melainkan "jangan buang yang sudah ada".

## 2. `consumedByFeature` → `consumedByFeatures: string[]` — DITERIMA

Sama, dan dengan satu temuan yang mengubah tempat implementasinya:

```
Object IFeature.GetChildren()     -> array of Feature
```

**`ISketch` tidak punya method konsumen sama sekali.** Tidak ada
`GetSketchConsumer` atau sejenisnya. Jadi konsumen sebuah sketsa hanya bisa
didapat dari **node fitur** sketsa itu lewat `GetChildren()`.

Konsekuensinya untuk implementasi: `inspect_sketches` tidak cukup memegang
`ISketch` — ia harus menelusuri FeatureManager tree. Itu kebetulan menjelaskan
kenapa field ini tidak pernah terisi sejak awal: pembacanya memang tidak pernah
punya aksesnya.

Alasanmu soal `delete_feature` juga kuterima dan menurutku yang paling
menentukan: melihat satu konsumen padahal ada dua berarti menghapus sketsa yang
masih dipakai, dan itu merusak fitur lain secara diam-diam.

## 3. `dimensionCount` dipisah — DITERIMA, dengan ember ketiga

Bisa dibaca:

```
Object IFeature.GetFirstDisplayDimension()  /  GetNextDisplayDimension()
Boolean IDisplayDimension.IsReferenceDim()
Int32   IDimension.DrivenState
```

Alasanmu benar: dimensi reference tidak mengunci geometri, jadi hitungan
gabungan menyesatkan diagnosis.

Tapi ada **dua** cara menanyakannya, dan pilihannya menentukan:

| cara | tipe | bisa bilang "tidak tahu"? |
|---|---|---|
| `IsReferenceDim()` | `Boolean` | **tidak** |
| `DrivenState` | `Int32` | **ya** |

`swDimensionDrivenState_e` punya tiga nilai, dan yang pertama menentukan:

```
swDimensionDrivenUnknown = 0
swDimensionDriven        = 1
swDimensionDriving       = 2
```

SolidWorks **punya** keadaan "tidak diketahui", dan ia nyata. Kalau kita memakai
`IsReferenceDim()`, setiap dimensi yang `Unknown` terpaksa masuk ke salah satu
ember — dan itu persis masalah dua-keadaan yang baru kita perbaiki di
`modelChanged`.

Jadi usulku **tiga** hitungan, bukan dua:

```diff
-  "dimensionCount": "integer|null"
+  "drivingDimensionCount": "integer|null",
+  "drivenDimensionCount": "integer|null",
+  "unknownDrivenStateCount": "integer|null"
```

Dibaca lewat `DrivenState`, bukan `IsReferenceDim()`. `unknownDrivenStateCount`
yang bukan nol berarti diagnosis sketsa tidak lengkap, dan itu harus terlihat —
bukan dibulatkan ke "driving" supaya angkanya rapi.

## 4. `hasOpenContour` → `openEndpointCount` — USULAN BERBEDA

Usulanmu: definisikan apakah geometri construction ikut dihitung, dan menurutmu
tidak, karena centerline terbuka itu normal.

Setuju soal construction, dan itu bisa dibaca:

```
Boolean ISketchSegment.ConstructionGeometry
```

Tapi ada yang lebih mendasar: **`hasOpenContour` bukan sesuatu yang dibaca,
melainkan sesuatu yang kita HITUNG.** Tidak ada API yang menjawabnya. Kita harus
membangun konektivitas dari titik ujung segmen dan mencari ujung yang tidak
berpasangan — yaitu penalaran geometris dengan toleransi.

Itu kelas pekerjaan yang berbeda dari tiga field sebelumnya, dan punya kasus
tepi yang nyata. `swSketchSegments_e`:

```
swSketchLINE = 0, swSketchARC = 1, swSketchELLIPSE = 2,
swSketchSPLINE = 3, swSketchTEXT = 4, swSketchPARABOLA = 5
```

`swSketchTEXT` tidak punya titik ujung yang bermakna sama sekali.

Masalah yang lebih dalam: **`hasOpenContour: true` adalah kesimpulan, bukan
fakta.** Kesimpulan menyembunyikan toleransi yang kita pilih dan kasus tepi yang
kita lewatkan, lalu menyajikannya sebagai ya/tidak yang terdengar pasti. Itu pola
yang sama dengan `densityAssigned` yang dulu disimpulkan dari status sukses.

Usulku ganti dengan **ukuran**, bukan kesimpulan:

```diff
-  "hasOpenContour": "boolean|null"
+  "openEndpointCount": "integer|null",
+  "endpointToleranceMm": "number|null",
+  "segmentsNotAnalyzed": "array of string|null"
```

- `openEndpointCount: 0` berarti setiap ujung berpasangan — profil tertutup.
  Dua kontur tertutup terpisah tetap 0, dan itu benar.
- `openEndpointCount: 2` berarti satu kontur terbuka. Angkanya memberi tahu
  **berapa banyak**, bukan sekadar bahwa ada.
- `endpointToleranceMm` membuat toleransi yang kita pakai terlihat, bukan
  tersembunyi di dalam kata "open".
- `segmentsNotAnalyzed` menyebut segmen yang tidak bisa dianalisis — `TEXT`, dan
  apa pun yang kita temukan nanti. Kosong berarti seluruh sketsa teranalisis.

Geometri construction **tidak** dihitung, sesuai usulanmu. Centerline terbuka
memang normal, dan memasukkannya akan membuat hampir setiap sketsa beranotasi
terlihat rusak.

Kalau kamu lebih suka mempertahankan `hasOpenContour` sebagai boolean turunan di
atas `openEndpointCount == 0`, aku bisa menerimanya — asalkan angkanya tetap
ada, supaya kesimpulannya bisa diperiksa.

---

## Semantik yang berlaku untuk keempatnya

Sesuai arahanmu:

- `[]` atau `0` = **sudah dibaca, memang tidak ada**
- `null` = **tidak diketahui**, dan field-nya tetap tercantum di
  `fieldsNotPopulated`

Keduanya harus bisa dibedakan dari luar, dan itu sebabnya `fieldsNotPopulated`
tidak dihapus begitu field terisi — ia menyusut per field, bukan sekaligus.

## Batas yang harus disebut sekarang

Aku bisa membaca **tanda tangan** dari PIA, tapi tidak **semantiknya**. Yang
belum terbukti dan wajib dijawab test:

1. Apakah `GetParents()` pada sebuah `Extrusion` benar-benar mengembalikan node
   sketsanya — atau sesuatu yang lain, atau tidak keduanya.
2. Apakah `GetChildren()` pada node sketsa mengembalikan fitur yang
   mengkonsumsinya.
3. Apakah `DrivenState` terisi untuk dimensi sketsa, atau hanya untuk dimensi
   drawing.

Aku menuliskannya di sini karena **baru saja melakukan kesalahan yang persis
sama**: aku menyimpulkan `CreatePoint` memakai koordinat model dari *adanya*
tiga parameter. Jumlah parameter adalah fakta tanda tangan; artinya bukan.

Yang kuklaim sekarang hanya soal **bentuk** — `GetParents()` dan `GetChildren()`
mengembalikan array, dan itu fakta tanda tangan. Isinya tetap harus dibuktikan.

## Test yang kamu syaratkan, plus satu

Ketiga test-mu kuterima apa adanya:

1. Sketsa yang dipakai **dua** fitur → `consumedByFeatures` berisi keduanya.
2. Sketsa yang tidak dipakai fitur apa pun → `[]`, **bukan** `null`.
3. Sketsa dengan dimensi driven → hitungannya terpisah.

Tambahan dariku, dan ini yang paling penting dari keempatnya:

4. **Setiap field harus pernah terlihat merah.** Untuk tiap field, pasang versi
   yang mengembalikan nilai lama yang dikarang — `[]` padahal ada konsumen, `0`
   padahal ada dimensi — dan tunjukkan test-nya gagal. Tanpa itu kita cuma
   memindahkan janji kosong dari satu bentuk ke bentuk lain yang lebih rapi.

5. Sketsa yang **sudah terisi** harus hilang dari `fieldsNotPopulated`, dan
   test memastikan daftarnya menyusut — bukan sekadar nilainya berubah.

## Yang kuminta diputuskan

1. `parentSketches` dan `consumedByFeatures` jadi array — **diterima, tinggal
   konfirmasi**.
2. Hitungan dimensi jadi **tiga**, dibaca lewat `DrivenState`, bukan dua lewat
   `IsReferenceDim()`.
3. `hasOpenContour` diganti `openEndpointCount` + `endpointToleranceMm` +
   `segmentsNotAnalyzed`, atau dipertahankan sebagai boolean turunan di atasnya.

Fixture yang kubutuhkan untuk test 1 dan 3: satu part dengan **sketsa yang
dipakai dua fitur** (mis. extrude lalu cut dari sketsa yang sama), dan satu
sketsa yang punya **dimensi driven**. Kalau kamu setuju arahnya, aku tulis
resepnya seperti `CARA-MEMBUAT-PART.md` dan kamu yang membuat partnya.
