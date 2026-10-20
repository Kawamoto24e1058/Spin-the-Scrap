using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class RunStateTests
    {
        static RunState NewRun(BalanceConfig config = null, ulong seed = 1) =>
            new RunState(MvpContent.Create(), config ?? new BalanceConfig(), seed);

        [Test]
        public void StartsWithStartingReelAndMoney()
        {
            var run = NewRun();
            Assert.AreEqual(5, run.Reel.Count);
            Assert.IsTrue(run.Reel.All(s => s.SymbolId == "empty_can"));
            Assert.AreEqual(10, run.Money);
            Assert.AreEqual(1, run.Cycle);
            Assert.AreEqual(3, run.RelicOffers.Count);
        }

        [Test]
        public void SpinCostRisesWithinCycle()
        {
            var config = new BalanceConfig { StartingMoney = 1000 };
            var run = NewRun(config);
            double first = run.CurrentSpinCost;
            Assert.IsTrue(run.TrySpin(out _));
            Assert.Greater(run.CurrentSpinCost, first - 0.0001);
            for (int i = 0; i < 5; i++) run.TrySpin(out _);
            Assert.Greater(run.CurrentSpinCost, first);
        }

        [Test]
        public void SpinPaysOutAndChargesCost()
        {
            var run = NewRun();
            double before = run.Money;
            double cost = run.CurrentSpinCost;
            Assert.IsTrue(run.TrySpin(out var result));
            Assert.AreEqual(before - cost + result.Total, run.Money, 1e-9);
        }

        [Test]
        public void CannotSpinWithoutMoney()
        {
            var run = NewRun(new BalanceConfig { StartingMoney = 0 });
            Assert.IsFalse(run.CanSpin);
            Assert.IsFalse(run.TrySpin(out var result));
            Assert.IsNull(result);
        }

        [Test]
        public void SellingAddsSellPriceScaledByCondition()
        {
            var run = NewRun();
            double before = run.Money;
            Assert.AreEqual(15, run.SellScrap("cracked_mirror"));
            Assert.AreEqual(7, run.SellScrap("cracked_mirror", 0.5)); // 切り捨て
            Assert.AreEqual(before + 22, run.Money);
        }

        [Test]
        public void PaydayFailsBelowQuota()
        {
            var run = NewRun(new BalanceConfig { StartingMoney = 0 });
            for (int i = 0; i < 3; i++) run.CompleteDive();
            Assert.IsTrue(run.IsPaydayDue);
            Assert.AreEqual(PaydayOutcome.Failed, run.ResolvePayday());
            Assert.IsTrue(run.IsFailed);
        }

        [Test]
        public void PayingFinalQuotaClearsAndContinuesToEndless()
        {
            var config = BalanceConfig.CreateMvp();
            config.StartingMoney = 1000;
            var run = NewRun(config);

            for (int i = 0; i < 3; i++) run.CompleteDive();
            Assert.AreEqual(PaydayOutcome.Paid, run.ResolvePayday());
            Assert.AreEqual(2, run.Cycle);
            Assert.AreEqual(940, run.Money);

            for (int i = 0; i < 3; i++) run.CompleteDive();
            Assert.AreEqual(PaydayOutcome.Cleared, run.ResolvePayday());
            Assert.IsTrue(run.IsCleared);
            Assert.IsTrue(run.IsEndless);
            Assert.AreEqual(300 * config.EndlessQuotaGrowth, run.Quota);
        }

        [Test]
        public void CompleteDiveIsBlockedUntilPaydayResolved()
        {
            var run = NewRun();
            for (int i = 0; i < 3; i++) run.CompleteDive();
            Assert.Throws<System.InvalidOperationException>(() => run.CompleteDive());
        }

        [Test]
        public void RemovingFromReelCostsFee()
        {
            var run = NewRun();
            var target = run.Reel[0];
            double fee = run.RemovalFee;
            Assert.IsTrue(run.TryRemoveFromReel(target.InstanceId));
            Assert.AreEqual(4, run.Reel.Count);
            Assert.AreEqual(10 - fee, run.Money);
        }

        [Test]
        public void BuyingRelicRemovesItFromOffers()
        {
            var run = NewRun(new BalanceConfig { StartingMoney = 1000 });
            string offer = run.RelicOffers[0];
            Assert.IsTrue(run.TryBuyRelic(offer));
            Assert.AreEqual(1, run.Relics.Count);
            CollectionAssert.DoesNotContain(run.RelicOffers, offer);
            Assert.IsFalse(run.TryBuyRelic(offer));
        }

        [Test]
        public void RegularCardMakesFirstSpinOfCycleFree()
        {
            var config = new BalanceConfig { StartingMoney = 1000, RelicOffersPerCycle = 5 };
            var run = NewRun(config);
            Assert.IsTrue(run.TryBuyRelic("regular_card"));
            Assert.AreEqual(0, run.CurrentSpinCost);
            run.TrySpin(out _);
            Assert.Greater(run.CurrentSpinCost, 0);
        }

        [Test]
        public void EnemyBonusIsConsumedOnce()
        {
            var config = new BalanceConfig { StartingMoney = 1000, StartingReel = { "mannequin_head", "mannequin_head", "mannequin_head" } };
            config.StartingReel.RemoveAll(id => id == "empty_can");
            var run = NewRun(config);
            run.TrySpin(out var result);
            Assert.AreEqual(1, result.EnemyBonus);
            Assert.AreEqual(1, run.ConsumeEnemyBonus());
            Assert.AreEqual(0, run.ConsumeEnemyBonus());
        }

        [Test]
        public void DestroyedSymbolsLeaveTheReel()
        {
            var config = new BalanceConfig { StartingMoney = 1000 };
            config.StartingReel.Clear();
            for (int i = 0; i < 10; i++) config.StartingReel.Add("lottery_bundle");
            var run = NewRun(config);
            run.TrySpin(out var result);
            Assert.AreEqual(10 - result.DestroyedInstances.Count(), run.Reel.Count);
        }
    }
}
