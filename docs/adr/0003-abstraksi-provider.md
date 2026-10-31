# ADR 0003 — Abstraksi provider sejak hari pertama

Tanggal: 2026-10-31 · Status: diterima

## Konteks

Antigravity CLI (`agy`) adalah provider AI pilihan untuk pengembangan awal. Jalan termudah
adalah memanggilnya langsung dari `ProviderHost` dan selesai.

Riset 2026-10-31 menemukan tiga hal yang mengubah perhitungan itu:

1. **Set flag berbeda antarrilis.** Ada build yang menolak `--output-format` dengan
   "flags provided but not defined", sementara sumber lain mendokumentasikannya sebagai
   flag standar.
2. **Tidak ada dokumentasi resmi Google yang berhasil ditemukan** untuk flag CLI. Semua
   pengetahuan kita berasal dari cheat sheet dan README pihak ketiga.
3. **Pertanyaan ToS belum terjawab.** Ada thread di Google AI Developers Forum berjudul
   "Is external orchestration of Antigravity CLI headless mode supported with account-based
   usage?" — menggambarkan persis apa yang akan kita lakukan, tanpa jawaban resmi yang
   ditemukan.

Poin 3 adalah risiko yang tidak bisa diselesaikan dengan kode.

## Keputusan

`ProviderHost` bekerja di balik interface `IAgentProvider`. Antigravity adalah implementasi
**pertama**, bukan satu-satunya. Interface-nya sengaja sempit:

```csharp
public interface IAgentProvider
{
    string Id { get; }
    ProviderState State { get; }
    Task<ProviderProbe> ProbeAsync(CancellationToken ct);
    ProviderCommand BuildCommand(AgentRequest request);
    IAsyncEnumerable<AgentEvent> ParseOutput(Stream stdout, CancellationToken ct);
}
```

Yang berbeda per provider hanya **dua hal**: cara menyusun command, dan cara mem-parse
output. Policy, ledger, verifikasi, trace, dan UI dibagi bersama.

Selain itu, **flag probing saat startup** wajib: jalankan `--version` dan `--help` sekali
per sesi, parse flag yang benar-benar ada, simpan di `ProviderDescriptor`. Jangan hardcode
flag.

## Alasan

Biaya abstraksi ini kecil — satu interface dengan dua method yang benar-benar bervariasi.
Biaya tidak melakukannya adalah rewrite kalau salah satu dari tiga risiko di atas terwujud.

Repo referensi sudah membuktikan pola ini bekerja: `agent_providers.py` mereka mendukung
Codex, Claude Code, Gemini CLI, dan OpenCode di balik satu interface, dan perbedaannya
benar-benar terbatas pada command builder dan result parser. Keempatnya mengikuti pola
prompt-sekali-headless yang sama.

Flag probing menghilangkan seluruh kelas bug "jalan di laptopku" dengan biaya satu
pemanggilan proses per sesi.

## Konsekuensi

- Satu lapisan tak langsung di `ProviderHost`. Dapat diterima.
- Penggantian provider menjadi pekerjaan satu hari, bukan rewrite.
- Status provider harus dibedakan ketat (`not_installed` sampai `execution_verified`) dan UI
  tidak boleh menyamakannya. CLI ada di PATH bukan bukti model bisa dipanggil.
- Jalur `ANTIGRAVITY_API_KEY` disediakan sebagai opsi, dengan peringatan jelas di UI bahwa
  penagihan masuk ke API key, bukan ke subscription.

Kandidat provider alternatif, urut berdasarkan kemiripan pola: Claude Code, Codex CLI,
Gemini CLI.

## Catatan

Kalau pertanyaan ToS di poin 3 terjawab dengan "tidak diizinkan", kita punya dua jalan
keluar yang sudah siap: pindah ke provider lain, atau beralih ke pemakaian berbasis API key
yang posisinya lebih jelas. Keduanya tersedia **karena** ADR ini, bukan sebagai improvisasi
saat krisis.

Untuk pemakaian internal dua orang, risikonya rendah. Untuk distribusi publik, pertanyaan
ini harus dijawab lebih dulu.
