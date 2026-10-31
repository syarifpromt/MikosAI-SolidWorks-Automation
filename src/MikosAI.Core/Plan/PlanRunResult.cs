using System;
using System.Collections.Generic;
using MikosAI.Core.Execution;

namespace MikosAI.Core.Plan
{
    /// <summary>Kenapa sebuah plan tidak dijalankan sama sekali.</summary>
    public enum PlanRejectionReason
    {
        /// <summary>Tidak ditolak - plan dijalankan.</summary>
        None,

        /// <summary>Plan tidak lolos validasi struktur atau aturan semantik.</summary>
        Invalid,

        /// <summary>Ada tool yang kemampuannya belum verified atau pilot.</summary>
        ToolsNotAvailable,

        /// <summary>Ada pertanyaan terbuka yang blocking dan belum dijawab manusia.</summary>
        BlockingQuestions,

        /// <summary>Referensi parameter tidak bisa diselesaikan.</summary>
        ParameterResolution,
    }

    /// <summary>Hasil satu step.</summary>
    public sealed class StepOutcome
    {
        public string StepId { get; set; } = string.Empty;
        public string Tool { get; set; } = string.Empty;

        /// <summary>Hasil dari executor. Null kalau step tidak pernah dijalankan.</summary>
        public ToolResult? Result { get; set; }

        /// <summary>
        /// Verifikasi gabungan untuk step ini: milik executor, ditambah
        /// pemeriksaan terhadap blok `expect` di plan.
        /// </summary>
        public VerificationResult Verification { get; set; } = new VerificationResult();

        /// <summary>Usulan pemulihan kalau step ini gagal.</summary>
        public RecoveryDecision? Recovery { get; set; }

        /// <summary>Step dilewati karena run sudah dihentikan sebelumnya.</summary>
        public bool Skipped { get; set; }

        public long DurationMs { get; set; }
    }

    /// <summary>
    /// Hasil menjalankan satu DesignPlan.
    ///
    /// Perhatikan <see cref="Executed"/>: false berarti model TIDAK PERNAH
    /// disentuh. Itu jaminan penting - plan yang ditolak tidak meninggalkan
    /// dokumen setengah jadi.
    /// </summary>
    public sealed class PlanRunResult
    {
        public string RunId { get; set; } = string.Empty;

        /// <summary>
        /// false berarti plan ditolak sebelum step pertama, dan executor tidak
        /// pernah dipanggil sekali pun.
        /// </summary>
        public bool Executed { get; set; }

        public PlanRejectionReason RejectionReason { get; set; } = PlanRejectionReason.None;

        /// <summary>Pesan yang bisa ditampilkan ke pengguna Lapis 1.</summary>
        public string? RejectionMessage { get; set; }

        public PlanValidationResult? Validation { get; set; }

        public IReadOnlyList<StepOutcome> Steps { get; set; } = Array.Empty<StepOutcome>();

        /// <summary>Hasil pemeriksaan kriteria penerimaan tingkat plan.</summary>
        public IReadOnlyList<VerificationCheck> AcceptanceChecks { get; set; } = Array.Empty<VerificationCheck>();

        /// <summary>
        /// Putusan akhir, mengagregasi SEMUA step dan SEMUA kriteria acceptance.
        /// Satu FAIL yang required membuat seluruh run FAIL, walaupun step lain
        /// PASS.
        /// </summary>
        public VerificationResult Final { get; set; } = new VerificationResult();

        public string? DocumentPath { get; set; }

        public long DurationMs { get; set; }

        /// <summary>
        /// Satu-satunya cara yang benar untuk menanyakan "apakah run ini
        /// berhasil". Butuh plan benar-benar dijalankan, putusan akhir PASS, dan
        /// tidak ada kewajiban review yang menggantung.
        /// </summary>
        public bool IsSuccessful => Executed && Final.MayBeMarkedComplete;
    }
}
