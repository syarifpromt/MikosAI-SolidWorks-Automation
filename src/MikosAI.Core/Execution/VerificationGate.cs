using System.Collections.Generic;
using System.Linq;

namespace MikosAI.Core.Execution
{
    /// <summary>
    /// Mengagregasi pemeriksaan verifikasi menjadi satu putusan.
    ///
    /// Ini mekanisme keamanan inti proyek. Dua aturan yang ditegakkan di sini:
    ///
    ///   1. BLOCKED &gt; FAIL &gt; WARN &gt; PASS. Yang terparah menang.
    ///   2. Tidak ada bukti bukan berarti lulus. Daftar pemeriksaan yang kosong
    ///      untuk operasi yang butuh verifikasi menghasilkan BLOCKED.
    ///
    /// Pemeriksaan yang ditandai optional dibatasi maksimum WARN, supaya
    /// pemeriksaan tambahan yang gagal tidak memblokir hasil yang sebenarnya
    /// benar. Pemeriksaan required dihitung penuh dan tidak bisa ditutupi.
    /// </summary>
    public static class VerificationGate
    {
        /// <summary>
        /// Menilai sekumpulan pemeriksaan.
        /// </summary>
        /// <param name="checks">Pemeriksaan yang sudah dijalankan.</param>
        /// <param name="manualReviewRequired">
        /// Dari capabilities.yaml. Kemampuan berstatus pilot selalu true.
        /// </param>
        /// <param name="requiresEvidence">
        /// true untuk operasi yang menulis. Kalau true dan tidak ada pemeriksaan
        /// sama sekali, hasilnya BLOCKED - kita tidak punya bukti apa pun bahwa
        /// operasinya berhasil. Tool read-only memakai false.
        /// </param>
        public static VerificationResult Evaluate(
            IReadOnlyList<VerificationCheck>? checks,
            bool manualReviewRequired = false,
            bool requiresEvidence = true)
        {
            var list = checks is null
                ? new List<VerificationCheck>()
                : checks.ToList();

            Verdict verdict;

            if (list.Count == 0)
            {
                // Tidak ada bukti. Untuk operasi tulis itu BLOCKED, bukan PASS -
                // "tidak diperiksa" tidak sama dengan "benar".
                verdict = requiresEvidence ? Verdict.Blocked : Verdict.Pass;
            }
            else
            {
                verdict = Verdict.Pass;

                foreach (var check in list)
                {
                    var effective = check.Required
                        ? check.Verdict
                        : Cap(check.Verdict, Verdict.Warn);

                    if (effective > verdict)
                    {
                        verdict = effective;
                    }
                }
            }

            return new VerificationResult
            {
                Verdict = verdict,
                Checks = list,
                ManualReviewRequired = manualReviewRequired,
            };
        }

        /// <summary>
        /// Menggabungkan beberapa hasil verifikasi, mis. seluruh step dalam satu
        /// DesignPlan. Satu FAIL yang required membuat seluruh run FAIL, walaupun
        /// step lain PASS.
        /// </summary>
        public static VerificationResult Combine(IReadOnlyList<VerificationResult> results)
        {
            if (results is null || results.Count == 0)
            {
                return new VerificationResult
                {
                    Verdict = Verdict.Blocked,
                    Checks = new List<VerificationCheck>(),
                    ManualReviewRequired = false,
                };
            }

            var verdict = Verdict.Pass;
            var allChecks = new List<VerificationCheck>();
            var manualReview = false;

            foreach (var result in results)
            {
                if (result.Verdict > verdict)
                {
                    verdict = result.Verdict;
                }

                manualReview |= result.ManualReviewRequired;
                allChecks.AddRange(result.Checks);
            }

            return new VerificationResult
            {
                Verdict = verdict,
                Checks = allChecks,
                ManualReviewRequired = manualReview,
            };
        }

        private static Verdict Cap(Verdict verdict, Verdict ceiling)
            => verdict > ceiling ? ceiling : verdict;
    }
}
