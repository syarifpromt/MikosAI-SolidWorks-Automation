using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MikosAI.Core.Tracing
{
    /// <summary>
    /// Penulis trace JSONL append-only.
    ///
    /// Trace adalah RIWAYAT PROSES. Evidence di ToolResult adalah FAKTA HASIL
    /// AKHIR. Keduanya tidak boleh dicampur.
    ///
    /// Dua jaminan yang ditegakkan di sini:
    ///
    ///   1. Nilai sensitif diredaksi. Hanya ringkasan argumen yang disimpan -
    ///      tidak pernah prompt penuh, API key, atau token.
    ///   2. Kegagalan menulis trace TIDAK menggagalkan eksekusi. Observabilitas
    ///      adalah jalur samping; kalau disk penuh, pekerjaan tetap jalan.
    /// </summary>
    public sealed class TraceWriter : IDisposable
    {
        /// <summary>Kunci yang isinya diredaksi, tanpa memandang huruf besar-kecil.</summary>
        private static readonly string[] SensitiveKeyFragments =
        {
            "prompt", "api_key", "apikey", "token", "secret", "password",
            "credential", "authorization", "bearer", "cookie",
        };

        /// <summary>Panjang maksimum satu nilai string di trace.</summary>
        private const int MaxValueLength = 200;

        private readonly object _lock = new object();
        private readonly string _path;
        private StreamWriter? _writer;
        private bool _disabled;

        public TraceWriter(string traceDirectory, string runId)
        {
            RunId = runId;
            _path = Path.Combine(traceDirectory, $"{runId}.jsonl");

            try
            {
                Directory.CreateDirectory(traceDirectory);
                _writer = new StreamWriter(
                    new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception)
            {
                // Tidak bisa menulis trace bukan alasan untuk menolak bekerja.
                _disabled = true;
            }
        }

        public string RunId { get; }

        public string Path_ => _path;

        /// <summary>Trace dinonaktifkan karena tidak bisa menulis. Eksekusi tetap sah.</summary>
        public bool IsDisabled => _disabled;

        /// <summary>
        /// Menulis satu event. Tidak pernah melempar exception.
        /// </summary>
        /// <param name="eventName">
        /// run.started, step.started, tool.called, tool.succeeded, tool.failed,
        /// verification.completed, recovery.decided, run.completed, run.failed,
        /// run.blocked.
        /// </param>
        public void Write(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
        {
            if (_disabled || _writer is null)
            {
                return;
            }

            try
            {
                var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ts"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["event"] = eventName,
                    ["runId"] = RunId,
                };

                if (fields is not null)
                {
                    foreach (var pair in fields)
                    {
                        payload[pair.Key] = Redact(pair.Key, pair.Value);
                    }
                }

                // Jejak ini adalah bukti audit. Angka di dalamnya harus
                // terbaca seperti yang dimaksud, bukan dengan ekor G17 yang
                // ditambahkan penulis JSON net48.
                var line = JsonSerializer.Serialize(payload, MikosAI.Core.Json.JsonAngka.Opsi);

                lock (_lock)
                {
                    _writer.WriteLine(line);
                    _writer.Flush();
                }
            }
            catch (Exception)
            {
                // Sekali gagal, berhenti mencoba - jangan membanjiri log dengan
                // kegagalan logging.
                _disabled = true;
            }
        }

        /// <summary>
        /// Meredaksi nilai berdasarkan nama kuncinya, lalu memotong yang terlalu
        /// panjang. Nama kunci yang mengandung penanda sensitif diganti penuh.
        /// </summary>
        internal static object? Redact(string key, object? value)
        {
            if (IsSensitive(key))
            {
                return "[redacted]";
            }

            switch (value)
            {
                case null:
                    return null;

                case string text:
                    return Truncate(text);

                case IReadOnlyDictionary<string, object?> nested:
                    var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var pair in nested)
                    {
                        copy[pair.Key] = Redact(pair.Key, pair.Value);
                    }
                    return copy;

                default:
                    return value;
            }
        }

        internal static bool IsSensitive(string key)
        {
            var lower = key.ToLowerInvariant();

            foreach (var fragment in SensitiveKeyFragments)
            {
                if (lower.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Truncate(string text)
            => text.Length <= MaxValueLength
                ? text
                : text.Substring(0, MaxValueLength) + $"... [{text.Length} karakter]";

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch (Exception)
                {
                    // Tidak ada yang bisa dilakukan saat menutup.
                }

                _writer = null;
                _disabled = true;
            }
        }
    }
}
