using System;
using System.Globalization;

namespace SpinTheScrap.Core
{
    public enum Language
    {
        Japanese,
        English,
    }

    /// <summary>
    /// インフレする数値の表示（要件書 5.4）。
    /// 日本語は万・億・兆、英語は K・M・B。それを超えたら指数表記。
    /// </summary>
    public static class NumberFormatter
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static readonly (double scale, string suffix)[] JapaneseUnits = { (1e12, "兆"), (1e8, "億"), (1e4, "万") };
        static readonly (double scale, string suffix)[] EnglishUnits = { (1e9, "B"), (1e6, "M"), (1e3, "K") };

        public static string Format(double value, Language language)
        {
            if (double.IsNaN(value)) return "???";
            if (double.IsPositiveInfinity(value)) return "∞";
            if (double.IsNegativeInfinity(value)) return "-∞";
            if (value < 0) return "-" + Format(-value, language);

            var units = language == Language.Japanese ? JapaneseUnits : EnglishUnits;
            double exponentFrom = units[0].scale * (language == Language.Japanese ? 1e4 : 1e3);
            if (value >= exponentFrom) return value.ToString("0.00e+0", Invariant);

            foreach (var (scale, suffix) in units)
            {
                if (value < scale) continue;
                return ThreeSignificant(value / scale) + suffix;
            }
            return Math.Floor(value).ToString("#,0", Invariant);
        }

        static string ThreeSignificant(double scaled)
        {
            if (scaled < 10) return Truncate(scaled, 2).ToString("0.##", Invariant);
            if (scaled < 100) return Truncate(scaled, 1).ToString("0.#", Invariant);
            return Math.Floor(scaled).ToString("#,0", Invariant);
        }

        /// <summary>切り捨て。四捨五入だと 9999.5万 が「10000万」になるため。</summary>
        static double Truncate(double v, int digits)
        {
            double p = Math.Pow(10, digits);
            return Math.Floor(v * p) / p;
        }
    }
}
