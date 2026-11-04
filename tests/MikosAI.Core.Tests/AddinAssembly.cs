using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Memuat MikosAI.Addin lewat refleksi, bukan lewat rujukan tipe.
    ///
    /// KENAPA REFLEKSI.
    ///
    /// Nama assembly add-in berbeda antara dua mode build:
    ///
    ///   build nyata       -> bin\Release\net48\MikosAI.Addin.dll
    ///   mode compile-check -> bin\compilecheck\net48\MikosAI.Addin.CompileCheck.dll
    ///
    /// Perbedaan nama itu bukan kecelakaan, melainkan pengaman: ia memastikan
    /// build stub tidak pernah bisa menimpa DLL yang didaftarkan ke SolidWorks.
    /// Mengikat test ke salah satu nama akan memaksa pengaman itu dilepas, atau
    /// membuat test hanya bisa jalan di mesin bermesin CAD. Keduanya buruk, jadi
    /// test-lah yang menyesuaikan.
    ///
    /// Tipe interop ditanam dengan EmbedInteropTypes, jadi memuat assembly ini
    /// TIDAK butuh PIA SolidWorks terpasang.
    /// </summary>
    internal static class AddinAssembly
    {
        private static readonly Lazy<Assembly> Lazy = new Lazy<Assembly>(Muat);

        internal static Assembly Current => Lazy.Value;

        /// <summary>
        /// Mengambil tipe internal dari add-in. Melempar kalau tidak ada - tipe
        /// yang hilang berarti test ini sudah tidak menguji apa pun, dan itu
        /// harus merah, bukan diam.
        /// </summary>
        internal static Type Tipe(string namaLengkap)
            => Current.GetType(namaLengkap, throwOnError: true)
               ?? throw new InvalidOperationException(namaLengkap);

        private static Assembly Muat()
        {
            var kandidat = new List<string>
            {
                Path.Combine(RepoFiles.Root, "src", "MikosAI.Addin", "bin", "Release", "net48", "MikosAI.Addin.dll"),
                Path.Combine(RepoFiles.Root, "src", "MikosAI.Addin", "bin", "compilecheck", "net48", "MikosAI.Addin.CompileCheck.dll"),
                Path.Combine(RepoFiles.Root, "src", "MikosAI.Addin", "bin", "Debug", "net48", "MikosAI.Addin.dll"),
            };

            // Yang terbaru menang. Kalau kedua mode pernah dibangun, yang relevan
            // adalah yang baru saja dihasilkan build ini.
            var ada = kandidat
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            if (ada.Count == 0)
            {
                throw new FileNotFoundException(
                    "DLL MikosAI.Addin tidak ditemukan. Dicari di:" + Environment.NewLine +
                    string.Join(Environment.NewLine, kandidat.Select(k => "  " + k)) + Environment.NewLine +
                    "Bangun solution-nya dulu: dotnet build MikosAI.sln -c Release");
            }

            // LoadFrom, bukan LoadFile: LoadFrom ikut menelusuri folder DLL itu
            // untuk dependensinya (MikosAI.Core, WebView2), dan tanpa itu
            // refleksi atas tipe-tipenya akan gagal memuat.
            return Assembly.LoadFrom(ada[0]);
        }
    }
}
