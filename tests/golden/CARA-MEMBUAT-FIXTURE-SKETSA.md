# Cara membuat fixture sketsa

Dua part untuk menguji empat field `fieldsNotPopulated`. Dipisah dua supaya
kegagalan membuat salah satunya tidak memblokir yang lain.

Satuan **MMGS** di kedua part. Semua angka harapan di bawah diturunkan dari
geometri — kalau partnya meleset, yang gagal test, dan orang akan menyalahkan
kode.

---

# Part 1 — `fixture-konsumen.SLDPRT`

Menguji: sketsa dipakai dua fitur, sketsa dengan relasi ke sketsa lain, dan
dimensi driven.

## Langkah

1. **File → New → Part**, satuan MMGS.

2. **Sketch1** pada **Front Plane**:
   - **Center Rectangle** berpusat tepat di **Origin**
   - dimensi **100 mm** (horizontal) dan **60 mm** (vertikal)
   - harus **Fully Defined**
   - Sudutnya berada di `(±50, ±30)` — angka ini dipakai di langkah 5.

3. **Features → Extruded Boss/Base** dari Sketch1, `Blind`, **10 mm**, arah
   default (+Z). Namanya `Boss-Extrude1`.

4. **Features → Extruded Cut** **memakai Sketch1 YANG SAMA**, `Blind`,
   **3 mm**, arah default (+Z). Namanya `Cut-Extrude1`.

   Pilih `Sketch1` dari FeatureManager tree, jangan menggambar sketsa baru.
   Setelah ini SolidWorks memperlakukan Sketch1 sebagai **shared sketch** dan
   biasanya memindahkannya ke tingkat atas tree, tidak lagi bersarang di bawah
   `Boss-Extrude1`. Itu tanda langkahnya berhasil.

   Hasilnya plat `100 × 60 × 7`, dari `z = 3` sampai `z = 10`. Cut membuang
   bagian `0..3`, boss mengisi `0..10`.

   > **Kalau SolidWorks menolak memakai ulang Sketch1**, hentikan dan beri tahu
   > aku. Jangan diakali dengan menyalin sketsa — salinan adalah sketsa yang
   > berbeda, dan test ini justru menguji sketsa yang SAMA dipakai dua kali.

5. **Sketch2** pada **Front Plane**, **jangan diextrude**:
   - satu **garis horizontal** dari `(-50, 30)` ke `(-10, 30)`
   - ujung kirinya diberi relasi **Coincident** ke **titik sudut kiri-atas
     Sketch1** (`-50, 30`). Ini yang membuat Sketch2 menjadi *dependent*
     Sketch1 tanpa menjadi konsumennya — inti dari test ini.
   - dimensi **panjang 40 mm** → ini **driving**
   - lalu tambahkan **satu dimensi lagi**: jarak horizontal dari ujung kanan
     garis ke Origin. Sketsa sudah fully defined, jadi SolidWorks akan bertanya
     *"Make Dimension Driven?"* → jawab **Yes**. Nilainya **10 mm**.
   - status akhir harus **Fully Defined**

6. **Plane1** — reference plane lewat SATU GARIS `Sketch1`:
   - **Features → Reference Geometry → Plane**
   - referensi pertama: **sisi atas `Sketch1`** (garis dari `(-50, 30)` ke
     `(50, 30)`)
   - referensi kedua: **Top Plane**, dengan **sudut 30°**
   - namanya `Plane1`, dan **jangan dipakai untuk apa pun**

   Ini contoh tandingan untuk asumsi "child bukan sketsa berarti konsumen".
   `Plane1` memakai geometri `Sketch1`, jadi ia child-nya — tapi ia jelas
   bukan fitur yang mengkonsumsi sketsa sebagai profil.

7. Simpan sebagai `tests/golden/fixture-konsumen.SLDPRT`.

## Nilai yang diharapkan

**Body:** `bodyCount` 1, `size` `100 × 60 × 7`, `volumeMm3` **42000**,
bounding box `z` dari **3** sampai **10**.

**Sketch1**

