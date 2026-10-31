using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MikosAI.Addin
{
    /// <summary>
    /// Menyelesaikan dependensi add-in dari folder add-in itu sendiri.
    ///
    /// KENAPA INI WAJIB ADA.
    ///
    /// Add-in kita didaftarkan dengan <c>RegAsm /codebase</c>, jadi CLR memuat
    /// MikosAI.Addin.dll dari path yang tercatat di registry. Tapi DEPENDENSINYA
    /// dicari relatif ke app base HOST - yaitu folder SLDWORKS.exe, bukan folder
    /// kita.
    ///
    /// Akibatnya MikosAI.Core.dll, System.Text.Json.dll, YamlDotNet.dll dan
    /// seluruh dependensi transitifnya TIDAK akan ditemukan, dan gejalanya
    /// adalah yang paling membingungkan: SolidWorks melaporkan LoadAddIn=0,
    /// add-in tidak pernah terinstansiasi, dan tidak ada satu pun pesan error.
    ///
    /// Handler ini dipasang dari STATIC CONSTRUCTOR MikosAiAddin, yang berjalan
    /// sebelum instance apa pun dibuat - jadi sebelum field initializer yang
    /// menyentuh dependensi dieksekusi. Urutan itu penting dan rapuh: jangan
    /// pindahkan pemasangannya ke constructor instance.
    ///
    /// Kelas ini sendiri HANYA memakai tipe dari mscorlib/System, yang sudah
    /// pasti dimuat. Kalau ia butuh dependensi eksternal, ia tidak akan pernah
    /// bisa memuat dirinya sendiri.
    /// </summary>
    internal static class AssemblyResolver
    {
        private static readonly object Lock = new object();
        private static readonly List<string> Jejak = new List<string>();

        private static bool _terpasang;
        private static string? _folderAddin;

        /// <summary>
        /// Catatan percobaan resolusi, untuk dimasukkan ke host-status.json.
        /// Hanya yang gagal dan yang berhasil-lewat-handler dicatat; resolusi
        /// normal oleh CLR tidak lewat sini sama sekali.
        /// </summary>
        internal static IReadOnlyList<string> Jejak_
        {
            get
            {
                lock (Lock)
                {
                    return Jejak.ToArray();
                }
            }
        }

        internal static void Pasang()
        {
            lock (Lock)
            {
                if (_terpasang)
                {
                    return;
                }

                try
                {
                    _folderAddin = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location);
                    AppDomain.CurrentDomain.AssemblyResolve += Selesaikan;
                    _terpasang = true;
                }
                catch (Exception exception)
                {
                    // Tidak ada tempat lain untuk melapor pada titik ini -
                    // HostDiagnostics pun belum tentu bisa dimuat.
                    Catat($"GAGAL memasang handler: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        internal static string? FolderAddin => _folderAddin;

        internal static bool Terpasang => _terpasang;

        private static Assembly? Selesaikan(object sender, ResolveEventArgs args)
        {
            if (string.IsNullOrEmpty(_folderAddin))
            {
                return null;
            }

            string namaSederhana;
            try
            {
                namaSederhana = new AssemblyName(args.Name).Name;
            }
            catch (Exception)
            {
                // Nama tidak bisa diparse - serahkan kembali ke CLR.
                return null;
            }

            // Jangan coba menyelesaikan resource satelit; itu normal gagal.
            if (namaSederhana.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var kandidat = Path.Combine(_folderAddin!, namaSederhana + ".dll");

            if (!File.Exists(kandidat))
            {
                Catat($"TIDAK ADA  {namaSederhana} (dicari di {kandidat})");
                return null;
            }

            try
            {
                var assembly = Assembly.LoadFrom(kandidat);
                Catat($"DIMUAT     {namaSederhana}");
                return assembly;
            }
            catch (Exception exception)
            {
                Catat($"GAGAL MUAT {namaSederhana}: {exception.GetType().Name}: {exception.Message}");
                return null;
            }
        }

        private static void Catat(string pesan)
        {
            lock (Lock)
            {
                // Batasi, supaya kegagalan berulang tidak membengkakkan
                // diagnostik tanpa batas.
                if (Jejak.Count < 64)
                {
                    Jejak.Add(pesan);
                }
            }
        }
    }
}
