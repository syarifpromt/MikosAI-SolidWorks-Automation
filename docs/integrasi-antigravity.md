# Integrasi Antigravity CLI (`agy`)

Status: draft v0.1 · Riset dilakukan 2026-10-31

**Baca dokumen ini sebelum menulis satu baris pun `ProviderHost`.** Ada satu mode kegagalan
yang akan membuat kita menghabiskan berhari-hari kalau tidak diketahui lebih dulu.

---

## 0. Peringatan tentang kualitas sumber

Antigravity CLI dirilis Google sekitar **Mei 2026** bersama Antigravity 2.0, menggantikan
Gemini CLI untuk pengguna konsumen. Itu **setelah** batas pengetahuan model yang menulis
dokumen ini, jadi semua di bawah berasal dari riset web, bukan ingatan.

Riset tidak menemukan halaman dokumentasi resmi Google yang bisa diakses untuk flag CLI.
Semua detail di bawah berasal dari sumber pihak ketiga: cheat sheet, panduan komunitas,
README library integrasi, dan satu thread forum Google AI Developers.

**Konsekuensi praktis:** setiap angka dan nama flag di dokumen ini harus diverifikasi
terhadap `agy --help` pada versi yang benar-benar terpasang, **sebelum** dijadikan asumsi
kode. Lihat bagian 4 soal flag probing — kita memperlakukan drift versi sebagai hal normal,
bukan kejadian luar biasa.

---

## 1. Temuan kritis: `agy -p` diam ketika stdout bukan TTY

Ini temuan terpenting dari seluruh riset.

**Perilaku.** Ketika stdout `agy` bukan terminal sungguhan, `agy -p` dilaporkan
**tidak mengeluarkan apa pun — tanpa output, tanpa error, exit code `0`**.

**Kenapa ini berbahaya bagi kita.** Arsitektur kita memanggil `agy` sebagai subprocess dari
add-in C# dengan pipe yang diredirect. Itu **persis** kondisi pemicunya. Dan mode
kegagalannya adalah yang terburuk: kelihatan seperti sukses. Exit code 0, stdout kosong.
Kalau `ProviderHost` naif, kita akan menyimpulkan "AI menjawab string kosong" dan bukan
"integrasi rusak".

**Rujukan.** Paket PyPI `agy-headless-bridge` ada khusus untuk mengatasi ini. Deskripsinya
menyebut `agy` "goes silent — no output, no error, exit 0" ketika stdout bukan terminal, dan
merujuknya sebagai "upstream bug #76". Catatan: halaman itu tidak memberi URL issue tracker
langsung, jadi nomor bug itu sendiri belum terverifikasi.

**Solusi: ConPTY.** Alokasikan pseudo-terminal, jalankan `agy` terhubung ke situ, sehingga
`agy` melihat TTY dan mengeluarkan output.

- Di Windows ini berarti **ConPTY** (`CreatePseudoConsole` / `ClosePseudoConsole` di
  `kernel32`, plus `InitializeProcThreadAttributeList` dengan atribut
  `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE_ATTRIBUTE` = `0x00020016`). Tersedia sejak
  Windows 10 1809.
- Bridge Python yang jadi rujukan memakai `pywinpty` di Windows dan modul `pty` stdlib di
  Unix. Kita di C#, jadi kita panggil ConPTY langsung — atau, kalau mau menghemat waktu di
  Fase 2, pakai sidecar Python dengan `pywinpty` sebagai jembatan sementara dan ganti ke
  ConPTY native nanti.
- Output pseudo-terminal mengandung escape sequence ANSI, repaint carriage-return, glyph
  spinner, dan box-drawing. **Semua harus dibersihkan** sebelum di-parse.

**Aturan yang tidak boleh dilanggar:**

> `exit code 0` dengan stdout kosong **bukan** sukses. Klasifikasikan sebagai
> `provider_silent` dan tampilkan sebagai kegagalan integrasi ke pengguna — bukan sebagai
> jawaban AI yang kosong.

Kelas error `provider_silent` di `RecoveryAdvisor` ada khusus untuk ini.

---

## 2. Mode headless dan format output

Pola yang dilaporkan secara luas:

```
agy -p "<prompt>" --output-format json
```

- `-p` (alias `--print` / `--prompt`) menjalankan satu prompt non-interaktif lalu keluar.
- `--output-format` menerima `text` (default), `json`, atau `stream-json`.
- `--json-schema` dilaporkan memaksa output mengikuti skema yang kita berikan, hasilnya di
  bawah field `.structured_output`.

