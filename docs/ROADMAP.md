# Roadmap MikosAI

Ditulis ulang 2026-11-09. Versi lama ada di `ROADMAP-arsip-2026-11-09.md` —
546 baris, 168 kotak centang, menumpuk dari belasan sesi. Isinya masih
berharga sebagai catatan **kenapa** sesuatu diputuskan, tapi sudah tidak bisa
dipakai untuk menjawab "kita di mana".

Berkas ini menjawab tiga pertanyaan itu saja:

1. Apa yang **benar-benar bekerja** hari ini
2. Apa yang tersisa sampai **v0.1 selesai**
3. Apa yang datang **sesudahnya**, dan apa yang tidak akan pernah

---

## 1. Di mana kita sekarang

**Jalur baca selesai dan terverifikasi. Jalur tulis terbukti untuk satu
bentuk.**

AI bisa, dari dalam SolidWorks, membaca dokumen aktif dan melaporkannya dengan
jujur — termasuk mengakui apa yang tidak diketahuinya. Dan ia bisa membuat satu
bentuk (balok berdimensi) pada part baru, menyimpannya, membukanya ulang, lalu
memverifikasi hasilnya dari berkas yang tersimpan.

Yang belum: bentuk apa pun selain balok.

### Angka jujurnya

| | |
|---|---|
| Capability `verified` | **1** dari 12 |
| Capability `pilot` | **1** |
| Tool terekspos ke MCP | **7** (6 baca + 1 tulis) |
| Test otomatis | **402**, hijau |
| Build terakhir yang diuji di SolidWorks | `8dd3dec` |

Sepuluh capability sisanya `not_implemented`. Itu terdengar banyak, tapi
**sebagian besar bukan bagian dari v0.1** — lihat bagian 4.

---

## 2. Yang sudah bekerja hari ini

Semua baris di bawah punya bukti yang bisa dijalankan ulang, bukan klaim.

### Fondasi

| | bukti |
|---|---|
| Add-in hidup di dalam `SLDWORKS.exe` | 7/7 gerbang `addin.ps1 -Action Probe` |
| Thread utama tertangkap, pelanggaran jadi exception tercatat | test refleksi — method baru yang lupa penjaga langsung merah |
| Server MCP di `127.0.0.1`, port ephemeral, token per sesi | `IsolasiJaringanTests` + `mcp-roundtrip.ps1` |
| Panel chat di TaskPane | terbukti 2026-11-05 |
| Tombol toolbar + tab ribbon | terbukti 2026-11-05 |

### Membaca model — `document_inspection`, **`verified`**

Enam tool: `get_active_document`, `list_features`, `get_bounding_box`,
`get_mass_properties`, `inspect_sketches`, `get_selection`.

Bukti pada build `8dd3dec`:

```
golden-document.ps1    34 / 0
golden-sketsa.ps1      33 / 0, nol hipotesis patah
golden-kontur.ps1     144 / 0
```

**Batas yang diakui:** terbukti untuk part prismatik sederhana di SW 33.1.1.
Assembly, permukaan, dan konfigurasi ganda **belum pernah diuji**.
`get_selection` baru teruji jalurnya, bukan isinya.

### Menulis model — `parametric_block`, **`pilot`**

Satu tool: `create_parametric_block`. Membuat balok berdimensi pada part
**baru**, menyimpan, menutup, membuka ulang, memverifikasi.

```
golden-balok.ps1        SW_PASS 3/3, volume 80000 mm³ setelah reopen
golden-block-flow.ps1   3/3, PlanRunner + reopen + 5 fresh check
```

**Kenapa tetap `pilot`:** satu bentuk, satu versi SolidWorks. Review manusia
wajib. Itu bukan formalitas — belum ada bukti ia benar untuk bentuk lain.

---

## 3. Sampai v0.1 selesai

> **Definisi v0.1:** *mounting plate 100×80×10 mm dengan 4 lubang M6
> counterbore*, dibuat dari chat, lulus **3 kali berturut-turut**, dengan setiap
> fitur diverifikasi lewat baca-ulang setelah simpan-buka ulang.

Ini definisi yang sudah dipakai sejak awal, dan tidak diubah.

### Yang sudah terbukti — 2026-11-10

**Mounting plate lengkap, SW_PASS 3/3 berturut-turut** pada build `fbce808`,
SolidWorks 33.1.1, lewat harness pilot native.

```
lubang : ISO M6, counterbore 14.547 mm, tembus 6.6 mm
posisi : (20,20,10) (80,20,10) (20,60,10) (80,60,10) — deviasi maks 0 mm
volume : 80000 → 76519.678200 mm³
```

