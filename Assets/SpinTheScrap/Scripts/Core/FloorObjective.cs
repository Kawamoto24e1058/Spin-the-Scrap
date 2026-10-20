using System;

namespace SpinTheScrap.Core
{
    /// <summary>階層の攻略条件の種類（docs/floor-design.md）。</summary>
    public enum FloorObjectiveKind
    {
        /// <summary>通常の階：指定の数だけ敵を倒す。</summary>
        DefeatEnemies,
        /// <summary>ボス階層：ボスを倒す。</summary>
        DefeatBoss,
    }

    /// <summary>階層の攻略条件と進み具合。達成するとエレベーターが動く。</summary>
    public sealed class FloorObjective
    {
        public FloorObjectiveKind Kind { get; }
        /// <summary>達成に必要な量（敵の体数、ボスは 1）。</summary>
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

        /// <summary>進み具合を報告する。種類が違う報告は無視する。この報告で達成したら true。</summary>
        public bool Report(FloorObjectiveKind kind, double amount = 1)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (kind != Kind || IsComplete) return false;
            Progress = Math.Min(Target, Progress + amount);
            return IsComplete;
        }

        /// <summary>倒れて階層をやり直すとき。</summary>
        public void ResetProgress() => Progress = 0;

        /// <summary>階層の攻略条件。ボス階層ならボス、通常の階なら敵の数（深いほど多い）。</summary>
        public static FloorObjective For(int floor, BalanceConfig config)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            if (config.IsBossFloor(floor)) return new FloorObjective(FloorObjectiveKind.DefeatBoss, 1);
            return new FloorObjective(FloorObjectiveKind.DefeatEnemies, config.KillTargetFor(floor));
        }
    }
}