**Jebakan parsing argumen.** Satu README integrasi melaporkan bahwa
`agy -p --output-format stream-json` ditolak karena `-p` "menelan" flag berikutnya. Jadi
**prompt harus langsung menyusul `-p`**:

```
agy -p "<prompt>" --output-format stream-json     # benar
agy -p --output-format stream-json "<prompt>"     # salah
```

**Bentuk envelope JSON** dilaporkan berisi satu objek dengan `status`, `duration_seconds`,
`num_turns`, dan objek `usage` dengan hitungan token input / output / thinking / cached.
Nama field untuk isi jawaban dilaporkan sebagai `.response`, tapi **ini tidak
terkonfirmasi** oleh dokumentasi resmi. Jangan hardcode tanpa verifikasi.

`stream-json` mengeluarkan event JSONL. Untuk kita ini **lebih disukai** daripada `json`
tunggal: chat di TaskPane bisa menampilkan progres bertahap dan jejak pemanggilan tool,
bukan diam lalu tiba-tiba selesai. Itu perbedaan besar dalam rasa pemakaian.

**Rencana fallback berlapis:**

1. `--output-format stream-json` — pilihan utama, progres inkremental
2. `--output-format json` — kalau stream tidak didukung versi terpasang
3. `text` + pembersihan ANSI — jalur terakhir, parsing paling rapuh

---

## 3. Dukungan MCP client — kabar baik

Antigravity **bisa** menjadi MCP client. Ini yang membuat seluruh rancangan
"add-in sebagai MCP server" layak.

Lokasi konfigurasi yang didokumentasikan Google saat ini:

- per proyek: `.agents/mcp_config.json`
- global: `~/.gemini/config/mcp_config.json`

Catatan: halaman Google yang lebih lama menyebut `~/.gemini/antigravity/mcp_config.json`,
jadi path ini pernah berubah. Installer kita harus mendeteksi, bukan mengasumsikan.

Dua bentuk entry didukung:

- **Server lokal (stdio):** format `mcpServers` dengan `command` dan `args`.
- **Server remote (HTTP):** memakai field `serverUrl`.

Kita memakai bentuk **`serverUrl`**, karena server MCP kita hidup di dalam `SLDWORKS.exe`
dan tidak bisa dispawn oleh `agy`.

**Kita menulis konfigurasi per-proyek, bukan global.** Add-in memiliki direktori kerjanya
sendiri dan men-spawn `agy` dengan cwd diarahkan ke situ:

```
%LOCALAPPDATA%\MikosAI\agent-workspace\
└── .agents\
    └── mcp_config.json      <- ditulis ulang setiap ConnectToSW
```

```json
{
  "mcpServers": {
    "mikosai-solidworks": {
      "serverUrl": "http://127.0.0.1:<port-sesi-ini>/mcp"
    }
  }
}
```

Alasannya: konfigurasi global `~/.gemini/config/mcp_config.json` adalah file pribadi
pengguna. Kalau kita menulis ke sana, kita berisiko menimpa MCP server lain yang mereka
pakai untuk pekerjaan sendiri, dan uninstall jadi kotor. Dengan folder milik sendiri, kita
menulis bebas setiap sesi — dan karena itu **port bisa ephemeral**, tidak perlu angka tetap.

Dua hal yang perlu diverifikasi (item 6 di bagian 7):

1. Apakah `agy` benar-benar membaca `.agents/mcp_config.json` relatif ke cwd yang kita
   tentukan, bukan relatif ke lokasi lain.
2. Apakah konfigurasi per-proyek menggantikan atau digabung dengan yang global.

Kalau ternyata hanya konfigurasi global yang dibaca, fallback-nya adalah **merge
non-destruktif**: baca file yang ada, tambahkan entri kita, tulis kembali. Jangan pernah
menimpa entri milik pengguna.

Autentikasi bearer token juga perlu diverifikasi: apakah entry `serverUrl` Antigravity
mendukung custom header. Kalau tidak, alternatifnya token di query string (kurang baik tapi
dapat diterima untuk loopback-only) atau pembuktian kepemilikan lewat file sesi ber-ACL.

---

## 4. Drift versi flag: perlakukan sebagai normal