Tiga kali, identik sampai desimal terakhir, masing-masing diverifikasi
**setelah simpan-tutup-buka ulang**.

Dan ada pemeriksaan silang yang berdiri sendiri: empat lubang membuang
**870.08045 mm³ masing-masing**, persis sama dengan yang dibuang satu lubang
pada run sebelumnya. Keempatnya identik dan memotong penuh — itu bukan sesuatu
yang bisa kebetulan cocok.

`hole_wizard` **selesai**, termasuk transformasi posisi ke ruang sketsa dan
baca-ulang yang ditransformasi balik.

### Disambungkan ke MCP dan chat — 2026-11-10, build `a7f8bd8`

| skrip | hasil |
|---|---|
| `golden-plat.ps1 -Runs 3` | **SW_PASS 3/3** — posisi lubang kini dibaca **setelah reopen** |
| `golden-plat-flow.ps1 -Runs 3` | **SW_PASS 3/3 lewat tool MCP**, jalur yang dipakai AI dari chat |

Jalur MCP juga membuktikan dua penolakan **tanpa menyentuh model**: posisi di
tengah ketebalan (`z = 5`), dan permintaan tanpa standar — yang **tidak** diberi
ISO diam-diam. Panggilan ulang dengan `operationId` yang sama mengembalikan hasil
lama, bukan membuat plat kedua.

Satu koreksi atas run sebelumnya: SW_PASS `fbce808` memverifikasi posisi lubang
**sebelum simpan**, padahal skripnya berkomentar sebaliknya. Writer-nya sah;
klaim "posisi terverifikasi setelah reopen" baru benar sejak `a7f8bd8`.

### Yang MASIH kurang untuk v0.1

1. **Shortcut chat di panel belum diketik manusia.** Jalur LLM (agy → MCP) dan
   flow-nya sudah terbukti; kalimat
   `buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore ISO jarak 20 mm dari tepi`
   di kotak chat belum pernah dicoba.
2. **Belum 50 eksekusi tanpa crash** — kriteria "layak dipakai".
3. **Pemulihan setelah gagal** belum diuji sebagai kriteria tersendiri.
4. **Sketsa posisi lubang under-defined.** Titiknya ditaruh dengan koordinat,
   bukan dimensi desain — mengubah ukuran plat tidak menggeser lubang ikut.
   Gerbang Fase 3 menuntut sketsa fully defined lewat dimensi desain, jadi ini
   harus dibereskan sebelum gerbang itu dinyatakan tertutup penuh.

Empat lubang memang jadi **satu fitur**, jadi `linear_pattern` tidak
dibutuhkan — seperti dugaan kontraknya.

Yang sudah terjawab lewat run nyata, dan sempat salah semua di tebakan awal:

| | |
|---|---|
| `Value1…Value12` | **tidak pernah disentuh.** Jalur `CreateDefinition` → `InitializeHole` → `CreateFeature` mengisi dimensi dari basis data Hole Wizard, jadi perangkap urutan parameter hilang alih-alih diuji-kelilingi |
| `AccessSelections` | **MEMATIKAN SolidWorks** kalau dipanggil pada definisi yang baru dibuat — tanpa exception, tanpa jejak managed. Pasangan Access/Release hanya untuk mengedit definisi yang sudah ada |
| Posisi | Hole Wizard menaruh **satu** titik saja. Sisanya ditaruh sendiri lewat `CreatePoint` di ruang sketsa, dan titik otomatisnya **dipindah** lewat `SetCoords`, bukan dihapus |
| Lingkup M0 | hanya `simple` dan `counterbore`. Tipe lain `BLOCKED` sampai test-nya SW_PASS |

### Urutan kerja

1. **`hole_wizard` sebagai tool tulis**, di atas jalur yang sudah terbukti —
   `NewDocument` → tulis → simpan → tutup → buka ulang → verifikasi.
2. **Test transformasi posisi** di face yang **bukan** Front Plane — face atas,
   face samping, dan face pada offset. Harus pernah terlihat **merah** tanpa
   transformasi. Di Front Plane koordinat sketsa kebetulan sama dengan
   koordinat model, jadi transform yang hilang pun akan lulus.
3. **Rantai mounting plate**: balok + 4 lubang, dari chat.
4. **3 kali berturut-turut**, lalu 50 eksekusi tanpa crash.

### Yang TIDAK dibutuhkan untuk v0.1

Supaya tidak ada yang tersedot ke sana:

