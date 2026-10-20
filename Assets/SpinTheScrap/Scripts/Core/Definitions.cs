using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>効果の発動条件の種類（要件書 8.3 EffectDefinition）。</summary>
    public enum ConditionKind
    {
        /// <summary>常に成立。カウントは 1。</summary>
        Always,
        /// <summary>周囲 8 マスにある指定タグのシンボル数。</summary>
        AdjacentTagCount,
        /// <summary>同じ列（自分以外）にある指定タグのシンボル数。</summary>
        SameColumnTagCount,
        /// <summary>盤面全体（自分以外）にある指定タグのシンボル数。</summary>
        BoardTagCount,
        /// <summary>確率で成立。カウントは 1。</summary>
        Chance,
    }

    /// <summary>効果の処理の種類。加算と乗算は明確に区別する（要件書 5.4）。</summary>
    public enum EffectOp
    {
        None,
        /// <summary>自分の値に Amount × 条件カウントを加算。</summary>
        AddFlat,
        /// <summary>自分の値を Amount 倍。</summary>
        MultiplySelf,
        /// <summary>盤面合計を Amount 倍（重ね掛け可）。</summary>
        MultiplyBoard,
        /// <summary>同じ列の TargetTag シンボルの効果をもう 1 回発動させる（再発動は連鎖しない）。</summary>
        RetriggerSameColumnTag,
        /// <summary>ラインの成立に必要な個数を Amount だけ減らす（最低 2）。</summary>
        WidenLines,
        /// <summary>自分を消滅させる（このスピンの値は 0、リールから除外）。</summary>
        DestroySelf,
        /// <summary>次の潜行の敵を Amount 体増やす。</summary>
        SpawnEnemies,
    }

    public sealed class EffectCondition
    {
        public ConditionKind Kind { get; }
        public SymbolTag Tag { get; }
        /// <summary>カウントがこの値以上で成立。</summary>
        public int MinCount { get; }
        public double Probability { get; }

        public EffectCondition(ConditionKind kind, SymbolTag tag = SymbolTag.None, int minCount = 1, double probability = 0)
        {
            Kind = kind;
            Tag = tag;
            MinCount = Math.Max(1, minCount);
            Probability = probability;
        }

        public static EffectCondition Always() => new EffectCondition(ConditionKind.Always);
        public static EffectCondition Adjacent(SymbolTag tag, int minCount = 1) => new EffectCondition(ConditionKind.AdjacentTagCount, tag, minCount);
        public static EffectCondition SameColumn(SymbolTag tag, int minCount = 1) => new EffectCondition(ConditionKind.SameColumnTagCount, tag, minCount);
        public static EffectCondition OnBoard(SymbolTag tag, int minCount = 1) => new EffectCondition(ConditionKind.BoardTagCount, tag, minCount);
        public static EffectCondition Chance(double probability) => new EffectCondition(ConditionKind.Chance, probability: probability);
    }

    /// <summary>「条件」と「処理」の組み合わせ。新シンボルの多くを既存部品の組み合わせで作る（要件書 8.3）。</summary>
    public sealed class EffectDefinition
    {
        public EffectCondition Condition { get; }
        public EffectOp Op { get; }
        public double Amount { get; }
        /// <summary>RetriggerSameColumnTag の対象タグ。</summary>
        public SymbolTag TargetTag { get; }
        /// <summary>条件が不成立だったときの処理（例：宝くじの「外れると消滅」）。</summary>
        public EffectOp ElseOp { get; }
        /// <summary>同じシンボル定義が盤面に複数あっても、1 スピンで 1 回だけ成立させる。</summary>
        public bool OncePerSpin { get; }

        public EffectDefinition(EffectCondition condition, EffectOp op, double amount = 0,
            SymbolTag targetTag = SymbolTag.None, EffectOp elseOp = EffectOp.None, bool oncePerSpin = false)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Op = op;
            Amount = amount;
            TargetTag = targetTag;
            ElseOp = elseOp;
            OncePerSpin = oncePerSpin;
        }
    }

    /// <summary>シンボル（＝ガラクタ）定義。要件書 8.3 SymbolDefinition のうちエンジン非依存の部分。</summary>
    public sealed class SymbolDefinition
    {
        public string Id { get; }
        public string NameKey { get; }
        public string DescriptionKey { get; }
        /// <summary>リール上での 1 スピンあたりの基本値。</summary>
        public double BaseValue { get; }
        /// <summary>店で即売したときの価格。</summary>
        public double SellPrice { get; }
        public SymbolTag Tags { get; }
        public Rarity Rarity { get; }
        public ScrapSize Size { get; }
        public bool Fragile { get; }
        public IReadOnlyList<EffectDefinition> Effects { get; }

        public SymbolDefinition(string id, double baseValue, double sellPrice, SymbolTag tags,
            ScrapSize size = ScrapSize.Small, bool fragile = false, Rarity rarity = Rarity.Common,
            IReadOnlyList<EffectDefinition> effects = null, string nameKey = null, string descriptionKey = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            NameKey = nameKey ?? "symbol." + id + ".name";
            DescriptionKey = descriptionKey ?? "symbol." + id + ".desc";
            BaseValue = baseValue;
            SellPrice = sellPrice;
            Tags = tags;
            Size = size;
            Fragile = fragile;
            Rarity = rarity;
            Effects = effects ?? Array.Empty<EffectDefinition>();
        }
    }

    public enum RelicKind
    {
        /// <summary>Tag を持つシンボルの基本値に +Amount。</summary>
        TagFlatBonus,
        /// <summary>Tag を持つシンボルの値を ×Amount。</summary>
        TagMultiplier,
        /// <summary>1 サイクルの最初の 1 回は無料。</summary>
        FirstSpinFreePerCycle,
        /// <summary>回す費用を ×Amount。</summary>
        SpinCostMultiplier,
        /// <summary>割れ物が割れても価値が Amount（割合）残る。潜行側で参照する。</summary>
        BrokenFragileKeepsValue,
    }

    /// <summary>レリック（ラン中の永続強化）定義（要件書 5.3）。</summary>
    public sealed class RelicDefinition
    {
        public string Id { get; }
        public string NameKey { get; }
        public string DescriptionKey { get; }
        public double Price { get; }
        public RelicKind Kind { get; }
        public SymbolTag Tag { get; }
        public double Amount { get; }

        public RelicDefinition(string id, double price, RelicKind kind, SymbolTag tag = SymbolTag.None, double amount = 0,
            string nameKey = null, string descriptionKey = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            Price = price;
            Kind = kind;
            Tag = tag;
            Amount = amount;
            NameKey = nameKey ?? "relic." + id + ".name";
            DescriptionKey = descriptionKey ?? "relic." + id + ".desc";
        }
    }

    /// <summary>リールに入っているシンボル 1 個。投入者（OwnerId）を持ち、「誰のガラクタで当たったか」を出せる。</summary>
    public sealed class SymbolInstance
    {
        public int InstanceId { get; }
        public string SymbolId { get; }
        public int OwnerId { get; }

        public SymbolInstance(int instanceId, string symbolId, int ownerId)
        {
            InstanceId = instanceId;
            SymbolId = symbolId;
            OwnerId = ownerId;
        }

        public override string ToString() => SymbolId + "#" + InstanceId;
    }
}
