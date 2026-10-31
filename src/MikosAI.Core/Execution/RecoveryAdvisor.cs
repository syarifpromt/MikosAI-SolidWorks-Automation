using System;
using System.Runtime.InteropServices;

namespace MikosAI.Core.Execution
{
    /// <summary>Tindakan yang diusulkan. Catat: diusulkan, bukan dijalankan.</summary>
    public enum RecoveryAction
    {
        /// <summary>Coba lagi operasi yang sama. Hanya untuk error transient, ada batasnya.</summary>
        Retry,

        /// <summary>Agent di atas perlu menyusun ulang rencana.</summary>
        Replan,

        /// <summary>Manusia harus bertindak dulu.</summary>
        UserActionRequired,

        /// <summary>Hentikan dan jangan lanjutkan. Tujuan default saat tidak yakin.</summary>
        Block,

        /// <summary>Gagal permanen.</summary>
        Fail,
    }

    public sealed class RecoveryDecision
    {
        public RecoveryAction Action { get; set; } = RecoveryAction.Block;
        public ErrorKind Kind { get; set; } = ErrorKind.Fatal;
        public string Reason { get; set; } = string.Empty;
        public int AttemptsUsed { get; set; }

        /// <summary>true berarti kendali dikembalikan ke agent di atas untuk mencari jalan lain.</summary>
        public bool AgentResolutionRequired { get; set; }
    }

    /// <summary>
    /// Mengklasifikasi kegagalan dan mengusulkan tindakan berikutnya.
    ///
    /// PENTING: Advisor hanya memutuskan, TIDAK mengeksekusi. Yang menjalankan
    /// tindakan adalah PlanRunner atau agent di atasnya. Pemisahan ini yang
    /// mencegah AI mengulang-ulang operasi yang merusak model.
    ///
    /// Default aman: tidak yakin berarti Block atau Fail, tidak pernah Retry.
    /// </summary>
    public static class RecoveryAdvisor
    {
        /// <summary>Batas retry. Sengaja kecil.</summary>
        public const int DefaultMaxRetries = 2;

        /// <summary>
        /// Klasifikasi deterministik dengan prioritas tetap:
        ///
        ///   1. ErrorKind yang sudah eksplisit di hasil
        ///   2. Verifikasi yang gagal
        ///   3. Tipe exception
        ///   4. Kata kunci di pesan
        ///   5. Tidak dikenal -> Fatal
        ///
        /// Urutannya tetap supaya hasilnya bisa diuji dan tidak berubah-ubah.
        /// </summary>
        public static ErrorKind Classify(ToolResult? result, Exception? exception = null)
        {
            // 1. Kalau pemanggil sudah menyatakan kelasnya, hormati.
            if (result?.Error is not null)
            {
                return result.Error.Kind;
            }

            // 2. Operasi terlaksana tapi verifikasi menolak. Retry tidak akan
            //    mengubah apa pun - hasilnya akan sama.
            if (result is not null && result.Ok && result.Verification is not null)
            {
                switch (result.Verification.Verdict)
                {
                    case Verdict.Fail:
                        return ErrorKind.VerificationFailed;
                    case Verdict.Blocked:
                        return ErrorKind.PolicyBlocked;
                }
            }

            // 3. Tipe exception.
            if (exception is not null)
            {
                var kind = ClassifyException(exception);
                if (kind.HasValue)
                {
                    return kind.Value;
                }

                var fromMessage = ClassifyMessage(exception.Message);
                if (fromMessage.HasValue)
                {
                    return fromMessage.Value;
                }
            }

            // 4. Kata kunci di pesan error.
            if (result?.Error?.Message is { Length: > 0 } message)
            {
                var fromMessage = ClassifyMessage(message);
                if (fromMessage.HasValue)
                {
                    return fromMessage.Value;
                }
            }

            // 5. Tidak dikenal. Jangan berasumsi bisa dipulihkan.
            return ErrorKind.Fatal;
        }

