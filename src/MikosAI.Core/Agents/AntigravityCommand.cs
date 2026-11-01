using System;
using System.Collections.Generic;

namespace MikosAI.Core.Agents
{
    /// <summary>
    /// Menyusun argumen baris perintah untuk Antigravity CLI.
    ///
    /// Dipisah dari eksekusi proses supaya bisa diuji tanpa memasang CLI apa
    /// pun. Eksekusinya ada di MikosAI.Addin.
    ///
    /// Semua bentuk di sini DIVERIFIKASI terhadap agy 1.4.11 pada 2026-11-01,
    /// bukan diambil dari panduan pihak ketiga.
    /// </summary>
    public static class AntigravityCommand
    {
        /// <summary>Nama server MCP kita di konfigurasi agy. Harus khas.</summary>
        public const string NamaServerMcp = "mikosai-solidworks";

        /// <summary>
        /// Batas waktu satu permintaan. 0 berarti menunggu sampai selesai, dan
        /// itu berbahaya di dalam SolidWorks - permintaan yang menggantung akan
        /// menyisakan proses anak selamanya.
        /// </summary>
        public const int BatasWaktuDetik = 180;

        /// <summary>
        /// Menyusun argumen untuk satu permintaan.
        ///
        /// Urutan <c>-p &lt;prompt&gt;</c> lalu flag lain TERVERIFIKASI bekerja:
        /// perintah yang diuji persis
        /// <c>agy -p "..." --output-format json --print-timeout 90s</c>
        /// dan berhasil.
        ///
        /// Flag hanya dikirim kalau benar-benar ada di <c>--help</c> versi
        /// terpasang. Ada build agy yang menolak <c>--output-format</c>, dan
        /// mengirim flag tak dikenal menggagalkan seluruh permintaan.
        /// </summary>
        public static IReadOnlyList<string> BuatArgumen(AgentRequest request, ProviderProbe probe)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (probe is null) throw new ArgumentNullException(nameof(probe));

            var argumen = new List<string>();

            // Prompt langsung menyusul -p.
            argumen.Add("-p");
            argumen.Add(request.Prompt ?? string.Empty);

            if (probe.Supports("--output-format"))
            {
                argumen.Add("--output-format");
                argumen.Add(PilihFormat(request, probe));
            }

            if (probe.Supports("--print-timeout"))
            {
                argumen.Add("--print-timeout");
                argumen.Add($"{BatasWaktuDetik}s");
            }

            if (!string.IsNullOrWhiteSpace(request.Model) && probe.Supports("--model"))
            {
                argumen.Add("--model");
                argumen.Add(request.Model!);
            }

            return argumen;
        }

        /// <summary>
        /// Format keluaran: stream-json kalau bisa, karena hanya itu yang
        /// memberi progres bertahap lewat text_delta. Kalau tidak, json.
        /// Kalau tidak keduanya, text - dan parser kita tetap menanganinya.
        /// </summary>
        public static string PilihFormat(AgentRequest request, ProviderProbe probe)
        {
            if (!probe.Supports("--output-format"))
            {
                return "text";
            }

            return request.PreferStreaming ? "stream-json" : "json";
        }

        /// <summary>
        /// Argumen untuk mendaftarkan server MCP kita.
        ///
        /// Memakai jalur resmi <c>agy mcp add</c> alih-alih menulis berkas
        /// konfigurasi sendiri: formatnya tidak perlu ditebak, dan custom header
        /// didukung sehingga bearer token dipakai sebagaimana mestinya.
        ///
        /// Dokumentasinya menyebut "Add or update", jadi memanggil ini setiap
        /// sesi akan menimpa entri lama - penting karena port kita ephemeral.
        ///
        /// Flag HARUS mendahului nama server; agy menolak flag yang ditaruh
        /// setelahnya.
        /// </summary>
        public static IReadOnlyList<string> BuatArgumenDaftarMcp(Uri endpoint, string token)
        {
            if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

            if (!endpoint.IsLoopback)
            {
                // Jaring pengaman yang sama seperti di AgentWorkspace:
                // mendaftarkan URL non-loopback berarti mengekspos tool CAD
                // ke jaringan.
                throw new ArgumentException(
                    $"Hanya endpoint loopback yang boleh didaftarkan. Diterima: {endpoint}",
                    nameof(endpoint));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("Token sesi kosong.", nameof(token));
            }

            return new List<string>
            {
                "mcp",
                "add",
                "--header",
                $"Authorization: Bearer {token}",
                NamaServerMcp,
                endpoint.ToString(),
            };
        }

        public static IReadOnlyList<string> BuatArgumenHapusMcp()
            => new List<string> { "mcp", "remove", NamaServerMcp };

        public static IReadOnlyList<string> BuatArgumenVersi() => new List<string> { "--version" };

        public static IReadOnlyList<string> BuatArgumenBantuan() => new List<string> { "--help" };
    }
}
