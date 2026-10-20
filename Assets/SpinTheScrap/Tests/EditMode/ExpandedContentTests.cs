using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    /// <summary>シンボル 20 種・レリック 10 種に増やした分の確認。</summary>
    public class ExpandedContentTests
    {
        static readonly ContentDatabase Db = MvpContent.Create();
        int nextId = 1;

        SymbolInstance[] Board(params (int row, int col, string id)[] placements)
        {
            var cells = new SymbolInstance[Core.Board.CellCount];
            foreach (var (row, col, id) in placements)
                cells[Core.Board.Index(row, col)] = new SymbolInstance(nextId++, id, 0);
            return cells;
        }

        static SpinResult Eval(SymbolInstance[] cells, ulong seed = 1, params RelicDefinition[] relics) =>
            SlotMachine.Evaluate(cells, Db, relics, new Rng(seed));

        static double ValueAt(SpinResult r, int row, int col) => r.Cells[Core.Board.Index(row, col)].Value;

        [Test]
        public void HasTwentySymbolsAndTenRelicsWithUniqueIds()
        {
            Assert.AreEqual(20, Db.Symbols.Count);
            Assert.AreEqual(10, Db.Relics.Count);
            Assert.AreEqual(Db.Symbols.Count, Db.Symbols.Select(s => s.Id).Distinct().Count());
            Assert.AreEqual(Db.Relics.Count, Db.Relics.Select(r => r.Id).Distinct().Count());
        }

        [Test]
        public void EveryTagHasAtLeastTwoSymbols()
        {
            foreach (SymbolTag tag in System.Enum.GetValues(typeof(SymbolTag)))
            {
                if (tag == SymbolTag.None) continue;
                Assert.GreaterOrEqual(Db.Symbols.Count(s => s.Tags.HasAny(tag)), 2, tag.ToString());
            }
        }

        [Test]
        public void TwoHugeSymbolsExist()
        {
            Assert.AreEqual(2, Db.Symbols.Count(s => s.Size == ScrapSize.Huge));
        }

        [Test]
        public void LunchBoxGainsFromAdjacentFood()
        {
            // 弁当箱の隣にスプーン 2 本（どちらも食べ物）。ラインは成立しない配置
            var r = Eval(Board((1, 2, "lunch_box"), (0, 1, "bent_spoon"), (2, 3, "bent_spoon")));
            Assert.AreEqual(3 + 3 * 2, ValueAt(r, 1, 2));
            Assert.AreEqual(1 + 1, ValueAt(r, 0, 1), "スプーンは隣の弁当箱（食べ物）で +1");
        }

        [Test]
        public void TinRobotCountsOtherToys()
        {
            var r = Eval(Board((0, 0, "tin_robot"), (2, 4, "jack_in_the_box"), (2, 0, "plush_arm")));
            // ぬいぐるみの片腕もおもちゃ。ロボット自身は数えない
            Assert.AreEqual(2 + 1 * 2, ValueAt(r, 0, 0));
        }

        [Test]
        public void JackInTheBoxSometimesMultipliesBoard()
        {
            int boosted = 0;
            for (ulong seed = 1; seed <= 400; seed++)
            {
                var r = Eval(Board((1, 2, "jack_in_the_box"), (0, 0, "empty_can")), seed);
                if (r.BoardMultiplier > 1) boosted++;
            }
            Assert.That(boosted, Is.InRange(95, 175), "約 1/3");
        }

        [Test]
        public void DiceSometimesPaysTwelveTimesButNeverDisappears()
        {
            int big = 0;
            for (ulong seed = 1; seed <= 600; seed++)
            {
                var r = Eval(Board((1, 2, "pair_of_dice")), seed);
                Assert.IsEmpty(r.DestroyedInstances);
                if (ValueAt(r, 1, 2) >= 12) big++;
            }
            Assert.That(big, Is.InRange(70, 130), "約 1/6");
        }

        [Test]
        public void LosingTicketsStackWithGamble()
        {
            var r = Eval(Board((0, 0, "losing_ticket"), (2, 4, "losing_ticket"), (2, 0, "pair_of_dice")));
            Assert.AreEqual(1 + 2 * 2, ValueAt(r, 0, 0), "もう 1 枚の馬券とサイコロでギャンブル 2 つ");
        }

        [Test]
        public void CreepyDollAddsEnemyEveryTimeItAppears()
        {
            var r = Eval(Board((1, 2, "creepy_doll"), (0, 0, "cracked_mirror")));
            Assert.AreEqual(3 + 2, ValueAt(r, 1, 2), "盤面の呪い（手鏡）1 つで +2");
            Assert.AreEqual(1, r.EnemyBonus);
        }

        [Test]
        public void VendingMachineGainsPerElectric()
        {
            var r = Eval(Board((1, 2, "broken_vending_machine"), (0, 0, "old_calculator"), (2, 4, "tin_robot")));
            Assert.AreEqual(8 + 2 * 2, ValueAt(r, 1, 2));
        }

        [Test]
        public void HoledCoinDoublesLuck()
        {
            var plain = Eval(Board((1, 2, "rusty_horseshoe")));
            var coin = Eval(Board((1, 2, "rusty_horseshoe")), 1, Db.GetRelic("holed_coin"));
            Assert.AreEqual(ValueAt(plain, 1, 2) * 2, ValueAt(coin, 1, 2));
        }

        [Test]
        public void RustyScaleRaisesSellPrice()
        {
            // 闇市の品ぞろえは seed で決まるので、錆びた秤が並ぶ seed を探して買う
            var config = new BalanceConfig { StartingMoney = 100 };
            RunState run = null;
            for (ulong seed = 1; seed <= 200 && run == null; seed++)
            {
                var candidate = new RunState(Db, config, seed);
                if (candidate.RelicOffers.Contains("rusty_scale")) run = candidate;
            }
            Assert.IsNotNull(run, "200 通りの seed のどれかで並ぶはず");
            double before = run.SellPriceFor("safe_door");
            Assert.IsTrue(run.TryBuyRelic("rusty_scale"));
            Assert.AreEqual(System.Math.Floor(Db.GetSymbol("safe_door").SellPrice * 1.25), run.SellPriceFor("safe_door"));
            Assert.Greater(run.SellPriceFor("safe_door"), before);
        }
    }
}
