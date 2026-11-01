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
        ///
        /// PASS dan WARN sama-sama berarti operasinya BERHASIL. Bedanya, WARN
        /// menyisakan catatan yang perlu dilihat manusia.
        ///
        /// Itu bukan pelonggaran. Yang memisahkan "penting" dari "informasional"
        /// adalah flag <see cref="VerificationCheck.Required"/>, bukan tingkat
        /// verdict: pemeriksaan required yang gagal menghasilkan FAIL, dan
        /// pemeriksaan optional dibatasi maksimum WARN. Kalau WARN ikut
        /// memblokir penyelesaian, WARN jadi sama saja dengan FAIL dan dua
        /// tingkat itu tidak ada gunanya.
        ///
        /// Ini ditemukan lewat tool read-only: membaca kotak pembatas dokumen
        /// kosong menghasilkan WARN ("belum ada solid body"), dan dengan aturan
        /// lama itu dilaporkan ke AI sebagai ERROR - padahal pembacaannya
        /// sempurna. Satu verdict sedang menjawab dua pertanyaan berbeda:
        /// apakah operasinya berhasil, dan apakah modelnya sehat.
        ///
        /// Yang TIDAK dilonggarkan: FAIL, BLOCKED, dan status yang tidak
        /// dikenali tetap menahan penyelesaian, begitu juga
        /// <see cref="ManualReviewRequired"/>.
        /// </summary>
        public bool MayBeMarkedComplete
            => (Verdict == Verdict.Pass || Verdict == Verdict.Warn) && !ManualReviewRequired;
    }
}
