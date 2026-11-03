using System;

namespace MikosAI.Addin
{
    /// <summary>
    /// Saklar fitur lewat variabel lingkungan.
    ///
    /// KENAPA ADA.
    ///
    /// SolidWorks mati dengan Fault Module kosong, dan host-status.json
    /// menunjukkan ConnectToSW selesai sempurna tanpa satu pun error. Artinya
    /// crash terjadi SETELAH itu - di salah satu dari tiga subsistem yang
    /// berjalan di latar: WebView2, server MCP, atau subprocess agy.
    ///
    /// Menebak mana yang bersalah sudah gagal tiga kali, dan setiap tebakan
    /// memakan satu siklus restart SolidWorks di mesin orang lain. Jadi
    /// sekarang setiap subsistem bisa dimatikan tanpa rebuild, dan penyebabnya
    /// dipersempit dalam dua sampai tiga percobaan.
    ///
    /// Dibaca sekali saat ConnectToSW. Ubah variabel, buka ulang SolidWorks.
    ///
    /// Cara memakainya ada di docs/setup.md bagian "Mempersempit crash".
    /// </summary>
    internal static class Fitur
    {
        /// <summary>Matikan TaskPane dan WebView2 sepenuhnya.</summary>
        internal static bool TaskPaneMati => Aktif("MIKOSAI_DISABLE_TASKPANE");

        /// <summary>Matikan server MCP.</summary>
        internal static bool McpMati => Aktif("MIKOSAI_DISABLE_MCP");

        /// <summary>Matikan deteksi provider dan seluruh subprocess agy.</summary>
        internal static bool ProviderMati => Aktif("MIKOSAI_DISABLE_PROVIDER");

        /// <summary>
        /// Mode aman: hanya ConnectToSW, dispatcher, dan diagnostik.
        /// Tidak ada UI, tidak ada server, tidak ada subprocess.
        ///
        /// Kalau SolidWorks TETAP mati dengan ini, penyebabnya bukan salah satu
        /// dari ketiga subsistem itu - dan itu informasi yang sama berharganya.
        /// </summary>
        internal static bool ModeAman => Aktif("MIKOSAI_SAFE_MODE");

        /// <summary>
        /// Pakai WebView2 untuk panel. OPT-IN, dan defaultnya MATI.
        ///
        /// WebView2 menjatuhkan SolidWorks di mesin uji kami, dan bukan lewat
        /// exception yang bisa ditangkap:
        ///
        ///   - Penyempitan saklar fitur: mematikan TaskPane adalah SATU-SATUNYA
        ///     mode yang tidak crash
        ///   - Jejak aktivitas berhenti di webview:siap - WebView2 selesai
        ///     diinisialisasi dan halaman sudah dinavigasi
        ///   - Log SolidWorks mencatat SWEXH/UNHANDLED
        ///   - Penangkap AppDomain.UnhandledException TIDAK PERNAH terpicu
        ///
        /// Yang terakhir itu yang menentukan: crash-nya tidak lewat CLR sama
        /// sekali. Itu structured exception native, dan tidak ada try/catch
        /// managed yang bisa menangkapnya.
        ///
        /// Jadi panel WinForms biasa yang dipakai. Lebih sederhana tampilannya,
        /// tapi tidak menyentuh komponen native mana pun - dan seluruh lapisan
        /// di bawahnya tidak berubah sedikit pun.
        ///
        /// MIKOSAI_ENABLE_WEBVIEW2=1 menyalakannya kembali, untuk mencoba lagi
        /// di mesin atau versi SolidWorks lain.
        /// </summary>
        internal static bool PakaiWebView2 => Aktif("MIKOSAI_ENABLE_WEBVIEW2");

        internal static bool PakaiTaskPane => !ModeAman && !TaskPaneMati;

        internal static bool PakaiMcp => !ModeAman && !McpMati;

        internal static bool PakaiProvider => !ModeAman && !ProviderMati;

        /// <summary>Ringkasan untuk host-status.json, supaya hasil uji tidak ambigu.</summary>
        internal static string Ringkasan()
        {
            if (ModeAman)
            {
                return "mode-aman";
            }

            var mati = string.Empty;

            if (TaskPaneMati) mati += "taskpane ";
            if (!PakaiWebView2) mati += "webview2(panel-polos) ";
            if (McpMati) mati += "mcp ";
            if (ProviderMati) mati += "provider ";

            return mati.Length == 0 ? "semua-aktif" : "mati: " + mati.Trim();
        }

        private static bool Aktif(string nama)
        {
            try
            {
                var nilai = Environment.GetEnvironmentVariable(nama);

                if (string.IsNullOrWhiteSpace(nilai))
                {
                    return false;
                }

                var kecil = nilai!.Trim().ToLowerInvariant();

                return kecil == "1" || kecil == "true" || kecil == "yes" || kecil == "on";
            }
            catch (Exception)
            {
                // Variabel lingkungan tidak terbaca: anggap fitur aktif, karena
                // itu perilaku normalnya.
                return false;
            }
        }
    }
}
