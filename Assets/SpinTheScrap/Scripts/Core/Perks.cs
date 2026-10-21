using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// 強化スロットで育てるタグのレベル（docs/floor-design.md「強化スロット」）。プレイヤーごとに持つ。
    /// 回すたびに、タグごとの合計の値がそのタグの経験値になる。
    /// </summary>
    public sealed class PerkState
    {
        /// <summary>
        /// Lv1〜5 になる経験値。空き缶だけのリールだと 1 回 5 前後、組み合わせを作ると 20〜40 入るので、
        /// うまく組めばランの終盤に Lv5 に届くくらいに合わせている。
        /// </summary>
        public static readonly double[] Thresholds = { 20, 60, 140, 260, 440 };

        readonly Dictionary<SymbolTag, double> xp = new Dictionary<SymbolTag, double>();

        public double Xp(SymbolTag tag) => xp.TryGetValue(tag, out var v) ? v : 0;

        public int Level(SymbolTag tag)
        {
            double value = Xp(tag);
            int level = 0;
            while (level < Thresholds.Length && value >= Thresholds[level]) level++;
            return level;
        }

        /// <summary>経験値を足す。レベルが上がったら true。</summary>
        public bool AddXp(SymbolTag tag, double amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (tag == SymbolTag.None || amount == 0) return false;
            int before = Level(tag);
            xp[tag] = Xp(tag) + amount;
            return Level(tag) > before;
        }

        /// <summary>経験値の入っている強化すべて（状態の保存用）。</summary>
        public IEnumerable<KeyValuePair<SymbolTag, double>> AllXp => xp;

        /// <summary>経験値をそのまま設定する（協力プレイで、ホストが計算した値をクライアントに写す）。</summary>
        public void SetXp(SymbolTag tag, double value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (tag == SymbolTag.None) return;
            xp[tag] = value;
        }

        /// <summary>レベルから能力値を求める。</summary>
        public PlayerStats Stats(double baseMaxHealth)
        {
            int metal = Level(SymbolTag.Metal);
            int curse = Level(SymbolTag.Curse);
            int food = Level(SymbolTag.Food);
            return new PlayerStats
            {
                MaxHealth = baseMaxHealth + 15 * food,
                DamageMultiplier = 1 + 0.15 * metal + 0.3 * curse,
                DamageTakenMultiplier = 1 + 0.1 * curse,
                StunChance = 0.08 * Level(SymbolTag.Electric),
                CritChance = Math.Min(0.6, 0.06 * Level(SymbolTag.Luck)),
                CritMultiplier = 2,
                MoveSpeedMultiplier = 1 + 0.05 * Level(SymbolTag.Fragile),
                ExplosionRadiusMultiplier = 1 + 0.15 * Level(SymbolTag.Toy),
                HealOnKill = food >= 3 ? 5 : 0,
                MedalRateMultiplier = 1 + 0.1 * Level(SymbolTag.Gamble),
                RevealRange = 8 * Level(SymbolTag.Paper),
                Stealth = Math.Min(0.6, 0.08 * Level(SymbolTag.Doll)),
            };
        }
    }

    /// <summary>強化で決まるプレイヤーの能力値。</summary>
    public struct PlayerStats
    {
        public double MaxHealth;
        public double DamageMultiplier;
        public double DamageTakenMultiplier;
        public double StunChance;
        public double CritChance;
        public double CritMultiplier;
        public double MoveSpeedMultiplier;
        public double ExplosionRadiusMultiplier;
        public double HealOnKill;
        public double MedalRateMultiplier;
        /// <summary>この距離 (m) 以内の敵の位置が壁越しに見える（紙）。</summary>
        public double RevealRange;
        /// <summary>敵の見える距離を減らす割合（人形）。0.2 なら 8 割の距離まで近づかないと見つからない。</summary>
        public double Stealth;
    }

    /// <summary>1 回の攻撃の結果。</summary>
    public struct HitResult
    {
        public double Damage;
        public bool Critical;
        public bool Stun;
        /// <summary>攻撃した側が回復する量（吸収の改造）。</summary>
        public double Heal;
        /// <summary>攻撃した側が受けるダメージ（呪いの武器・改造）。</summary>
        public double SelfDamage;
    }

    /// <summary>戦闘の計算（武器 × 能力値）。エンジン非依存なので、フェーズ 1 ではホストだけが計算する。</summary>
    public static class CombatRules
    {
        /// <summary>
        /// 武器で 1 回当てたときの結果。critRoll と stunRoll は [0, 1) の乱数（呼び出し側が渡すので決定的にテストできる）。
        /// </summary>
        public static HitResult Hit(WeaponInstance weapon, PlayerStats stats, double critRoll, double stunRoll)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            bool crit = critRoll < stats.CritChance;
            double damage = weapon.Damage * stats.DamageMultiplier * (crit ? stats.CritMultiplier : 1);
            double stunChance = 1 - (1 - weapon.StunChance) * (1 - stats.StunChance);
            return new HitResult
            {
                Damage = damage,
                Critical = crit,
                Stun = stunRoll < stunChance,
                Heal = damage * weapon.LifestealRatio,
                SelfDamage = weapon.SelfDamage,
            };
        }

        /// <summary>素手（武器がないとき）。弱いが壊れない。</summary>
        public static readonly WeaponDefinition Fists =
            new WeaponDefinition("fists", WeaponKind.Melee, SymbolTag.None, 0, damage: 8, interval: 0.45, range: 2.5, capacity: int.MaxValue);
    }
}