| field | nilai |
|---|---|
| `constrainedStatus` | `FullyDefined` |
| `consumedByFeatures` | `["Boss-Extrude1", "Cut-Extrude1"]` — dua, urutan bebas |
| `dependents` | memuat keduanya, **dan kemungkinan `Sketch2` serta `Plane1`** — lihat catatan hipotesis |
| `drivingDimensionCount` | 2 |
| `drivenDimensionCount` | 0 |
| `unknownDrivenStateCount` | 0 |
| `openEndpointCount` | 0 |
| `branchPointCount` | 0 |
| `endpointOnSegmentCount` | 0 |
| `connectivityLimits` | `[]` |

Center Rectangle menghasilkan 4 garis + 2 garis konstruksi diagonal. Konstruksi
**tidak** dihitung, jadi keempat garis membentuk loop tertutup: 0 ujung bebas,
0 titik cabang.

**Sketch2**

| field | nilai |
|---|---|
| `constrainedStatus` | `FullyDefined` |
| `consumedByFeatures` | `[]` — **kosong, bukan null** |
| `drivingDimensionCount` | 1 |
| `drivenDimensionCount` | 1 |
| `unknownDrivenStateCount` | 0 |
| `openEndpointCount` | 2 |
| `branchPointCount` | 0 |
| `endpointOnSegmentCount` | 0 |
| `connectivityLimits` | `["undeclared_point_on_segment"]` |

### `Plane1` — contoh tandingan

| | harapan |
|---|---|
| muncul di `Sketch1.dependents` | **ya** (hipotesis) |
| muncul di `Sketch1.consumedByFeatures` | **TIDAK** |
| volume body | tetap **42000 mm³** — plane tidak menambah material |

Kalau `Plane1` muncul di `consumedByFeatures`, berarti aturan klasifikasi kami
salah dan harus diperbaiki sebelum `delete_feature` ditulis.

`typeName` sebenarnya dari `Plane1` dan `Cut-Extrude1` **belum pernah kami
lihat**, dan itu disengaja: laporan run pertama akan menyebutkannya, dan daftar
putih tipe konsumen diisi dari situ — bukan dari ingatan. Lihat
`docs/proposal-deteksi-konsumen.md`.

## Hipotesis yang diuji part ini

**`GetChildren()` mengembalikan semua dependent, bukan hanya konsumen.**

Sketch2 punya relasi ke geometri Sketch1 tapi tidak dipakai fitur apa pun. Jadi:

- kalau `Sketch1.dependents` memuat `Sketch2` → hipotesis **benar**, dan dua
  field (`consumedByFeatures` + `dependents`) memang dibutuhkan
- kalau tidak memuatnya → `GetChildren()` ternyata sudah konsumen saja, dan
  **satu field cukup**. Kontraknya disederhanakan, dan buktinya dilaporkan.

Ini satu-satunya cara memutuskannya. Refleksi PIA hanya bisa menunjukkan bahwa
`GetChildren()` mengembalikan array — isinya tidak.

---

# Part 2 — `fixture-kontur.SLDPRT`

Menguji konektivitas: ujung bebas, titik cabang, geometri konstruksi, dan loop
ganda. **Tidak ada fitur sama sekali** — hanya sketsa, semuanya tidak
dikonsumsi.

## Langkah

1. **File → New → Part**, satuan MMGS. Jangan membuat fitur apa pun.

2. **SketchKotak** pada **Front Plane**:
   - **Corner Rectangle** dari `(0, 0)` ke `(40, 20)`
   - satu **Centerline** (geometri konstruksi) dari `(10, 10)` ke `(30, 10)` —
     kedua ujungnya **bebas di dalam** persegi, tidak menyentuh tepinya

3. **SketchT** pada **Front Plane**: **tiga garis terpisah**, bukan satu garis
   yang dilewati:
   - `(0, 0)` → `(20, 0)`
   - `(20, 0)` → `(40, 0)`
   - `(20, 0)` → `(20, 20)`

   Ketiganya berbagi titik di `(20, 0)`. Pastikan relasi **Coincident**
   terbentuk di sana — itu yang membuatnya titik cabang.

   > Penting: menggambar **satu** garis `(0,0)→(40,0)` lalu menempelkan garis
   > vertikal di tengahnya TIDAK sama. Di kasus itu hanya satu ujung yang
   > bertemu, bukan tiga, jadi `branchPointCount` akan 0 dan test tidak menguji
   > apa yang dimaksud.

