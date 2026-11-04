# Integrasi Antigravity CLI (`agy`)

Status: draft v0.1 · Riset dilakukan 2026-10-31

**Baca dokumen ini sebelum menulis satu baris pun `ProviderHost`.** Ada satu mode kegagalan
yang akan membuat kita menghabiskan berhari-hari kalau tidak diketahui lebih dulu.

---

## 0. Status: TERVERIFIKASI di mesin nyata

Dokumen ini semula berisi asumsi dari riset web pihak ketiga. Pada **2026-11-01**
seluruhnya diverifikasi langsung terhadap **agy 1.4.11/1.4.12** di mesin bermesin
SolidWorks 2025.

| # | Pertanyaan | Jawaban |
|---|---|---|
| 1 | Versi dan flag | **1.4.12.** `-p`/`--print`/`--prompt`, `--output-format`, `--json-schema`, `--print-timeout`, `--model`, `--mode`, `--effort`, `--dangerously-skip-permissions` |
| 2 | Bug stdout non-TTY | **TIDAK ADA.** Output lewat pipe jalan sempurna |
| 3 | Nama field jawaban | **`response`** |
| 4 | `stream-json` | **Didukung.** `text_delta` untuk progres, `event: "result"` untuk hasil akhir |
| 5 | Custom header untuk bearer token | **Didukung**, lewat `agy mcp add --header` |
| 6 | Lokasi `mcp_config.json` | **Tidak relevan** — pakai `agy mcp add` |
| 7 | Rate limit | Belum diuji sampai batas. Satu permintaan: ~4 detik, ~6.8k token input |
| 8 | ToS orkestrasi eksternal | **Masih belum terjawab.** Risiko legal, bukan teknis |

Tujuh dari delapan terjawab, dan dua di antaranya **menyederhanakan rancangan
secara signifikan** — lihat bagian 1 dan 3.

---

## 1. ConPTY TIDAK diperlukan

Rancangan awal mewajibkan ConPTY karena `agy -p` dilaporkan diam total ketika
stdout bukan terminal — tanpa output, tanpa error, exit 0. Itu risiko R1, dan
disebut "temuan paling penting dari seluruh riset".

**Laporan itu tidak berlaku untuk 1.4.11.** Diuji dengan output diarahkan ke
pipe:

```
exit code: 0
panjang output: 251 karakter
{"conversation_id":"1e58604b-...","status":"SUCCESS","response":"merah\n",
 "duration_seconds":3.94,"num_turns":1,
 "usage":{"input_tokens":6828,"output_tokens":6,"total_tokens":6834}}
```

Jadi pipe biasa cukup, dan seluruh kerumitan ConPTY — `CreatePseudoConsole`,
pembersih ANSI, repaint carriage-return — tidak diperlukan.

**Yang TETAP dipertahankan:** deteksi `provider_silent`. Exit code 0 dengan
stdout kosong tetap diperlakukan sebagai kegagalan integrasi, bukan jawaban
kosong. Alasannya: laporan bug itu nyata untuk versi tertentu, dan kalau muncul
lagi di rilis berikutnya kita akan mengenalinya dalam hitungan detik alih-alih
berhari-hari. Pembersih ANSI juga dipertahankan — murah, dan tidak merusak apa
pun kalau keluarannya sudah bersih.

Risiko R1 turun dari **Kritis** menjadi **rendah**.

---

## 1b. agy tidak selalu ada di PATH

Ditemukan dengan cara yang mahal. Di mesin uji pertama, `agy --version` gagal
dengan `CommandNotFoundException` — padahal agy **sudah terpasang dan sudah
login**.

Installer menaruhnya di:

```
%USERPROFILE%\.gemini\bin\agy.exe
```

dan hanya menambahkannya ke PATH kalau pengguna menjalankan `agy install`.

Lebih buruk lagi untuk kita: add-in berjalan di dalam `SLDWORKS.exe`, yang bisa
mewarisi PATH berbeda dari terminal pengguna. Jadi deteksi berbasis PATH saja
**tidak cukup**, dan urutan pencariannya:

1. `MIKOSAI_AGY_PATH` (override)
2. `%USERPROFILE%\.gemini\bin\agy.exe`
3. PATH

Berkas lain yang relevan di `~/.gemini/`: `oauth_creds.json` (bukti sudah login),
`antigravity-cli/` (settings), `bin/`.

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