Satu komentar di DEV Community melaporkan build `agy` yang **menolak** `--output-format`
dengan "flags provided but not defined", dan help text-nya tidak memuat flag itu sama
sekali. Build yang sama memuat `--dangerously-skip-permissions`, `--continue`, dan
`--conversation`.

Artinya set flag berbeda antarrilis. Jangan hardcode.

**Yang kita lakukan: flag probing saat startup.**

```
1. Jalankan `agy --version`     -> catat versi
2. Jalankan `agy --help`        -> parse flag yang benar-benar ada
3. Simpan ke ProviderDescriptor -> ToolExecutor pilih flag dari situ
4. Pin versi yang sudah diuji di capabilities.yaml
```

Probing ini sekali per sesi, murah, dan menghilangkan seluruh kelas bug "jalan di laptopku".

---

## 5. Autentikasi

Dua jalur, dengan konsekuensi berbeda:

| Jalur | Cara | Konsekuensi |
|---|---|---|
| **OAuth interaktif** | Sign in sekali lewat browser; run headless berikutnya memakai credential yang di-cache | Dipakai subscription. **Tidak bisa** dijalankan headless untuk sign-in pertama. |
| **API key** | Set `ANTIGRAVITY_API_KEY` | Bisa sepenuhnya unattended, tapi **ditagih ke API key, bukan ke subscription Antigravity** |

Untuk kita: jalur **OAuth interaktif** yang tepat. Pengguna sudah duduk di depan SolidWorks;
sign-in sekali lewat browser bukan beban. Add-in hanya perlu mendeteksi keadaan belum
terautentikasi dan memandu, bukan menyembunyikannya.

Run headless yang belum terautentikasi dilaporkan **gagal dengan error authentication**,
bukan menggantung. Itu bagus — kita bisa membedakannya dari bug silent TTY.

Dukungan `ANTIGRAVITY_API_KEY` tetap kita sediakan sebagai opsi untuk pengguna yang mau
otomasi batch, dengan peringatan penagihan yang jelas di UI.

---

## 6. Perizinan shell di mode headless

Dilaporkan bahwa perintah shell **otomatis ditolak** di mode headless kecuali ada allow-rule
di bawah `permissions.allow` pada `~/.gemini/antigravity-cli/settings.json`.

Untuk kita dampaknya kecil — dan itu memang **disengaja**. Operasi CAD kita diekspos lewat
tool MCP, bukan lewat shell. AI tidak pernah perlu izin shell untuk membangun geometri.
Kalau suatu saat kita merasa butuh shell, itu tanda rancangan tool kita kurang lengkap.

---

## 7. Daftar verifikasi terbuka

Harus dijawab di Fase 2, dengan `agy` yang benar-benar terpasang. Semuanya memblokir
implementasi `ProviderHost` final.

| # | Pertanyaan | Kalau jawabannya buruk |
|---|---|---|
| 1 | Berapa versi `agy` terpasang, dan flag apa yang ada di `--help`? | Sesuaikan command builder |
| 2 | Apakah bug silent non-TTY masih ada di versi itu? | Kalau sudah ditambal, ConPTY tidak perlu — tapi tetap pertahankan deteksi `provider_silent` |
| 3 | Apa nama field sebenarnya untuk isi jawaban di envelope `json`? | Perbaiki parser |
| 4 | Apakah `stream-json` didukung, dan apa bentuk event-nya? | Turun ke `json` tunggal, UI kehilangan progres bertahap |
| 5 | Apakah entry `serverUrl` mendukung custom header untuk bearer token? | Pakai token query string atau pembuktian via file sesi ber-ACL |
| 6 | Apakah `.agents/mcp_config.json` dibaca relatif ke cwd, dan menggantikan atau menggabung dengan config global? | Fallback ke merge non-destruktif pada config global |
| 7 | Berapa rate limit nyata untuk pemakaian interaktif? | Tambah caching lokal, batch permintaan, atau sarankan provider lain |
| 8 | Apakah ToS mengizinkan orkestrasi eksternal `agy` dengan pemakaian berbasis akun? | **Lihat di bawah — ini risiko legal, bukan teknis** |

### Catatan soal item 8

Riset menemukan thread di Google AI Developers Forum berjudul
*"Is external orchestration of Antigravity CLI headless mode supported with account-based
usage?"* — pertanyaan yang persis menggambarkan apa yang akan kita lakukan, dan **belum
ditemukan jawaban resminya**.

