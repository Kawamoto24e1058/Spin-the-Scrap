using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    /// <summary>道具（武器を含む）の ID。スロットの当たりで出る。</summary>
    public static class ToolIds
    {
        public const string Cart = "cart";
        public const string InsulatedBoots = "insulated_boots";
        public const string Battery = "battery";
        public const string Bandage = "bandage";
        public const string Firecracker = "firecracker";
        /// <summary>鉄パイプ。数は耐久（殴れる回数）。</summary>
        public const string Pipe = "pipe";
        /// <summary>釘打ち機。数は弾数。</summary>
        public const string NailGun = "nail_gun";
        /// <summary>スタンガン。数は使える回数。当てた敵をひるませる。</summary>
        public const string Taser = "taser";
    }

    /// <summary>
    /// 道具の定義。永続（一度手に入れればずっと使える）か、回数のある物（使うと減る、持てる数に上限）か。
    /// 武器も同じ扱いで、数を耐久や弾数として使う。
    /// </summary>
    public sealed class ToolDefinition
    {
        public string Id { get; }
        public bool Permanent { get; }
        /// <summary>持てる数の上限（永続なら 1）。</summary>
        public int MaxCount { get; }
        public string NameKey => "tool." + Id + ".name";
        public string DescriptionKey => "tool." + Id + ".desc";

        public ToolDefinition(string id, bool permanent, int maxCount = 1)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            Permanent = permanent;
            MaxCount = permanent ? 1 : Math.Max(1, maxCount);
        }
    }

    /// <summary>道具の一覧（docs/floor-design.md）。数値は調整前の初期値。</summary>
    public sealed class ToolCatalog
    {
        readonly List<ToolDefinition> tools;

        public IReadOnlyList<ToolDefinition> Tools => tools;

        public ToolCatalog(IEnumerable<ToolDefinition> definitions)
        {
            tools = definitions.ToList();
            if (tools.Select(t => t.Id).Distinct().Count() != tools.Count) throw new ArgumentException("Duplicate tool id");
        }

        public ToolDefinition Get(string id)
        {
            var tool = tools.FirstOrDefault(t => t.Id == id);
            if (tool == null) throw new KeyNotFoundException("Unknown tool: " + id);
            return tool;
        }

        public static ToolCatalog CreateMvp()
        {
            return new ToolCatalog(new[]
            {
                new ToolDefinition(ToolIds.Cart, permanent: true),
                new ToolDefinition(ToolIds.InsulatedBoots, permanent: true),
                new ToolDefinition(ToolIds.Battery, permanent: false, maxCount: 3),
                new ToolDefinition(ToolIds.Bandage, permanent: false, maxCount: 3),
                new ToolDefinition(ToolIds.Firecracker, permanent: false, maxCount: 3),
                new ToolDefinition(ToolIds.Pipe, permanent: false, maxCount: 30),
                new ToolDefinition(ToolIds.NailGun, permanent: false, maxCount: 36),
                new ToolDefinition(ToolIds.Taser, permanent: false, maxCount: 6),
            });
        }
    }

    /// <summary>スロットの当たりで出る物 1 つ。ToolId が null ならメダル。</summary>
    public struct SpinReward
    {
        public string ToolId;
        public int Count;
        public double Medals;
        /// <summary>出た物の質（1〜3）。ラインの長さと幸運で決まる。</summary>
        public int Tier;

        public bool IsMedals => ToolId == null;
    }

    /// <summary>
    /// 当たったラインのタグから、スロット台が出す物を決める（docs/floor-design.md「スロットから出る物」）。
    /// ラインが長いほど質が上がり、幸運が混ざると質がもう 1 段上がる。
    /// 何を拾ってリールに入れるかで、何が出やすいかを自分たちで組み立てられる。
    /// </summary>
    public static class SpinRewards
    {
        /// <summary>タグを見る順番。複数のタグを共有するラインでは先にある方を使う（幸運は質を上げるだけ）。</summary>
        static readonly SymbolTag[] Priority =
        {
            SymbolTag.Curse, SymbolTag.Metal, SymbolTag.Electric, SymbolTag.Food,
            SymbolTag.Toy, SymbolTag.Doll, SymbolTag.Gamble, SymbolTag.Paper, SymbolTag.Fragile, SymbolTag.Luck,
        };

        /// <summary>ギャンブルのラインで代わりに選ばれる系統。</summary>
        static readonly SymbolTag[] GambleTargets = { SymbolTag.Metal, SymbolTag.Electric, SymbolTag.Food, SymbolTag.Toy };

        public static int TierFor(LineHit line)
        {
            int tier = line.Length >= 5 ? 3 : line.Length == 4 ? 2 : 1;
            if (line.SharedTags.HasAny(SymbolTag.Luck) && line.SharedTags != SymbolTag.Luck) tier++;
            return Math.Min(tier, 3);
        }

        public static List<SpinReward> FromSpin(SpinResult result, Rng rng)
        {
            var rewards = new List<SpinReward>();
            foreach (var line in result.Lines) rewards.AddRange(FromLine(line, rng));
            return rewards;
        }

        public static List<SpinReward> FromLine(LineHit line, Rng rng)
        {
            int tier = TierFor(line);
            var tag = Priority.FirstOrDefault(t => line.SharedTags.HasAny(t));
            if (tag == SymbolTag.Gamble) tag = GambleTargets[rng.Next(GambleTargets.Length)];

            var list = new List<SpinReward>();
            void Tool(string id, int count) => list.Add(new SpinReward { ToolId = id, Count = count, Tier = tier });

            switch (tag)
            {
                case SymbolTag.Metal:
                    if (tier == 1) Tool(ToolIds.Pipe, 10);
                    else Tool(ToolIds.NailGun, 12 * (tier - 1));
                    if (tier == 3) Tool(ToolIds.Cart, 1);
                    break;
                case SymbolTag.Electric:
                    if (tier == 1) Tool(ToolIds.Battery, 1);
                    else Tool(ToolIds.Taser, 2 * (tier - 1));
                    if (tier == 3) Tool(ToolIds.InsulatedBoots, 1);
                    break;
                case SymbolTag.Food:
                    Tool(ToolIds.Bandage, tier);
                    break;
                case SymbolTag.Toy:
                case SymbolTag.Doll:
                    Tool(ToolIds.Firecracker, tier);
                    break;
                case SymbolTag.Curse:
                    // 呪いは強い武器を多めに出す。代わりに次の階層の敵が増える（盤面の呪い効果とは別）
                    Tool(ToolIds.NailGun, 12 * tier);
                    break;
                default:
                    // 紙・割れ物・幸運だけのライン：メダル
                    list.Add(new SpinReward { Medals = Math.Ceiling(line.Bonus), Tier = tier });
                    break;
            }
            return list;
        }

        /// <summary>呪いのラインなら、出る物の代わりに次の階層の敵が増える数。</summary>
        public static int EnemyBonusFor(SpinResult result) =>
            result.Lines.Count(l => Priority.FirstOrDefault(t => l.SharedTags.HasAny(t)) == SymbolTag.Curse);
    }
}