## 3. Registrasi MCP: pakai `agy mcp add`, bukan berkas konfigurasi

Rancangan awal menulis `.agents/mcp_config.json` sendiri, karena format dan
lokasi berkasnya tidak pasti. **Itu tidak perlu.** agy punya subcommand khusus:

```
agy mcp add [flags] <name> <commandOrUrl> [args...]

  --type    stdio atau http (default stdio; URL http/https dideteksi otomatis)
  --header  HTTP header "Key: Value" (repeatable)
  --env     KEY=value (repeatable)

Contoh resmi:
  agy mcp add --header "Authorization: Bearer TOKEN" api https://example.com/mcp
```

Untuk kita:

```
agy mcp add --header "Authorization: Bearer <token-sesi>" \
    mikosai-solidworks http://127.0.0.1:<port>/mcp
```

Ini menyelesaikan **dua** pertanyaan terbuka sekaligus:

- **Custom header didukung**, jadi bearer token dipakai sebagaimana mestinya.
  Fallback token-di-query-string tidak diperlukan.
- **Tidak ada format berkas yang perlu ditebak**, dan tidak ada risiko merusak
  konfigurasi pengguna karena kita memakai jalur resmi.

### Harga yang dibayar, dan mitigasinya

`agy mcp add` menulis ke konfigurasi **global** pengguna, bukan ke workspace
milik kita. Itu mengorbankan sebagian alasan `AgentWorkspace` ada.

Mitigasinya:

- Nama server khusus: `mikosai-solidworks`. Tidak mungkin bertabrakan.
- `add` dijalankan setiap `ConnectToSW` — dokumentasinya menyebut
  "Add or **update**", jadi entri lama otomatis tertimpa dan port ephemeral
  tetap aman.
- `agy mcp remove mikosai-solidworks` dijalankan saat `DisconnectFromSW`.
- Kalau SolidWorks crash, entri basi menunjuk port mati — tapi `add` berikutnya
  memperbaikinya, dan `agy mcp list` memperlihatkannya.

`AgentWorkspace` tetap dipakai sebagai **cwd** saat men-spawn agy, karena `init`
event melaporkan `cwd` dan agy memakainya sebagai konteks proyek.

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

## 6. Perizinan di mode headless — TOOL MCP JUGA KENA

**Dugaan awal di dokumen ini salah, dan salahnya ke arah yang melegakan diri sendiri.**

Yang tertulis sebelumnya: perizinan headless hanya soal perintah shell, dan "untuk kita
dampaknya kecil — operasi CAD kita diekspos lewat tool MCP, bukan lewat shell".

Pemanggilan `agy` pertama membantahnya:

```
jetski: no output produced — a tool required the "mcp" permission that headless mode
cannot prompt for, so it was auto-denied.
```

```json
"status": "SUCCESS", "response": "", "denied_actions": [{"action": "mcp", "display_name": "CallMcpTool"}]
```

Tool MCP punya kelas izinnya **sendiri**, dan di mode `-p` ia otomatis ditolak karena
tidak ada yang bisa ditanyai. Jadi dampaknya bukan kecil — tanpa aturan izin, **tidak
satu pun tool kita bisa dipanggil**.

### Dua jebakan di dalamnya

**`status: SUCCESS` dan exit code 0 bukan bukti berhasil.** Jawaban di atas berstatus
SUCCESS dengan `response` kosong. Script verifikasi yang menilai dari status atau exit
code akan melaporkannya lulus. `denied_actions` adalah satu-satunya bukti yang bisa
dibaca mesin.

**Sintaksnya `mcp(<server>/<tool>)`, dengan garis miring.** `mcp(mikosai-solidworks)`
diterima sebagai JSON yang sah tapi tidak cocok dengan apa pun — tool-nya tetap ditolak,
tanpa keluhan soal aturannya. Bentuk yang benar diambil dari string di dalam `agy.exe`:

```
mcp(<server_name>/<tool_name>)
mcp(chrome_devtools/*)
mcp(*)
```

### Cara kita memberikannya

`--settings` menerima objek JSON setelan **untuk sesi itu saja, tidak pernah disimpan**:

```powershell
'{"permissions":{"allow":["mcp(mikosai-solidworks/*)"]}}'
```

Tiga keputusan di situ, masing-masing karena alasannya:

