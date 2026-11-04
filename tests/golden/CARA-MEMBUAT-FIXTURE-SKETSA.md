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

6. Simpan sebagai `tests/golden/fixture-konsumen.SLDPRT`.

## Nilai yang diharapkan

**Body:** `bodyCount` 1, `size` `100 × 60 × 7`, `volumeMm3` **42000**,
bounding box `z` dari **3** sampai **10**.

**Sketch1**

| field | nilai |
|---|---|
| `constrainedStatus` | `FullyDefined` |
| `consumedByFeatures` | `["Boss-Extrude1", "Cut-Extrude1"]` — dua, urutan bebas |
| `dependents` | memuat keduanya, **dan kemungkinan `Sketch2`** — lihat catatan hipotesis |
| `drivingDimensionCount` | 2 |
| `drivenDimensionCount` | 0 |
| `unknownDrivenStateCount` | 0 |
| `openEndpointCount` | 0 |
| `branchPointCount` | 0 |

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

4. **SketchDuaLoop** pada **Front Plane**: dua **Corner Rectangle** terpisah:
   - `(0, 0)` ke `(20, 10)`
   - `(30, 0)` ke `(50, 10)`

   Keduanya tidak bersentuhan.

5. Simpan sebagai `tests/golden/fixture-kontur.SLDPRT`.

## Nilai yang diharapkan

| sketsa | `openEndpointCount` | `branchPointCount` | `consumedByFeatures` |
|---|---|---|---|
| `SketchKotak` | **0** | 0 | `[]` |
| `SketchT` | **3** | **1** | `[]` |
| `SketchDuaLoop` | **0** | 0 | `[]` |

- `SketchKotak`: 4 garis tertutup. Centerline **tidak dihitung** karena
  konstruksi — kalau ikut dihitung, angkanya jadi 2 dan test merah. Itu memang
  yang diuji.
- `SketchT`: ujung bebas di `(0,0)`, `(40,0)`, `(20,20)`. Di `(20,0)` tiga ujung
  bertemu → satu titik cabang. Tidak punya ujung bebas di sana, tapi tetap bukan
  profil yang sah untuk extrude.
- `SketchDuaLoop`: dua loop tertutup terpisah tetap **0** ujung bebas. Ini yang
  membedakan "menghitung ujung" dari "menghitung loop".

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

```powershell
powershell -ExecutionPolicy Bypass -File scripts\golden-sketsa.ps1
```

Skrip itu belum ada — ia ditulis bersama implementasinya. Yang kubutuhkan
sekarang hanya kedua partnya, supaya angka harapan di atas bisa dibandingkan
dengan pembacaan nyata alih-alih dengan dugaan.
