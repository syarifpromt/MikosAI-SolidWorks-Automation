using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MikosAI.Core.Execution
{
    /// <summary>
    /// Yang benar-benar menjalankan tool CAD.
    ///
    /// Implementasi nyata ada di MikosAI.Addin: ia memanggil SolidWorks PIA
    /// lewat IMainThreadDispatcher, lalu MENGISI Verification dengan hasil
    /// baca-ulang model. Hanya executor yang bisa melakukan itu, karena hanya
    /// dia yang punya akses ke model.
    ///
    /// PlanRunner tidak pernah tahu apa pun tentang SolidWorks - ia hanya
    /// mengatur urutan, menambahkan harapan tingkat plan, dan mengagregasi.
    /// </summary>
    public interface IToolExecutor
    {
        /// <summary>
        /// Menjalankan satu tool.
        ///
        /// Kontrak yang harus dipenuhi implementasi:
        ///
        ///   - Untuk tool yang menulis, Verification WAJIB diisi. Mengembalikan
        ///     null berarti "tidak ada bukti", dan PlanRunner akan
        ///     memperlakukannya sebagai BLOCKED.
        ///   - Jangan melempar exception untuk kegagalan yang bisa diduga -
        ///     kembalikan ToolResult dengan Error terisi. Exception tetap
        ///     ditangkap PlanRunner, tapi kelas error-nya jadi kurang akurat.
        /// </summary>
        Task<ToolResult> ExecuteAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken = default);
    }

    /// <summary>Satu kriteria penerimaan tingkat plan.</summary>
    public sealed class AcceptanceCriterion
    {
        public string Id { get; set; } = string.Empty;
        public string Check { get; set; } = string.Empty;
        public JsonObject? Arguments { get; set; }
        public bool Required { get; set; } = true;
    }

    /// <summary>
    /// Mengevaluasi kriteria penerimaan tingkat plan, SETELAH semua step selesai
    /// dan dokumen dibuka ulang.
    ///
    /// Dipisah dari IToolExecutor karena waktunya berbeda dan maknanya berbeda:
    /// pemeriksaan per step membuktikan step itu jalan, acceptance membuktikan
    /// hasil akhirnya benar setelah disimpan dan dibuka lagi.
    /// </summary>
    public interface IAcceptanceEvaluator
    {
        Task<IReadOnlyList<VerificationCheck>> EvaluateAsync(
            IReadOnlyList<AcceptanceCriterion> criteria,
            CancellationToken cancellationToken = default);
    }
}
