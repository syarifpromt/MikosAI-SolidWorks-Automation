# Proposal kecil — T yang berakhir di tengah bentang

Status: **MENUNGGU PERSETUJUAN.** Belum diterapkan.

Dugaanmu benar, dan ini kasus yang lebih umum daripada yang sudah kami tangani.

---

## Kasusnya

```
  Garis atas  (0,0) ———————————————— (40,0)     satu segmen, TIDAK dipotong
                        |
  Batang              (20,0)
                        |
                     (20,20)
```

Batang berakhir di **tengah badan** garis atas. SolidWorks tidak memotong garis
yang ditabrak, jadi tidak ada titik ujung bersama — yang ada relasi coincident
**titik-ke-garis**.

Penghitungan berbasis identitas `SketchPoint` melihat ujung `(20,0)` sebagai
ujung bebas biasa. Hasilnya:

```
openEndpointCount  : 4      <- A(0,0), B(40,0), C(20,0), D(20,20)
branchPointCount   : 0      <- tidak ada titik tempat >2 ujung bertemu
```

Dan `branchPointCount: 0` terbaca sebagai **"tidak ada percabangan"**, padahal
bentuknya jelas bercabang. Itu persis kelas kegagalan yang kita kejar
sepanjang proyek ini: angka yang benar menurut definisinya sendiri, tapi
menjawab pertanyaan yang berbeda dari yang dibaca orang.

## Cara mendeteksinya

Direfleksikan dari PIA:

```
Int32  ISketchRelation.GetRelationType()     -> swConstraintType_e
Object ISketchRelation.GetEntities()
Object ISketchRelation.GetEntitiesType()     -> swSketchRelationEntityTypes_e
Int32  ISketchRelationManager.GetRelationsCount(Int32 Filter)
```

`swConstraintType_COINCIDENT = 9`. Yang membedakan kedua kasus adalah **tipe
entitas yang terlibat**, bukan tipe relasinya:

| kasus | entitas relasi |
|---|---|
| tiga garis berbagi satu titik | `(titik, titik)` |
| batang berakhir di badan garis | `(titik, segmen)` |

Jadi: cacah relasi `COINCIDENT` yang entitasnya `(titik, segmen)` dan titiknya
adalah **ujung** sebuah segmen.

## Usulanku: field terpisah, bukan digabung

```diff
   "openEndpointCount": "integer|null",
   "branchPointCount": "integer|null",
+  "endpointOnSegmentCount": "integer|null",
   "nearlyCoincidentEndpointCount": "integer|null",
```

**Bukan** dimasukkan ke `branchPointCount`, dengan tiga alasan.

**1. Mekanisme deteksinya berbeda.** `branchPointCount` dihitung dari identitas
`SketchPoint`; `endpointOnSegmentCount` dari kueri relasi. Kalau keduanya
digabung jadi satu angka, angka itu artinya bergantung pada mekanisme mana yang
berhasil — dan kalau salah satu rusak, yang tersisa tetap terlihat seperti
jawaban penuh.

**2. Definisi yang tidak melar.** `branchPointCount` tetap persis "titik tempat
lebih dari dua ujung bertemu". Begitu kita menambahkan kasus kedua ke dalamnya,
namanya berbohong sedikit, dan kasus ketiga nanti akan lebih mudah dimasukkan
lagi.

**3. Kesimpulan adalah pekerjaan aturan verifikasi, bukan pekerjaan ukuran.**
Aturannya nanti berbunyi: profil tidak sah kalau
`branchPointCount > 0 || endpointOnSegmentCount > 0`. Itu tempat yang benar
untuk menggabungkannya — di sana penggabungan terlihat dan bisa diubah.

### Hubungan dengan `openEndpointCount`

`endpointOnSegmentCount` adalah **himpunan bagian** dari `openEndpointCount`,
bukan angka yang berdiri sendiri. Pada contoh di atas: `openEndpointCount: 4`,
dan `C` termasuk di dalamnya, **juga** dihitung sebagai
`endpointOnSegmentCount: 1`.

Ini harus ditulis di kontrak, karena angka yang tumpang tindih adalah undangan
untuk dijumlahkan. `4 + 1` tidak berarti apa-apa.

Alternatif yang kutolak: mengeluarkan `C` dari `openEndpointCount` supaya
keduanya terpisah. Itu akan membuat `openEndpointCount` berarti "ujung bebas
yang menurut kami benar-benar bebas" — sebuah penilaian yang disamarkan sebagai
hitungan. `C` memang tidak tersambung ke ujung mana pun, dan itu faktanya.

## Batas deteksi yang harus diakui

Deteksi berbasis relasi hanya menemukan percabangan yang **dideklarasikan**.
Titik yang secara geometris duduk di atas sebuah garis **tanpa relasi** — hal
yang biasa pada sketsa under-defined — tidak akan ditemukan.

Untuk pertanyaan sebenarnya ("apakah ini profil yang sah untuk extrude"),
SolidWorks peduli pada geometri, bukan pada relasi. Jadi deteksi relasi
**tidak lengkap**, dan itu harus terlihat oleh pemanggil — bukan hanya tertulis
di dokumen yang tidak ada di depannya saat ia memanggil tool.

Usulku, memakai pola yang sama dengan `fieldsNotPopulated`:

```diff
+  "connectivityLimits": "array of string"
```

Isinya nama pemeriksaan yang **tidak** dilakukan, mis.
`"endpoint_on_segment_without_relation"`. Daftarnya mengecil kalau nanti kita
menambahkan deteksi geometris.

Kalau menurutmu ini satu field terlalu banyak, alternatifnya menuliskannya di
`notes` saja. Aku lebih memilih field, dengan alasan yang sama yang dulu
memenangkan `fieldsNotPopulated`: catatan di dokumen tidak berada di depan mata
siapa pun pada saat tool dipanggil.

Satu hal lagi yang kutemukan dan belum kita bahas: `swConstraintType_ATINTERSECT
= 13` — titik yang diletakkan di perpotongan dua entitas. Itu kasus ketiga, dan
aku **belum** mengusulkan penanganannya. Disebutkan di sini supaya tidak hilang.

## Yang kuminta diputuskan

1. `endpointOnSegmentCount` sebagai field terpisah, **himpunan bagian** dari
   `openEndpointCount` — bukan digabung ke `branchPointCount`.
2. `connectivityLimits: string[]`, atau cukup `notes` saja.
3. Apakah `swConstraintType_ATINTERSECT` perlu ditangani sekarang, atau masuk
   `connectivityLimits` sampai ada kasus nyata.
