using System.Collections.Generic;
using System.Linq;

namespace MikosAI.Core.Tools
{
    /// <summary>
    /// Field yang ADA di kontrak tapi BELUM diisi pembaca, DIHITUNG PER JAWABAN.
    ///
    /// KENAPA DIHITUNG, BUKAN DAFTAR TETAP.
    ///
    /// Versi pertama kelas ini memuat dua daftar konstan. Itu cukup selama
    /// tidak ada satu pun field yang terisi, tapi begitu implementasinya
    /// datang, daftar konstan akan terus menyebut field yang sudah terisi -
    /// dan mekanisme kejujuran yang berbohong ke arah sebaliknya sama tidak
    /// bergunanya.
    ///
    /// Jadi daftarnya diturunkan dari bacaan yang sebenarnya: sebuah field
    /// masuk HANYA kalau nilainya null, yaitu "tidak diketahui". Kosong dan nol
    /// TIDAK masuk, karena keduanya berarti sudah dibaca dan memang tidak ada.
    ///
    /// Kenapa ini dikirim sama sekali: null saja tetap terbaca sebagai "tidak
    /// ada", bukan "tidak diketahui". Pemanggil perlu tahu bedanya pada saat ia
    /// memanggil tool - bukan dengan membaca dokumen yang tidak ada di
    /// depannya.
    ///
    /// Yang paling berbahaya di antara field-field ini consumedByFeatures:
    /// "tidak dipakai fitur apa pun" adalah alasan yang masuk akal untuk
    /// MENGHAPUS sebuah sketsa.
    /// </summary>
    public static class FieldBelumDibaca
    {
        public static IReadOnlyList<string> UntukFitur(IReadOnlyList<FeatureReading> fitur)
        {
            var hasil = new List<string>();

            // Satu fitur saja yang tidak terbaca sudah cukup untuk membuat
            // seluruh jawaban tidak bisa dipercaya untuk field itu. Melaporkan
            // "sebagian terbaca" akan menggoda pemanggil memakai yang terbaca
            // dan melupakan sisanya.
            if (fitur.Any(f => f.ParentSketches is null))
            {
                hasil.Add("parentSketches");
            }

            return hasil;
        }

        public static IReadOnlyList<string> UntukSketsa(IReadOnlyList<SketchReading> sketsa)
        {
            var hasil = new List<string>();

            if (sketsa.Any(s => s.ConsumedByFeatures is null)) hasil.Add("consumedByFeatures");
            if (sketsa.Any(s => s.Dependents is null)) hasil.Add("dependents");
            if (sketsa.Any(s => s.DrivingDimensionCount is null)) hasil.Add("drivingDimensionCount");
            if (sketsa.Any(s => s.DrivenDimensionCount is null)) hasil.Add("drivenDimensionCount");
            if (sketsa.Any(s => s.UnknownDrivenStateCount is null)) hasil.Add("unknownDrivenStateCount");
            if (sketsa.Any(s => s.DimensionCountRead is null)) hasil.Add("dimensionCountRead");

            if (sketsa.Any(s => s.OpenEndpointCount is null)) hasil.Add("openEndpointCount");
            if (sketsa.Any(s => s.BranchPointCount is null)) hasil.Add("branchPointCount");
            if (sketsa.Any(s => s.OpenEndpointOnSegmentCount is null)) hasil.Add("openEndpointOnSegmentCount");
            if (sketsa.Any(s => s.JoinedEndpointOnSegmentCount is null)) hasil.Add("joinedEndpointOnSegmentCount");
            if (sketsa.Any(s => s.NearlyCoincidentEndpointCount is null)) hasil.Add("nearlyCoincidentEndpointCount");
            if (sketsa.Any(s => s.EndpointToleranceMm is null)) hasil.Add("endpointToleranceMm");
            if (sketsa.Any(s => s.SegmentsNotAnalyzed is null)) hasil.Add("segmentsNotAnalyzed");
            if (sketsa.Any(s => s.Limits is null)) hasil.Add("limits");

            return hasil;
        }
    }
}
