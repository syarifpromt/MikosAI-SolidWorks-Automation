using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MikosAI.Core.Agents
{
    /// <summary>Hasil pemeriksaan provider di mesin ini.</summary>
    public sealed class ProviderProbe
    {
        public ProviderState State { get; set; } = ProviderState.NotInstalled;

        public string? Version { get; set; }

        public string? ExecutablePath { get; set; }

        /// <summary>
        /// Flag yang benar-benar ada di <c>--help</c> versi terpasang.
        ///
        /// Diperiksa, tidak diasumsikan: set flag `agy` dilaporkan berbeda
        /// antarrilis - ada build yang menolak <c>--output-format</c> dengan
        /// "flags provided but not defined". Lihat docs/integrasi-antigravity.md
        /// bagian 4.
        /// </summary>
        public IReadOnlyList<string> SupportedFlags { get; set; } = Array.Empty<string>();

        public string? Message { get; set; }

        /// <summary>
        /// Deteksi flag tidak menghasilkan apa pun padahal CLI-nya menjawab
        /// <c>--version</c>.
        ///
        /// Ini MUSTAHIL sebagai keadaan nyata: CLI tanpa satu pun flag tidak
        /// ada. Jadi artinya yang gagal adalah deteksinya, bukan kemampuan
        /// CLI-nya - dan itu wajib dibedakan dari "flag ini memang tidak
        /// didukung".
        ///
        /// Tanpa pembedaan itu, kegagalan deteksi menyamar sebagai CLI minimal:
        /// <c>--settings</c> tidak dikirim, agy menolak semua tool MCP di mode
        /// headless, dan yang terlihat pengguna hanyalah jawaban kosong. Itu
        /// persis gejala yang kami lacak lama sebelum stderr dibaca.
        /// </summary>
        public bool DeteksiFlagGagal => SupportedFlags.Count == 0;

        public bool Supports(string flag)
        {
            foreach (var item in SupportedFlags)
            {
                if (string.Equals(item, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Permintaan ke provider, dalam bentuk netral.</summary>
    public sealed class AgentRequest
    {
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Direktori kerja. Untuk Antigravity ini folder AgentWorkspace milik
        /// add-in.
        ///
        /// Dulu komentar di sini menyebut ".agents/mcp_config.json" sebagai
        /// alasannya. Berkas itu terbukti DIABAIKAN agy - pendaftaran MCP
        /// bersifat global lewat `agy mcp add` - jadi alasan cwd tinggal satu:
        /// menjaga proses anak tidak menulis apa pun ke folder repo pengguna.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Path berkas setelan sesi yang diteruskan lewat <c>--settings</c>.
        /// null berarti tidak diteruskan.
        ///
        /// Isinya aturan izin MCP, dan tanpanya SEMUA tool kita ditolak di mode
        /// headless - lihat AntigravityCommand.BuatArgumen.
        ///
        /// PATH BERKAS, BUKAN JSON INLINE. PowerShell 5.1 melepas tanda kutip
        /// ganda saat meneruskan argumen ke exe native, dan `agy` menolak hasil
        /// pelepasan itu dengan "invalid character 'p' looking for beginning of
        /// object key string". Melewatkan path menghapus seluruh persoalan
        /// kutip-mengutip, dan `--settings` memang menerima keduanya.
        ///
        /// Berkasnya TIDAK memuat secret apa pun - hanya aturan izin.
        /// </summary>
        public string? BerkasIzin { get; set; }

        public string? Model { get; set; }

        /// <summary>
        /// Minta keluaran bertahap kalau provider mendukungnya. Chat terasa jauh
        /// lebih hidup dengan progres daripada diam lalu tiba-tiba selesai.
        /// </summary>
        public bool PreferStreaming { get; set; } = true;
    }

    /// <summary>Perintah yang siap dijalankan.</summary>
    public sealed class ProviderCommand
    {
        public string Executable { get; set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

        public string WorkingDirectory { get; set; } = string.Empty;

        public IReadOnlyDictionary<string, string>? Environment { get; set; }

        /// <summary>
        /// Harus dijalankan lewat pseudo-terminal, bukan pipe biasa.
        ///
        /// `agy -p` dilaporkan tidak mengeluarkan apa pun ketika stdout bukan
        /// terminal: tanpa output, tanpa error, exit 0. Itu persis kondisi
        /// subprocess kita. Lihat docs/integrasi-antigravity.md bagian 1.
        /// </summary>
        public bool RequiresPseudoConsole { get; set; }

        /// <summary>Format keluaran yang diharapkan: text, json, atau stream-json.</summary>
        public string OutputFormat { get; set; } = "text";
    }

    /// <summary>Jenis peristiwa saat provider berjalan, untuk ditampilkan di chat.</summary>
    public enum AgentEventKind
    {
        /// <summary>Teks jawaban, bisa bertahap.</summary>
        Text,

        /// <summary>Provider memanggil sebuah tool.</summary>
        ToolCall,

        /// <summary>Catatan proses - bukan jawaban.</summary>
        Progress,

        /// <summary>Kegagalan.</summary>
        Failure,

        /// <summary>Selesai.</summary>
        Completed,
    }

    public sealed class AgentEvent
    {
        public AgentEventKind Kind { get; set; } = AgentEventKind.Text;
        public string? Text { get; set; }
        public string? ToolName { get; set; }

        public static AgentEvent OfText(string text) => new AgentEvent { Kind = AgentEventKind.Text, Text = text };
        public static AgentEvent OfProgress(string text) => new AgentEvent { Kind = AgentEventKind.Progress, Text = text };
        public static AgentEvent OfFailure(string text) => new AgentEvent { Kind = AgentEventKind.Failure, Text = text };
    }

    /// <summary>Kenapa sebuah permintaan gagal.</summary>
    public sealed class AgentFailure
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        /// <summary>Apa yang harus dilakukan manusia, kalau ada.</summary>
        public string? NextAction { get; set; }
    }

    /// <summary>
    /// Hasil akhir yang dinormalkan, sama bentuknya untuk provider apa pun.
    /// Lihat docs/adr/0003-abstraksi-provider.md.
    /// </summary>
    public sealed class AgentResult
    {
        public string? Summary { get; set; }

        /// <summary>DesignPlan kalau provider menghasilkannya.</summary>
        public JsonNode? Plan { get; set; }

        public IReadOnlyList<string> ChangedFiles { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Verification { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Risks { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> NextSteps { get; set; } = Array.Empty<string>();

        public AgentFailure? Failure { get; set; }

        public bool IsSuccessful => Failure is null;
    }

    /// <summary>
    /// Satu provider AI CLI.
    ///
    /// Permukaannya SENGAJA sempit. Yang benar-benar berbeda antarprovider hanya
    /// dua hal: cara menyusun perintah, dan cara mem-parse keluaran. Policy,
    /// ledger, verifikasi, trace, dan UI dibagi bersama.
    ///
    /// Antigravity adalah implementasi pertama, bukan satu-satunya. Lihat
    /// docs/adr/0003 untuk alasannya - termasuk alasan yang bukan teknis.
    /// </summary>
    public interface IAgentProvider
    {
        string Id { get; }

        string DisplayName { get; }

        /// <summary>Memeriksa keadaan provider di mesin ini. Tidak boleh melempar exception.</summary>
        Task<ProviderProbe> ProbeAsync(CancellationToken cancellationToken = default);

        /// <summary>Menyusun perintah. Harus menghormati flag yang benar-benar ada di probe.</summary>
        ProviderCommand BuildCommand(AgentRequest request, ProviderProbe probe);

        /// <summary>Menerjemahkan keluaran mentah menjadi hasil yang dinormalkan.</summary>
        AgentResult ParseResult(string stdout, int exitCode, ProviderProbe probe);
    }
}
