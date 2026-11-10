using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MikosAI.Core.Plan
{
    /// <summary>
    /// Grammar chat deterministik untuk balok dan plat: ukuran eksplisit,
    /// tanpa nilai tebakan.
    ///
    /// Ini jalur PINTAS tanpa LLM. Bentuknya sengaja kaku: kalimat yang lebih
    /// bebas dilayani AI lewat tool MCP, yang memeriksa argumen terhadap kontrak
    /// yang sama. Kekakuan di sini bukan keterbatasan yang memalukan - ia yang
    /// membuat jalur ini tidak pernah menebak.
    ///
    /// Bentuk yang diterima:
    ///   buat balok 100 x 80 x 10 mm
    ///   buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore ISO jarak 20 mm dari tepi
    ///
    /// Kalimat yang MENYEBUT lubang tapi tidak lengkap ditolak dengan petunjuk.
    /// Melengkapinya sendiri - memilih standar, jumlah, atau jarak - adalah
    /// persis hal yang dilarang.
    /// </summary>
    public sealed class BlockChatRequest
    {
        public bool Handled { get; private set; }
        public bool Valid { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }
        public double Depth { get; private set; }

        /// <summary>null berarti balok polos.</summary>
        public HoleSpec? Holes { get; private set; }

        public string Message { get; private set; } = string.Empty;

        private const string ContohLubang =
            "buat plat 100 x 80 x 10 mm dengan 4 lubang M6 counterbore ISO jarak 20 mm dari tepi";

        public static BlockChatRequest Parse(string text)
        {
            // Balok ditangani kalau katanya muncul di mana pun - perilaku lama
            // yang dijaga test. Plat hanya kalau kalimatnya MEMULAI perintah
            // membuat: kata "plat" terlalu umum ("plat-referensi") untuk
            // membajak pertanyaan biasa yang seharusnya ke AI.
            var sebutBalok=Regex.IsMatch(text,@"\b(balok|block)\b",RegexOptions.IgnoreCase);
            var perintahPlat=Regex.IsMatch(text,@"^\s*(?:buat(?:kan)?|bikin|create|make)\s+(?:(?:sebuah|a)\s+)?(?:plat|pelat|plate)\b",RegexOptions.IgnoreCase);
            if (!sebutBalok && !perintahPlat) return new BlockChatRequest();

            var result=new BlockChatRequest
            {
                Handled=true,
                Message="Tuliskan panjang × lebar × tebal dalam mm, misalnya: buat balok 120 × 50 × 12 mm. "
                    + "Untuk plat berlubang: " + ContohLubang + ". Saat ini dibuat pada part baru.",
            };

            var match=Regex.Match(text,
                @"^\s*(?:buat(?:kan)?|bikin|create|make)\s+(?:(?:sebuah|a)\s+)?(?:balok|block|plat|pelat|plate)\s+(?:(?:ukuran|berukuran)\s+)?"
                + @"(?<w>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<h>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<d>\d+(?:[.,]\d+)?)\s*mm"
                + @"(?<sisa>.*?)\s*[.!]?\s*$",
                RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            if (!match.Success) return result;

            result.Width=Angka(match.Groups["w"].Value);
            result.Height=Angka(match.Groups["h"].Value);
            result.Depth=Angka(match.Groups["d"].Value);
            if (!new[]{result.Width,result.Height,result.Depth}.AllValid())
            {
                result.Message="Setiap ukuran harus antara 0,001 dan 10.000 mm.";
                return result;
            }

            var sisa=match.Groups["sisa"].Value.Trim();
            if (sisa.Length==0)
            {
                result.Valid=true;
                return result;
            }

            if (!Regex.IsMatch(sisa,@"lubang|hole",RegexOptions.IgnoreCase))
                return result;   // sisa kalimat yang tidak dikenali: tolak, jangan diabaikan

            return BacaLubang(result,sisa);
        }

        private static BlockChatRequest BacaLubang(BlockChatRequest result,string sisa)
        {
            var m=Regex.Match(sisa,
                @"^(?:dengan|with)\s+(?<n>\d+)\s+lubang\s+(?<size>M\d+(?:[.,]\d+)?)\s+(?<type>counterbore)"
                + @"(?:\s+(?<std>[A-Za-z]+))?\s+(?:jarak|inset)\s+(?<inset>\d+(?:[.,]\d+)?)\s*mm\s+dari\s+tepi$",
                RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

            if (!m.Success)
            {
                result.Message="Permintaan lubang belum lengkap atau belum didukung lewat chat langsung. "
                    + "Sebutkan jumlah, ukuran, tipe, standar, dan jarak dari tepi, misalnya: " + ContohLubang + ".";
                return result;
            }

            var std=m.Groups["std"].Success ? m.Groups["std"].Value : null;
            if (std is null || std.Equals("jarak",StringComparison.OrdinalIgnoreCase))
            {
                result.Message="Sebutkan standar lubangnya - tidak ada default. Yang sudah terbukti: ISO. Contoh: " + ContohLubang + ".";
                return result;
            }
            if (!std.Equals("ISO",StringComparison.OrdinalIgnoreCase))
            {
                result.Message=$"Standar '{std}' belum terbukti lewat jalur ini. Yang dibuka: ISO.";
                return result;
            }

            if (!int.TryParse(m.Groups["n"].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var jumlah) || jumlah!=4)
            {
                result.Message="Lewat chat langsung, lubang hanya didukung sebagai 4 lubang di pojok "
                    + "(jarak sama dari tiap tepi). Untuk pola lain, minta AI menyusun posisinya.";
                return result;
            }

            var inset=Angka(m.Groups["inset"].Value);
            var w=result.Width; var h=result.Height; var d=result.Depth;
            if (double.IsNaN(inset) || inset<=0 || inset>=w/2 || inset>=h/2)
            {
                result.Message=$"Jarak dari tepi harus lebih dari 0 dan kurang dari setengah ukuran plat ({Math.Min(w,h)/2} mm).";
                return result;
            }

            // Posisi DITURUNKAN dari jarak yang dinyatakan pengguna, bukan
            // ditebak. Balok menempati x 0..w, y 0..h, dan permukaan atasnya
            // di z = d.
            var posisi=new[]
            {
                new Titik3D(inset,inset,d),     new Titik3D(w-inset,inset,d),
                new Titik3D(inset,h-inset,d),   new Titik3D(w-inset,h-inset,d),
            };

            var ukuran=m.Groups["size"].Value.ToUpperInvariant().Replace(',','.');
            try { result.Holes=HoleSpec.Buat("counterbore","ISO",ukuran,posisi); }
            catch(ArgumentException e) { result.Message=e.Message; return result; }

            result.Valid=true;
            return result;
        }

        private static double Angka(string teks)
            => double.TryParse(teks.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var v) ? v : double.NaN;
    }

    internal static class BlockSizeValidation
    {
        internal static bool AllValid(this double[] values)
        {
            foreach(var n in values) if (double.IsNaN(n)||double.IsInfinity(n)||n<0.001||n>10000) return false;
            return true;
        }
    }
}
