using System;

namespace SpinTheScrap.Core
{
    /// <summary>階層の攻略条件の種類（docs/floor-design.md）。階層ごとにランダムに 1 つ選ぶ。</summary>
    public enum FloorObjectiveKind
    {
        /// <summary>施設のどこかにある鍵を見つけ、エレベーターまで持ち帰る。</summary>
        FindKey,
        /// <summary>指定の数だけ敵を倒す。</summary>
        DefeatEnemies,
        /// <summary>発電機にガラクタを入れ、指定の電力（ガラクタの価値の合計）をためる。</summary>
        PowerGenerator,
        /// <summary>指定の特大のガラクタをエレベーターまで運ぶ。</summary>
        HaulHuge,
    }

    /// <summary>階層の攻略条件と進み具合。達成するとエレベーターが動く。</summary>
    public sealed class FloorObjective
    {
        public FloorObjectiveKind Kind { get; }
        /// <summary>達成に必要な量（鍵・特大は 1、敵は体数、発電機は電力）。</summary>
        public double Target { get; }
        public double Progress { get; private set; }
        public bool IsComplete => Progress >= Target;
        public string NameKey => "objective." + Kind;

        public FloorObjective(FloorObjectiveKind kind, double target)
        {
            if (target <= 0) throw new ArgumentOutOfRangeException(nameof(target));
            Kind = kind;
            Target = target;
        }

        /// <summary>
        /// 進み具合を報告する。種類が違う報告は無視する（例：鍵の階層で敵を倒しても進まない）。
        /// この報告で達成したら true。
        /// </summary>
        public bool Report(FloorObjectiveKind kind, double amount = 1)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (kind != Kind || IsComplete) return false;
            Progress = Math.Min(Target, Progress + amount);
            return IsComplete;
        }

        /// <summary>倒れて階層をやり直すとき。</summary>
        public void ResetProgress() => Progress = 0;

        /// <summary>階層の番号から攻略条件を選ぶ。直前と同じ種類は避ける。深いほど目標が大きい。</summary>
        public static FloorObjective Roll(int floor, Rng rng, FloorObjectiveKind? previous = null)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            var kinds = (FloorObjectiveKind[])Enum.GetValues(typeof(FloorObjectiveKind));
            FloorObjectiveKind kind;
            do kind = kinds[rng.Next(kinds.Length)];
            while (previous.HasValue && kind == previous.Value && kinds.Length > 1);
            return new FloorObjective(kind, TargetFor(kind, floor));
        }

        public static double TargetFor(FloorObjectiveKind kind, int floor)
        {
            switch (kind)
            {
                case FloorObjectiveKind.DefeatEnemies: return 2 + floor / 3;
                case FloorObjectiveKind.PowerGenerator: return 15 + 5 * floor;
                default: return 1;
            }
        }
    }
}
