# Cara membuat `fixture-fitur.SLDPRT`

Tujuannya satu: **menumbuhkan daftar putih tipe konsumen dengan bukti.**

Hari ini daftarnya hanya memuat `Extrusion` dan `ICE`. Setiap part yang memakai
revolve atau hole wizard akan mengibarkan `unclassified_child`, dan itu memang
disengaja — tapi ia tidak boleh dibiarkan berkibar selamanya.

Syarat masuk daftar tetap **dua bukti**:

1. `typeName` teramati dari model nyata, dan
2. fitur itu benar-benar memakai sketsa sebagai profil, dengan **efek yang
   terverifikasi**.

Part ini menyediakan keduanya sekaligus.

Satuan **MMGS**. Dibagi dua tahap supaya kegagalan di tahap kedua tidak membuang
tahap pertama.

---

# Tahap 1 — balok dan revolve

## Langkah

1. **File → New → Part**, satuan MMGS.

2. **SketchBase** pada **Front Plane**: **Center Rectangle** berpusat di Origin,
   dimensi **80 mm** (horizontal) dan **60 mm** (vertikal). Harus **Fully
   Defined**.

3. **Extruded Boss/Base** dari SketchBase, `Blind`, **10 mm**, arah default.
   Namanya `Boss-Extrude1`. Balok menempati `z = 0..10`.

4. **SketchRevolve** pada **Right Plane** — sketsa ini berada jauh di atas balok
   supaya kedua body **tidak bersentuhan**:
   - satu **Centerline** mendatar pada **z = 60**, membentang di sekitar
     `y = -15` sampai `y = 15`
   - **Corner Rectangle** dari `(y = -10, z = 60)` ke `(y = 10, z = 70)` —
     sisi bawahnya **berimpit dengan centerline**
   - beri dimensi sehingga **Fully Defined**

5. **Features → Revolved Boss/Base**, sumbu = centerline tadi, **360°**.
   Namanya `Revolve1`.

   Hasilnya silinder berjari-jari **10 mm** dan panjang **20 mm**, bersumbu
   searah Y, terpusat di `z = 60`. Ia **tidak menyentuh** balok.

   > Kalau SolidWorks menawarkan `Merge result`, biarkan apa adanya — kedua body
   > memang terpisah, jadi hasilnya tetap dua body.

6. Simpan sebagai `tests/golden/fixture-fitur.SLDPRT`.

## Nilai yang diharapkan setelah Tahap 1

Semuanya diturunkan dengan pensil:

| besaran | hitungan | nilai |
|---|---|---|
| volume balok | 80 × 60 × 10 | 48000 mm³ |
| volume silinder | π × 10² × 20 | 6283.185307 mm³ |
| **volume total** | 48000 + 6283.185307 | **54283.185307 mm³** |
| `bodyCount` | balok + silinder terpisah | **2** |

`SketchBase.consumedByFeatures` memuat `Boss-Extrude1`;
`SketchRevolve.consumedByFeatures` memuat `Revolve1`.

**`typeName` untuk `Revolve1` belum pernah kami lihat.** Itu yang dicari tahap
ini. Jangan ditebak di berkas harapan — sensus yang akan menyebutkannya.

---

# Tahap 2 — hole wizard

Lanjutkan pada part yang sama.

## Langkah

7. Pilih **permukaan atas balok** (bidang `z = 10`), lalu
   **Features → Hole Wizard**.
   - tipe **Counterbore**
   - standar **ISO**
   - ukuran **M6**
   - end condition **Through All**
   - letakkan **dua** lubang pada sumbu X, di `(x = -20, y = 0)` dan
     `(x = 20, y = 0)`

   Namanya akan seperti `CBORE for M6 Hex Head Bolt1` — **catat apa adanya**,
   jangan diganti.

8. Simpan ulang.

## Nilai yang diharapkan setelah Tahap 2

| besaran | harapan | kenapa |
|---|---|---|
| `bodyCount` | tetap **2** | lubang tidak memecah body |
| volume total | **berkurang** dari 54283.185307 | hanya arahnya yang dipatok |
| jumlah lubang | 2 | |

### Kenapa volume setelah hole wizard TIDAK dipatok angkanya

Diameter counterbore, kedalamannya, dan diameter lubang tembus ditentukan
**basis data Hole Wizard** untuk ISO M6 — bukan oleh kami. Menuliskan angka
volume di sini berarti menyalinnya dari keluaran tool, dan itu snapshot: kalau
pembacaannya salah, berkas harapan akan membekukan kesalahan itu lalu
menyebutnya lulus.

Yang **bisa** kami pertanggungjawabkan adalah arahnya: lubang tembus
menghilangkan material, jadi volume **wajib berkurang**. Itu yang dipatok.

Ini bukan kelonggaran, melainkan batas yang jujur: pada revolve kami memaku
angka eksak karena bisa dihitung; di sini kami tidak bisa, dan mengaku tidak
bisa.

---

# Yang dibuktikan part ini

| fitur | bukti nama | bukti peran |
|---|---|---|
| `Boss-Extrude1` | sensus | volume naik 48000 |
| `Revolve1` | **sensus — nama belum pernah dilihat** | volume naik 6283.185307 |
| hole wizard | **sensus — nama belum pernah dilihat** | volume berkurang |

Setelah run, daftar putih di `TipeKonsumenSketsa` diisi **dari hasil sensus**,
satu entri per tipe, masing-masing menyebut fixture dan build tempat ia diamati.

Sketsa posisi yang dibuat Hole Wizard kemungkinan muncul sebagai sketsa
tersendiri bertipe `3DProfileFeature`. Itu juga data sensus — **jangan dihapus**
dari part.

# Menjalankan

```powershell
powershell -ExecutionPolicy Bypass -File scripts\golden-fitur.ps1
```

Dengan `fixture-fitur.SLDPRT` aktif. Skrip itu memisahkan **GAGAL** dari
**SENSUS**: nama tipe dilaporkan untuk dibaca, tidak di-assert, karena kami
memang belum tahu apa isinya.
