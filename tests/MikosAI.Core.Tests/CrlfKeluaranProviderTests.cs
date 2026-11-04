using MikosAI.Core.Agents;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// CRLF adalah terminator baris, bukan repaint terminal.
    ///
    /// BAGAIMANA INI DITEMUKAN, DAN KENAPA ITU PENTING.
    ///
    /// Pembersih keluaran provider memecah teks pada '\n', lalu memecah setiap
    /// baris pada '\r' dan mengambil segmen TERAKHIR - dengan alasan terminal
    /// menimpa baris yang sama, jadi hanya segmen terakhir yang relevan.
    ///
    /// Alasan itu benar untuk repaint sungguhan. Tapi pada teks CRLF ia
    /// menghancurkan segalanya:
    ///
    ///     "a\r\nb"  --split('\n')-->  ["a\r", "b"]
    ///     "a\r"     --split('\r')-->  ["a", ""]     ambil terakhir -> ""
    ///
    /// Setiap baris menyisakan string kosong dan dibuang. Hanya baris TERAKHIR,
    /// yang tidak punya '\r' di ujung, yang selamat.
    ///
    /// agy di Windows mengeluarkan CRLF. Artinya jawaban JSON-nya menyusut
    /// menjadi satu baris penutup "}", parser gagal, dan panel menampilkan
    /// jawaban kosong atau teks tak berarti.
    ///
    /// Bug ini tersembunyi LAMA karena seluruh data uji di repo ini memakai LF.
    /// Ia baru terlihat ketika satu berkas test ditulis ulang dengan CRLF - dan
    /// yang berubah hanya yang KELIHATAN, bukan bug-nya.
    ///
    /// Test di bawah memakai "\r\n" eksplisit, bukan literal multi-baris, supaya
    /// tidak bisa ikut berubah karena pengaturan line ending berkas.
    /// </summary>
    public class CrlfKeluaranProviderTests
    {
        [Fact]
        public void baris_crlf_tidak_dihapus()
        {
            var bersih = ProviderOutcome.Bersihkan("baris satu\r\nbaris dua\r\nbaris tiga");

            Assert.Contains("baris satu", bersih);
            Assert.Contains("baris dua", bersih);
            Assert.Contains("baris tiga", bersih);
        }

        [Fact]
        public void lf_biasa_tetap_bekerja_seperti_sebelumnya()
        {
            var bersih = ProviderOutcome.Bersihkan("baris satu\nbaris dua");

            Assert.Contains("baris satu", bersih);
            Assert.Contains("baris dua", bersih);
        }

        /// <summary>
        /// Repaint SUNGGUHAN harus tetap dipangkas: '\r' tanpa '\n' di belakangnya
        /// memang berarti terminal menimpa baris yang sama.
        ///
        /// Ini yang membuat perbaikannya bukan sekadar "hapus pemecahan \r".
        /// </summary>
        [Fact]
        public void repaint_sungguhan_tetap_dipangkas()
        {
            var bersih = ProviderOutcome.Bersihkan("Memuat 10%\rMemuat 50%\rMemuat 100%\r\nselesai");

            Assert.DoesNotContain("10%", bersih);
            Assert.DoesNotContain("50%", bersih);
            Assert.Contains("Memuat 100%", bersih);
            Assert.Contains("selesai", bersih);
        }

        /// <summary>
        /// Kasus yang sebenarnya: envelope JSON ber-CRLF harus tetap terparsing.
        ///
        /// Tanpa perbaikan, yang tersisa hanyalah baris "}" - dan Summary berisi
        /// teks itu alih-alih jawaban AI.
        /// </summary>
        [Fact]
        public void envelope_json_crlf_tetap_terparsing()
        {
            var json =
                "{\r\n"
                + "  \"status\": \"SUCCESS\",\r\n"
                + "  \"response\": \"Dokumen aktif: Part1.SLDPRT\"\r\n"
                + "}";

            var hasil = ProviderOutcome.Shape(json, 0);

            Assert.Null(hasil.Failure);
            Assert.Equal("Dokumen aktif: Part1.SLDPRT", hasil.Summary);
        }

        /// <summary>
        /// stream-json: NDJSON ber-CRLF. Baris hasil ada di akhir, jadi tanpa
        /// perbaikan ia justru satu-satunya yang selamat - dan itu menyembunyikan
        /// bug-nya pada kasus ini.
        ///
        /// Yang diuji di sini adalah baris SEBELUMNYA tidak hilang, karena di
        /// situlah potongan teks bertahap berada.
        /// </summary>
        [Fact]
        public void ndjson_crlf_mempertahankan_semua_barisnya()
        {
            var ndjson =
                "{\"event\":\"init\"}\r\n"
                + "{\"event\":\"text_delta\",\"text_delta\":{\"text\":\"halo \"}}\r\n"
                + "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"halo dunia\"}}\r\n";

            var bersih = ProviderOutcome.Bersihkan(ndjson);

            Assert.Contains("\"event\":\"init\"", bersih);
            Assert.Contains("text_delta", bersih);
            Assert.Contains("halo dunia", bersih);

            var hasil = ProviderOutcome.Shape(ndjson, 0);

            Assert.Null(hasil.Failure);
            Assert.Equal("halo dunia", hasil.Summary);
        }

        /// <summary>
        /// Satu baris JSON dengan CRLF di ujung - bentuk paling mungkin dari
        /// `--output-format json` di Windows.
        ///
        /// Tanpa perbaikan, SELURUH jawaban hilang: "{...}\r\n" menyisakan
        /// kosong, dan yang tersisa adalah string hampa.
        /// </summary>
        [Fact]
        public void satu_baris_json_dengan_crlf_di_ujung_tidak_hilang()
        {
            var hasil = ProviderOutcome.Shape(
                "{\"status\":\"SUCCESS\",\"response\":\"beres\"}\r\n", 0);

            Assert.Null(hasil.Failure);
            Assert.Equal("beres", hasil.Summary);
        }
    }
}