- revolve, shell, mirror, circular pattern
- sweep, loft, spline, kurva dari persamaan
- assembly, drawing, export
- `fixture-fitur.SLDPRT` — resepnya sudah ada, **tidak menghalangi apa pun**
- menumbuhkan daftar putih tipe konsumen di luar yang datang sendiri

---

## 4. Sesudah v0.1

Berurutan menurut apa yang membuka apa, bukan menurut keinginan.

**4a. Fitur dasar yang hilang** — `revolve`, `shell`, `mirror`,
`circular_pattern`. Setelah satu rantai tulis terbukti, menambah fitur jadi
pekerjaan satu tool — bukan taruhan.

**4b. Penemuan entitas** — `find_faces`, `find_edges`, `create_reference_plane`,
`list_reference_geometry`. Hari ini satu-satunya sumber `EntityRef` adalah
`get_selection`, yang bergantung pada manusia mengklik. AI yang harus minta
tolong diklikkan bukan otomasi.

**4c. Siklus dokumen** — `create_document`, `open_document`, `save_document`,
`rebuild`, `export`. `rebuild` paling telanjang: `list_features` melaporkan
`rebuildRequired` dan tidak ada cara menindaklanjutinya.

**4d. Recovery** — `delete_feature`, `suppress_feature`. Kebijakannya **sudah
diputuskan**: tidak ada rollback otomatis, snapshot pasif sebelum plan,
pemulihan adalah keputusan manusia.

**4e. Assembly dan drawing.** Paling jauh, dan paling besar.

### Yang sengaja tidak akan dikerjakan

- **Eksekusi VBA / `run_api_script` bebas.** Tidak bisa diverifikasi, dan
  membatalkan seluruh gagasan ledger kemampuan.
- **Screenshot sebagai bukti.** Boleh ada sebagai alat bantu manusia, tapi
  **tidak boleh** menaikkan verdict apa pun. Plat yang lubangnya meleset 3 mm
  terlihat sempurna di gambar.
- **Auto-dimension / `Fix` massal** sebagai pengganti dimensi desain.

---

## 5. Aturan yang mengikat

Ini yang membuat sisa pekerjaan bisa dipercaya. Semuanya lahir dari kesalahan
nyata, bukan dari preferensi.

1. **Status sukses API bukan bukti.** Setiap tool yang mengubah model wajib
   membaca ulang hasilnya dari model.
2. **Ukuran yang tidak ada dilaporkan `null`, bukan 0.** Hitungan boleh 0 —
   tapi hanya kalau benar-benar dihitung. Nol yang tidak pernah dihitung adalah
   karangan yang kebetulan terlihat seperti hitungan.
3. **Jangan menebak nilai engineering.** Standar lubang, ukuran fastener,
   material, toleransi — kalau tidak disebut pengguna, kembalikan `BLOCKED` dan
   `openQuestions`.
4. **Klaim `SW_PASS` menyebut hash build yang TERMUAT.** Build termuat ≠ HEAD →
   klaim batal.
5. **Test hijau yang belum pernah terlihat merah tidak dianggap bukti.** Setiap
   perbaikan dibuktikan dengan memasang kembali bug-nya.
6. **Tanda tangan API direfleksikan dari PIA, tidak ditebak dari ingatan.**
   Dua kesalahan kontrak lahir bukan dari refleksinya melainkan dari cara
   keluarannya dicetak — karena itu ada `scripts/uji-pia.ps1`.
7. **`verified` tidak boleh dilebarkan diam-diam.** Ia selalu menyebut untuk
   apa ia berlaku, dan untuk apa ia belum pernah diuji.

---

## 6. Apa yang bisa dijalankan sekarang

```powershell
scripts\setup.ps1                  # build + test + registrasi (SolidWorks tertutup)
scripts\addin.ps1 -Action Probe    # 7 gerbang kesehatan add-in
scripts\uji-pia.ps1                # pencetak tanda tangan PIA
scripts\mcp-roundtrip.ps1 -Agy     # rantai penuh: agy -> MCP -> SolidWorks
```

Golden test — masing-masing butuh partnya aktif:

```powershell
scripts\golden-document.ps1   # plat-referensi.SLDPRT
scripts\golden-sketsa.ps1     # fixture-konsumen.SLDPRT
scripts\golden-kontur.ps1     # fixture-kontur.SLDPRT
scripts\golden-balok.ps1      # membuat part baru sendiri
scripts\golden-block-flow.ps1 # membuat part baru sendiri
```

Laporan run terakhir: `docs/laporan-run-8dd3dec.md`.
