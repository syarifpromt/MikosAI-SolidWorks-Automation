# ADR 0001 — Eksekusi CAD in-process dengan C#

Tanggal: 2026-10-31 · Status: diterima

## Konteks

Operasi CAD bisa dijalankan dari tiga tempat: Python out-of-process lewat COM (pendekatan
repo referensi), C# tipis sebagai shell dengan Python tetap mengeksekusi, atau C#
in-process penuh di dalam add-in.

Repo referensi memilih Python out-of-process dan punya library skrip yang matang. Memakai
ulang itu akan mempercepat start kita secara signifikan.

## Keputusan

Seluruh operasi CAD dijalankan **in-process di dalam add-in C#**, memanggil SolidWorks PIA
langsung. Python dipertahankan hanya sebagai sidecar opsional untuk beban yang tidak
menyentuh SolidWorks: OCCT, konversi mesh, solver FEA, baca ulang PDF.

## Alasan

Produk yang kita tuju adalah AI yang dioperasikan **di dalam** SolidWorks. Itu menuntut
hal-hal yang out-of-process tidak bisa berikan dengan baik:

- **Konteks seleksi pengguna.** AI perlu tahu apa yang sedang di-klik pengguna. Lewat COM
  ini harus di-polling; in-process langsung tersedia. Ini perbedaan antara "AI yang
  membantuku" dan "robot yang menyetir dari jauh".
- **Event SolidWorks.** Langganan event jangka panjang tidak praktis lewat COM
  out-of-process.
- **PropertyManagerPage.** Tidak mungkin dari luar proses, padahal ini antarmuka input
  terstruktur native SolidWorks.
- **Latensi.** Satu DesignPlan bisa berisi puluhan panggilan API. Marshalling COM per
  panggilan membuat interaksi terasa lambat.
- **Kerapuhan marshalling.** Repo referensi punya seluruh file (`sw_delivery_comtypes_worker.py`)
  dan backend fallback khusus untuk menangani kegagalan marshalling VARIANT/SAFEARRAY.
  Masalah itu hilang sepenuhnya in-process.

## Konsekuensi

**Yang kita bayar:**

- Sekitar 18 tool CAD harus ditulis dari awal di C#. Library Python mereka tidak bisa
  dipakai langsung — hanya dibaca sebagai referensi perilaku API.
- Crash di kode kita **menjatuhkan SolidWorks**, bukan hanya proses kita. Ini risiko R5 di
  roadmap.
- Terikat `net48` untuk add-in (lihat Konsekuensi di bawah).

**Mitigasi yang jadi wajib karena keputusan ini:**

- `MainThreadDispatcher` di Fase 0, bukan nanti. Tanpa itu, risiko crash menjadi kepastian
  crash.
- Semua operasi dibungkus guard; tidak ada klaim sukses sebelum verifikasi.

**Soal `net48`:** add-in SolidWorks dimuat sebagai COM in-process ke dalam CLR yang sudah
dijalankan SolidWorks. `net48` adalah jalur paling teruji untuk itu. Kebutuhan .NET modern
ditempatkan di proses terpisah.

## Alternatif yang ditolak

**Hybrid (C# tipis + Python eksekutor).** Bisa reuse besar dan onboarding lebih cepat, tapi
mewarisi seluruh masalah marshalling dan latensi — sementara kita justru membangun add-in
in-process. Kita akan membayar biaya kompleksitas add-in tanpa memetik manfaatnya.

**Python-first dengan add-in sebagai jendela saja.** Prototipe paling cepat, tapi "AI di
dalam SolidWorks" jadi ilusi kosmetik: tanpa event in-process, tanpa PMP, tanpa konteks
seleksi. Batasan teknisnya identik dengan repo referensi, jadi tidak ada diferensiasi.
