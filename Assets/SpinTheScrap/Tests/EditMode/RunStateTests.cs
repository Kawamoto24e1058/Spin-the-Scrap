using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class RunStateTests
    {
        static RunState NewRun(BalanceConfig config = null, ulong seed = 1) =>
            new RunState(MvpContent.Create(), config ?? new BalanceConfig(), seed);

        /// <summary>攻略条件を最後まで進める（種類に合わせて報告する）。</summary>
        static void CompleteObjective(RunState run) => run.ReportObjective(run.Objective.Kind, run.Objective.Target);

        [Test]
        public void StartsOnFloorOneWithReelMedalsLivesAndObjective()
        {
            var run = NewRun();
            Assert.AreEqual(5, run.Reel.Count);
            Assert.IsTrue(run.Reel.All(s => s.SymbolId == "empty_can"));
            Assert.AreEqual(10, run.Medals);
            Assert.AreEqual(1, run.Floor);
            Assert.AreEqual(3, run.Lives);
            Assert.IsNotNull(run.Objective);
            Assert.IsFalse(run.Objective.IsComplete);
            Assert.AreEqual(3, run.RelicOffers.Count);
        }

        [Test]
        public void SpinCostRisesWithinFloor()
        {
            var run = NewRun(new BalanceConfig { StartingMedals = 1000 });
            double first = run.CurrentSpinCost;
            for (int i = 0; i < 5; i++) run.TrySpin(out _, out _);
            Assert.Greater(run.CurrentSpinCost, first);
        }

        [Test]
        public void SpinPaysOutAndChargesCost()
        {
            var run = NewRun();
            double before = run.Medals;
            double cost = run.CurrentSpinCost;
            Assert.IsTrue(run.TrySpin(out var result, out var rewards));
            double medalRewards = rewards.Where(r => r.IsMedals).Sum(r => r.Medals);
            Assert.AreEqual(before - cost + result.Total + medalRewards, run.Medals, 1e-9);
        }

        [Test]
        public void SpinRewardsGoIntoTools()
        {
            // 空き缶だけのリールなら、金属のラインが出れば鉄パイプか釘打ち機が増える
            var run = NewRun(new BalanceConfig { StartingMedals = 1000 });
            for (int i = 0; i < 30; i++) run.TrySpin(out _, out _);
            Assert.Greater(run.ToolCount(ToolIds.Pipe) + run.ToolCount(ToolIds.NailGun), 0);
        }

        [Test]
        public void CannotSpinWithoutMedals()
        {
            var run = NewRun(new BalanceConfig { StartingMedals = 0 });
            Assert.IsFalse(run.CanSpin);
            Assert.IsFalse(run.TrySpin(out var result, out var rewards));
            Assert.IsNull(result);
            Assert.IsNull(rewards);
        }

        [Test]
        public void ConvertingScrapGivesMedalsScaledByCondition()
        {
            var run = NewRun();
            double before = run.Medals;
            Assert.AreEqual(15, run.ConvertToMedals("cracked_mirror"));
            Assert.AreEqual(7, run.ConvertToMedals("cracked_mirror", 0.5)); // 切り捨て
            Assert.AreEqual(before + 22, run.Medals);
        }

        [Test]
        public void CannotAdvanceUntilObjectiveIsComplete()
        {
            var run = NewRun();
            Assert.Throws<System.InvalidOperationException>(() => run.AdvanceFloor());
            CompleteObjective(run);
            run.AdvanceFloor();
            Assert.AreEqual(2, run.Floor);
            Assert.IsFalse(run.Objective.IsComplete, "新しい階層の攻略条件");
            Assert.AreEqual(0, run.SpinsThisFloor);
        }

        [Test]
        public void ReportsOfOtherKindsDoNotCount()
        {
            var run = NewRun();
            var other = run.Objective.Kind == FloorObjectiveKind.DefeatEnemies ? FloorObjectiveKind.FindKey : FloorObjectiveKind.DefeatEnemies;
            Assert.IsFalse(run.ReportObjective(other, 100));
            Assert.AreEqual(0, run.Objective.Progress);
        }

        [Test]
        public void ClearingTheLastFloorClearsTheRun()
        {
            var run = NewRun(BalanceConfig.CreateMvp());
            for (int f = 1; f < run.Config.FloorCount; f++)
            {
                CompleteObjective(run);
                run.AdvanceFloor();
            }
            Assert.IsTrue(run.IsLastFloor);
            Assert.IsFalse(run.IsCleared);
            CompleteObjective(run);
            run.AdvanceFloor();
            Assert.IsTrue(run.IsCleared);
            Assert.IsTrue(run.IsOver);
            Assert.IsFalse(run.CanSpin);
        }

        [Test]
        public void GoingDownCostsALifeAndResetsObjectiveProgress()
        {
            var run = NewRun();
            run.ReportObjective(run.Objective.Kind, run.Objective.Target / 2);
            run.OnDowned();
            Assert.AreEqual(2, run.Lives);
            Assert.AreEqual(0, run.Objective.Progress);
            Assert.IsFalse(run.IsFailed);
            run.OnDowned();
            run.OnDowned();
            Assert.IsTrue(run.IsFailed);
            Assert.IsFalse(run.ReportObjective(run.Objective.Kind, 1), "ランが終わったら進まない");
        }

        [Test]
        public void RemovingFromReelCostsFee()
        {
            var run = NewRun();
            var target = run.Reel[0];
            double fee = run.RemovalFee;
            Assert.IsTrue(run.TryRemoveFromReel(target.InstanceId));
            Assert.AreEqual(4, run.Reel.Count);
            Assert.AreEqual(10 - fee, run.Medals);
        }

        [Test]
        public void BuyingRelicRemovesItFromOffers()
        {
            var run = NewRun(new BalanceConfig { StartingMedals = 1000 });
            string offer = run.RelicOffers[0];
            Assert.IsTrue(run.TryBuyRelic(offer));
            Assert.AreEqual(1, run.Relics.Count);
            CollectionAssert.DoesNotContain(run.RelicOffers, offer);
            Assert.IsFalse(run.TryBuyRelic(offer));
        }

        [Test]
        public void RegularCardMakesFirstSpinOfFloorFree()
        {
            var config = new BalanceConfig { StartingMedals = 1000, RelicOffersPerFloor = 10 };
            var run = NewRun(config);
            Assert.IsTrue(run.TryBuyRelic("regular_card"));
            Assert.AreEqual(0, run.CurrentSpinCost);
            run.TrySpin(out _, out _);
            Assert.Greater(run.CurrentSpinCost, 0);
        }

        [Test]
        public void EnemyBonusIsConsumedOnce()
        {
            var config = new BalanceConfig { StartingMedals = 1000, StartingReel = { "mannequin_head", "mannequin_head", "mannequin_head" } };
            config.StartingReel.RemoveAll(id => id == "empty_can");
            var run = NewRun(config);
            run.TrySpin(out var result, out _);
            Assert.GreaterOrEqual(run.PendingEnemyBonus, result.EnemyBonus);
            int bonus = run.ConsumeEnemyBonus();
            Assert.Greater(bonus, 0);
            Assert.AreEqual(0, run.ConsumeEnemyBonus());
        }

        [Test]
        public void DestroyedSymbolsLeaveTheReel()
        {
            var config = new BalanceConfig { StartingMedals = 1000 };
            config.StartingReel.Clear();
            for (int i = 0; i < 10; i++) config.StartingReel.Add("lottery_bundle");
            var run = NewRun(config);
            run.TrySpin(out var result, out _);
            Assert.AreEqual(10 - result.DestroyedInstances.Count(), run.Reel.Count);
        }
    }
}
