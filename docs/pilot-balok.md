# Pilot native balok parametrik

Status 2026-11-07: **SW_PASS native untuk balok pilot 100 × 80 × 10 mm,
tiga run berturut-turut; variasi 120 × 50 × 12 mm lulus satu run.**
Tool tulis belum dipromosikan atau diekspos ke AI.

## Hasil nyata

Pengujian pada SolidWorks `33.1.1` (2025 SP1.1), DLL build time
`2026-11-07T07:01:11Z`, commit dasar `52357c5` dengan perubahan lokal.
SHA-256 DLL yang termuat:
`2D12C52040D572728DEE835A6387DE183615581EABE166CDFD1AC788C8022211`.
Ini bukti working tree yang dibangun, bukan klaim untuk commit bersih itu.
Setup dan 382 test otomatis lulus; host tetap connected tanpa error diagnostik.

| ukuran mm | volume setelah reopen mm³ | run native |
|---|---:|---:|
| 100 × 80 × 10 | 80000 | 3/3 berturut-turut |
| 120 × 50 × 12 | 72000 | 1/1 |

Kedua dimensi profil serta dimensi milik fitur extrude terbaca driving dan
sesuai nilai plan setelah reopen. Body count satu, posisi min `(0,0,0)`,
ukuran sesuai, dan tidak ada error/warning save/open. Pemeriksaan skrip juga
membandingkan hasil reopen dengan parameter input secara terpisah dari
status SW_PASS yang dikembalikan writer.

Artefak lokal (diabaikan Git):

- `tests/golden/output/block-pilot/ddfa98f3567849e7a8f803613c93fd24/`: tiga run dasar.
- `tests/golden/output/block-pilot/8332a7eaf387453e9d31ceae96ac23f1/`: variasi ukuran.

Masing-masing memuat part, plan, hasil/trace, hash part dan manifest metadata
DLL. Nama file part memuat UUID run agar tidak ambigu terhadap dokumen
bernama sama pada direktori berbeda.

Run awal membongkar dua masalah harness: `Get-FileHash` tidak dapat membuka
part yang sedang dibuka SolidWorks; stream baca dengan FileShare.ReadWrite
berhasil tanpa mengubah file. Nama `block-1.SLDPRT` yang berulang antar-run
juga membuat penutupan dokumen tidak andal; skrip kini memberi nama unik.
Run gagal tetap disimpan dan tidak dihitung sebagai run yang lulus. Satu
kesalahan pengelompokan aritmetika array PowerShell juga dikoreksi sebelum
tiga run lulus di atas.

Target pertama jalur tulis adalah part balok baru 100 × 80 × 10 mm, volume
80000 mm³. `examples/block-pilot.plan.json` memakai empat tool yang sudah
ada dalam kontrak: `create_sketch`, `add_rectangle` dengan dimensi,
`close_sketch`, `extrude_boss`. Pilot menerima perubahan width/height/depth;
bentuk alur tetap corner rectangle di `(0,0)` pada Front Plane, blind normal.

Ini acceptance harness manual untuk membuktikan implementasi sebelum promosi
ledger, bukan endpoint AI. Ia tidak mengganti status capability, tidak
mengubah PlanValidator, dan tidak berpura-pura menjalankan PlanRunner produksi.
Writer yang diuji berada di add-in, early-bound terhadap PIA dan seluruh
operasinya dijalankan lewat MainThreadDispatcher. Pemicu terpisah dari tool
read-only: request manual dikonsumsi timer host melalui dispatcher.

## Jalankan

Simpan pekerjaan dan tutup semua proses SolidWorks, lalu:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
```

Buka SolidWorks dengan MikosAI aktif; tidak perlu membuka fixture. Keluar dari
mode Edit Sketch atau dialog modal. Jalankan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\golden-balok.ps1
```

Default tiga run, masing-masing pada dokumen baru. Artefak `.SLDPRT`, plan
aktual, hasil/trace, dan SHA-256 berada dalam direktori run baru di
`tests/golden/output/block-pilot/`. Berkas yang sudah ada tidak ditimpa.
Satu run menyimpan dan menutup hanya part yang ia buat, membuka ulang part itu,
dan membiarkannya terbuka agar hasil bisa diperiksa. Dokumen pengguna lainnya
tidak diedit atau ditutup.

Uji parameter kedua, setelah run dasar berhasil:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\golden-balok.ps1 -Width 120 -Height 50 -Depth 12 -Runs 1
```

Jika default template tidak dikonfigurasi, gunakan `-TemplatePath` dengan
path template `.prtdot` milik instalasi. Tidak menebak lokasi template.

## Bukti yang diwajibkan

1. Sketsa baru terbuka pada reference plane bernama Front Plane. Nama aktual
   dibaca ulang dan harus sesuai plan pilot (`BlockProfile`).
2. Empat garis rectangle non-konstruksi, dua dimensi driving dengan nilai
   aktual sesuai parameter. Jangkar memakai Coincident ke origin, tidak Fix.
   Status Fully Defined harus benar-benar dibaca sebelum extrude.
3. Extrude memakai parameter blind normal, tanpa draft/offset. Test regresi
   menangkap seluruh 23 argumen yang benar-benar diteruskan ke
   `FeatureExtrusion2`; signature dicocokkan compiler PIA asli.
4. Rebuild berhasil, fitur ditemukan tanpa error/warning, satu solid body,
   ukuran dan posisi bounding box sesuai, volume width × height × depth.
5. Save tanpa error/warning, part benar-benar tertutup, buka ulang tanpa
   error/warning, rebuild lalu assert ulang profil, kedua dimensi desain,
   dimensi depth, ukuran, posisi dan volume. Nama dimensi disimpan tanpa suffix
   dokumen karena SaveAs mengubah nama dokumen di FullName.

Jejak tiap langkah mencatat snapshot baca ulang dan perubahan yang teramati
untuk lingkup pilot. Jika snapshot gagal, modelChanged null disertai alasan.
Ini belum implementasi envelope modelChanged untuk seluruh tool produksi.
Exception menghentikan run dan mempertahankan part gagal; tidak ada rollback
atau retry otomatis. Request yang sudah diproses diarsipkan, bukan dieksekusi
lagi saat timer berikutnya.

## Batas sebelum integrasi AI

Belum mendukung plane/face arbitrer, center rectangle, draft, extrude dua arah,
atau pembaruan part yang sudah ada. Pilot belum membuktikan bahasa natural
menjadi part, karena tidak melewati provider AI atau PlanRunner produksi.
Native test sempit di atas sudah berhasil. Berikutnya gunakan bukti untuk review promosi tool
yang benar-benar didukung dan integrasikan writer dengan executor/runner
produksi. Jangan mengaktifkan seluruh `sketch_authoring`/`part_features` karena
hanya empat operasi sempit ini yang diuji.
