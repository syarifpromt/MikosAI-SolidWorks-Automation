using System;
using System.Collections.Generic;

namespace MikosAI.Core.Tools
{
    /// <summary>
    /// Tipe fitur yang TERBUKTI memakai sketsa sebagai profil, path, atau
    /// posisi.
    ///
    /// SYARAT MASUK: DUA BUKTI, BUKAN SATU.
    ///
    ///   1. typeName TERAMATI dari model nyata - bukan dari ingatan, bukan dari
    ///      dokumentasi pihak ketiga.
    ///   2. Ada fixture yang SENGAJA membuat fitur itu dari sebuah sketsa
    ///      sebagai profil/path, dengan efek yang terverifikasi - misalnya
    ///      volume yang berubah.
    ///
    /// Nama yang teramati TANPA bukti peran tetap di luar daftar. Melihat
    /// sebuah tipe muncul sebagai child tidak membuktikan ia mengkonsumsi
    /// sketsanya: reference plane yang dibuat lewat sebuah garis sketsa juga
    /// muncul sebagai child, dan ia jelas bukan konsumen.
    ///
    /// KENAPA DAFTAR PUTIH. Daftar hitam membuat tipe yang belum dikenal
    /// otomatis jadi konsumen - mengklaim pengetahuan yang tidak kita punya,
    /// dan klaimnya tidak terlihat dari luar. Daftar putih yang kurang lengkap
    /// hanya membuat consumedByFeatures KURANG melaporkan, dan itu tidak
    /// berbahaya karena delete_feature memakai dependents yang memuat semuanya.
    ///
    /// Selama daftar ini belum lengkap, setiap sketsa yang punya child di luar
    /// daftar akan mengibarkan limit `unclassified_child`. Itu DISENGAJA dan
    /// tidak boleh dibungkam - ia yang membuat daftar ini tidak bisa
    /// ditinggalkan setengah jadi.
    /// </summary>
    public static class TipeKonsumenSketsa
    {
        /// <summary>
        /// Entri daftar putih beserta buktinya.
        ///
        /// Bukti ikut disimpan di dalam entri, bukan di komentar, supaya
        /// penambahan tanpa bukti terlihat sebagai baris yang alasannya kosong
        /// - bukan sebagai satu kata yang menyelinap ke dalam array.
        /// </summary>
        public sealed class Entri
        {
            public string TypeName { get; }
            public string Fixture { get; }
            public string DiamatiPada { get; }

            public Entri(string typeName, string fixture, string diamatiPada)
            {
                TypeName = typeName;
                Fixture = fixture;
                DiamatiPada = diamatiPada;
            }
        }

        /// <summary>
        /// Hanya tipe yang sudah teramati beserta bukti perannya.
        ///
        /// Extrusion teramati pada Boss-Extrude1 di plat-referensi.SLDPRT;
        /// ICE pada Cut-Extrude1 di fixture-konsumen.SLDPRT. Nama tipe
        /// untuk revolve, hole wizard, sweep, dan loft BELUM pernah kami
        /// lihat, dan menuliskannya dari ingatan adalah cara yang sama persis
        /// dengan menebak.
        ///
        /// Sensus nama tipe dilakukan oleh run fixture pertama: dependents
        /// membawa typeName, jadi laporannya akan menyebutkan nama sebenarnya.
        ///
        /// CATATAN: Extrusion masuk dengan bukti peran dari plat-referensi,
        /// di mana Boss-Extrude1 memang menghasilkan volume 80000 mm3 dari
        /// Sketch1. fixture-konsumen membuktikan SATU sketsa dipakai dua fitur,
        /// dengan volume akhir 42000 mm3; run awal masih punya kegagalan lain.
        /// </summary>
        private static readonly Entri[] Daftar =
        {
            new Entri(
                "Extrusion",
                "tests/golden/plat-referensi.SLDPRT",
                "SolidWorks 2025 SP01.1, build add-in 62d5aa8, 2026-11-05"),
            new Entri(
                "ICE",
                "tests/golden/fixture-konsumen.SLDPRT",
                "SolidWorks revisi 33.1.1, build add-in 52357c5, 2026-11-07; "
                + "Cut-Extrude1 memakai Sketch1 yang sama; volume 42000 mm3, "
                + "berkurang dari boss 60000 mm3. Run belum lulus seluruhnya."),
        };

        public static IReadOnlyList<Entri> Semua => Daftar;

        public static bool Dikenal(string? typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            foreach (var e in Daftar)
            {
                if (string.Equals(e.TypeName, typeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
