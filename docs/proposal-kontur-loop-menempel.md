# Proposal — dua loop tertutup yang bersentuhan

Status: **MENUNGGU PERSETUJUAN.** Belum diterapkan.

---

## Cacatnya bukan kasus yang terlewat, melainkan kontradiksi internal

Definisi yang berlaku sekarang berbunyi:

> `endpointOnSegmentCount`: ujung segmen yang punya relasi coincident ke BADAN
> segmen lain.

Sudut persegi B **adalah** ujung segmen — ujung dari dua garis B sekaligus — dan
ia **memang** punya relasi coincident ke badan sisi A. Menurut prosa itu, ia
harus terhitung.

Yang membuangnya hanya klausa tambahan "himpunan bagian dari
`openEndpointCount`". Jadi kontraknya mengatakan dua hal yang saling
bertentangan, dan klausa yang menang adalah klausa yang **aku** perjuangkan.

Aku menambahkannya untuk mencegah angka tumpang tindih. Itu tujuan yang benar,
tapi caranya salah: aku menjadikan sifat *turunan* (terbuka atau tidak) sebagai
bagian dari *definisi* yang diukur. Yang diukur seharusnya kontak
titik-ke-badan; terbuka atau tidak adalah sifat lain dari titik yang sama.

## Usulan: partisi, bukan subset

Ganti satu angka dengan **dua angka yang saling lepas**:

```diff
-  "endpointOnSegmentCount": "integer|null",
+  "openEndpointOnSegmentCount": "integer|null",
+  "joinedEndpointOnSegmentCount": "integer|null",
```

| field | arti |
|---|---|
| `openEndpointOnSegmentCount` | ujung **terbuka** yang menyentuh badan segmen lain — batang T |
| `joinedEndpointOnSegmentCount` | ujung yang **tersambung** ke ujung lain, tapi juga menyentuh badan segmen lain — sudut loop yang menempel |

Keduanya **tidak beririsan**, karena sebuah ujung tidak bisa sekaligus terbuka
dan tersambung. Jumlahnya adalah seluruh kontak titik-ke-badan yang
dideklarasikan di ujung segmen — dan kali ini penjumlahannya **benar**, bukan
dilarang.

Itu lebih baik daripada aturan "jangan dijumlahkan" yang dulu kupasang. Aturan
yang melarang aritmetika hanya bertahan selama orang membacanya; partisi membuat
aritmetikanya benar dengan sendirinya.

`openEndpointOnSegmentCount` tetap himpunan bagian dari `openEndpointCount`, dan
itu hubungan yang bersih karena **total** — bukan irisan sebagian.

### Aturan verifikasi

```
profil tidak valid kalau
    branchPointCount > 0
 || openEndpointOnSegmentCount > 0
 || joinedEndpointOnSegmentCount > 0
```

Penggabungan tetap hanya di aturan, bukan di ukuran.

### Titik yang bukan ujung

Relasi `(titik, segmen)` juga bisa melibatkan titik yang **bukan** ujung segmen
— mis. `SketchPoint` berdiri sendiri yang diletakkan di atas sebuah garis. Itu
tidak mengubah topologi region, jadi **tidak** dihitung di kedua angka.

Tapi ia terdeteksi dengan murah, jadi dilaporkan sebagai batas:
`non_endpoint_point_on_segment`. Lebih baik disebut daripada dibuang diam-diam.

---

## Tinjauan ulang pemicu `undeclared_point_on_segment`

Kamu benar, dan lebih jauh dari yang kamu tulis: **pemicunya tidak bisa
diperbaiki dengan andal, pada prinsipnya.**

Pemicu sekarang `openEndpointCount > endpointOnSegmentCount` mengandaikan kontak
tak-berelasi hanya bisa bersembunyi di ujung terbuka. Kasusmu membuktikan
sebaliknya. Tapi memperbaikinya butuh mengetahui di mana kontak tak-berelasi
*mungkin* ada — dan satu-satunya cara mengetahuinya adalah pemeriksaan geometris,
yaitu persis hal yang kita katakan tidak kita lakukan.

Pemicu apa pun yang dihitung dari relasi akan buta terhadap hal yang justru
tidak punya relasi. Itu bukan bug yang bisa ditambal; itu bentuk pemicunya yang
salah.

### Usulan: pindahkan dari per-sketsa ke per-respons

```diff
   "outputData": {
     "sketches": "array of {... connectivityLimits|null}",
     "sketchCount": "integer",
     "fullyDefinedCount": "integer",
+    "analysisLimits": "array of string",
     "fieldsNotPopulated": "array of string"
   }
```

- **`analysisLimits`** (per respons) memuat batas yang merupakan sifat **metode
  kami**, bukan sifat sebuah sketsa. `undeclared_point_on_segment` pindah ke
  sini, dan ia **selalu ada** selama kita belum melakukan pemeriksaan geometris.
  Itu memang konstan — dan konstanta yang disebut sekali per respons adalah
  informasi, sementara konstanta yang diulang di setiap sketsa adalah kebisingan.
- **`connectivityLimits`** (per sketsa) menyimpan yang memang bergantung pada
  sketsanya: `at_intersect_not_analyzed` dan `non_endpoint_point_on_segment`.
  Keduanya muncul hanya kalau relasi yang bersangkutan benar-benar ada di sketsa
  itu, jadi aturan kondisionalmu tetap berlaku di tempat ia memang masuk akal.

Ini menjawab keberatanmu yang semula tanpa membuat pemicunya berbohong. Yang
berpindah hanya **tempatnya**, sesuai dengan apakah batas itu sifat sketsa atau
sifat alat.

---

## Yang kuminta diputuskan

1. `endpointOnSegmentCount` dipecah jadi `openEndpointOnSegmentCount` dan
   `joinedEndpointOnSegmentCount`, saling lepas, boleh dijumlahkan.
2. `undeclared_point_on_segment` pindah ke `analysisLimits` per respons dan
   menjadi konstan, karena pemicu berbasis relasi tidak bisa melihat hal yang
   tidak punya relasi.
3. `non_endpoint_point_on_segment` ditambahkan sebagai ID batas per sketsa.

Sketsa kelima sudah kutambahkan ke resep fixture dengan angka harapannya —
lihat `tests/golden/CARA-MEMBUAT-FIXTURE-SKETSA.md`. Angka yang tidak bergantung
pada keputusan ini (`openEndpointCount`, `branchPointCount`) sudah final; nama
field yang baru ditandai menunggu persetujuan.