| Keputusan | Alasan |
|---|---|
| `--settings`, bukan mengedit `~/.gemini/settings.json` | Setelan pengguna tidak tersentuh |
| Lingkup server kita, bukan `mcp(*)` | Izin yang lebih luas daripada yang diuji membuat hasilnya tidak mewakili pemakaian nyata |
| **BUKAN** `--dangerously-skip-permissions` | Itu menyetujui semua tool termasuk shell |

Dua jebakan teknis saat meneruskannya, keduanya ditemukan dengan cara yang mahal:

- **Inline gagal.** PowerShell 5.1 melepas tanda kutip ganda saat meneruskan argumen ke
  exe native, jadi `{"permissions":...}` tiba sebagai `{permissions:...}`. `--settings`
  juga menerima path berkas, yang menghapus seluruh masalah itu.
- **BOM gagal.** `Set-Content -Encoding utf8` di PowerShell 5.1 menulis UTF-8 **dengan**
  BOM, dan parser JSON `agy` menolaknya dengan
  `invalid character '﻿' looking for beginning of value`. Pakai
  `UTF8Encoding($false)`.

Lihat `scripts/mcp-roundtrip.ps1`.

### Yang masih benar dari dugaan awal

AI tetap tidak perlu izin **shell** untuk membangun geometri. Kalau suatu saat kita merasa
butuh shell, itu tanda rancangan tool kita kurang lengkap. Yang salah hanyalah anggapan
bahwa tool MCP bebas dari perizinan.

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
| 5 | ~~Apakah entry `serverUrl` mendukung custom header?~~ **YA** — field `headers` ada di pendaftaran, dan panggilan tool BERHASIL padahal server kita menolak token salah dengan 401. Jadi header-nya memang terkirim. | — |
| 6 | ~~Apakah `.agents/mcp_config.json` dibaca relatif ke cwd?~~ **TIDAK — DIABAIKAN SEPENUHNYA.** Server palsu ditaruh di `.agents/mcp_config.json` sebuah folder; `agy mcp list` dari folder itu tidak menampilkannya. Pendaftaran bersifat GLOBAL lewat `agy mcp add`, terlihat dari direktori mana pun. | Lihat catatan di bawah — ini punya dua konsekuensi |
| 7 | Berapa rate limit nyata untuk pemakaian interaktif? | Tambah caching lokal, batch permintaan, atau sarankan provider lain |
| 8 | Apakah ToS mengizinkan orkestrasi eksternal `agy` dengan pemakaian berbasis akun? | **Lihat di bawah — ini risiko legal, bukan teknis** |

### Catatan soal item 6 — dua konsekuensi yang tidak enak

Jawabannya bukan sekadar "oh, pakai yang global saja".

**Pertama: pendaftaran kita terlihat oleh SEMUA sesi `agy` di mesin ini.**

`~/.gemini/config/mcp_config.json` memuat `serverUrl` kita beserta bearer token-nya.
Selama add-in berjalan, sesi `agy` apa pun — di proyek apa pun, tidak ada hubungannya
dengan SolidWorks — bisa memanggil tool CAD kita.

Ini **bukan** peningkatan hak akses: server hanya bind loopback, dan sesi itu berjalan
sebagai pengguna yang sama. Tapi ia lebih luas daripada yang dirancang, dan itu harus
dinyatakan, bukan dibiarkan jadi kejutan. Peredamnya: entry-nya dihapus saat
`DisconnectFromSW` lewat `agy mcp remove`.

**Kedua: `AgentWorkspace.Prepare` menulis token ke berkas yang TIDAK ADA PEMBACANYA.**

`.agents/mcp_config.json` dibuat untuk dibaca `agy`. Terbukti `agy` mengabaikannya. Jadi
yang tersisa hanyalah satu salinan bearer token di disk, tanpa manfaat apa pun — dan
secret tanpa pembaca adalah risiko murni.

Berbeda dari `session.json`, yang juga memuat token tapi **punya** pembaca
(`scripts/mcp-roundtrip.ps1`) dan ditulis dengan ACL khusus pengguna.

**Sudah diperbaiki.** Penulisan `.agents/mcp_config.json` dihapus, bersama
`BuildMcpConfig`, `WriteMcpConfig`, dan `McpConfigPath`. Di dalam workspace kita, token
sekarang hanya ada di `session.json`, dan ada test yang mencari token itu di **seluruh**
berkas workspace — jadi salinan baru akan langsung merah, bukan diam-diam bertambah.

Lihat gerbang terkait di `docs/ROADMAP.md`.

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
