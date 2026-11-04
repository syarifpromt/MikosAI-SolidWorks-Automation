using System;
using System.Collections.Generic;

namespace MikosAI.Core.Tools
{
    /// <summary>
    /// Field yang ADA di kontrak tapi BELUM pernah diisi pembaca.
    ///
    /// KENAPA INI DISIARKAN, BUKAN CUKUP DITULIS DI DOKUMEN.
    ///
    /// Empat field diemisikan sejak lama tanpa pernah dibaca dari SolidWorks.
    /// Masing-masing terbaca sebagai pernyataan yang berbeda, dan semuanya
    /// salah:
    ///
    ///   hasOpenContour: false  -> "sudah diperiksa, sketsanya tertutup"
    ///   dimensionCount: 0      -> "sketsa ini tanpa dimensi"
    ///   consumedByFeature: null-> "sketsa ini tidak dipakai fitur apa pun"
    ///   parentSketch: null     -> "fitur ini tidak punya sketsa induk"
    ///
    /// Yang ketiga paling berbahaya begitu tool tulis ada: "tidak dipakai
    /// fitur apa pun" adalah alasan yang masuk akal untuk MENGHAPUS sketsa.
    ///
    /// Semuanya kini null. Tapi null saja tidak cukup - null tetap terbaca
    /// sebagai "tidak ada", bukan "tidak diketahui". Jadi daftarnya ikut
    /// dikirim di setiap jawaban, supaya pemanggil tahu BEDANYA tanpa harus
    /// membaca dokumen yang tidak ada di depannya saat ia memanggil tool.
    ///
    /// Daftar ini MENGECIL saat field benar-benar diimplementasikan. Kalau ada
    /// yang mengisi field tanpa menghapusnya dari sini, test akan merah.
    /// </summary>
    public static class FieldBelumDibaca
    {
        public static readonly IReadOnlyList<string> ListFitur =
            Array.AsReadOnly(new[] { "parentSketch" });

        public static readonly IReadOnlyList<string> ListSketsa =
            Array.AsReadOnly(new[] { "dimensionCount", "consumedByFeature", "hasOpenContour" });
    }
}
