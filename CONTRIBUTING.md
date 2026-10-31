# Cara kerja di repo ini

Proyek dua orang. Dokumen ini singkat karena yang panjang tidak akan dibaca.

---

## Bahasa

- **Dokumen dan commit message:** bahasa Indonesia
- **Identifier kode, nama tool, nama file, skema JSON:** bahasa Inggris
- **Komentar kode:** Indonesia boleh, Inggris boleh — konsisten dalam satu file

## Jalur kerja

Dua jalur paralel, kontraknya skema tool JSON + `capabilities.yaml`:

- **Jalur Platform** — shell add-in, dispatcher, MCP server, provider host, UI
- **Jalur CAD** — tool CAD, verifikasi, golden test, ledger

Keduanya bekerja melawan fake sampai Fase 3. Kalau kamu butuh mengubah kontrak, itu
percakapan, bukan commit.

## Branch dan commit

- `main` selalu bisa di-build
- Satu branch per unit kerja: `feat/mcp-server`, `fix/dispatcher-deadlock`
- Commit message: apa yang berubah dan kenapa, satu baris cukup
- Tidak perlu PR review untuk pekerjaan di jalurmu sendiri. **Wajib review** kalau
  menyentuh `capabilities.yaml`, skema tool, atau `MainThreadDispatcher`

---

## Aturan yang tidak bisa dinegosiasi

Lima hal ini bukan preferensi gaya. Melanggarnya menghasilkan bug yang mahal, dan semuanya
sudah terbukti mahal di proyek lain.

### 1. Tidak ada panggilan API SolidWorks di luar `MainThreadDispatcher`

```csharp
// SALAH - akan crash secara acak, dan tidak bisa direproduksi
var count = _swApp.GetDocumentCount();

// BENAR
var count = await _dispatcher.InvokeAsync(() => _swApp.GetDocumentCount(), ct);
```

API SolidWorks tidak thread-safe. Ada tiga sumber panggilan konkuren di sistem kita
(HttpListener, pembaca pipe provider, callback WebView2). Tidak ada pengecualian untuk
aturan ini, termasuk untuk "hanya membaca satu properti".

### 2. Status tidak dikenal menjadi `BLOCKED`

```csharp
// SALAH
return status == "fail" ? Verdict.Fail : Verdict.Pass;

// BENAR
return status switch {
    "pass" or "warn" or "review_required" => ...,
    "fail" or "failed"                    => Verdict.Fail,
    "blocked"                             => Verdict.Blocked,
    _                                     => Verdict.Blocked,   // default aman
};
```

### 3. Tidak menaikkan status di `capabilities.yaml` tanpa bukti test

`verified` berarti: golden test lulus di SolidWorks nyata, versi tercatat, tiga kali
berturut-turut. Tidak ada jalan pintas. Ini pelanggaran paling serius di repo ini karena
merusak satu-satunya mekanisme yang melindungi pengguna.

### 4. Tidak ada `Face1` / `Edge1` / koordinat layar

Referensi berbasis indeks rusak begitu ada fitur disisipkan di atasnya. Pakai kriteria
stabil: normal + centroid, atau persistent reference ID. Kalau belum ada infrastrukturnya,
tandai tool-nya `not_implemented` — jangan kirim yang rapuh.

### 5. `exit code 0` dengan stdout kosong bukan sukses

Khusus provider CLI. `agy` dilaporkan diam total ketika stdout bukan TTY: tanpa output,
tanpa error, exit 0. Itu **kegagalan integrasi**, bukan jawaban AI yang kosong.
Klasifikasikan sebagai `provider_silent`. Lihat `docs/integrasi-antigravity.md` §1.

---

## Verifikasi

Setiap operasi tulis diverifikasi dengan **membaca ulang model**, bukan dengan memercayai
return value. Minimum untuk part:

1. fitur ada di FeatureManager tree dengan nama yang diharapkan
2. `EditRebuild3` sukses, tanpa rebuild error
3. status constraint sketsa dibaca lewat `GetConstrainedStatus` — **bukan** `Fix` massal
4. `MassProperties` mengembalikan volume bukan nol
5. **simpan, tutup, buka ulang**, assert ulang dimensi kunci

Poin 5 yang paling sering dilewati dan paling sering menangkap bug. Jangan lewati.

## Test

- **Golden test** — alur lengkap di SolidWorks nyata. Lambat, wajib untuk promosi
  `verified`
- **Test logika kontrol** — fake handler/reviewer, tanpa SolidWorks, tanpa jaringan.
  Harus cepat dan harus selalu hijau
- **Test regresi jebakan** — khusus untuk mode kegagalan yang sudah pernah kita temukan
  (TLB hilang, handler PMP tanpa `IDispatch`, provider silent). Test ini ada supaya bug yang
  sama tidak kembali

Yang **tidak** kita tulis: test untuk test runner kita sendiri.

## Keamanan dan privasi

- MCP server hanya bind `127.0.0.1`. Tidak pernah `0.0.0.0`
- Bearer token per sesi, file sesi ber-ACL ke pengguna saat ini
- Trace menyimpan **ringkasan** argumen saja — tidak pernah prompt penuh, API key, token,
  atau path absolut yang tidak perlu
- Jangan commit path mesin, nama user Windows, atau file bukti berisi data lokal

## Menambah tool baru

1. Tambahkan entri di `capabilities.yaml` dengan status `not_implemented`
2. Tulis skema input/output JSON
3. Implementasikan di `ToolExecutor`, dieksekusi lewat dispatcher
4. Tulis check verifikasi
5. Tulis golden test
6. Jalankan di SolidWorks nyata, tiga kali
7. Baru naikkan status, dan catat versi SolidWorks-nya

Urutannya memang begitu. Status naik paling akhir.

## Debugging add-in

SolidWorks mengunci DLL yang sudah dimuat. Sebelum rebuild: `UnloadAddIn` atau tutup
SolidWorks. Jangan timpa paksa — gejalanya membingungkan dan membuang waktu.

Kalau add-in tidak muncul padahal `LoadAddIn` mengembalikan 0: kemungkinan besar TLB belum
terdaftar. SolidWorks bisa memuat DLL dan melaporkan sukses tanpa pernah menginstansiasi
`ISwAddin`. Periksa `%LOCALAPPDATA%\MikosAI\host-status.json`, jangan percaya return code.
