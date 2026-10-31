using System;
using System.Collections.Generic;

namespace MikosAI.Core.Execution
{
    /// <summary>Klasifikasi error. Menentukan tindakan yang diusulkan RecoveryAdvisor.</summary>
    public enum ErrorKind
    {
        /// <summary>Gangguan sementara. Satu-satunya kelas yang boleh retry.</summary>
        Transient,

        /// <summary>Argumen salah. Model perlu menyusun ulang rencananya.</summary>
        InvalidArgument,

        /// <summary>Dependensi lingkungan tidak ada. Butuh tindakan manusia.</summary>
        EnvironmentMissing,

        /// <summary>Tool tidak tersedia, tapi kemampuannya ada. Coba komposisi tool lain.</summary>
        ToolUnavailable,

        /// <summary>Backend tidak tersedia.</summary>
        BackendUnavailable,

        /// <summary>Operasi terlaksana tapi verifikasi gagal. Jangan retry - hasilnya akan sama.</summary>
        VerificationFailed,

        /// <summary>Manusia harus melakukan sesuatu dulu.</summary>
        UserActionRequired,

        /// <summary>Kebijakan memblokir. TIDAK BOLEH dilewati pemulihan.</summary>
        PolicyBlocked,

        /// <summary>Kemampuannya memang belum ada. Kembalikan kendali ke agent di atas.</summary>
        CapabilityGap,

        /// <summary>
        /// Khusus provider CLI: exit code 0 dengan stdout kosong.
        /// Ini kegagalan integrasi, BUKAN jawaban AI yang kosong.
        /// Lihat docs/integrasi-antigravity.md bagian 1.
        /// </summary>
        ProviderSilent,

        /// <summary>Tidak diketahui atau tidak bisa dipulihkan. Tujuan default.</summary>
        Fatal,
    }

    /// <summary>Error terstruktur.</summary>
    public sealed class ToolError
    {
        public ErrorKind Kind { get; set; } = ErrorKind.Fatal;

        public string? Code { get; set; }

        /// <summary>
        /// Untuk UI Lapis 1: sebut tindakan konkret yang harus dilakukan manusia,
        /// bukan kode internal. Jargon seperti capability_gap tidak boleh sampai
        /// ke pengguna biasa.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>HRESULT COM mentah kalau ada, untuk diagnosis.</summary>
        public string? HResult { get; set; }
    }

    /// <summary>Bukti hasil akhir. Berbeda dari Trace, yang merekam proses.</summary>
    public sealed class ToolEvidence
    {
        public string? DocumentPath { get; set; }
        public string? SolidWorksRevision { get; set; }
        public IReadOnlyList<string>? FeatureNames { get; set; }
        public string? Sha256 { get; set; }
    }

    /// <summary>
    /// Envelope untuk setiap hasil tool.
    ///
    /// PERHATIKAN perbedaan dua properti ini, karena inilah prinsip inti proyek
    /// yang dipaksa masuk ke bentuk data:
    ///
    ///   Ok                  -> panggilan terlaksana tanpa exception
    ///   Verification.Verdict -> tugasnya benar-benar berhasil
    ///
    /// extrude_cut yang mengembalikan Ok=true tapi volumenya tidak berkurang
    /// adalah cut yang tidak memotong apa pun: Ok=true, Verdict=FAIL.
    ///
    /// Jangan pernah menyimpulkan keberhasilan tugas dari Ok. Pakai
    /// <see cref="IsTaskSuccessful"/>.
    /// </summary>
    public sealed class ToolResult
    {
        /// <summary>Panggilan terlaksana tanpa exception. BUKAN berarti tugas berhasil.</summary>
        public bool Ok { get; set; }

        /// <summary>Id di capabilities.yaml. Dipakai menegakkan gating.</summary>
        public string CapabilityId { get; set; } = string.Empty;

        public IReadOnlyDictionary<string, object?>? Data { get; set; }

        public VerificationResult? Verification { get; set; }

        public ToolError? Error { get; set; }

        public ToolEvidence? Evidence { get; set; }

        public long DurationMs { get; set; }

        /// <summary>
        /// Satu-satunya cara yang benar untuk menanyakan "apakah tugas ini berhasil".
        /// Butuh Ok, verifikasi ada, verdict Pass, dan review manual sudah selesai.
        /// Verifikasi yang null dianggap belum terbukti - bukan berhasil.
        /// </summary>
        public bool IsTaskSuccessful
            => Ok && Error is null && Verification is not null && Verification.MayBeMarkedComplete;

        public static ToolResult Failure(string capabilityId, ErrorKind kind, string message, string? code = null)
            => new ToolResult
            {
                Ok = false,
                CapabilityId = capabilityId,
                Error = new ToolError { Kind = kind, Message = message, Code = code },
            };
    }
}
