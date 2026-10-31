# ADR 0004 — Clean-room dengan pinjaman selektif

Tanggal: 2026-10-31 · Status: diterima

## Konteks

`wzyn20051216/solidworks-automation-skill` berlisensi MIT, jadi kita bebas secara legal
untuk fork, submodule, atau copy dengan atribusi.

Repo itu berisi 591 file, 200+ file test, 65 tool MCP, dan dokumentasi arsitektur yang
bagus. Mengambilnya utuh akan menghemat banyak waktu.

## Keputusan

Repo baru milik kita, arsitektur kita sendiri. Konsep dan rancangan diadopsi secara sadar.
File tertentu boleh di-port, tapi setiap port dicatat di `ATTRIBUTION.md`.

## Alasan

Yang berharga dari repo itu adalah **rancangan lapisan keandalannya**, dan rancangan bisa
diadopsi tanpa mengambil kodenya. Yang akan kita warisi kalau fork justru hal-hal yang
sudah kita putuskan untuk tidak dibangun:

- dua UI desktop yang bersaing (PySide6 dan Tauri+React, keduanya aktif),
- lapisan attestation/governance dengan trust root dan drift checking,
- file bukti SHA-256 yang di-commit ke repo,
- seluruh dokumentasi dan pesan commit berbahasa Mandarin,
- library eksekusi Python yang — karena ADR 0001 — tidak bisa kita pakai langsung.

Poin terakhir menentukan. Keuntungan utama fork adalah memakai ulang 65 tool Python mereka.
Tapi kita memilih eksekusi C# in-process, jadi keuntungan itu hilang. Yang tersisa hanyalah
beban.

Dan untuk tim dua orang, 591 file yang tidak kita tulis sendiri dan tidak bisa kita baca
(bahasa) adalah beban kognitif yang nyata, bukan aset.

## Konsekuensi

- Mulai dari nol untuk kode. Lebih lambat di awal.
- Setiap keputusan adalah keputusan kita, bisa dijelaskan, dan bisa dicari asalnya di
  `docs/adr/`.
- Tidak mendapat update upstream. Dapat diterima — arsitektur kita berbeda, jadi
  kebanyakan update upstream tidak akan relevan.
- `ATTRIBUTION.md` wajib dirawat. Kalau ada file yang di-port, dicatat beserta lisensi
  MIT-nya.

## Yang kita baca, bukan kita copy

Saat implementasi, file-file ini layak dibuka sebagai referensi perilaku:

| File mereka | Untuk |
|---|---|
| `docs/architecture/execution-core.md` | Rancangan lapisan keandalan |
| `references/agent-provider-architecture.md` | State machine provider |
| `references/solidworks-addin-host.md` | Gerbang penerimaan add-in, jebakan TLB dan `IDispatch` |
| `capabilities.yaml` | Bentuk ledger kemampuan |
| `scripts/core/recovery.py` | Taksonomi klasifikasi error |
| `subskills/solidworks-vibecad/schemas/design_plan.schema.json` | Pembanding skema DesignPlan |
| `references/complex-hole-features.md` | Pelajaran lapangan hole feature |
| `scripts/sw_entity_reference.py` | Cara mereka menghindari `Face1`/`Edge1` |

Membaca kode MIT untuk memahami perilaku API SolidWorks adalah hal yang wajar dan legal.
Yang kita hindari adalah mewarisi struktur proyeknya.

## Alternatif yang ditolak

**Fork langsung.** Dapat 200+ test dan library skrip gratis, tapi juga mewarisi dua UI yang
bersaing, lapisan governance, dan seluruh dokumen Mandarin. Dan karena ADR 0001, library
Python-nya tidak terpakai.

**Submodule read-only.** Bisa ikut update upstream, tapi mengikat kita pada keputusan desain
mereka — khususnya eksekusi out-of-process, yang justru kita tolak di ADR 0001.
