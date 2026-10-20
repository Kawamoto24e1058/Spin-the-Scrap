using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// ランの数値（docs/floor-design.md）。すべて「調整前の初期値」。
    /// Tools/Simulator で期待値を見ながら調整する前提。
    /// </summary>
    public sealed class BalanceConfig
    {
        /// <summary>ランの階層数。最後の階層を攻略したらクリア。</summary>
        public int FloorCount = 10;
        /// <summary>残機。倒れると 1 減ってその階層をやり直す。0 でラン終了。</summary>
        public int StartingLives = 3;
        public double StartingMedals = 10;

        /// <summary>階層 1 の 1 回目の費用。</summary>
        public double BaseSpinCost = 2;
        /// <summary>階層が 1 つ深くなるごとの基本費用の倍率。</summary>
        public double SpinCostFloorGrowth = 1.5;
        /// <summary>同じ階層で回すごとの費用の倍率（回すほど費用が上がる）。</summary>
        public double SpinCostStepGrowth = 1.25;

        /// <summary>シンボルの廃棄手数料（× 階層）。</summary>
        public double RemovalFeePerFloor = 3;
        public int MaxRelics = 5;
        public int RelicOffersPerFloor = 3;

        /// <summary>階層 1 の施設の部屋数（入口を含む）。2 階層ごとに 1 部屋ずつ増える。</summary>
        public int BaseRoomCount = 4;
        public int MaxRoomCount = 9;

        /// <summary>懐中電灯の電池 1 本で点けていられる時間 (s)。</summary>
        public double FlashlightBatterySeconds = 180;

        public List<string> StartingReel = new List<string> { "empty_can", "empty_can", "empty_can", "empty_can", "empty_can" };

        public int RoomCountFor(int floor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            return Math.Min(BaseRoomCount + (floor - 1) / 2, MaxRoomCount);
        }

        public double SpinCostFor(int floor, int spinsThisFloor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            double cost = BaseSpinCost * Math.Pow(SpinCostFloorGrowth, floor - 1) * Math.Pow(SpinCostStepGrowth, spinsThisFloor);
            return Math.Ceiling(cost);
        }

        /// <summary>MVP 用（5 階層）。</summary>
        public static BalanceConfig CreateMvp()
        {
            return new BalanceConfig { FloorCount = 5 };
        }
    }
}
