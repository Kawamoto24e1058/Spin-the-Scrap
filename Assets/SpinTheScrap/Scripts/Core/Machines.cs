using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    /// <summary>スロット台の種類（docs/floor-design.md）。種類ごとにリールが別で、ランを通して持ち越す。</summary>
    public enum MachineKind
    {
        /// <summary>武器そのものが出る。</summary>
        Weapon,
        /// <summary>自分の能力（タグのレベル）が上がる。</summary>
        Perk,
        /// <summary>持っている武器に改造が付く。</summary>
        Mod,
        /// <summary>消耗品が出る。</summary>
        Tool,
    }

    /// <summary>スロット台の数値。すべて調整前の初期値。</summary>
    public sealed class MachineRules
    {
        public double ToolBaseCost = 2;
        public double PerkBaseCost = 4;
        public double WeaponBaseCost = 6;
        public double ModBaseCost = 8;
        /// <summary>階層が 1 つ深くなるごとの費用の倍率。</summary>
        public double CostFloorGrowth = 1.5;
        /// <summary>同じ種類の台を同じ階で回すごとの費用の倍率。</summary>
        public double CostStepGrowth = 1.3;

        /// <summary>盤面の合計の値がこれ以上なら質 2。</summary>
        public double Tier2Value = 20;
        /// <summary>盤面の合計の値がこれ以上なら質 3。</summary>
        public double Tier3Value = 45;
        /// <summary>盤面の合計の値がこれ以上なら大当たり。</summary>
        public double JackpotValue = 100;

        /// <summary>回したときに鳴る音の半径 (m)。敵が寄ってくる。</summary>
        public double SpinNoiseRadius = 14;
        public double JackpotNoiseRadius = 40;

        public double BaseCost(MachineKind kind)
        {
            switch (kind)
            {
                case MachineKind.Tool: return ToolBaseCost;
                case MachineKind.Perk: return PerkBaseCost;
                case MachineKind.Weapon: return WeaponBaseCost;
                case MachineKind.Mod: return ModBaseCost;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public double CostFor(MachineKind kind, int floor, int spinsThisFloor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            if (spinsThisFloor < 0) throw new ArgumentOutOfRangeException(nameof(spinsThisFloor));
            return Math.Ceiling(BaseCost(kind) * Math.Pow(CostFloorGrowth, floor - 1) * Math.Pow(CostStepGrowth, spinsThisFloor));
        }

        public int TierFor(double boardTotal, bool luckyLine)
        {
            int tier = boardTotal >= Tier3Value ? 3 : boardTotal >= Tier2Value ? 2 : 1;
            if (luckyLine) tier++;
            return Math.Min(tier, 3);
        }
    }

    /// <summary>
    /// 回した結果の読み取り（どの台でも共通）。盤面のシンボルの最終的な値をタグごとに合計し、
    /// いちばん大きいタグを「系統」、盤面の合計を「質」にする。隣接効果やラインで値を伸ばすほど良い物が出る。
    /// </summary>
    public sealed class SpinReading
    {
        /// <summary>同点のときに優先するタグの順番。</summary>
        static readonly SymbolTag[] TiePriority =
        {
            SymbolTag.Curse, SymbolTag.Metal, SymbolTag.Electric, SymbolTag.Toy, SymbolTag.Food,
            SymbolTag.Doll, SymbolTag.Luck, SymbolTag.Gamble, SymbolTag.Fragile, SymbolTag.Paper,
        };

        readonly Dictionary<SymbolTag, double> tagTotals;

        public SpinResult Result { get; }
        /// <summary>合計がいちばん大きいタグ（盤面が空なら None）。</summary>
        public SymbolTag Family { get; }
        /// <summary>質（1〜3）。</summary>
        public int Tier { get; }
        public bool Jackpot { get; }
        public IReadOnlyDictionary<SymbolTag, double> TagTotals => tagTotals;

        SpinReading(SpinResult result, Dictionary<SymbolTag, double> totals, SymbolTag family, int tier, bool jackpot)
        {
            Result = result;
            tagTotals = totals;
            Family = family;
            Tier = tier;
            Jackpot = jackpot;
        }

        public double TotalOf(SymbolTag tag) => tagTotals.TryGetValue(tag, out var v) ? v : 0;

        public static SpinReading Read(SpinResult result, ContentDatabase db, MachineRules rules)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var totals = new Dictionary<SymbolTag, double>();
            void Add(SymbolTag tags, double value)
            {
                foreach (var tag in TiePriority)
                    if (tags.HasAny(tag)) totals[tag] = (totals.TryGetValue(tag, out var v) ? v : 0) + value;
            }

            foreach (var cell in result.Cells)
            {
                if (cell == null || cell.IsEmpty || cell.Destroyed) continue;
                Add(db.GetSymbol(cell.Instance.SymbolId).Tags, cell.Value * result.BoardMultiplier);
            }
            foreach (var line in result.Lines) Add(line.SharedTags, line.Bonus * result.BoardMultiplier);

            var family = SymbolTag.None;
            double best = 0;
            foreach (var tag in TiePriority)
            {
                double v = totals.TryGetValue(tag, out var t) ? t : 0;
                if (v > best)
                {
                    best = v;
                    family = tag;
                }
            }

            bool luckyLine = result.Lines.Any(l => l.SharedTags.HasAny(SymbolTag.Luck));
            int tier = rules.TierFor(result.Total, luckyLine);
            return new SpinReading(result, totals, family, tier, result.Total >= rules.JackpotValue);
        }
    }

    /// <summary>1 回回した結果。台の種類ごとに中身が違う。</summary>
    public sealed class MachineOutcome
    {
        public MachineKind Kind { get; internal set; }
        public SpinReading Reading { get; internal set; }
        public double Cost { get; internal set; }
        /// <summary>武器スロット：出た武器。</summary>
        public WeaponInstance Weapon { get; internal set; }
        /// <summary>改造スロット：付いた改造。</summary>
        public WeaponModKind? Mod { get; internal set; }
        /// <summary>強化スロット：レベルが上がったタグ。</summary>
        public List<SymbolTag> LevelUps { get; } = new List<SymbolTag>();
        /// <summary>道具スロット：出た道具（ID と数）。メダルなら ToolId は null。</summary>
        public string ToolId { get; internal set; }
        public int ToolCount { get; internal set; }
        public double Medals { get; internal set; }
    }
}