4. **SketchTBadan** pada **Front Plane** — kasus T yang paling umum di dunia
   nyata, dan BERBEDA dari SketchT:
   - satu garis **utuh** dari `(0, -40)` ke `(40, -40)` — **jangan dipotong**
   - satu garis batang dari `(20, -40)` ke `(20, -20)`
   - ujung bawah batang diberi relasi **Coincident ke GARIS atasnya**, bukan ke
     titik ujung mana pun. Caranya: pilih titik ujung batang **dan garis**
     (bukan titik), lalu beri Coincident.

   > Bedanya dengan SketchT sangat menentukan, dan mudah tertukar. Di SketchT
   > tiga ujung berbagi satu titik. Di sini garis atas tetap SATU segmen utuh
   > yang tidak punya ujung di `(20, -40)` — yang ada hanya relasi
   > titik-ke-garis.
   >
   > Kalau kamu tidak sengaja memotong garis atas, atau relasinya jatuh ke
   > titik tengah (midpoint) alih-alih ke garisnya, sketsa ini berubah jadi
   > salinan SketchT dan tidak menguji apa pun. Periksa: garis atas harus
   > terpilih sebagai SATU entitas saat diklik.

5. **SketchDuaLoop** pada **Front Plane**: dua **Corner Rectangle** terpisah:
   - `(0, 0)` ke `(20, 10)`
   - `(30, 0)` ke `(50, 10)`

   Keduanya tidak bersentuhan.

6. **SketchLoopMenempel** pada **Front Plane** — dua loop TERTUTUP yang
   bersentuhan di satu titik:
   - **Corner Rectangle** `A` dari `(0, -80)` ke `(40, -60)`
   - **segitiga** `B` dari tiga garis: `(20, -50)` → `(35, -50)` → `(27, -60)`
     → kembali ke `(20, -50)`
   - titik `(27, -60)` diberi relasi **Coincident ke SISI ATAS persegi A**
     (garis dari `(0,-60)` ke `(40,-60)`), bukan ke titik ujung mana pun

   > **Kenapa segitiga, bukan dua persegi.** Dua persegi sejajar sumbu tidak
   > bisa dipakai untuk kasus ini: sudut persegi B yang menyentuh sisi A selalu
   > membawa salah satu sisinya berimpit segaris dengan sisi A, sehingga yang
   > terbentuk bukan sentuhan satu titik melainkan tepi yang berbagi. Segitiga
   > menghindarinya karena tidak ada sisinya yang sejajar sisi atas A.
   >
   > Periksa seperti pada SketchTBadan: relasinya harus ke **garis**, bukan ke
   > titik ujung atau Midpoint. Sisi atas A harus tetap SATU segmen utuh.

7. Simpan sebagai `tests/golden/fixture-kontur.SLDPRT`.

## Nilai yang diharapkan

| sketsa | `openEndpointCount` | `branchPointCount` | `endpointOnSegmentCount` | `connectivityLimits` |
|---|---|---|---|---|
| `SketchKotak` | **0** | 0 | 0 | `[]` |
| `SketchT` | **3** | **1** | 0 | `["undeclared_point_on_segment"]` |
| `SketchTBadan` | **4** | **0** | **1** | `["undeclared_point_on_segment"]` |
| `SketchDuaLoop` | **0** | 0 | 0 | `[]` |
| `SketchLoopMenempel` | **0** | **0** | lihat bawah | `[]` |

`consumedByFeatures` adalah `[]` untuk keempatnya — **kosong, bukan null**.
Part ini tidak punya fitur sama sekali.

- `SketchKotak`: 4 garis tertutup. Centerline **tidak dihitung** karena
  konstruksi — kalau ikut dihitung, angkanya jadi 2 dan test merah. Itu memang
  yang diuji.
- `SketchT`: ujung bebas di `(0,0)`, `(40,0)`, `(20,20)`. Di `(20,0)` tiga ujung
  bertemu → satu titik cabang. Tidak punya ujung bebas di sana, tapi tetap bukan
  profil yang sah untuk extrude.
