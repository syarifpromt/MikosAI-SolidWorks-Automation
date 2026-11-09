using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Execution;
using MikosAI.Core.Threading;
using MikosAI.Core.Tools;
using MikosAI.Core.Plan;

namespace MikosAI.Addin
{
    /// <summary>
    /// Executor tool CAD nyata.
    ///
    /// SETIAP pembacaan dijalankan lewat <see cref="IMainThreadDispatcher"/>.
    /// Request MCP tiba di thread HttpListener, dan API SolidWorks tidak
    /// thread-safe - menyentuhnya dari sana menghasilkan crash acak yang tidak
    /// bisa direproduksi, dan karena kita in-process, crash itu menjatuhkan
    /// SolidWorks milik pengguna.
    ///
    /// Tidak ada jalan pintas di kelas ini. Lihat CONTRIBUTING.md aturan 1.
    ///
    /// Tahap ini HANYA tool read-only. Tool yang menulis adalah Fase 3, dan
    /// menuntut verifikasi baca-ulang yang jauh lebih ketat.
    /// </summary>
    internal sealed class SolidWorksToolExecutor : IToolExecutor
    {
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly Func<SolidWorksReader?> _ambilPembaca;
        private readonly ParametricBlockFlow? _blockFlow;

        internal SolidWorksToolExecutor(
            IMainThreadDispatcher dispatcher,
            Func<SolidWorksReader?> ambilPembaca)
            : this(dispatcher,ambilPembaca,null) { }

        internal SolidWorksToolExecutor(IMainThreadDispatcher dispatcher,Func<SolidWorksReader?> ambilPembaca,ParametricBlockFlow? blockFlow)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _ambilPembaca = ambilPembaca ?? throw new ArgumentNullException(nameof(ambilPembaca));
            _blockFlow=blockFlow;
        }

        public async Task<ToolResult> ExecuteAsync(
            string toolName,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            if(toolName==ParametricBlockFlow.ToolName)
                return _blockFlow!=null ? await _blockFlow.ExecuteAsync(arguments,cancellationToken).ConfigureAwait(false)
                    : ToolResult.Failure(ParametricBlockFlow.CapabilityId,ErrorKind.EnvironmentMissing,"Jalur balok belum siap.");
            try
            {
                return await _dispatcher.InvokeAsync(
                    () => Jalankan(toolName, arguments),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Kegagalan dispatcher sendiri - mis. SolidWorks sedang menutup.
                return ToolResult.Failure(
                    ReadOnlyToolShaper.CapabilityId,
                    RecoveryAdvisor.Classify(null, exception),
                    $"Tool '{toolName}' tidak bisa dijalankan di thread utama SolidWorks: {exception.Message}");
            }
        }

        /// <summary>
        /// Berjalan DI THREAD UTAMA SolidWorks. Semua sentuhan API ada di sini.
        /// </summary>
        private ToolResult Jalankan(string toolName, JsonObject arguments)
        {
            var pembaca = _ambilPembaca();

            if (pembaca is null)
            {
                return ToolResult.Failure(
                    ReadOnlyToolShaper.CapabilityId,
                    ErrorKind.EnvironmentMissing,
                    "SolidWorks tidak lagi terhubung. Tutup dan buka ulang panel MikosAI.");
            }

            switch (toolName)
            {
                case "get_active_document":
                    return ReadOnlyToolShaper.ActiveDocument(pembaca.BacaDokumen());

                case "list_features":
                    return ReadOnlyToolShaper.FeatureTree(pembaca.BacaFitur());

                case "get_bounding_box":
                    return ReadOnlyToolShaper.BoundingBox(pembaca.BacaKotakPembatas());

                case "get_mass_properties":
                    return ReadOnlyToolShaper.MassProperties(pembaca.BacaPropertiMassa());

                case "inspect_sketches":
                    var namaSketsa = arguments["sketchName"]?.GetValue<string>();
                    return ReadOnlyToolShaper.Sketches(pembaca.BacaSketsa(namaSketsa));

                case "get_selection":
                    return ReadOnlyToolShaper.Selection(pembaca.BacaSeleksi());

                default:
                    // Gating sudah menolak tool yang tidak diekspos sebelum
                    // sampai ke sini. Kalau tetap lolos, itu capability gap -
                    // kendali dikembalikan ke agent di atas.
                    return ToolResult.Failure(
                        ReadOnlyToolShaper.CapabilityId,
                        ErrorKind.CapabilityGap,
                        $"Tool '{toolName}' belum diimplementasikan di executor ini.");
            }
        }
    }
}
