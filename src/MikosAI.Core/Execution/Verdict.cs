using System;

namespace MikosAI.Core.Execution
{
    /// <summary>
    /// Hasil verifikasi. Urutan keparahan: BLOCKED &gt; FAIL &gt; WARN &gt; PASS.
    /// Nilai enum sengaja diurutkan naik sesuai keparahan supaya agregasi
    /// bisa memakai perbandingan biasa.
    /// </summary>
    public enum Verdict
    {
        /// <summary>Semua pemeriksaan lulus.</summary>
        Pass = 0,

        /// <summary>Lulus dengan catatan. Belum tentu boleh dianggap selesai.</summary>
        Warn = 1,

        /// <summary>Pemeriksaan gagal. Tugas tidak berhasil.</summary>
        Fail = 2,

        /// <summary>
        /// Tidak bisa ditentukan, atau terhalang kebijakan. Ini juga tujuan
        /// default untuk status yang tidak dikenali - tidak pernah Pass.
        /// </summary>
        Blocked = 3,
    }

    /// <summary>Normalisasi string status dari sumber luar menjadi <see cref="Verdict"/>.</summary>
    public static class VerdictParser
    {
        /// <summary>
        /// Menerjemahkan status apa pun menjadi Verdict.
        ///
        /// ATURAN YANG TIDAK BOLEH DILANGGAR: apa pun yang tidak dikenali
        /// menjadi <see cref="Verdict.Blocked"/>, bukan Pass. Status yang tidak
        /// kita mengerti berarti kita tidak tahu apakah tugasnya berhasil, dan
        /// "tidak tahu" bukan "berhasil".
        /// </summary>
        public static Verdict Parse(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Verdict.Blocked;
            }

            switch (status!.Trim().ToLowerInvariant())
            {
                case "pass":
                case "passed":
                case "ok":
                case "success":
                    return Verdict.Pass;

                // review_required lulus secara teknis tapi menyisakan kewajiban
                // manusia. Itu WARN, dan manualReviewRequired yang menahan
                // penyelesaiannya - lihat VerificationGate.
                case "warn":
                case "warning":
                case "review_required":
                    return Verdict.Warn;

                case "fail":
                case "failed":
                case "error":
                    return Verdict.Fail;

                case "blocked":
                case "block":
                    return Verdict.Blocked;

                default:
                    return Verdict.Blocked;
            }
        }

        /// <summary>Bentuk kanonik untuk serialisasi dan trace.</summary>
        public static string ToWire(Verdict verdict)
        {
            switch (verdict)
            {
                case Verdict.Pass: return "PASS";
                case Verdict.Warn: return "WARN";
                case Verdict.Fail: return "FAIL";
                case Verdict.Blocked: return "BLOCKED";
                default: throw new ArgumentOutOfRangeException(nameof(verdict));
            }
        }
    }
}