- `SketchTBadan`: **inilah sketsa yang paling penting di berkas ini.**
  `branchPointCount` **0** padahal bentuknya jelas bercabang — dan angka 0 itu
  BENAR menurut definisinya, karena memang tidak ada titik tempat lebih dari dua
  ujung bertemu. Yang salah adalah membacanya sebagai "tidak ada percabangan".
  `endpointOnSegmentCount: 1` yang menangkapnya.

  Keempat ujungnya — `(0,-40)`, `(40,-40)`, `(20,-40)`, `(20,-20)` — tidak
  satu pun berpasangan dengan ujung lain, jadi `openEndpointCount` adalah **4**.
  Ujung `(20,-40)` dihitung di KEDUA angka: `endpointOnSegmentCount` adalah
  himpunan bagian dari `openEndpointCount`, bukan angka terpisah. **Jangan
  dijumlahkan** — `4 + 1` tidak berarti apa-apa.

- `SketchDuaLoop`: dua loop tertutup terpisah tetap **0** ujung bebas. Ini yang
  membedakan "menghitung ujung" dari "menghitung loop".

- `SketchLoopMenempel`: **kasus yang membongkar definisi lama.** Kedua loop
  tertutup, jadi `openEndpointCount` **0**. Di titik sentuh hanya dua ujung B
  yang bertemu — sisi A tidak punya ujung di sana — jadi `branchPointCount`
  juga **0**. Dengan definisi lama, `endpointOnSegmentCount` ikut **0** karena ia
  himpunan bagian dari ujung terbuka. Semuanya bersih, padahal ada titik sentuh
  yang membuat region profil ambigu.

  Sentuhan itu **dideklarasikan** lewat relasi `(titik, segmen)`, jadi
  mekanismenya sudah bisa melihatnya — definisi subset yang membuangnya.

  Angka yang menangkapnya menunggu keputusan atas
  `docs/proposal-kontur-loop-menempel.md`. Di bawah usulan di sana:
  `openEndpointOnSegmentCount` **0**, `joinedEndpointOnSegmentCount` **1**.
  Dua angka pertama di tabel sudah final apa pun keputusannya.

### `connectivityLimits` — kenapa isinya berbeda per sketsa

`undeclared_point_on_segment` dicantumkan **hanya** kalau
`openEndpointCount > endpointOnSegmentCount`, yaitu saat masih ada ujung terbuka
yang belum dijelaskan oleh relasi mana pun — di situlah titik yang duduk di atas
garis tanpa relasi mungkin bersembunyi.

Pada `SketchKotak` dan `SketchDuaLoop` semua ujung berpasangan, jadi tidak ada
yang bisa bersembunyi dan batasnya **tidak relevan**. Daftar yang selalu sama di
setiap respons akan diabaikan, dan mekanisme kejujuran yang diabaikan sama saja
dengan tidak ada.

Keempat sketsa ini kebetulan sudah memenuhi syarat test yang kamu minta: dua
sketsa tempat batasnya **muncul** (`SketchT`, `SketchTBadan`) dan dua tempat ia
**tidak muncul** (`SketchKotak`, `SketchDuaLoop`). Tidak perlu sketsa tambahan.

`at_intersect_not_analyzed` tidak muncul di sketsa mana pun di sini — tidak ada
relasi `ATINTERSECT` yang dibuat. Kalau nanti ada kasus nyata, fixture-nya
ditambah lebih dulu, baru implementasinya.

`[]` berarti **"tidak ada batas yang KAMI KETAHUI berlaku"**, bukan "analisis
lengkap". Batas yang belum kami sadari tidak akan muncul di sana menurut
definisinya.

`nearlyCoincidentEndpointCount` diharapkan **0** di ketiganya.

### Yang SENGAJA tidak dipatok

Hitungan dimensi di Part 2 **tidak** dipatok. Sketsa di sini boleh under-defined;
yang diuji konektivitasnya, bukan constraint-nya. Memaku angka dimensi akan
membuat test merah karena hal yang bukan kesalahan — alasan yang sama dengan
`featureCount` di `plat-referensi.json`.

---

# Kasus yang TIDAK bisa dibuat di part nyata