        /// <summary>
        /// Mengusulkan tindakan untuk sebuah kelas error.
        /// </summary>
        /// <param name="attemptsUsed">Berapa kali operasi ini sudah dicoba.</param>
        public static RecoveryDecision Decide(ErrorKind kind, int attemptsUsed = 0, int maxRetries = DefaultMaxRetries)
        {
            switch (kind)
            {
                case ErrorKind.Transient:
                    // Satu-satunya kelas yang boleh retry, dan tetap ada batas.
                    return attemptsUsed < maxRetries
                        ? Decision(RecoveryAction.Retry, kind, $"Error sementara, percobaan {attemptsUsed + 1} dari {maxRetries}.", attemptsUsed)
                        : Decision(RecoveryAction.Fail, kind, $"Error sementara masih terjadi setelah {maxRetries} percobaan.", attemptsUsed);

                case ErrorKind.InvalidArgument:
                    return Decision(RecoveryAction.Replan, kind, "Argumen tidak valid. Rencana perlu disusun ulang.", attemptsUsed);

                case ErrorKind.EnvironmentMissing:
                    return Decision(RecoveryAction.UserActionRequired, kind, "Dependensi lingkungan tidak ada. Perlu dipasang lebih dulu.", attemptsUsed);

                case ErrorKind.ToolUnavailable:
                    // Kemampuannya ada, tool-nya tidak. Agent boleh mencoba
                    // menyusunnya dari tool lain.
                    return new RecoveryDecision
                    {
                        Action = RecoveryAction.Replan,
                        Kind = kind,
                        Reason = "Tool tidak tersedia walaupun kemampuannya ada. Coba susun dari tool lain.",
                        AttemptsUsed = attemptsUsed,
                        AgentResolutionRequired = true,
                    };

                case ErrorKind.BackendUnavailable:
                    return Decision(RecoveryAction.Block, kind, "Backend tidak tersedia.", attemptsUsed);

                case ErrorKind.VerificationFailed:
                    // Operasi terlaksana, hasilnya salah. Retry menghasilkan
                    // kesalahan yang sama.
                    return Decision(RecoveryAction.Replan, kind, "Verifikasi gagal. Operasi terlaksana tapi hasilnya tidak sesuai.", attemptsUsed);

                case ErrorKind.UserActionRequired:
                    return Decision(RecoveryAction.UserActionRequired, kind, "Perlu tindakan manusia.", attemptsUsed);

                case ErrorKind.PolicyBlocked:
                    // Pemulihan TIDAK BOLEH melewati gerbang kebijakan.
                    return Decision(RecoveryAction.Block, kind, "Terhalang kebijakan. Pemulihan tidak boleh melewati gerbang ini.", attemptsUsed);

                case ErrorKind.CapabilityGap:
                    return new RecoveryDecision
                    {
                        Action = RecoveryAction.Block,
                        Kind = kind,
                        Reason = "Kemampuan ini belum ada. Kendali dikembalikan ke agent di atas.",
                        AttemptsUsed = attemptsUsed,
                        AgentResolutionRequired = true,
                    };

                case ErrorKind.ProviderSilent:
                    // Integrasi rusak. Retry hanya mengulang keheningan yang sama.
                    return Decision(
                        RecoveryAction.Block,
                        kind,
                        "Provider CLI keluar dengan kode 0 tanpa output. Ini kegagalan integrasi, bukan jawaban kosong. Periksa ConPTY dan autentikasi.",
                        attemptsUsed);

                case ErrorKind.Fatal:
                default:
                    return Decision(RecoveryAction.Fail, ErrorKind.Fatal, "Kegagalan tidak dikenal atau tidak bisa dipulihkan.", attemptsUsed);
            }
        }

        /// <summary>Jalur pintas: klasifikasi lalu putuskan.</summary>
        public static RecoveryDecision Advise(ToolResult? result, Exception? exception = null, int attemptsUsed = 0)
            => Decide(Classify(result, exception), attemptsUsed);

        private static RecoveryDecision Decision(RecoveryAction action, ErrorKind kind, string reason, int attemptsUsed)
            => new RecoveryDecision { Action = action, Kind = kind, Reason = reason, AttemptsUsed = attemptsUsed };

        private static ErrorKind? ClassifyException(Exception exception)
        {
            switch (exception)
            {
                case ArgumentException:
                    return ErrorKind.InvalidArgument;

                case TimeoutException:
                    return ErrorKind.Transient;

                case System.IO.FileNotFoundException:
                case System.IO.DirectoryNotFoundException:
                    return ErrorKind.EnvironmentMissing;

                case UnauthorizedAccessException:
                    return ErrorKind.UserActionRequired;

                case InvalidCastException:
                    // Jebakan yang sudah terdokumentasi: handler PropertyManagerPage
                    // tanpa IDispatch membuat ICreatePropertyManagerPage melempar ini.
                    return ErrorKind.EnvironmentMissing;

                case COMException com:
                    return ClassifyHResult(com.HResult);

                default:
                    return null;
            }
        }

        private static ErrorKind ClassifyHResult(int hresult)
        {
            // Kode COM yang sering muncul saat SolidWorks sibuk atau sedang
            // menampilkan dialog modal. Ini benar-benar sementara.
            const int RpcServerUnavailable = unchecked((int)0x800706BA);
            const int RpcCallRejected = unchecked((int)0x80010001); // RPC_E_CALL_REJECTED
            const int RpcServerCallRetryLater = unchecked((int)0x8001010A); // RPC_E_SERVERCALL_RETRYLATER

            switch (hresult)
            {
                case RpcCallRejected:
                case RpcServerCallRetryLater:
                    return ErrorKind.Transient;

                case RpcServerUnavailable:
                    // SolidWorks hilang. Bukan sesuatu yang bisa kita pulihkan sendiri.
                    return ErrorKind.EnvironmentMissing;

                default:
                    return ErrorKind.Fatal;
            }
        }

        private static ErrorKind? ClassifyMessage(string message)
        {
            var lower = message.ToLowerInvariant();

            if (Contains(lower, "license", "lisensi", "not licensed"))
            {
                return ErrorKind.UserActionRequired;
            }

            if (Contains(lower, "not installed", "tidak terpasang", "could not be found", "not found"))
            {
                return ErrorKind.EnvironmentMissing;
            }

            if (Contains(lower, "timed out", "timeout", "busy", "sibuk"))
            {
                return ErrorKind.Transient;
            }

            if (Contains(lower, "not implemented", "belum diimplementasikan", "capability gap"))
            {
                return ErrorKind.CapabilityGap;
            }

            return null;
        }

        private static bool Contains(string haystack, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (haystack.Contains(needle))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
