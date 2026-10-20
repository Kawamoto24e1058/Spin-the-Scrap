using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// 決定的な乱数（SplitMix64）。状態が 1 個の ulong なので、
    /// ホスト権威型の同期（要件書 8.2）やセーブで状態をそのまま受け渡せる。
    /// </summary>
    public sealed class Rng
    {
        public ulong State { get; set; }

        public Rng(ulong seed)
        {
            State = seed;
        }

        public ulong NextULong()
        {
            ulong z = State += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0, 1) の一様乱数。</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>[0, maxExclusive) の整数。</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextDouble() * maxExclusive);
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
