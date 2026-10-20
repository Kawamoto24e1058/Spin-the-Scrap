using System;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// 潜行中の体力とハザードの数値（要件書 4.2）。すべて調整前の初期値。
    /// Unity 側（PlayerHealth・ElectricPuddle など）はここの値と計算だけを使う。
    /// </summary>
    public sealed class HazardRules
    {
        public double MaxHealth = 100;

        /// <summary>
        /// この速さ (m/s) 以下の着地では傷つかない。プレイヤーの重力は 20m/s² なので、
        /// 中 2 階（2m）から飛び降りると約 8.9m/s（無傷）、4m から落ちると約 12.6m/s。
        /// </summary>
        public double FallDamageStartSpeed = 9.5;
        /// <summary>FallDamageStartSpeed を 1m/s 超えるごとのダメージ。</summary>
        public double FallDamagePerSpeed = 15;

        /// <summary>電気が流れる水たまりの中にいる間、1 秒あたりのダメージ。</summary>
        public double PuddleDamagePerSecond = 30;
        /// <summary>金属のガラクタを投げ込むとショートして止まる時間 (s)。</summary>
        public double PuddleShortCircuitSeconds = 10;
        /// <summary>水たまりをショートさせられるタグ。</summary>
        public SymbolTag ShortCircuitTags = SymbolTag.Metal;

        /// <summary>着地の速さ (m/s、下向きを正) から落下ダメージを求める。</summary>
        public double FallDamage(double landingSpeed)
        {
            if (landingSpeed <= FallDamageStartSpeed) return 0;
            return Math.Ceiling((landingSpeed - FallDamageStartSpeed) * FallDamagePerSpeed);
        }

        public bool CanShortCircuit(SymbolTag tags) => tags.HasAny(ShortCircuitTags);

        /// <summary>絶縁ブーツを履いているときの、水たまりのダメージの倍率。</summary>
        public double InsulatedBootsPuddleFactor = 0.3;

        /// <summary>水たまりの中に seconds 秒いたときのダメージ。</summary>
        public double PuddleDamage(double seconds, bool insulatedBoots) =>
            PuddleDamagePerSecond * seconds * (insulatedBoots ? InsulatedBootsPuddleFactor : 1);

        /// <summary>包帯 1 つで回復する体力。</summary>
        public double BandageHeal = 40;

        /// <summary>入口以外の部屋に水たまりが出る確率。奥ほど高い。</summary>
        public double PuddleChance(RoomKind kind, int depth)
        {
            if (kind == RoomKind.Entrance) return 0;
            return Math.Min(0.25 + 0.2 * depth, 0.8);
        }
    }

    /// <summary>潜行中の体力。ダメージと回復だけを持ち、倒れたかどうかを返す。</summary>
    public sealed class HealthState
    {
        public double Max { get; private set; }
        public double Current { get; private set; }
        public bool IsDown => Current <= 0;

        public HealthState(double max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            Max = max;
            Current = max;
        }

        /// <summary>最大体力を変える（強化）。増えた分だけ今の体力も増える。減ったら今の体力を上限に合わせる。</summary>
        public void SetMax(double max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            if (!IsDown && max > Max) Current += max - Max;
            Max = max;
            Current = Math.Min(Current, Max);
        }

        /// <summary>ダメージを与える。この呼び出しで倒れたら true。</summary>
        public bool Damage(double amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (IsDown || amount == 0) return false;
            Current = Math.Max(0, Current - amount);
            return IsDown;
        }

        public void Reset() => Current = Max;

        /// <summary>回復する。倒れていたら回復しない。実際に回復した量を返す。</summary>
        public double Heal(double amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (IsDown) return 0;
            double before = Current;
            Current = Math.Min(Max, Current + amount);
            return Current - before;
        }
    }
}