Ini risiko nyata, dan bukan risiko yang bisa diselesaikan dengan kode. Tiga mitigasi:

1. Abstraksi provider dibangun sejak hari pertama, sehingga pindah provider itu pekerjaan
   satu hari, bukan rewrite.
2. Jalur `ANTIGRAVITY_API_KEY` disediakan — pemakaian API key komersial jauh lebih jelas
   posisinya daripada orkestrasi subscription konsumen.
3. Sebelum distribusi publik, cek ToS Antigravity yang berlaku saat itu. Untuk pemakaian
   internal dua orang, risikonya rendah; untuk produk yang dibagikan, ini harus dijawab.

---

## 8. Kontrak abstraksi provider

Antigravity adalah implementasi **pertama**, bukan satu-satunya. Interface-nya sengaja
dibuat sempit:

```csharp
public interface IAgentProvider
{
    string Id { get; }                  // "antigravity"
    ProviderState State { get; }
    Task<ProviderProbe> ProbeAsync(CancellationToken ct);
    ProviderCommand BuildCommand(AgentRequest request);
    IAsyncEnumerable<AgentEvent> ParseOutput(Stream stdout, CancellationToken ct);
}
```

Per provider yang berbeda hanya **dua hal**: cara menyusun command, dan cara mem-parse
output. Segala hal lain — policy, ledger, verifikasi, trace, UI — dibagi bersama.

Hasil akhir dinormalkan ke satu bentuk, apa pun providernya:

```json
{
  "summary": "apa yang dikerjakan putaran ini",
  "plan": { "...DesignPlan..." },
  "changedFiles": [],
  "verification": [],
  "risks": [],
  "nextSteps": []
}
```

Kandidat provider alternatif, urut berdasarkan kemiripan pola pemanggilan: Claude Code
(`-p ... --output-format json`), Codex CLI, Gemini CLI. Ketiganya mengikuti pola
prompt-sekali-headless yang sama, jadi penggantian sungguh-sungguh murah.

---

## 9. Sumber

Seluruh temuan di dokumen ini berasal dari sumber pihak ketiga di bawah. Tidak ada
dokumentasi resmi Google untuk flag CLI yang berhasil ditemukan saat riset.

- [Is external orchestration of Antigravity CLI headless mode supported with account-based usage? — Google AI Developers Forum](https://discuss.ai.google.dev/t/is-external-orchestration-of-antigravity-cli-headless-mode-supported-with-account-based-usage/183051)
- [agy-headless-bridge 1.2.1 — PyPI](https://pypi.org/project/agy-headless-bridge/1.2.1/) — sumber temuan bug silent non-TTY dan solusi ConPTY
- [Antigravity CLI Cheat Sheet: agy Commands and Flags — computingforgeeks](https://computingforgeeks.com/antigravity-cli-cheat-sheet/)
- [Antigravity CLI (agy) Cheat Sheet & Quick Reference — cheatsheets.zip](https://cheatsheets.zip/antigravity-cli)
- [Antigravity CLI: A Hands-On Guide to Google's Terminal Coding Agent — DEV Community](https://dev.to/arindam_1729/antigravity-cli-a-hands-on-guide-to-googles-terminal-coding-agent-5bc7) — termasuk komentar soal flag yang berbeda antarbuild
- [Introducing the Antigravity CLI (agy) — Leanpub](https://leanpub.com/read/solo-ai/introducing-the-antigravity-cli-agy-your-ai-agent-in-the-terminal)
- [Antigravity Agent — Ralph TUI docs](https://ralph-tui.com/docs/plugins/agents/antigravity)
- [antigravity cli setup — AgentWorks wiki](https://github.com/manishiitg/agentworks/wiki/antigravity-cli-setup) — soal API key untuk pemakaian unattended
- [Configure the Google Workspace MCP servers — Google for Developers](https://developers.google.com/workspace/guides/configure-mcp-servers)
- [Integrate custom MCP server in Google's Antigravity — Medium](https://kundan-kumar.medium.com/integrate-custom-mcp-server-in-googles-antigravity-6f74bc6b95ef)
- [Google Antigravity — Wikipedia](https://en.wikipedia.org/wiki/Google_Antigravity) — tanggal rilis CLI dan hubungannya dengan Gemini CLI
- [antigravity-intern — PyPI](https://pypi.org/project/antigravity-intern/) — contoh menjembatani `agy` sebagai sub-agent