**`unknownDrivenStateCount > 0`.** `swDimensionDrivenUnknown = 0` adalah nilai
yang dikembalikan SolidWorks, bukan sesuatu yang bisa dibuat pengguna dari UI.

Jadi kasus itu diuji di `MikosAI.Core` dengan bacaan palsu — `SketchReading`
yang dibuat langsung di test, tanpa SolidWorks. Dicatat di sini supaya tidak
ada yang mengira kasus itu pernah lulus di mesin nyata.

Konsekuensinya jujur: kalau `DrivenState` ternyata **tidak pernah** terisi untuk
dimensi sketsa dan hanya berlaku untuk dimensi drawing, seluruh pemisahan tiga
ember jadi tidak berguna di part. Itu dilaporkan apa adanya — **tidak** jatuh
diam-diam ke `IsReferenceDim()`, sesuai keputusanmu.

# Setelah kedua part jadi

Aktifkan add-in MikosAI, buka `fixture-konsumen.SLDPRT` sebagai dokumen aktif,
dan keluar dari Edit Sketch. Dari akar repo jalankan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\addin.ps1 -Action Probe
powershell -ExecutionPolicy Bypass -File scripts\golden-sketsa.ps1
```

Skrip tersebut hanya menguji **fixture-konsumen**, termasuk batas body
`z = 3..10`. Untuk **fixture-kontur**, pembaca konektivitas garis dan skrip
terpisah tersedia; hasil run nyata dicatat di bawah.

Setelah menyimpan pekerjaan, tutup SolidWorks, jalankan `scripts\setup.ps1`,
lalu buka kembali SolidWorks dengan add-in aktif dan `fixture-kontur.SLDPRT`
sebagai dokumen aktif. Keluar dari Edit Sketch, kemudian jalankan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\golden-kontur.ps1
```

Default-nya tiga run berturut-turut; `-Runs 1` untuk diagnosis singkat.
Angka harapan terkini ada di `fixture-kontur.json`:

| sketsa | open | branch | open-on-segment | joined-on-segment |
|---|---:|---:|---:|---:|
| SketchKotak | 0 | 0 | 0 | 0 |
| SketchT | 3 | 1 | 0 | 0 |
| SketchTBadan | 4 | 0 | 1 | 0 |
| SketchDuaLoop | 0 | 0 | 0 | 0 |
| SketchLoopMenempel | 0 | 0 | 0 | 1 |

Run nyata pada 2026-11-07, SolidWorks revisi `33.1.1`: seluruh hitungan di
tabel cocok dalam tiga run. Skrip awal menghasilkan **156 lulus, 3 gagal**,
karena mensyaratkan `limits = []` pada semua sketsa. Pembacaan MCP langsung
menunjukkan `SketchT.limits = ["non_endpoint_point_on_segment"]`; empat
sketsa lainnya mempunyai daftar kosong.

**SW_PASS untuk hitungan saja: lima sketsa fixture, tiga kali berturut-turut.**
Disetujui pengguna pada 2026-11-07. Tidak berlaku untuk semua jenis sketsa,
deteksi limit, atau klasifikasi origin/proyeksi. DLL melaporkan commit dasar
`52357c5` dengan perubahan lokal; ini bukan bukti untuk commit bersih itu.

Keputusan baseline `observedLimits` **DITOLAK pengguna**. Hasil 159 lulus
sebelumnya tidak boleh dipakai sebagai bukti kebenaran detektor limit.
`hypothesizedLimits` kini kembali `[]` untuk semua sketsa berdasarkan resep
geometri. Asersi limit dilaporkan sebagai **HIPOTESIS**, tidak menaikkan
hitungan PASS, tidak menaikkan exit code, dan selisihnya tetap ditulis
**BELUM DIJELASKAN**. Run ulang skrip yang dikoreksi pada DLL yang sama:
**144 lulus, 0 gagal; 15 hipotesis limit, 3 belum dijelaskan**.

`non_endpoint_point_on_segment` belum disetujui sebagai ID kontrak. Emisinya
dicabut dari sumber build berikutnya; kandidatnya disimpan dalam dump privat
untuk ditelusuri, bukan dinyatakan sudah selesai karena emisinya berhenti.
Belum ada keputusan apakah origin/proyeksi termasuk titik analisis.

