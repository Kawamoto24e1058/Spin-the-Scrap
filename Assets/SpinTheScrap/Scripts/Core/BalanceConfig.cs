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
        public int FloorCount = 9;
        /// <summary>この数ごとにボス階層（通常 → 通常 → ボス）。</summary>
        public int BossEvery = 3;
        /// <summary>通常の階で倒す敵の数（階層 1）。階層ごとに +2。</summary>
        public int BaseKillTarget = 8;
        /// <summary>残機。倒れると 1 減ってその階層をやり直す。0 でラン終了。</summary>
        public int StartingLives = 3;
        public double StartingMedals = 10;
        /// <summary>階層が 1 つ深くなるごとに、換金で得られるメダルが増える割合（地下 9 階で ×2.6）。</summary>
        public double MedalValueFloorGrowth = 0.2;

        /// <summary>シンボルの廃棄手数料（× 階層）。</summary>
        public double RemovalFeePerFloor = 3;
        public int MaxRelics = 5;
        public int RelicOffersPerFloor = 3;
        public double BasePlayerHealth = 100;

        /// <summary>階層 1 の施設の部屋数（入口を含む）。2 階層ごとに 1 部屋ずつ増える。</summary>
        public int BaseRoomCount = 4;
        public int MaxRoomCount = 9;

        /// <summary>懐中電灯の電池 1 本で点けていられる時間 (s)。</summary>
        public double FlashlightBatterySeconds = 180;

        /// <summary>どの台も、最初のリールはこれ。</summary>
        public List<string> StartingReel = new List<string> { "empty_can", "empty_can", "empty_can", "empty_can", "empty_can" };

        public bool IsBossFloor(int floor) => floor >= 1 && BossEvery > 0 && floor % BossEvery == 0;

        /// <summary>ボス階層のボス。1 体目は巨体、2 体目は電気を吸う機械、3 体目以降は群れを呼ぶ人形。</summary>
        public EnemyKind BossFor(int floor)
        {
            if (!IsBossFloor(floor)) throw new ArgumentException("Not a boss floor: " + floor, nameof(floor));
            int index = floor / BossEvery;
            return index <= 1 ? EnemyKind.Brute : index == 2 ? EnemyKind.Dynamo : EnemyKind.Puppeteer;
        }

        public int KillTargetFor(int floor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            return BaseKillTarget + 2 * (floor - 1);
        }

        public int RoomCountFor(int floor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            return Math.Min(BaseRoomCount + (floor - 1) / 2, MaxRoomCount);
        }

        /// <summary>試作用（3 階層：通常 → 通常 → ボス）。</summary>
        public static BalanceConfig CreateMvp()
        {
            return new BalanceConfig { FloorCount = 3 };
        }
    }
}
