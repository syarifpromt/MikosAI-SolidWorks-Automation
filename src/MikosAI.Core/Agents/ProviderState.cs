using System;

namespace MikosAI.Core.Agents
{
    /// <summary>
    /// Keadaan provider AI, dibedakan KETAT.
    ///
    /// Ini bukan kerewelan. Tiga hal berikut TIDAK satu pun membuktikan bahwa
    /// permintaan ke model benar-benar bisa jalan:
    ///
    ///   - CLI ada di PATH
    ///   - environment variable terisi
    ///   - konfigurasi provider ada di berkas
    ///
    /// Dan provider yang melaporkan sukses TIDAK membuktikan geometri SolidWorks
    /// benar. Karena itu setiap tingkat punya bukti yang berbeda, dan UI tidak
    /// boleh menyamakannya.
    /// </summary>
    public enum ProviderState
    {
        /// <summary>Entry CLI tidak ditemukan, atau <c>--version</c> gagal.</summary>
        NotInstalled = 0,

        /// <summary><c>--version</c> sukses. Belum berarti bisa memanggil model.</summary>
        Installed = 1,

        /// <summary>Perintah status autentikasi resmi mengembalikan gagal.</summary>
        AuthFailed = 2,

        /// <summary>
        /// CLI tidak punya perintah pemeriksaan autentikasi yang murah, jadi
        /// belum bisa dipastikan. TIDAK boleh ditampilkan sebagai terhubung.
        /// </summary>
        AuthUnverified = 3,

        /// <summary>Perintah autentikasi resmi sukses. Boleh disebut terautentikasi.</summary>
        AuthVerified = 4,

        /// <summary>
        /// Satu permintaan nyata mengembalikan hasil terstruktur sesuai protokol.
        /// Hanya tingkat ini yang boleh disebut "eksekusi terverifikasi", dan itu
        /// berlaku untuk permintaan ITU saja - bukan jaminan untuk berikutnya.
        /// </summary>
        ExecutionVerified = 5,
    }

    public static class ProviderStateRules
    {
        /// <summary>
        /// Apakah masuk akal mencoba mengirim permintaan.
        ///
        /// AuthUnverified ikut diizinkan dengan sengaja: beberapa CLI memang
        /// tidak punya cara murah memeriksa autentikasi, dan satu-satunya cara
        /// tahu adalah mencoba. Permintaan pertama itulah yang memverifikasinya.
        /// </summary>
        public static bool MayAttemptRequest(ProviderState state)
            => state == ProviderState.AuthUnverified
               || state == ProviderState.AuthVerified
               || state == ProviderState.ExecutionVerified;

        /// <summary>
        /// Apakah boleh ditampilkan ke pengguna sebagai "terhubung".
        ///
        /// Installed dan AuthUnverified TIDAK boleh - keduanya belum punya bukti
        /// bahwa permintaan ke model bisa jalan.
        /// </summary>
        public static bool MayBeShownAsConnected(ProviderState state)
            => state == ProviderState.AuthVerified
               || state == ProviderState.ExecutionVerified;

        /// <summary>
        /// Kalimat yang jujur untuk setiap keadaan, untuk UI Lapis 1.
        ///
        /// Tidak ada jargon internal di sini - pengguna tidak perlu tahu istilah
        /// AuthUnverified, mereka perlu tahu apa yang harus dilakukan.
        /// </summary>
        public static string Describe(ProviderState state, string displayName)
        {
            switch (state)
            {
                case ProviderState.NotInstalled:
                    return $"{displayName} belum terpasang.";

                case ProviderState.Installed:
                    return $"{displayName} terpasang, tapi belum login.";

                case ProviderState.AuthFailed:
                    return $"Login {displayName} gagal atau sudah kedaluwarsa.";

                case ProviderState.AuthUnverified:
                    return $"{displayName} terpasang. Status login belum bisa dipastikan - "
                           + "permintaan pertama yang akan membuktikannya.";

                case ProviderState.AuthVerified:
                    return $"{displayName} siap.";

                case ProviderState.ExecutionVerified:
                    return $"{displayName} siap, dan sudah berhasil menjalankan permintaan.";

                default:
                    // Keadaan tak dikenal tidak pernah ditampilkan sebagai siap.
                    return $"Status {displayName} tidak dikenali.";
            }
        }

        /// <summary>
        /// Apa yang harus dilakukan manusia untuk maju dari keadaan ini.
        /// null berarti tidak ada yang perlu dilakukan.
        /// </summary>
        public static string? NextAction(ProviderState state, string displayName)
        {
            switch (state)
            {
                case ProviderState.NotInstalled:
                    return $"Pasang {displayName}, lalu buka ulang panel ini.";

                case ProviderState.Installed:
                case ProviderState.AuthFailed:
                    return $"Jalankan {displayName} sekali di terminal dan selesaikan login, "
                           + "lalu buka ulang panel ini.";

                default:
                    return null;
            }
        }
    }
}