Dump ketiga sketsa pembanding memerlukan build diagnostik terbaru. Setelah
menyimpan pekerjaan dan memuat ulang add-in lewat `setup.ps1`, aktifkan
`fixture-kontur.SLDPRT`, keluar dari Edit Sketch, lalu jalankan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dump-relasi-sketsa.ps1
```

Dump ditulis ke `tests/golden/evidence/relasi-kontur/`: semua relasi SketchT,
SketchKotak, SketchDuaLoop, tipe entitas, ID/tipe/koordinat titik, geometri
garis target, entitas definisi, dan pasangan pemicu detektor lama. Ini jalur
diagnostik privat melalui dispatcher; tidak menambah tool atau field kontrak.
Pertanyaan serta batas kesimpulan dicatat di `docs/temuan-relasi-fixture-kontur.md`.

Dump lengkap pada 2026-11-07 menjelaskan pasangan pemicu lama: SketchT
mempunyai Coincident titik External `(0,0)` ke garis `(20,0)..(40,0)`.
Parameter proyeksi **-1**, sehingga titik berada pada perpanjangan garis,
bukan badan segmen. SketchKotak dan SketchDuaLoop mempunyai Coincident
titik–titik di `(0,0)` tetapi tidak mempunyai Coincident titik–garis.
Asal titik eksternal sebagai origin dan pembuatan otomatis relasi belum
terbukti. Usulan semantik dicatat terpisah di
`docs/proposal-titik-referensi-konektivitas.md`, belum diterapkan. Harapan
geometri dan status HIPOTESIS untuk asersi limit tidak diubah.

Sketsa T dan loop menempel sengaja menghasilkan verdict profil tidak valid.
Golden test memeriksa ukuran yang dilaporkan, bukan mensyaratkan verdict
seluruh sketsa PASS. Geometri konstruksi dikecualikan. Kurva selain garis
belum didukung: `segmentsNotAnalyzed` menyebutnya dan hitungan konektivitas
tetap null agar angka parsial tidak terlihat seperti analisis lengkap.
`analysisLimits` menyebut `undeclared_point_on_segment` pada tingkat respons;
ID titik menentukan sambungan, toleransi 1e-6 mm hanya mendeteksi titik
terbuka berbeda yang berimpit atau hampir berimpit.

Run awal pada build `52357c5`, SolidWorks revisi `33.1.1`, membuktikan
`Sketch1.dependents` memuat `Boss-Extrude1`, `Cut-Extrude1`, `Sketch2`, dan
`Plane1`. Tipe cut yang teramati adalah `ICE`; plane adalah `RefPlane` dan
tetap bukan konsumen. Volume terbaca **42000 mm³** dan dimensi driving/driven
sesuai harapan. Run itu menghasilkan **24 lulus, 3 gagal**, jadi bukan bukti
kelulusan fixture secara keseluruhan.

Perbaikan setelah run tersebut: tipe `ICE` ditambahkan dengan bukti fixture,
helper PowerShell mempertahankan array kosong sebagai `[]`, dan pembacaan
bounding box menggunakan titik ekstrem solid body sebagai pengganti
`GetPartBox` yang bersifat perkiraan.

Run ulang pada 2026-11-07, SolidWorks revisi `33.1.1`, setelah perbaikan
pemanggil helper array di skrip: **33 lulus, 0 gagal, 0 hipotesis patah**.
Volume, ukuran, posisi body, dua konsumen Sketch1, dependents, dan dimensi
kedua sketsa cocok. Ini satu run nyata yang lulus, belum tiga run berturut-turut.
DLL melaporkan commit dasar `52357c5`, tetapi dibangun dari working tree dengan
perubahan lokal; commit itu sendiri belum mewakili sumber perbaikannya.

Bagian resep di atas masih memakai sebagian nama field lama. Kontrak terkini
di `schemas/tools.json` memakai `openEndpointOnSegmentCount`,
`joinedEndpointOnSegmentCount`, `limits` per sketsa, dan `analysisLimits`
per respons. Keberadaan field dalam kontrak bukan bukti implementasinya.
