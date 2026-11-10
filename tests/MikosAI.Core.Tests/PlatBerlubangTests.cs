using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Execution;
using MikosAI.Core.Plan;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Plat berlubang lewat chat dan lewat tool MCP.
    ///
    /// Jalur tulis lubangnya sudah SW_PASS 3/3 di SolidWorks nyata. Yang diuji
    /// di sini adalah PINTU MASUKNYA: apa yang diterima, dan - yang lebih
    /// penting - apa yang ditolak sebelum SolidWorks disentuh.
    ///
    /// Setiap penolakan di bawah melindungi satu aturan yang sama: jangan
    /// menebak nilai engineering, dan jangan membuat sesuatu di tempat yang
    /// tidak diminta.
    /// </summary>
    public class PlatBerlubangTests
    {
        private const string Plat = "buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore ISO jarak 20 mm dari tepi";

        // ==================================================================
        // Grammar chat
        // ==================================================================

        [Fact]
        public void plat_lengkap_diterima_dan_posisinya_diturunkan_dari_jarak_tepi()
        {
            var r = BlockChatRequest.Parse(Plat);

            Assert.True(r.Handled && r.Valid, r.Message);
            Assert.Equal(100, r.Width); Assert.Equal(80, r.Height); Assert.Equal(10, r.Depth);
            Assert.NotNull(r.Holes);
            Assert.Equal("ISO", r.Holes!.Standard);
            Assert.Equal("M6", r.Holes.FastenerSize);

            // Diturunkan dari jarak 20 mm yang DINYATAKAN pengguna, di
            // permukaan atas z = 10 - persis posisi yang sudah SW_PASS.
            var posisi = r.Holes.Positions.Select(p => (p.X, p.Y, p.Z)).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
            Assert.Equal(new[] { (20.0, 20.0, 10.0), (80.0, 20.0, 10.0), (20.0, 60.0, 10.0), (80.0, 60.0, 10.0) }, posisi);
        }

        /// <summary>
        /// Tanpa standar, satu-satunya cara melanjutkan adalah memilihkannya.
        /// Itu yang dilarang.
        /// </summary>
        [Fact]
        public void tanpa_standar_ditolak_bukan_diberi_iso()
        {
            var r = BlockChatRequest.Parse("buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore jarak 20 mm dari tepi");

            Assert.True(r.Handled);
            Assert.False(r.Valid);
            Assert.Null(r.Holes);
            Assert.Contains("standar", r.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore DIN jarak 20 mm dari tepi")]   // standar belum terbukti
        [InlineData("buat plat 100 x 80 x 10 mm dengan 6 lubang M6 counterbore ISO jarak 20 mm dari tepi")]   // bukan pola 4 pojok
        [InlineData("buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore ISO jarak 40 mm dari tepi")]   // jarak >= setengah tinggi
        [InlineData("buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore ISO jarak 0 mm dari tepi")]    // nol
        [InlineData("buat plat 100 x 80 x 10 mm dengan 4 lubang M6 countersink ISO jarak 20 mm dari tepi")]   // tipe belum dibuka
        [InlineData("buat plat 100 x 80 x 10 mm dengan beberapa lubang")]                                   // tidak lengkap
        public void permintaan_lubang_yang_tidak_lengkap_atau_belum_terbukti_ditolak(string teks)
        {
            var r = BlockChatRequest.Parse(teks);

            Assert.True(r.Handled);
            Assert.False(r.Valid);
            Assert.Null(r.Holes);
            Assert.NotEmpty(r.Message);
        }

        /// <summary>
        /// Kata "plat" terlalu umum untuk membajak pertanyaan biasa. Hanya
        /// kalimat yang MEMULAI perintah membuat yang ditangani jalur pintas.
        /// </summary>
        [Theory]
        [InlineData("apa isi plat-referensi?")]
        [InlineData("berapa volume plat ini")]
        public void pertanyaan_tentang_plat_tetap_ke_ai(string teks)
        {
            Assert.False(BlockChatRequest.Parse(teks).Handled);
        }

        [Fact]
        public void balok_polos_tetap_tanpa_lubang()
        {
            var r = BlockChatRequest.Parse("buat balok 100 x 80 x 10 mm");
            Assert.True(r.Valid);
            Assert.Null(r.Holes);
        }

        // ==================================================================
        // Flow (tool MCP dan chat memakai jalur yang sama)
        // ==================================================================

        private static ToolCatalog Catalog()
            => ToolCatalog.Load(RepoFiles.ToolsJson, CapabilityLedger.Parse(File.ReadAllText(RepoFiles.CapabilitiesYaml)));

        private static JsonObject Lubang(params (double x, double y, double z)[] titik)
        {
            var posisi = new JsonArray();
            foreach (var t in titik) posisi.Add(new JsonObject { ["x"] = t.x, ["y"] = t.y, ["z"] = t.z });
            return new JsonObject { ["holeType"] = "counterbore", ["standard"] = "ISO", ["fastenerSize"] = "M6", ["positions"] = posisi };
        }

        private static JsonObject Args(JsonObject? lubang, string? id = null)
        {
            var a = new JsonObject { ["width"] = 100, ["height"] = 80, ["depth"] = 10, ["operationId"] = id ?? Guid.NewGuid().ToString("N") };
            if (lubang is not null) a["holes"] = lubang;
            return a;
        }

        private static readonly (double, double, double)[] EmpatPojok =
            { (20, 20, 10), (80, 20, 10), (20, 60, 10), (80, 60, 10) };

        [Fact]
        public async Task lubang_sampai_ke_session_dan_tercatat_di_plan()
        {
            HoleSpec? diterima = null;
            var session = new Session();
            var flow = new ParametricBlockFlow(Catalog(), (w, h, d, holes, p) => { diterima = holes; return session; }, id => id + ".SLDPRT");

            var hasil = await flow.ExecuteAsync(Args(Lubang(EmpatPojok)));

            Assert.True(hasil.Ok, hasil.Error?.Message);
            Assert.NotNull(diterima);
            Assert.Equal(4, diterima!.Positions.Count);

            // Plan yang tercatat adalah plan yang dijalankan - termasuk
            // lubangnya. PlanValidator memeriksanya terhadap inputSchema tool,
            // jadi enum standar di kontrak ikut ditegakkan.
            var plan = (JsonObject)hasil.Data!["plan"]!;
            var argLangkah = plan["steps"]![0]!["arguments"]!.AsObject();
            Assert.Equal("ISO", argLangkah["holes"]!["standard"]!.GetValue<string>());
            Assert.Equal(4, argLangkah["holes"]!["positions"]!.AsArray().Count);
        }

        [Theory]
        [InlineData(50, 40, 0)]      // z bukan permukaan atas
        [InlineData(50, 40, 5)]      // di tengah ketebalan
        [InlineData(0, 40, 10)]      // tepat di tepi
        [InlineData(150, 40, 10)]    // di luar balok
        [InlineData(50, -5, 10)]     // di luar balok
        public async Task posisi_di_luar_permukaan_atas_ditolak_sebelum_solidworks_disentuh(double x, double y, double z)
        {
            var dibuat = 0;
            var flow = new ParametricBlockFlow(Catalog(), (w, h, d, holes, p) => { dibuat++; return new Session(); }, id => id);

            var hasil = await flow.ExecuteAsync(Args(Lubang((x, y, z))));

            Assert.False(hasil.Ok);
            Assert.Equal(ErrorKind.InvalidArgument, hasil.Error!.Kind);
            Assert.False(hasil.ModelChanged);
            Assert.Equal(0, dibuat);
        }

        [Theory]
        [InlineData("standard", null)]
        [InlineData("standard", "DIN")]
        [InlineData("holeType", "countersink")]
        [InlineData("fastenerSize", null)]
        public async Task nilai_yang_hilang_atau_belum_terbukti_ditolak(string kunci, string? nilai)
        {
            var dibuat = 0;
            var flow = new ParametricBlockFlow(Catalog(), (w, h, d, holes, p) => { dibuat++; return new Session(); }, id => id);
            var lubang = Lubang(EmpatPojok);
            if (nilai is null) lubang.Remove(kunci); else lubang[kunci] = nilai;

            var hasil = await flow.ExecuteAsync(Args(lubang));

            Assert.False(hasil.Ok);
            Assert.Equal(0, dibuat);
        }

        [Fact]
        public async Task posisi_kembar_ditolak()
        {
            var flow = new ParametricBlockFlow(Catalog(), (w, h, d, holes, p) => new Session(), id => id);
            var hasil = await flow.ExecuteAsync(Args(Lubang((20, 20, 10), (20, 20, 10))));
            Assert.False(hasil.Ok);
        }

        /// <summary>
        /// operationId yang sama dengan lubang berbeda harus ditolak. Tanpa
        /// lubang di sidik jari, panggilan kedua akan menerima hasil plat
        /// pertama - jawaban yang salah dengan ID yang benar.
        /// </summary>
        [Fact]
        public async Task operation_id_yang_sama_dengan_lubang_berbeda_ditolak()
        {
            var session = new Session();
            var flow = new ParametricBlockFlow(Catalog(), (w, h, d, holes, p) => session, id => id + ".SLDPRT");
            var id = Guid.NewGuid().ToString("N");

            Assert.True((await flow.ExecuteAsync(Args(Lubang(EmpatPojok), id))).Ok);
            var kedua = await flow.ExecuteAsync(Args(Lubang((30, 30, 10)), id));

            Assert.False(kedua.Ok);
            Assert.Equal(1, session.Executions);
        }

        private sealed class Session : IBlockExecutionSession
        {
            public int Executions;

            public Task<ToolResult> ExecuteAsync(string name, JsonObject args, CancellationToken cancellationToken = default)
            {
                Executions++;
                var r = new ToolResult
                {
                    Ok = true, CapabilityId = ParametricBlockFlow.CapabilityId,
                    Verification = VerificationGate.Evaluate(new[] { VerificationCheck.Pass("native") }),
                    Evidence = new ToolEvidence { DocumentPath = "new.SLDPRT" },
                };
                r.IsMutation = true; r.ModelChanged = true;
                return Task.FromResult(r);
            }

            public Task<IReadOnlyList<VerificationCheck>> EvaluateAsync(IReadOnlyList<AcceptanceCriterion> criteria, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<VerificationCheck>>(new[] { VerificationCheck.Pass("fresh") });
        }
    }
}
