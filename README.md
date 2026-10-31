# MikosAI — SolidWorks Automation

AI yang dioperasikan **dari dalam SolidWorks**. Ketik apa yang kamu mau dalam bahasa biasa
di panel samping, dapat model parametrik yang sudah diverifikasi — dan kalau kamu mau
mengambil alih kendali, semuanya terbuka untuk diedit.

> **Status: pra-implementasi.** Saat ini repo hanya berisi arsitektur, roadmap, dan ledger
> kemampuan. Belum ada kode. Semua entri di `capabilities.yaml` masih
> `not_implemented` — dan itu disengaja: status di file itu tidak pernah naik tanpa bukti
> test.

---

## Ide intinya

Hampir semua otomasi AI untuk CAD menaruh AI **di luar** dan menyetir SolidWorks dari jauh
lewat COM. Hasilnya lambat, rapuh, dan terasa seperti robot yang mengendalikan mouse-mu.

MikosAI membalik itu. Server MCP hidup **di dalam proses SolidWorks**, dan AI CLI adalah
subprocess yang dikelola oleh add-in:

```
+-- SLDWORKS.exe ------------------------------+
|  MikosAI Add-in (C#, in-process)             |
|   - TaskPane + WebView2  <- UI chat di sini   |
|   - MainThreadDispatcher                     |
|   - ToolExecutor -> PIA langsung              |
|   - MCP Server (loopback)                    |
+-------------------^--------------------------+
                    |
          Antigravity CLI (`agy`)
```

Konsekuensinya: tanpa marshalling COM, latensi mikrodetik, akses penuh ke event dan
PropertyManagerPage, dan konteks seleksi pengguna langsung tersedia. AI tahu apa yang sedang
kamu klik.

## Dua lapis, bukan satu kompromi

**Mudah dipakai.** Chat bahasa natural plus tombol preset. Tidak pernah melihat JSON, tidak
pernah melihat nama tool. Kalau sesuatu terblokir, pesannya menjelaskan apa yang perlu kamu
lakukan.

**Tetap overpower.** Setiap permintaan menghasilkan **DesignPlan** — JSON parametrik
tervalidasi. Buka panelnya, ubah `thickness: 10` jadi `12`, jalankan ulang. Deterministik,
tanpa memanggil AI, tanpa biaya token. Plus panel Evidence, Trace viewer, dan override
model.

Kekuatan itu opsional dan progresif. Itu bedanya dengan alat yang hanya punya lapis kedua.

## Kenapa hasilnya bisa dipercaya

Tiga aturan yang ditegakkan secara struktural, bukan lewat niat baik:

1. **Tool mengembalikan `success` bukan berarti tugas berhasil.** Setiap operasi tulis
   diverifikasi dengan membaca ulang model — termasuk simpan, tutup, buka ulang, lalu assert
   ulang dimensi kuncinya.
2. **Status tidak dikenal menjadi `BLOCKED`, bukan `PASS`.** Default selalu aman.
3. **AI tidak bisa memanggil yang belum diuji.** Kemampuan berstatus `reference_only` atau
   `not_implemented` tidak pernah muncul di daftar tool MCP. Bukan dilarang lewat prompt —
   tool-nya memang tidak ada.

## Build dan test

`MikosAI.Core` tidak punya dependensi SolidWorks, jadi bisa dibuild dan ditest di mesin mana
pun — termasuk mesin tanpa SolidWorks dan tanpa Visual Studio:

```powershell
dotnet build
dotnet test
```

Itu yang membuat Jalur Platform bisa bekerja penuh tanpa CAD. Proyek `MikosAI.Addin` (yang
menyentuh PIA SolidWorks) butuh instalasi SolidWorks dan dikerjakan mulai Fase 0 di sisi
yang punya lisensi.

## Kebutuhan

- Windows 10 1809+ (ConPTY), SolidWorks dengan seat lisensi aktif dan sesi desktop
- .NET Framework 4.8, WebView2 Runtime
- Antigravity CLI (`agy`) terpasang dan sudah login

Target verifikasi awal: SolidWorks 2026. Versi lain belum diuji dan `capabilities.yaml`
akan menyatakannya dengan jujur.

## Dokumentasi

| Dokumen | Isi |
|---|---|
| [docs/ARSITEKTUR.md](docs/ARSITEKTUR.md) | Acuan arsitektur — mulai dari sini |
| [docs/ROADMAP.md](docs/ROADMAP.md) | 5 fase, gerbang penerimaan, pembagian kerja, registry risiko |
| [docs/integrasi-antigravity.md](docs/integrasi-antigravity.md) | Integrasi `agy`. **Wajib dibaca sebelum menyentuh ProviderHost** |
| [docs/skema-tool-dan-plan.md](docs/skema-tool-dan-plan.md) | Keputusan skema tool dan DesignPlan, plus konvensi yang mudah salah |
| [docs/bedah-repo-referensi.md](docs/bedah-repo-referensi.md) | Analisis proyek yang menjadi acuan kita |
| [docs/adr/](docs/adr/) | Catatan keputusan arsitektur dan alasannya |
| [capabilities.yaml](capabilities.yaml) | Ledger kemampuan — satu sumber kebenaran |
| [schemas/](schemas/) | Kontrak: `tools.json`, `design-plan.schema.json`, `common.schema.json` |
| [examples/mounting-plate.plan.json](examples/mounting-plate.plan.json) | DesignPlan nyata — target golden test Fase 3 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Cara kerja, aturan yang tidak bisa dinegosiasi |
| [ATTRIBUTION.md](ATTRIBUTION.md) | Atribusi pekerjaan pihak ketiga |

## Lisensi

MIT. Lihat [ATTRIBUTION.md](ATTRIBUTION.md) untuk pekerjaan pihak ketiga yang menjadi acuan.
