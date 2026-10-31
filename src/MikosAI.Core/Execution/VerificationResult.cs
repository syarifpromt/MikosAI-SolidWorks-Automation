using System.Collections.Generic;

namespace MikosAI.Core.Execution
{
    /// <summary>Satu pemeriksaan verifikasi beserta bukti yang benar-benar dibaca dari model.</summary>
    public sealed class VerificationCheck
    {
        /// <summary>Mis. feature_exists_in_tree, rebuild_succeeds, save_reopen_dimension_readback.</summary>
        public string Id { get; set; } = string.Empty;

        public Verdict Verdict { get; set; } = Verdict.Blocked;

        /// <summary>
        /// Kalau true, FAIL di sini tidak boleh ditutupi oleh PASS tingkat atas.
        /// Pemeriksaan optional dibatasi maksimum WARN - lihat <see cref="VerificationGate"/>.
        /// </summary>
        public bool Required { get; set; } = true;

        /// <summary>Nilai yang benar-benar dibaca dari model. Bukti, bukan klaim.</summary>
        public object? Observed { get; set; }

        public object? Expected { get; set; }

        public string? Message { get; set; }

        public static VerificationCheck Pass(string id, object? observed = null, bool required = true)
            => new VerificationCheck { Id = id, Verdict = Verdict.Pass, Observed = observed, Required = required };

        public static VerificationCheck Fail(string id, string message, object? observed = null, object? expected = null, bool required = true)
            => new VerificationCheck { Id = id, Verdict = Verdict.Fail, Message = message, Observed = observed, Expected = expected, Required = required };

        public static VerificationCheck Blocked(string id, string message, bool required = true)
            => new VerificationCheck { Id = id, Verdict = Verdict.Blocked, Message = message, Required = required };
    }

    /// <summary>Hasil verifikasi gabungan untuk satu operasi.</summary>
    public sealed class VerificationResult
    {
        public Verdict Verdict { get; set; } = Verdict.Blocked;

        public IReadOnlyList<VerificationCheck> Checks { get; set; } = new List<VerificationCheck>();

        /// <summary>
        /// Selama ini true, tugas TIDAK boleh ditandai selesai - walaupun
        /// <see cref="Verdict"/> bernilai Pass.
        /// </summary>
        public bool ManualReviewRequired { get; set; }

        /// <summary>
        /// Satu-satunya tempat yang boleh menjawab "apakah tugas ini selesai".
        /// Sengaja dibuat sebagai properti supaya tidak ada yang menyimpulkan
        /// selesai hanya dari Verdict.
        /// </summary>
        public bool MayBeMarkedComplete => Verdict == Verdict.Pass && !ManualReviewRequired;
    }
}
