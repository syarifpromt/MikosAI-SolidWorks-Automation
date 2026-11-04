# Cara membuat `plat-referensi.SLDPRT`

Part ini adalah acuan golden test. Angkanya harus **persis**, karena seluruh nilai
harapan di `plat-referensi.json` diturunkan dari geometri dengan hitungan tangan —
bukan disalin dari keluaran tool.

Kalau partnya meleset, yang gagal adalah test, dan orang akan menyalahkan kode.

## Langkah

1. **File → New → Part**, pakai template default.
2. Pastikan satuannya **MMGS** (lihat pojok kanan bawah). Kalau bukan, ubah lewat
   `Tools → Options → Document Properties → Units → MMGS`.
3. Pilih **Front Plane**, lalu **Sketch**.
4. Gambar **Center Rectangle** (`Tools → Sketch Entities → Center Rectangle`)
   dengan pusat **tepat di Origin**.
   - Pusat di origin itu penting: dari situlah pusat massa `(0, 0, 5)` berasal,
     dan itu salah satu pemeriksaan yang berdiri sendiri.
5. Beri dimensi **100 mm** (horizontal) dan **80 mm** (vertikal).
   Sketsa harus menjadi **fully defined** — statusnya terlihat di status bar.
6. **Features → Extruded Boss/Base**, `Blind`, tebal **10 mm**, arah default
   (menjauh dari penampil, ke +Z). Jangan centang `Reverse Direction`.
7. Klik kanan nama part di FeatureManager → **Material → Plain Carbon Steel**.
   - Densitasnya harus terbaca **7800 kg/m³**. Kalau materialmu memberi angka
     lain, massanya akan meleset dan itu BUKAN kesalahan kode.
8. Simpan sebagai `tests/golden/plat-referensi.SLDPRT` di dalam repo.

## Memeriksa sebelum commit

Dengan part itu **aktif** di SolidWorks:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\golden-document.ps1
```

Skrip itu membaca lewat MCP tiga kali berturut-turut dan membandingkannya dengan
`plat-referensi.json`. Kalau ada yang meleset, ia menyebut nilai yang diharapkan,
yang diterima, dan selisihnya.

## Kenapa 100 × 80 × 10, dan kenapa bukan part yang sudah ada

Part yang kami pakai sebelumnya berukuran `137.236205 × 73.061001 × 10` — hasil
sketsa yang digambar bebas. Angka seperti itu tidak bisa diperiksa siapa pun:
satu-satunya cara mengetahui benar-tidaknya adalah membandingkannya dengan
pembacaan sebelumnya.

Itu snapshot, bukan golden test. Snapshot hanya menjawab *"apakah hasilnya sama
seperti kemarin"*. Kalau pembacaannya sudah salah sejak awal, snapshot membekukan
kesalahan itu dan menyebutnya lulus — dan kami sudah pernah membayar persis itu:
`massGrams` yang dikarang dari densitas air pernah lolos sebagai sukses.

Dengan 100 × 80 × 10 setiap angka bisa dihitung dengan pensil:

| Besaran | Hitungan | Nilai |
|---|---|---|
| Volume | 100 × 80 × 10 | 80000 mm³ |
| Massa | 80000e-9 m³ × 7800 kg/m³ | 624 g |
| Luas permukaan | 2(100×80) + 2(100×10) + 2(80×10) | 19600 mm² |
| Pusat massa | simetri | (0, 0, 5) |

Empat pemeriksaan yang **saling bebas**. Tool harus melewati semuanya sekaligus,
dan satu pembacaan yang dikarang tidak mungkin kebetulan cocok dengan keempatnya.
