# Laporan run SolidWorks — build `8dd3dec`

Tanggal: 2026-11-09. SolidWorks 2025 SP01.1, revisi `33.1.1`.

**Build add-in yang TERMUAT = `8dd3dec` = HEAD repo saat diuji.** Itu syarat
klaim `SW_PASS`, dan di run ini ia terpenuhi — berbeda dari klaim sebelumnya
yang menyebut *"build dasar 52357c5 dengan perubahan lokal"*, yang tidak bisa
dibangun ulang siapa pun.

`host-status.json` saat run: `status: connected`, `commandGroup`, `taskPane`,
`mcpServer` semuanya hidup, **nol galat tercatat**.

---

## Ringkasan

| Skrip | Hasil | Catatan |
|---|---|---|
| `uji-pia.ps1` | **4 / 0** | tidak butuh SolidWorks berjalan |
| `golden-balok.ps1` | **SW_PASS 3/3** | volume 80000 mm³, dibaca ulang setelah reopen |
| `golden-block-flow.ps1` | **3/3** | PlanRunner + reopen + 5 fresh check |
| `golden-document.ps1` | **34 / 0** | setelah satu harapan basi diperbaiki, lihat di bawah |
| `golden-sketsa.ps1` | **33 / 0** | **0 hipotesis patah** |
| `golden-kontur.ps1` | **144 / 0** | |

| Lapisan | Mock | SolidWorks asli | Asumsi belum terverifikasi |
|---|---|---|---|
| Core | `MOCK_PASS` 402 | — | — |
| `document_inspection` | `MOCK_PASS` | **`SW_PASS`** | `get_selection` isinya masih butuh klik manusia |
| `parametric_block` | `MOCK_PASS` | **`SW_PASS`** | tetap `pilot`; review manusia wajib |

---

## Tiga hipotesis yang terjawab

Ketiganya terbukti, dan dua di antaranya membenarkan koreksi peninjau terhadap
rancangan awalku.

**1. `GetChildren()` memang bukan daftar konsumen.** `Sketch1.dependents`
memuat `Sketch2` (hanya punya relasi) dan `Plane1` (reference plane), sementara
`consumedByFeatures` memuat keduanya tidak. Pemisahan dua field itu **memang
dibutuhkan** — asumsiku semula, "child yang bukan sketsa berarti konsumen",
akan memasukkan `Plane1` sebagai konsumen dan membuat `delete_feature` kelak
salah menilai.

**2. `DrivenState` terisi untuk dimensi SKETSA**, bukan hanya drawing.
`Sketch2` melaporkan driving 1, driven 1, unknown 0 — persis desainnya.
Pemisahan tiga ember bekerja di part nyata, dan tidak perlu fallback ke
`IsReferenceDim()`.

**3. Identitas titik cukup untuk konektivitas.**
`endpointToleranceMm = 0.000001` di seluruh sketsa, dan
`nearlyCoincidentEndpointCount = 0` — konektivitas dihitung dari identitas
`SketchPoint`, bukan dari toleransi geometris.

## Sensus `typeName`

Hasil yang kami tunggu untuk mengisi daftar putih **dengan bukti, bukan
ingatan**:

| fitur | `typeName` | konsumen? |
|---|---|---|
| `Boss-Extrude1` | `Extrusion` | ya |
| `Cut-Extrude1` | **`ICE`** | ya |
| `Plane1` | `RefPlane` | **tidak** |
| `Sketch2` | `ProfileFeature` | **tidak** |

`ICE` tidak akan pernah kami tebak dari ingatan. Itu alasan sensusnya ada.

## Partisi konektivitas terbukti menutup titik butanya

Dua kasus yang paling menentukan, keduanya **bersih di setiap angka lain**:

| sketsa | `openEndpoint` | `branchPoint` | yang menangkap |
|---|---|---|---|
| `SketchTBadan` | 4 | **0** | `openEndpointOnSegmentCount = 1` |
| `SketchLoopMenempel` | **0** | **0** | `joinedEndpointOnSegmentCount = 1` |

`SketchLoopMenempel` adalah celah yang ditemukan peninjau: dengan definisi
subset yang lama, **setiap angka akan nol** dan laporannya terdengar sehat
padahal region profilnya ambigu. Partisi menangkapnya.

---

## Satu kegagalan di run ini, dan apa sebenarnya yang salah

`golden-document.ps1` awalnya **3 gagal**:

```
GAGAL  fieldsNotPopulated TIDAK memuat 'dimensionCount'
GAGAL  fieldsNotPopulated TIDAK memuat 'consumedByFeature'
GAGAL  fieldsNotPopulated TIDAK memuat 'hasOpenContour'
```

Produknya **tidak salah**. Ketiga nama itu sudah **dipensiunkan kontrak** —
`dimensionCount` dipecah jadi tiga ember, `consumedByFeature` jadi
`consumedByFeatures`, dan `hasOpenContour` dihapus demi `openEndpointCount`.
Mereka benar-benar absen dari `fieldsNotPopulated` karena mereka bukan field
lagi.

Cacatnya ada di cara aku menulis assertion itu: ia memaku **daftar nama**, dan
tidak bisa membedakan *"janji diingkari"* dari *"field dipensiunkan"* — ia
melaporkan keduanya dengan kalimat yang sama.

Diganti menjadi **invariant** yang diturunkan dari respons sebenarnya:

- setiap field yang nilainya `null` **wajib** diakui di `fieldsNotPopulated`
- setiap yang diakui **memang** `null`

Penggantian nama field tidak lagi membuatnya basi, sementara satu field `null`
yang tidak diakui tetap merah. **Daftar nama selalu menua; invariant tidak.**

Setelah diperbaiki: **34 / 0**. Perbaikan ini hanya menyentuh skrip dan berkas
harapan — **build produk tidak berubah sepanjang run**, tetap `8dd3dec`, jadi
seluruh klaim di atas tetap berlaku.

## Yang masih belum dijawab

- `get_selection` hanya teruji **jalurnya**, bukan isinya — butuh prosedur
  interaktif dengan manusia mengklik.
- `parametric_block` tetap **`pilot`**. Yang terbukti baru satu bentuk
  (balok prismatik di part baru) pada satu versi SolidWorks.
- Daftar putih tipe konsumen baru memuat `Extrusion` dan `ICE`. Revolve, hole
  wizard, sweep, dan loft belum punya fixture, jadi `unclassified_child` akan
  berkibar untuk mereka — **dan itu disengaja**.
