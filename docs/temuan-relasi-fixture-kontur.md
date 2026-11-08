# Temuan relasi fixture-kontur

Status 2026-11-07: **hitungan SW_PASS; pasangan pemicu dan salah klasifikasi
limit terjelaskan oleh dump. Asal referensi eksternal belum teridentifikasi.**

## Bukti yang diterima

Pengguna menerima SW_PASS hanya untuk hitungan lima sketsa fixture, dengan
hasil sesuai geometri tiga kali berturut-turut pada SW revisi `33.1.1`.
Build melaporkan commit dasar `52357c5`, dengan perubahan working tree.
Ini bukan bukti semua jenis sketsa atau build dari commit bersih tersebut.

| sketsa | open | branch | open-on-segment | joined-on-segment |
|---|---:|---:|---:|---:|
| SketchKotak | 0 | 0 | 0 | 0 |
| SketchT | 3 | 1 | 0 | 0 |
| SketchTBadan | 4 | 0 | 1 | 0 |
| SketchDuaLoop | 0 | 0 | 0 | 0 |
| SketchLoopMenempel | 0 | 0 | 0 | 1 |

Baseline limit yang disalin dari keluaran ditolak. Harapan limit kembali `[]`
berdasarkan geometri dan asersinya menjadi HIPOTESIS, tanpa PASS atau pengaruh
pada exit code. Run ulang: 144 lulus, 0 gagal, 15 hipotesis (3 selisih SketchT).
Angka 159 lulus sebelumnya tidak membuktikan detektor limit.

## Masalah pada aturan lama

Aturan lama menemukan relasi Coincident bertipe `(point, segment)`, lalu
menganggap titik berada pada badan segmen bila ID-nya berbeda dari ID ujung
target. Bila titik juga bukan ujung garis lain atau konstruksi, ia mengeluarkan
`non_endpoint_point_on_segment`. ID ini belum disetujui; emisi publiknya dicabut
dari sumber build berikutnya. Temuan tetap dicatat sebagai kandidat diagnostik.

**ID berbeda tidak membuktikan posisi pada badan segmen.** Titik referensi
dapat mempunyai ID lain tetapi koordinat sama dengan ujung target. Ini celah
yang terlihat dari kode, bukan bukti penyebab pada fixture nyata.

Ada juga perbedaan antara entitas hasil `GetEntities()` dan entitas definisi:
SolidWorks dapat membuat entitas internal untuk suatu relasi. Karena itu dump
memuat keduanya, sesuai [dokumentasi GetEntities](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISketchRelation~GetEntities.html).

## Hasil dump nyata

Capture `9e9bfe64da6e4d3b85efd572941e4f37`, SW `33.1.1`, DLL build time
`2026-11-07T05:40:33Z`, SHA-256
`B0193807BB9E0C634E889D1CE9C85646A15E90C401BC67064E603B8B09070797`.
Ketiga dump mempunyai jumlah relasi lengkap dan `errors = []`.
Raw capture tetap lokal di `tests/golden/evidence/relasi-kontur/`, sesuai
aturan gitignore untuk evidence; ringkasan ini tidak memuat path mesin/token.

| sketsa | Coincident point–point | Coincident segment–point | horizontal | vertikal | total | kandidat lama |
|---|---:|---:|---:|---:|---:|---:|
| SketchT | 1 | 1 | 2 | 1 | 5 | 1 |
| SketchKotak | 1 | 0 | 3 | 2 | 6 | 0 |
| SketchDuaLoop | 1 | 0 | 4 | 4 | 9 | 0 |

Kode dari refleksi PIA: Coincident=9, Horizontal=4, Vertical=5;
tipe entitas point=2, line=3. Indeks relasi berikut dimulai dari nol.

### SketchT: pemicu persis

`relations[1]` adalah Coincident (9), `entityTypes = [3,2]`:

- Garis target `0:1:2`: `(20,0,0)` sampai `(40,0,0)` mm,
  bukan garis pertama `(0,0)..(20,0)`.
- Titik hasil `GetEntities`: ID `2:3`, `Type=10 (External)`, posisi `(0,0,0)`.
  `ownerIsThisSketch=true`, tetapi ID tidak termasuk ujung segmen yang
  dianalisis maupun ujung konstruksi.
- `GetDefinitionEntities2` menunjuk garis yang sama dan sebuah titik
  `Type=1 (User)`, ID `0:1`, posisi `(0,0,0)`, `ownerIsThisSketch=false`.
  Ini bukti referensi ke titik di luar sketsa, belum identifikasi nama/asalnya.
- Jarak titik ke ujung target: **20 mm** dan **40 mm**. Parameter proyeksi
  pada garis **t=-1**, jarak ke garis tak berhingga **0 mm**.

