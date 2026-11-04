# Proposal — deteksi konsumen sketsa

Status: **MENUNGGU PERSETUJUAN.** Belum diterapkan.

---

## 0. Kesalahan saya yang harus diperbaiki lebih dulu

Di koreksi pertamamu, `dependents` ditentukan sebagai
**`array of {name, typeName}`**. Aku mengimplementasikannya di `74dad6a`
sebagai `string[]` — **`typeName` dibuang.**

Itu bukan kelalaian kecil. `typeName` justru satu-satunya hal yang membuat
klasifikasi bisa dilakukan dengan bukti, dan tanpanya hasil fixture nanti hanya
akan berbunyi *"ada child bernama Plane1"* — tanpa cara mengetahui apa itu.

Diperbaiki sebagai bagian dari proposal ini.

---

## 1. Asumsiku memang terlalu lebar — dan arah perbaikanmu benar

"Child yang bukan sketsa adalah konsumen" salah persis seperti yang kamu tulis.
`fixture-konsumen` hanya bisa membuktikan bahwa **sketsa dependent tidak ikut**;
ia tidak membuktikan bahwa setiap child non-sketsa **adalah** konsumen.

Kenapa daftar putih, bukan daftar hitam, dan ini alasan yang lebih kuat daripada
sekadar kehati-hatian:

Dengan **daftar hitam** (semua konsumen kecuali yang dikecualikan), tipe yang
belum kita kenal otomatis jadi konsumen. Itu **mengklaim pengetahuan yang tidak
kita punya**, dan klaimnya tidak terlihat.

Dengan **daftar putih**, tipe yang belum dikenal tidak masuk `consumedByFeatures`
— tapi ia tetap masuk `dependents` **dan** memicu limit. Jadi
ketidaktahuannya terlihat, dan tidak ada yang hilang: `delete_feature` memakai
`dependents`, yang memuat semuanya.

Arah salahnya juga penting. Daftar putih yang kurang lengkap membuat
`consumedByFeatures` **kurang** melaporkan — dan karena `delete_feature` tidak
memakainya, kekurangan itu tidak berbahaya. Daftar hitam yang kurang lengkap
membuat ia **lebih** melaporkan, yaitu mengarang.

## 2. Daftar putihnya, dengan alasan per tipe

Masalahnya: **aku tidak tahu nama tipe sebenarnya** untuk sebagian besar fitur.
Yang sudah **teramati** dari model nyata hanya satu:

| `typeName` | status | alasan |
|---|---|---|
| `Extrusion` | **teramati** pada `Boss-Extrude1` di part acuan | memakai sketsa sebagai profil |

Sisanya — cut, revolve, hole wizard, sweep, loft — **belum pernah kulihat nama
tipenya**. Menuliskannya sekarang dari ingatan adalah persis hal yang dilarang
briefing, dan dua kali minggu ini aku sudah tertipu oleh tebakan yang terasa
pasti.

### Usulan: fixture pertama menjadi sensus nama tipe

Daripada menebak isi daftar, **biarkan daftarnya kosong kecuali yang teramati**,
dan buat setiap tipe yang belum dikenal **melaporkan dirinya**:

```diff
-  "dependents": "array of string|null"
+  "dependents": "array of {name, typeName}|null"
```

Lalu limit kondisional `unclassified_child` muncul di sketsa yang punya child
bertipe di luar daftar putih. Karena `dependents` membawa `typeName`,
laporan fixture akan langsung memberi tahu kita nama tipe sebenarnya untuk cut,
plane, pattern, dan seterusnya.

Jalannya sekali, dan daftar putihnya terisi **dengan bukti** alih-alih dengan
ingatan. Setiap entri yang ditambahkan nanti punya satu baris alasan dan satu
rujukan ke run yang mengamatinya.

Itu juga menjawab keberatan implisit soal daftar putih: ia tidak akan
ditinggalkan setengah jadi, karena selama ia belum lengkap, setiap sketsa yang
terkena akan terus mengibarkan `unclassified_child`.

## 3. Di mana limit itu tinggal

`connectivityLimits` tidak cocok — `unclassified_child` bukan soal konektivitas.
Menaruhnya di sana akan membuat nama field itu berbohong sedikit, dan kasus
berikutnya akan lebih mudah dimasukkan lagi. Itu persis cara
`endpointOnSegmentCount` melar.

Usulku, selagi field-nya **belum diimplementasikan sama sekali** sehingga
perubahannya masih gratis:

```diff
-  "connectivityLimits": "array of string|null"
+  "limits": "array of string|null"
```

`limits` per sketsa memuat seluruh batas yang bergantung pada sketsa itu, apa
pun topiknya: `at_intersect_not_analyzed`, `non_endpoint_point_on_segment`,
`unclassified_child`. Per respons tetap `analysisLimits` untuk batas yang
merupakan sifat metode kami.

Pembagiannya jadi punya satu sumbu yang jelas — **bergantung sketsa atau tidak**
— bukan dua sumbu yang saling memotong (topik dan lingkup).

## 4. Invariant yang kamu minta

```
openEndpointOnSegmentCount + joinedEndpointOnSegmentCount
  = jumlah ujung yang punya relasi Coincident (titik, segmen)
```

Diterima, dan akan diperiksa **di produk**, bukan hanya di test — sama seperti
`dimension_counts_consistent`. Pelanggarannya berarti ada ujung yang lenyap
antara dideteksi dan diklasifikasi, dan kehilangan seperti itu sunyi.

---

## Yang kuminta diputuskan

1. `dependents` menjadi `array of {name, typeName}` — **perbaikan kesalahanku**,
   dan prasyarat untuk sisanya.
2. Daftar putih dimulai hanya dengan `Extrusion` yang teramati, dan tipe lain
   ditambahkan **setelah** sensus fixture — bukan ditebak sekarang.
3. `connectivityLimits` berganti nama jadi `limits` per sketsa, dengan sumbu
   pembagian "bergantung sketsa atau tidak".
4. `unclassified_child` sebagai ID limit kondisional.

Tambahan untuk resep `fixture-konsumen` — reference plane lewat satu garis
`Sketch1` — sudah kutulis, lihat
`tests/golden/CARA-MEMBUAT-FIXTURE-SKETSA.md`.
