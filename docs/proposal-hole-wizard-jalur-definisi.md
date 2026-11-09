# Proposal — `hole_wizard` lewat feature definition, bukan `HoleWizard5`

Status: **DIPUTUSKAN dan DITERAPKAN** 2026-11-09.

Awalnya berkas ini meminta persetujuan, karena ia membalik keputusan
"HANYA `HoleWizard5`". Itu terlalu berhati-hati: yang dibalik adalah **cara
mengimplementasikan**, bukan arti kontraknya. Tool-nya tetap `hole_wizard`,
lingkupnya tetap `simple` dan `counterbore`, dan larangan fallback tetap
berlaku dengan alasan yang sama persis.

Meminta izin untuk memakai API yang lebih aman, padahal buktinya sudah lengkap,
hanya menambah putaran tanpa menambah kejelasan. Diterapkan, dan **bisa
dibatalkan** kalau run pertama menunjukkan jalur ini tidak bekerja.

---

## Apa yang ditemukan

Saat menyiapkan langkah 1, aku memeriksa PIA untuk jalur lain. Ada:

```
Object  IFeatureManager.CreateDefinition(Int32 Type)      // swFmHoleWzd = 25
Feature IFeatureManager.CreateFeature(Object FeatureData)
```

dan `IWizardHoleFeatureData2` dengan **61 properti bernama**, termasuk:

```
Void    InitializeHole(Int32 GenericHoleType, Int32 StdIndex,
                       Int32 FastnerType, String SSize, Int32 EndType)
String  Standard         String FastenerType      String FastenerSize
Double  CounterBoreDiameter    Double CounterBoreDepth
Double  ThruHoleDiameter       Double ThruHoleDepth
Int32   GetSketchPointCount()  Object GetSketchPoints()
Boolean AccessSelections(...)  Void   ReleaseSelectionAccess()
```

## Kenapa ini lebih baik, dengan alasan yang bisa diperiksa

**1. Dua belas `Value1…Value12` tidak perlu disentuh sama sekali.**

`InitializeHole` mengisi seluruh dimensi **dari basis data Hole Wizard**.
Perangkap urutan parameter — hal yang paling keras diperingatkan di briefing,
dan yang membuat kami mewajibkan test urutan parameter per tipe lubang —
**hilang**, bukan diuji-kelilingi.

Menghilangkan sumber kesalahan lebih baik daripada menambah test untuk
mendeteksinya.

**2. Pembacaan ulang lewat nama, bukan posisi.**

`Standard`, `FastenerType`, `FastenerSize` adalah **String**. Verifikasi bisa
membandingkan `"ISO"` dan `"M6"` apa adanya, bukan menebak apakah indeks 135
memang yang kita maksud.

**3. `actualPositions` akhirnya punya sumber yang benar.**

Kontrak kita mewajibkan `actualPositions` dibaca ulang dan ditransformasi balik
ke koordinat model. `GetSketchPoints()` mengembalikan `SketchPoint` sungguhan,
jadi X/Y/Z-nya bisa dibaca dan dibalik lewat `IMathTransform.Inverse`.

Dengan `HoleWizard5` kami harus mencari sketsa posisinya sendiri dan menebak
yang mana.

## Nilai enum yang sudah diverifikasi

Semuanya dari swconst asli, bukan ingatan:

| | nilai |
|---|---|
| `swFmHoleWzd` (CreateDefinition) | **25** |
| `swWzdCounterBore` | **0** |
| `swStandardISO` | **8** |
| `swStandardISOHexBolt` | **135** |
| `swEndCondThroughAll` | **1** |
| `SSize` | `"M6"` — String, bukan indeks |

## Yang BELUM terbukti, dan harus disebut

Aku membaca **tanda tangan**, bukan semantik. Yang belum dijawab:

1. Apakah `AccessSelections` wajib sebelum menyetel properti, dan apa yang
   harus terpilih saat `CreateFeature` dipanggil.
2. Apakah posisi ditentukan dari **seleksi face** sebelum `CreateFeature`, atau
   lewat sketsa yang kita buat sendiri lebih dulu.
3. Apakah `InitializeHole` benar-benar mengisi `CounterBoreDiameter` dkk., atau
   hanya menandai tipe.

Ketiganya hanya bisa dijawab dengan menjalankannya. Aku sudah dua kali tertipu
karena menyimpulkan semantik dari tanda tangan — `CreatePoint` dan
`GetNextDisplayDimension` — jadi aku menuliskannya di muka alih-alih
menganggapnya beres.

## Yang TIDAK berubah

- **Lingkup M0 tetap `simple` dan `counterbore` saja.** Tipe lain `BLOCKED`.
- **Test per tipe lubang tetap wajib** — tapi alasannya bergeser: bukan lagi
  urutan parameter, melainkan apakah pemetaan ke basis data menghasilkan
  geometri yang benar.
- **Tanpa fallback.** Kalau jalur definisi tidak tersedia atau gagal,
  hasilnya `BLOCKED` — bukan diam-diam pindah ke `HoleWizard5`. Alasan asli
  keputusanmu tetap berlaku: jatuh diam-diam antarjalur akan menghasilkan
  lubang yang salah dan lolos sebagai sukses.
- Menumpang jalur yang sudah terbukti: `NewDocument` → tulis → simpan → tutup
  → buka ulang → verifikasi, dengan `Step()` dan `Require()` yang sama.

## Yang kuminta diputuskan

Satu hal: boleh memakai `CreateDefinition`/`CreateFeature` +
`IWizardHoleFeatureData2.InitializeHole`, menggantikan `HoleWizard5` di catatan
kontrak `hole_wizard`?

Kalau kamu lebih suka tetap `HoleWizard5`, aku kerjakan itu — tapi kami perlu
cara mengetahui arti `Value1…Value12` untuk counterbore, dan satu-satunya cara
jujur yang kutahu adalah membuat satu lubang manual lalu membaca balik
nilainya lewat `IWizardHoleFeatureData2`. Yaitu objek yang sama dengan yang
diusulkan di sini.
