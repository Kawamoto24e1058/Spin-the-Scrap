using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// MVP 用のシンボル 10 種・レリック 5 種（要件書 12.2 次のアクション 4）。
    /// Unity 側では ScriptableObject アセットが優先され、未設定のときにこの定義へフォールバックする。
    /// 数値はすべて調整前の初期値。
    /// </summary>
    public static class MvpContent
    {
        public static ContentDatabase Create() => new ContentDatabase(CreateSymbols(), CreateRelics());

        public static List<SymbolDefinition> CreateSymbols()
        {
            return new List<SymbolDefinition>
            {
                // 基本シンボル。初期リールはこれだけ。
                new SymbolDefinition("empty_can", baseValue: 1, sellPrice: 2, SymbolTag.Metal),

                // 金属の「土台」。隣接する金属 1 つにつき +1。
                new SymbolDefinition("rusty_screw", baseValue: 1, sellPrice: 3, SymbolTag.Metal,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Adjacent(SymbolTag.Metal), EffectOp.AddFlat, 1),
                    }),

                // 呪いの「爆発」。呪いが 3 つ以上（自分 + 2）で盤面 ×2、代わりに次の潜行で敵が増える。
                new SymbolDefinition("mannequin_head", baseValue: 2, sellPrice: 8, SymbolTag.Curse | SymbolTag.Doll,
                    size: ScrapSize.Large, rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Curse, 2), EffectOp.MultiplyBoard, 2, oncePerSpin: true),
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Curse, 2), EffectOp.SpawnEnemies, 1, oncePerSpin: true),
                    }),

                // 電化製品の「伸ばす」。同じ列の電化製品の効果をもう 1 回発動。
                new SymbolDefinition("broken_radio", baseValue: 3, sellPrice: 6, SymbolTag.Electric,
                    rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Always(), EffectOp.RetriggerSameColumnTag, targetTag: SymbolTag.Electric),
                    }),

                // 幸運。盤面に 1 つあると、ラインの成立に必要な個数が 1 つ減る（3 → 2）。
                new SymbolDefinition("chipped_lucky_cat", baseValue: 2, sellPrice: 10, SymbolTag.Luck | SymbolTag.Fragile,
                    fragile: true, rarity: Rarity.Rare,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Always(), EffectOp.WidenLines, 1),
                    }),

                // ギャンブル。低確率で基本値 ×100、外れると消滅。
                new SymbolDefinition("lottery_bundle", baseValue: 1, sellPrice: 1, SymbolTag.Gamble | SymbolTag.Paper,
                    rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Chance(0.05), EffectOp.MultiplySelf, 100, elseOp: EffectOp.DestroySelf),
                    }),

                // 電化製品の「土台」。盤面の他の電化製品 1 つにつき +2。ラジオと同じ列だと 2 回発動。
                new SymbolDefinition("old_calculator", baseValue: 2, sellPrice: 5, SymbolTag.Electric,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Electric), EffectOp.AddFlat, 2),
                    }),

                // 呪い × 割れ物。隣接する呪い 1 つにつき +2。運搬中に割れやすい。
                new SymbolDefinition("cracked_mirror", baseValue: 4, sellPrice: 15, SymbolTag.Curse | SymbolTag.Fragile,
                    size: ScrapSize.Large, fragile: true, rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Adjacent(SymbolTag.Curse), EffectOp.AddFlat, 2),
                    }),

                // 人形の「土台」。盤面の他の人形 1 つにつき +1。
                new SymbolDefinition("plush_arm", baseValue: 1, sellPrice: 2, SymbolTag.Doll | SymbolTag.Toy,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Doll), EffectOp.AddFlat, 1),
                    }),

                // 特大。協力しないと運べないぶん高価値。同じ列の金属 1 つにつき +3。
                new SymbolDefinition("safe_door", baseValue: 10, sellPrice: 40, SymbolTag.Metal,
                    size: ScrapSize.Huge, rarity: Rarity.Rare,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.SameColumn(SymbolTag.Metal), EffectOp.AddFlat, 3),
                    }),
            };
        }

        public static List<RelicDefinition> CreateRelics()
        {
            return new List<RelicDefinition>
            {
                new RelicDefinition("magnet_charm", price: 15, RelicKind.TagFlatBonus, SymbolTag.Metal, 3),
                new RelicDefinition("regular_card", price: 12, RelicKind.FirstSpinFreePerCycle),
                new RelicDefinition("bubble_wrap", price: 10, RelicKind.BrokenFragileKeepsValue, amount: 0.5),
                new RelicDefinition("cursed_glove", price: 20, RelicKind.TagMultiplier, SymbolTag.Curse, 1.5),
                new RelicDefinition("worn_lever", price: 18, RelicKind.SpinCostMultiplier, amount: 0.8),
            };
        }
    }
}
