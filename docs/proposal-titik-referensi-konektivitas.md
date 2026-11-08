# Proposal — titik referensi dan bentang segmen konektivitas

Status: **MENUNGGU PERSETUJUAN. Belum diterapkan.** Tidak mengubah skema,
ledger, atau harapan geometri. Bukti: `temuan-relasi-fixture-kontur.md` dan
dump SketchT/SketchKotak/SketchDuaLoop pada 2026-11-07.

## Masalah yang teramati

Coincident pada SketchT menghubungkan titik External `(0,0)` dengan garis
`(20,0)..(40,0)`. Titik berada pada perpanjangan, `t=-1`, bukan badan segmen.
Aturan lama salah menganggap jenis relasi dan ID berbeda cukup sebagai bukti
kontak ke badan. Ini tidak membenarkan ID `non_endpoint_point_on_segment`;
tidak diusulkan penambahan ID tersebut untuk temuan ini.

## Usulan keputusan

1. **Keanggotaan graf ditentukan oleh peran, bukan larangan tipe titik.**
   Node graf berasal dari ujung segmen non-konstruksi yang dianalisis dalam
   sketsa yang sama. Origin/titik referensi/proyeksi yang hanya mengikat posisi
   tidak menjadi node tambahan. Bila titik semacam itu benar-benar merupakan
   ujung segmen, ia tetap ikut dihitung. Jangan mengeluarkan seluruh Type=8
   atau Type=10 secara blanket, dan jangan menggabungkan titik berbeda hanya
   karena koordinatnya sama.
2. **Coincident point–line belum cukup untuk menyebut kontak ke badan.**
   Sebelum mengisi open/joined endpoint-on-segment, pastikan titik berada
   pada interior segmen hingga, bukan ujung atau perpanjangan. Pemeriksaan
   diterapkan pada kandidat relasi, bukan perluasan menjadi pencarian seluruh
   perpotongan/titik tanpa relasi. Untuk pasangan yang teramati, `t=-1`
   menolaknya dengan jelas.
3. **Origin/proyeksi yang bukan ujung tidak dihitung di open/joined maupun
   branch.** Asal referensi tetap dapat diperiksa dalam diagnostik. Kasus ini
   tidak memerlukan ID limit baru karena sudah dijelaskan sebagai relasi pada
   perpanjangan garis, bukan bukti adanya topologi yang belum dianalisis pada
   badan segmen.

Pemeriksaan interior numerik perlu rancangan toleransi eksplisit beserta
pelaporannya sebelum implementasi umum. Jangan diam-diam memakai
`endpointToleranceMm` untuk tujuan lain: kontrak sekarang menjadikannya
toleransi nearly-coincident, sedangkan sambungan dihitung dari identitas.
Proposal ini tidak menetapkan angka toleransi kontak baru tanpa review.

## Verifikasi untuk implementasi berikutnya

Perhitungan lima sketsa fixture tetap harus cocok. Kasus regresi minimal
berangkat dari data nyata: titik `(0,0)` terhadap garis `(20,0)..(40,0)`
ditolak sebagai kontak interior; titik pada ujung juga tidak menjadi kontak
badan. Kasus batang T dan loop menempel harus tetap terdeteksi.
Flag diagnostik keanggotaan titik harus memeriksa pemilik sketsa bersama ID,
agar ID dari sketsa lain tidak terhitung karena kebetulan angkanya sama.

Asal definitif referensi `(0,0)` dan apakah relasi dibuat otomatis tetap
belum terbukti. Tidak ada perubahan sketsa, penghapusan relasi, penerimaan
baseline, atau promosi status limit dalam proposal ini.
