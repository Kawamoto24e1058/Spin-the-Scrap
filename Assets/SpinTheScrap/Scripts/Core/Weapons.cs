using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    public enum WeaponKind
    {
        /// <summary>近接。数は耐久（振れる回数）。</summary>
        Melee,
        /// <summary>撃つ。数は弾数。</summary>
        Ranged,
    }

    /// <summary>武器の定義（docs/floor-design.md「武器スロット」）。数値は調整前の初期値。</summary>
    public sealed class WeaponDefinition
    {
        public string Id { get; }
        public WeaponKind Kind { get; }
        public SymbolTag Family { get; }
        public int Tier { get; }
        public double Damage { get; }
        /// <summary>攻撃の間隔 (s)。</summary>
        public double Interval { get; }
        /// <summary>届く距離 (m)。</summary>
        public double Range { get; }
        /// <summary>耐久または弾数。</summary>
        public int Capacity { get; }
        /// <summary>撃った弾が敵を貫通する。</summary>
        public bool Pierce { get; set; }
        /// <summary>当たった敵から近くの敵へ連鎖する数。</summary>
        public int Chain { get; set; }
        /// <summary>着弾点で爆発する半径 (m)。0 なら爆発しない。</summary>
        public double ExplosionRadius { get; set; }
        /// <summary>当てた敵がひるむ確率。</summary>
        public double StunChance { get; set; }
        /// <summary>当てるたびに自分が受けるダメージ。</summary>
        public double SelfDamage { get; set; }
        public string NameKey => "weapon." + Id + ".name";
        public string DescriptionKey => "weapon." + Id + ".desc";

        public WeaponDefinition(string id, WeaponKind kind, SymbolTag family, int tier, double damage, double interval, double range, int capacity)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            Kind = kind;
            Family = family;
            Tier = tier;
            Damage = damage;
            Interval = interval;
            Range = range;
            Capacity = capacity;
        }
    }

    public sealed class WeaponCatalog
    {
        readonly List<WeaponDefinition> weapons;

        public IReadOnlyList<WeaponDefinition> Weapons => weapons;

        public WeaponCatalog(IEnumerable<WeaponDefinition> definitions)
        {
            weapons = definitions.ToList();
            if (weapons.Select(w => w.Id).Distinct().Count() != weapons.Count) throw new ArgumentException("Duplicate weapon id");
        }

        public WeaponDefinition Get(string id)
        {
            var weapon = weapons.FirstOrDefault(w => w.Id == id);
            if (weapon == null) throw new KeyNotFoundException("Unknown weapon: " + id);
            return weapon;
        }

        /// <summary>系統と質から武器を選ぶ。その系統の武器がなければ金属の武器。</summary>
        public WeaponDefinition For(SymbolTag family, int tier)
        {
            var tag = family == SymbolTag.Doll ? SymbolTag.Toy : family;
            var pool = weapons.Where(w => w.Family == tag).ToList();
            if (pool.Count == 0) pool = weapons.Where(w => w.Family == SymbolTag.Metal).ToList();
            // その質の武器がなければ、質が近い方（低い方を優先）
            return pool.OrderBy(w => Math.Abs(w.Tier - tier) * 2 + (w.Tier > tier ? 1 : 0)).First();
        }

        public static WeaponCatalog CreateMvp()
        {
            return new WeaponCatalog(new[]
            {
                new WeaponDefinition("pipe", WeaponKind.Melee, SymbolTag.Metal, 1, damage: 20, interval: 0.55, range: 3.0, capacity: 40),
                new WeaponDefinition("nail_gun", WeaponKind.Ranged, SymbolTag.Metal, 2, damage: 14, interval: 0.18, range: 25, capacity: 60),
                new WeaponDefinition("rivet_cannon", WeaponKind.Ranged, SymbolTag.Metal, 3, damage: 45, interval: 0.6, range: 35, capacity: 24) { Pierce = true },
                new WeaponDefinition("taser", WeaponKind.Melee, SymbolTag.Electric, 1, damage: 12, interval: 0.5, range: 2.9, capacity: 30) { StunChance = 0.6 },
                new WeaponDefinition("arc_gun", WeaponKind.Ranged, SymbolTag.Electric, 2, damage: 16, interval: 0.4, range: 18, capacity: 40) { Chain = 2, StunChance = 0.2 },
                new WeaponDefinition("arc_gun_mk2", WeaponKind.Ranged, SymbolTag.Electric, 3, damage: 24, interval: 0.35, range: 22, capacity: 50) { Chain = 4, StunChance = 0.3 },
                new WeaponDefinition("firework_tube", WeaponKind.Ranged, SymbolTag.Toy, 1, damage: 40, interval: 1.1, range: 25, capacity: 8) { ExplosionRadius = 3 },
                new WeaponDefinition("firework_tube_mk2", WeaponKind.Ranged, SymbolTag.Toy, 3, damage: 70, interval: 1.0, range: 30, capacity: 12) { ExplosionRadius = 4.5 },
                new WeaponDefinition("cursed_cleaver", WeaponKind.Melee, SymbolTag.Curse, 1, damage: 55, interval: 0.7, range: 3.1, capacity: 40) { SelfDamage = 3 },
                new WeaponDefinition("cursed_cleaver_mk2", WeaponKind.Melee, SymbolTag.Curse, 2, damage: 85, interval: 0.65, range: 3.3, capacity: 50) { SelfDamage = 4 },
                new WeaponDefinition("cursed_cleaver_mk3", WeaponKind.Melee, SymbolTag.Curse, 3, damage: 130, interval: 0.6, range: 3.5, capacity: 60) { SelfDamage = 5 },
            });
        }
    }

    /// <summary>武器の改造（docs/floor-design.md「改造スロット」）。</summary>
    public enum WeaponModKind
    {
        /// <summary>攻撃力 +30%。</summary>
        Damage,
        /// <summary>当てると 25% でひるむ。</summary>
        Stun,
        /// <summary>与えたダメージの 10% 回復。</summary>
        Lifesteal,
        /// <summary>攻撃の間隔 -20%。</summary>
        FireRate,
        /// <summary>弾数・耐久 +50%（付けた時点で補充もする）。</summary>
        Capacity,
        /// <summary>攻撃力 +60%、当てるたびに自分に 2 ダメージ。</summary>
        Cursed,
        /// <summary>攻撃力 +15%。</summary>
        Minor,
    }

    /// <summary>手に持っている武器 1 本。残りの耐久・弾数と、付いた改造を持つ。</summary>
    public sealed class WeaponInstance
    {
        public const int MaxMods = 3;

        readonly List<WeaponModKind> mods = new List<WeaponModKind>();

        public WeaponDefinition Definition { get; }
        public int Remaining { get; private set; }
        public IReadOnlyList<WeaponModKind> Mods => mods;
        public bool IsSpent => Remaining <= 0;
        public bool CanTakeMod => mods.Count < MaxMods;

        public WeaponInstance(WeaponDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Remaining = definition.Capacity;
        }

        public int Capacity => (int)Math.Round(Definition.Capacity * (1 + 0.5 * Count(WeaponModKind.Capacity)));

        public double Damage => Definition.Damage
            * (1 + 0.3 * Count(WeaponModKind.Damage) + 0.6 * Count(WeaponModKind.Cursed) + 0.15 * Count(WeaponModKind.Minor));

        public double Interval => Definition.Interval * Math.Pow(0.8, Count(WeaponModKind.FireRate));

        public double StunChance => Math.Min(1, Definition.StunChance + 0.25 * Count(WeaponModKind.Stun));

        public double LifestealRatio => 0.1 * Count(WeaponModKind.Lifesteal);

        public double SelfDamage => Definition.SelfDamage + 2 * Count(WeaponModKind.Cursed);

        /// <summary>改造を付ける。上限なら false。</summary>
        public bool AddMod(WeaponModKind mod)
        {
            if (!CanTakeMod) return false;
            int before = Capacity;
            mods.Add(mod);
            // 弾数の改造は、増えた分を補充する
            if (Capacity > before) Remaining += Capacity - before;
            return true;
        }

        /// <summary>1 回攻撃する（耐久・弾を 1 減らす）。残りがなければ false。</summary>
        public bool TryUse()
        {
            if (IsSpent) return false;
            Remaining--;
            return true;
        }

        int Count(WeaponModKind kind) => mods.Count(m => m == kind);

        /// <summary>改造スロットの系統から付く改造を選ぶ。</summary>
        public static WeaponModKind ModFor(SymbolTag family)
        {
            switch (family)
            {
                case SymbolTag.Metal: return WeaponModKind.Damage;
                case SymbolTag.Electric: return WeaponModKind.Stun;
                case SymbolTag.Food: return WeaponModKind.Lifesteal;
                case SymbolTag.Fragile: return WeaponModKind.FireRate;
                case SymbolTag.Paper: return WeaponModKind.Capacity;
                case SymbolTag.Curse: return WeaponModKind.Cursed;
                default: return WeaponModKind.Minor;
            }
        }
    }
}
