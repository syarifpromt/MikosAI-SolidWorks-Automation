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
