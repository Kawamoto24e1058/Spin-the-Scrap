using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// MVP 用のシンボル 20 種・レリック 10 種（要件書 12.2 次のアクション 4）。
    /// タグ 1 系統につき「土台」「伸ばす」「爆発させる」の役割を持たせる（要件書 5.2）。
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

                // ---- 食べ物 ----
                // 食べ物の「土台」。金属でもあるので、金属の組み合わせにも混ざる。
                new SymbolDefinition("bent_spoon", baseValue: 1, sellPrice: 2, SymbolTag.Metal | SymbolTag.Food,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Adjacent(SymbolTag.Food), EffectOp.AddFlat, 1),
                    }),

                // 食べ物の「伸ばす」。隣接する食べ物 1 つにつき +3。
                new SymbolDefinition("lunch_box", baseValue: 3, sellPrice: 9, SymbolTag.Food,
                    size: ScrapSize.Large, rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Adjacent(SymbolTag.Food), EffectOp.AddFlat, 3),
                    }),

                // ---- おもちゃ ----
                // おもちゃ・金属・電化製品の 3 系統をつなぐ。盤面のおもちゃ 1 つにつき +1。
                new SymbolDefinition("tin_robot", baseValue: 2, sellPrice: 7, SymbolTag.Toy | SymbolTag.Metal | SymbolTag.Electric,
                    rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Toy), EffectOp.AddFlat, 1),
                    }),

                // おもちゃの「爆発」。1/3 で盤面 ×2（1 スピン 1 回）。
                new SymbolDefinition("jack_in_the_box", baseValue: 2, sellPrice: 6, SymbolTag.Toy,
                    size: ScrapSize.Large, rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Chance(1.0 / 3), EffectOp.MultiplyBoard, 2, oncePerSpin: true),
                    }),

                // ---- 紙・ギャンブル ----
                // 紙の「土台」。隣接する紙 1 つにつき +1。
                new SymbolDefinition("old_newspaper", baseValue: 2, sellPrice: 1, SymbolTag.Paper,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Adjacent(SymbolTag.Paper), EffectOp.AddFlat, 1),
                    }),

                // ギャンブルの「土台」。盤面のギャンブル 1 つにつき +2。宝くじの束と組む。
                new SymbolDefinition("losing_ticket", baseValue: 1, sellPrice: 1, SymbolTag.Gamble | SymbolTag.Paper,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Gamble), EffectOp.AddFlat, 2),
                    }),

                // ギャンブル × 幸運の「爆発」。1/6 で ×12。宝くじと違って外れても消えない。
                new SymbolDefinition("pair_of_dice", baseValue: 1, sellPrice: 4, SymbolTag.Gamble | SymbolTag.Luck,
                    rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.Chance(1.0 / 6), EffectOp.MultiplySelf, 12),
                    }),

                // ---- 幸運 ----
                // 幸運の「伸ばす」。盤面の幸運 1 つにつき +1。金属でもあるので空き缶ともラインを組める。
                new SymbolDefinition("rusty_horseshoe", baseValue: 2, sellPrice: 6, SymbolTag.Metal | SymbolTag.Luck,
                    rarity: Rarity.Uncommon,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Luck), EffectOp.AddFlat, 1),
                    }),

                // ---- 呪い ----
                // 呪いの「伸ばす」。盤面の呪い 1 つにつき +2。ただし盤面に出るたびに次の潜行の敵が増える。
                new SymbolDefinition("creepy_doll", baseValue: 3, sellPrice: 18, SymbolTag.Curse | SymbolTag.Doll,
                    size: ScrapSize.Large, rarity: Rarity.Rare,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Curse), EffectOp.AddFlat, 2),
                        new EffectDefinition(EffectCondition.Always(), EffectOp.SpawnEnemies, 1, oncePerSpin: true),
                    }),

                // ---- 特大 2 つ目 ----
                // 電化製品の「伸ばす」。盤面の電化製品 1 つにつき +2。協力しないと運べないぶん高価値。
                new SymbolDefinition("broken_vending_machine", baseValue: 8, sellPrice: 45, SymbolTag.Electric | SymbolTag.Metal,
                    size: ScrapSize.Huge, rarity: Rarity.Rare,
                    effects: new[]
                    {
                        new EffectDefinition(EffectCondition.OnBoard(SymbolTag.Electric), EffectOp.AddFlat, 2),
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
                new RelicDefinition("holed_coin", price: 16, RelicKind.TagMultiplier, SymbolTag.Luck, 2),
                new RelicDefinition("toy_box_key", price: 12, RelicKind.TagFlatBonus, SymbolTag.Toy, 2),
                new RelicDefinition("rubber_gloves", price: 20, RelicKind.TagMultiplier, SymbolTag.Electric, 1.5),
                new RelicDefinition("gamblers_notebook", price: 15, RelicKind.TagMultiplier, SymbolTag.Gamble, 1.5),
                new RelicDefinition("rusty_scale", price: 14, RelicKind.SellPriceMultiplier, amount: 1.25),
            };
        }
    }
}
