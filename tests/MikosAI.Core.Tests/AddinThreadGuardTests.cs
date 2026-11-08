using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Execution;
using MikosAI.Core.Threading;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Menegakkan aturan nomor 1 CONTRIBUTING.md lewat TEST, bukan lewat mata.
    ///
    /// Aturannya: API SolidWorks hanya boleh disentuh dari thread utama. Sampai
    /// sekarang aturan itu dijaga oleh pemanggilan `PastikanThreadUtama` di
    /// setiap metode - dan oleh harapan bahwa orang berikutnya ingat
    /// menambahkannya. Harapan bukan penegakan.
    ///
    /// Test di sini MENEMUKAN metodenya sendiri lewat refleksi. Jadi metode
    /// ketujuh yang lupa memasang penjaga akan membuat test ini merah tanpa ada
    /// yang perlu memperbarui daftar apa pun. Itu bedanya dengan test yang hanya
    /// memeriksa enam metode yang ada hari ini.
    ///
    /// KENAPA FIXTURE-NYA SETENGAH MATANG, DAN KENAPA ITU JUSTRU LEBIH KUAT.
    ///
    /// Objeknya dibuat lewat GetUninitializedObject, jadi field `_app` tetap
    /// null. Konsekuensinya: kalau sebuah metode menyentuh `_app` SEBELUM
    /// memanggil penjaganya, yang terjadi adalah NullReferenceException, bukan
    /// MainThreadViolationException - dan test gagal.
    ///
    /// Artinya test ini tidak cuma menuntut penjaganya ADA. Ia menuntut
    /// penjaganya berjalan LEBIH DULU daripada sentuhan API mana pun.
    /// </summary>
    public class AddinThreadGuardTests
    {
        private const string NamaReader = "MikosAI.Addin.SolidWorksReader";
        private const string NamaApi = "MikosAI.Addin.SolidWorksApi";
        private const string NamaDiagnostics = "MikosAI.Addin.HostDiagnostics";
        private const string NamaExecutor = "MikosAI.Addin.SolidWorksToolExecutor";

        /// <summary>
        /// Metode yang memang TIDAK menyentuh API SolidWorks, jadi tidak butuh
        /// penjaga. Daftarnya sengaja pendek dan beralasan.
        ///
        /// Arah defaultnya penting: metode baru dianggap WAJIB berpenjaga sampai
        /// seseorang sengaja menuliskannya di sini. Lupa menambahkan penjaga jadi
        /// test merah; mengecualikan sesuatu jadi keputusan yang terlihat di diff.
        /// </summary>
        private static readonly Dictionary<string, string> Dikecualikan =
            new Dictionary<string, string>
            {
                ["MikosAI.Addin.SolidWorksApi.BuatPembaca"] =
                    "Hanya merangkai objek - tidak ada panggilan API di dalamnya.",
            };

        // -------------------------------------------------------------------
        // Gerbang: pelanggaran thread jadi exception tercatat, bukan crash
        // -------------------------------------------------------------------

        [Theory]
        [InlineData(NamaReader)]
        [InlineData(NamaApi)]
        [InlineData("MikosAI.Addin.SolidWorksBlockWriter")]
        public void setiap_metode_yang_menyentuh_api_menolak_thread_selain_utama(string namaTipe)
        {
            var tipe = AddinAssembly.Tipe(namaTipe);
            var metode = MetodeYangHarusBerpenjaga(tipe);

            Assert.NotEmpty(metode);

            var gagal = new List<string>();

            foreach (var m in metode)
            {
                var instance = BuatFixture(tipe);
                var argumen = ArgumenDefault(m);

                Exception? dilempar = null;
                try
                {
                    m.Invoke(instance, argumen);
                }
                catch (TargetInvocationException tie)
                {
                    dilempar = tie.InnerException;
                }
                catch (Exception lain)
                {
                    dilempar = lain;
                }

                if (dilempar is MainThreadViolationException)
                {
                    continue;
                }

                gagal.Add(dilempar is null
                    ? $"{m.Name}: tidak melempar apa pun - penjaga thread tidak ada"
                    : $"{m.Name}: melempar {dilempar.GetType().Name} ({dilempar.Message}) " +
                      "- diharapkan MainThreadViolationException. Kalau ini NullReferenceException, " +
                      "penjaganya ada tapi berjalan SETELAH API disentuh.");
            }

            Assert.True(
                gagal.Count == 0,
                $"{gagal.Count} dari {metode.Count} metode di {namaTipe} tidak dijaga:" +
                Environment.NewLine + string.Join(Environment.NewLine, gagal.Select(g => "  " + g)));
        }

        /// <summary>
        /// Keenam metode baca yang dipakai tool read-only harus ikut terperiksa.
        ///
        /// Test di atas menemukan metodenya sendiri, yang berarti ia tetap lulus
        /// kalau sebuah metode DIHAPUS. Test ini menutup celah itu: ia menuntut
        /// keenamnya benar-benar ada.
        /// </summary>
        [Fact]
        public void keenam_metode_baca_tool_read_only_ada_dan_terjaga()
        {
            var tipe = AddinAssembly.Tipe(NamaReader);
            var terjaga = MetodeYangHarusBerpenjaga(tipe).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

            var wajib = new[]
            {
                "BacaDokumen",         // get_active_document
                "BacaFitur",           // list_features
                "BacaKotakPembatas",   // get_bounding_box
                "BacaPropertiMassa",   // get_mass_properties
                "BacaSketsa",          // inspect_sketches
                "BacaSeleksi",         // get_selection
            };

            var hilang = wajib.Where(w => !terjaga.Contains(w)).ToList();

            Assert.True(
                hilang.Count == 0,
                "Metode baca yang hilang dari SolidWorksReader: " + string.Join(", ", hilang));
        }

        // -------------------------------------------------------------------
        // Gerbang: semua 6 tool dieksekusi lewat dispatcher
        // -------------------------------------------------------------------

        /// <summary>
        /// Membuktikan <c>SolidWorksToolExecutor</c> TIDAK pernah menjalankan
        /// pembacaan secara langsung.
        ///
        /// Pembaca sengaja dibuat null. Hasil pemanggilannya jadi kegagalan
        /// EnvironmentMissing, dan itu tidak penting di sini - yang diuji adalah
        /// JALURNYA, bukan hasilnya. Kalau seseorang nanti memanggil pembacaan
        /// langsung tanpa melewati dispatcher, hitungan di bawah jadi nol dan
        /// test ini merah.
        /// </summary>
        [Fact]
        public async Task setiap_tool_read_only_dieksekusi_lewat_dispatcher()
        {
            var tipeExecutor = AddinAssembly.Tipe(NamaExecutor);
            var tipeReader = AddinAssembly.Tipe(NamaReader);

            var dispatcher = new DispatcherPencatat();

            var executor = (IToolExecutor)Activator.CreateInstance(
                tipeExecutor,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                args: new object?[] { dispatcher, PengambilPembacaNull(tipeReader) },
                culture: null)!;

            var tools = new[]
            {
                "get_active_document",
                "list_features",
                "get_bounding_box",
                "get_mass_properties",
                "inspect_sketches",
                "get_selection",
            };

            foreach (var tool in tools)
            {
                dispatcher.Reset();

                var hasil = await executor.ExecuteAsync(tool, new JsonObject());

                Assert.Equal(1, dispatcher.JumlahInvoke);

                // Hasilnya harus kegagalan yang jujur, bukan exception yang lolos:
                // pembacanya null, dan itu kondisi yang diantisipasi.
                Assert.False(hasil.Ok);
                Assert.NotNull(hasil.Error);
                Assert.Equal(ErrorKind.EnvironmentMissing, hasil.Error!.Kind);
            }
        }

        [Fact]
        public async Task executor_tetap_lewat_dispatcher_untuk_tool_yang_tidak_dikenal()
        {
            // Jalur default juga harus lewat dispatcher. Kalau tidak, ada cabang
            // yang menyentuh API di luar thread utama tanpa terlihat.
            var tipeExecutor = AddinAssembly.Tipe(NamaExecutor);
            var tipeReader = AddinAssembly.Tipe(NamaReader);
            var dispatcher = new DispatcherPencatat();

            var executor = (IToolExecutor)Activator.CreateInstance(
                tipeExecutor,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                args: new object?[] { dispatcher, PengambilPembacaNull(tipeReader) },
                culture: null)!;

            var hasil = await executor.ExecuteAsync("tool_yang_tidak_ada", new JsonObject());

            Assert.Equal(1, dispatcher.JumlahInvoke);
            Assert.False(hasil.Ok);
        }

        // -------------------------------------------------------------------
        // Alat bantu
        // -------------------------------------------------------------------

        /// <summary>
        /// Metode instance yang terlihat dari luar tipe (internal atau public),
        /// bukan accessor properti, bukan warisan, dan tidak dikecualikan.
        ///
        /// Helper privat tidak diikutkan: ia hanya dipanggil dari dalam metode
        /// yang sudah berpenjaga, jadi menuntutnya berpenjaga lagi tidak
        /// menambah jaminan apa pun.
        /// </summary>
        private static List<MethodInfo> MetodeYangHarusBerpenjaga(Type tipe)
            => tipe.GetMethods(
                       BindingFlags.Instance |
                       BindingFlags.Public |
                       BindingFlags.NonPublic |
                       BindingFlags.DeclaredOnly)
                   .Where(m => !m.IsSpecialName)                       // buang get_/set_
                   .Where(m => m.IsPublic || m.IsAssembly)             // public atau internal
                   .Where(m => !Dikecualikan.ContainsKey($"{tipe.FullName}.{m.Name}"))
                   .OrderBy(m => m.Name, StringComparer.Ordinal)
                   .ToList();

        /// <summary>
        /// Objek tanpa konstruktor, dengan hanya dua field yang diisi: dispatcher
        /// palsu yang selalu bilang "bukan thread utama", dan diagnostik
        /// sungguhan.
        ///
        /// HostDiagnostics dipakai apa adanya karena AddError hanya menambah ke
        /// memori - ia tidak menulis host-status.json, jadi menjalankan test ini
        /// tidak merusak keadaan diagnostik yang sedang diperiksa manusia.
        /// </summary>
        private static object BuatFixture(Type tipe)
        {
            var instance = FormatterServices.GetUninitializedObject(tipe);

            Isi(tipe, instance, "_dispatcher", new DispatcherBukanThreadUtama());
            Isi(tipe, instance, "_diagnostics", BuatDiagnostics());

            return instance;
        }

        private static object BuatDiagnostics()
        {
            var tipe = AddinAssembly.Tipe(NamaDiagnostics);
            return Activator.CreateInstance(tipe, nonPublic: true)!;
        }

        private static void Isi(Type tipe, object instance, string namaField, object nilai)
        {
            var field = tipe.GetField(namaField, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"Field '{namaField}' tidak ada di {tipe.FullName}. " +
                    "Kalau ia diganti nama, test ini harus ikut diperbarui - jangan dihapus.");

            field.SetValue(instance, nilai);
        }

        private static object?[] ArgumenDefault(MethodInfo metode)
            => metode.GetParameters()
                     .Select(p => p.ParameterType.IsValueType
                         ? Activator.CreateInstance(p.ParameterType)
                         : null)
                     .ToArray();

        /// <summary>
        /// Membuat <c>Func&lt;SolidWorksReader&gt;</c> yang mengembalikan null.
        ///
        /// Tipe pembacanya internal dan tidak bisa disebut dari sini, jadi
        /// delegate-nya dibentuk lewat generic method yang dibuat konkret saat
        /// runtime.
        /// </summary>
        private static Delegate PengambilPembacaNull(Type tipeReader)
        {
            var metode = typeof(AddinThreadGuardTests)
                .GetMethod(nameof(KembalikanNull), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(tipeReader);

            var tipeFunc = typeof(Func<>).MakeGenericType(tipeReader);

            return Delegate.CreateDelegate(tipeFunc, metode);
        }

        private static T? KembalikanNull<T>() where T : class => null;

        private sealed class DispatcherBukanThreadUtama : IMainThreadDispatcher
        {
            public bool IsOnMainThread => false;

            public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException(
                    "Fixture ini hanya untuk menguji penjaga thread; InvokeAsync tidak boleh terpanggil.");

            public Task InvokeAsync(Action work, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException(
                    "Fixture ini hanya untuk menguji penjaga thread; InvokeAsync tidak boleh terpanggil.");
        }

        /// <summary>
        /// Menghitung pemanggilan dan menjalankan pekerjaannya langsung.
        ///
        /// IsOnMainThread dibuat true: dari sudut pandang kode yang dijalankan,
        /// ia MEMANG sedang berada di thread utama - itu justru yang disimulasikan
        /// dispatcher sungguhan.
        /// </summary>
        private sealed class DispatcherPencatat : IMainThreadDispatcher
        {
            private int _jumlah;

            public int JumlahInvoke => _jumlah;

            public bool IsOnMainThread => true;

            public void Reset() => _jumlah = 0;

            public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _jumlah);
                return Task.FromResult(work());
            }

            public Task InvokeAsync(Action work, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _jumlah);
                work();
                return Task.CompletedTask;
            }
        }
    }
}
