using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MikosAI.Core.Plan
{
    /// <summary>Grammar awal chat balok: ukuran eksplisit, tanpa nilai tebakan.</summary>
    public sealed class BlockChatRequest
    {
        public bool Handled { get; private set; }
        public bool Valid { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }
        public double Depth { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public static BlockChatRequest Parse(string text)
        {
            if (!Regex.IsMatch(text,@"\b(balok|block)\b",RegexOptions.IgnoreCase)) return new BlockChatRequest();
            var match=Regex.Match(text,@"^\s*(?:buat(?:kan)?|bikin|create|make)\s+(?:(?:sebuah|a)\s+)?(?:balok|block)\s+(?:(?:ukuran|berukuran)\s+)?(?<w>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<h>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<d>\d+(?:[.,]\d+)?)\s*mm\s*[.!]?\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            var result=new BlockChatRequest { Handled=true,Message="Tuliskan panjang × lebar × tebal dalam mm, misalnya: buat balok 120 × 50 × 12 mm. Saat ini balok dibuat pada part baru." };
            if (!match.Success) return result;
            double Value(string name) => double.TryParse(match.Groups[name].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value) ? value : double.NaN;
            result.Width=Value("w"); result.Height=Value("h"); result.Depth=Value("d");
            result.Valid=new[]{result.Width,result.Height,result.Depth}.AllValid();
            if (!result.Valid) result.Message="Setiap ukuran harus antara 0,001 dan 10.000 mm.";
            return result;
        }
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