Jadi relasi Coincident ini berlaku pada **perpanjangan garis**, di luar
segmen hingga. Salah klasifikasi terjadi karena detektor lama hanya melihat
jenis relasi dan ketidaksamaan ID, tanpa memeriksa apakah titik berada pada
bentang segmen. Tidak ada titik pada badan target dalam pasangan ini.

`relations[0]` adalah Coincident point–point (`[2,2]`): wakil External `2:3`
di `(0,0)` dan ujung lokal Internal `0:1` di `(0,0)`. Relasi ini tidak
memasuki cabang point–segment. Tiga relasi sisanya ialah dua Horizontal dan
satu Vertical pada tiga garis T. Titik cabang lokal `1:2` tetap dipakai oleh
tiga ujung garis; ini menjelaskan hitungan branch=1 yang diterima.

### Kenapa SketchKotak dan SketchDuaLoop tidak memicu limit

Keduanya juga mempunyai relasi indeks 0 Coincident point–point (`[2,2]`):
wakil External `4:5` di `(0,0)` dipasangkan dengan ujung lokal Internal `0:1`
di `(0,0)`. Entitas definisinya kembali memuat titik User di luar sketsa.
**Tidak ada relasi Coincident point–segment** pada keduanya. Seluruh relasi
lainnya Horizontal/Vertical, sehingga filter `kind=9` + point/segment tidak
pernah menghasilkan pasangan kandidat. Penyebab perbedaan bukan sekadar
ketiga sketsa menyentuh koordinat origin, melainkan isi relasinya berbeda.

### Batas kesimpulan tentang origin

Dugaan bahwa referensi di `(0,0)` terlibat didukung dump. Namun dugaan target
garis `(0,0)..(20,0)` tidak cocok: target aktual `(20,0)..(40,0)`.
Titik pemicu bertipe **External (10), bukan Origin (8)**. Titik definisi
bertipe User dan dimiliki sketsa lain. Koordinat `(0,0)` saja tidak
membuktikan bahwa sumbernya origin model/sketsa; dump juga tidak membuktikan
bahwa relasi dibuat otomatis. Daftar Display/Delete Relations pengguna tetap
diperlukan jika hendak memastikan asal tersebut.

Catatan integritas dump versi 1: `isAnalyzedEndpointById` pada entitas definisi
asing hanya membandingkan pasangan ID dengan himpunan lokal, sehingga dapat
bernilai true karena ID berulang antar-sketsa. Selalu baca bersama
`ownerIsThisSketch`. Ini tidak memengaruhi kandidat yang ditemukan (pemilik
titik External tersebut adalah sketsa lokal), tetapi flag itu tidak boleh
dipakai sendirian sebagai bukti keanggotaan endpoint.

Usulan semantik titik referensi dan validasi bentang terdapat di
`proposal-titik-referensi-konektivitas.md`; belum diterapkan.

## Rencana diagnosis asal (belum selesai)

Dugaan awal pengguna dicatat di atas beserta bagian yang didukung dan yang
tidak cocok dengan capture. Tidak ada keputusan untuk mengeluarkan seluruh
origin, proyeksi, atau titik referensi dari analisis.

Dump tiga sketsa harus menjawab:

1. Untuk semua relasi: kode tipe, kode tipe entitas, entitas hasil API serta
   entitas definisi, termasuk nilai null dan kegagalan baca.
2. Untuk setiap pasangan pemicu lama: ID dan `ISketchPoint.Type`, koordinat,
   kepemilikan sketsa, apakah ID masuk himpunan ujung/konstruksi; ID dan kedua
   ujung target. Type Origin=8 dan External=10 direfleksikan dari PIA asli.
3. Jarak titik ke kedua ujung target, parameter proyeksi sepanjang garis,
   jarak ke garis. Parameter 0/1 dan jarak nol ke ujung berbeda dari posisi
   pada interior. Koordinat saja tidak menetapkan asal origin/proyeksi.
4. Bandingkan SketchKotak dan SketchDuaLoop yang juga menyentuh `(0,0)`.
   Apakah jenis relasinya berbeda, memakai ID ujung yang sama, atau melewati
   kondisi filter berbeda? Hasil perbandingan sudah dijawab di atas.

`scripts/dump-relasi-sketsa.ps1` menyalakan capture sementara melalui marker
lokal kedaluwarsa, memanggil `inspect_sketches` yang sudah ada, dan menyimpan
JSON serta metadata DLL. Semua sentuhan API tetap melalui dispatcher utama.
Tidak ada perubahan sketsa dan tidak ada tool/field kontrak tambahan.

Dump parsial atau dari request lama ditolak. Pasangan pemicu dan pembanding
ketiga sketsa sudah dilaporkan. Tidak ada ID limit baru yang disetujui atau
ditambahkan; tidak beralih ke field lain dalam diagnosis ini.
