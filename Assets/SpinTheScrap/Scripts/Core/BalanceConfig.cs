using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// ランの数値（要件書 3.1）。すべて「調整前の初期値」。
    /// Tools/Simulator で期待値を見ながら調整する前提。
    /// </summary>
    public sealed class BalanceConfig
    {
        public double StartingMoney = 10;
        /// <summary>完済までのサイクル数（発売時 4、MVP 2）。</summary>
        public int CycleCount = 4;
        public int DivesPerCycle = 3;
        /// <summary>サイクルごとのノルマ。CycleCount を超えたらエンドレスで EndlessQuotaGrowth 倍ずつ上がる。</summary>
        public double[] Quotas = { 60, 300, 1500, 7500 };
        public double EndlessQuotaGrowth = 5;

        /// <summary>サイクル 1 の 1 回目の費用。</summary>
        public double BaseSpinCost = 2;
        /// <summary>サイクルが進むごとの基本費用の倍率。</summary>
        public double SpinCostCycleGrowth = 2;
        /// <summary>同一サイクル内で回すごとの費用の倍率（要件書 5.1「回すほど費用が上がる」）。</summary>
        public double SpinCostStepGrowth = 1.25;

        /// <summary>闇市でのシンボル廃棄手数料（× サイクル）。</summary>
        public double RemovalFeePerCycle = 5;
        public int MaxRelics = 5;
        public int RelicOffersPerCycle = 3;

        /// <summary>サイクル 1 の施設の部屋数（入口を含む）。サイクルごとに 1 部屋ずつ増える。</summary>
        public int BaseRoomCount = 4;
        public int MaxRoomCount = 9;

        public List<string> StartingReel = new List<string> { "empty_can", "empty_can", "empty_can", "empty_can", "empty_can" };

        public double QuotaFor(int cycle)
        {
            if (cycle < 1) throw new ArgumentOutOfRangeException(nameof(cycle));
            if (Quotas.Length == 0) throw new InvalidOperationException("Quotas is empty");
            if (cycle <= Quotas.Length) return Quotas[cycle - 1];
            return Quotas[Quotas.Length - 1] * Math.Pow(EndlessQuotaGrowth, cycle - Quotas.Length);
        }

        public int RoomCountFor(int cycle)
        {
            if (cycle < 1) throw new ArgumentOutOfRangeException(nameof(cycle));
            return Math.Min(BaseRoomCount + cycle - 1, MaxRoomCount);
        }

        public double SpinCostFor(int cycle, int spinsThisCycle)
        {
            double cost = BaseSpinCost * Math.Pow(SpinCostCycleGrowth, cycle - 1) * Math.Pow(SpinCostStepGrowth, spinsThisCycle);
            return Math.Ceiling(cost);
        }

        /// <summary>MVP 用（2 サイクル）。</summary>
        public static BalanceConfig CreateMvp()
        {
            return new BalanceConfig { CycleCount = 2, Quotas = new double[] { 60, 300 } };
        }
    }
}
