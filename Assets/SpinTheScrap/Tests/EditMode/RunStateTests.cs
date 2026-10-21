using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class RunStateTests
    {
        static RunState NewRun(BalanceConfig config = null, ulong seed = 1) =>
            new RunState(MvpContent.Create(), config ?? new BalanceConfig(), seed);

        static RunState Rich(ulong seed = 1) => NewRun(new BalanceConfig { StartingMedals = 10000 }, seed);

        static void CompleteObjective(RunState run) => run.ReportObjective(run.Objective.Kind, run.Objective.Target);

        [Test]
        public void StartsOnFloorOneWithReelsMedalsLivesAndObjective()
        {
            var run = NewRun();
            foreach (var kind in RunState.AllMachines)
            {
                Assert.AreEqual(5, run.ReelOf(kind).Count, kind.ToString());
                Assert.IsTrue(run.ReelOf(kind).All(s => s.SymbolId == "empty_can"));
            }
            Assert.AreEqual(10, run.Medals);
            Assert.AreEqual(1, run.Floor);
            Assert.AreEqual(3, run.Lives);
            Assert.AreEqual(FloorObjectiveKind.DefeatEnemies, run.Objective.Kind);
            Assert.AreEqual(run.Config.KillTargetFor(1), run.Objective.Target);
        }

        [Test]
        public void EachMachineHasItsOwnReel()
        {
            var run = NewRun();
            run.AddToReel(MachineKind.Weapon, "rusty_screw", 0);
            Assert.AreEqual(6, run.ReelOf(MachineKind.Weapon).Count);
            Assert.AreEqual(5, run.ReelOf(MachineKind.Perk).Count, "ほかの台のリールは変わらない");
        }

        [Test]
        public void SpinCostDiffersByMachineAndRisesPerSpin()
        {
            var run = Rich();
            Assert.Less(run.CostOf(MachineKind.Tool), run.CostOf(MachineKind.Perk));
            Assert.Less(run.CostOf(MachineKind.Perk), run.CostOf(MachineKind.Weapon));
            Assert.Less(run.CostOf(MachineKind.Weapon), run.CostOf(MachineKind.Mod));
            double first = run.CostOf(MachineKind.Perk);
            run.TrySpin(MachineKind.Perk, 0, out _);
            Assert.Greater(run.CostOf(MachineKind.Perk), first);
            Assert.AreEqual(run.MachineRules.CostFor(MachineKind.Weapon, 1, 0), run.CostOf(MachineKind.Weapon), "ほかの台の費用は上がらない");
        }

        [Test]
        public void SpinChargesCostButDoesNotPayMedalsExceptToolFallback()
        {
            var run = Rich();
            double before = run.Medals;
            double cost = run.CostOf(MachineKind.Weapon);
            Assert.IsTrue(run.TrySpin(MachineKind.Weapon, 0, out var outcome));
            Assert.AreEqual(before - cost, run.Medals);
            Assert.AreEqual(cost, outcome.Cost);
        }

        [Test]
        public void WeaponMachineGivesWeaponToThePuller()
        {
            var run = Rich();
            Assert.IsNull(run.Player(0).Weapon);
            Assert.IsTrue(run.TrySpin(MachineKind.Weapon, 0, out var outcome));
            Assert.IsNotNull(outcome.Weapon);
            Assert.AreSame(outcome.Weapon, run.Player(0).Weapon);
            Assert.IsNull(run.Player(1).Weapon, "ほかの人には渡らない");
            Assert.AreEqual(SymbolTag.Metal, outcome.Weapon.Definition.Family, "空き缶だけなら金属の武器");
        }

        [Test]
        public void PerkMachineLevelsUpTheBoardsTags()
        {
            var run = Rich();
            for (int i = 0; i < 8; i++) run.TrySpin(MachineKind.Perk, 0, out _);
            Assert.Greater(run.Player(0).Perks.Level(SymbolTag.Metal), 0);
            Assert.Greater(run.StatsOf(0).DamageMultiplier, 1);
            Assert.AreEqual(0, run.Player(1).Perks.Level(SymbolTag.Metal));
        }

        [Test]
        public void ModMachineNeedsAWeapon()
        {
            var run = Rich();
            Assert.IsFalse(run.CanSpin(MachineKind.Mod, 0), "武器がなければ回せない");
            double before = run.Medals;
            Assert.IsFalse(run.TrySpin(MachineKind.Mod, 0, out _));
            Assert.AreEqual(before, run.Medals, "回せないときはメダルを取らない");
            run.TrySpin(MachineKind.Weapon, 0, out _);
            Assert.IsTrue(run.TrySpin(MachineKind.Mod, 0, out var outcome));
            Assert.AreEqual(WeaponModKind.Damage, outcome.Mod, "空き缶だけなら金属の改造");
            Assert.AreEqual(1, run.Player(0).Weapon.Mods.Count);
        }

        [Test]
        public void ToolMachineFallsBackToMedalsForMetal()
        {
            var run = Rich();
            double before = run.Medals;
            run.TrySpin(MachineKind.Tool, 0, out var outcome);
            if (outcome.ToolId == null)
                Assert.AreEqual(before - outcome.Cost + outcome.Medals, run.Medals);
            else
                Assert.AreEqual(ToolIds.Cart, outcome.ToolId, "金属は質 3 のときだけ台車");
        }

        [Test]
        public void CannotSpinWithoutMedals()
        {
            var run = NewRun(new BalanceConfig { StartingMedals = 0 });
            Assert.IsFalse(run.CanSpin(MachineKind.Tool, 0));
            Assert.IsFalse(run.TrySpin(MachineKind.Tool, 0, out var outcome));
            Assert.IsNull(outcome);
        }

        [Test]
        public void ConvertingScrapGivesMedalsScaledByConditionAndGamblePerk()
        {
            var run = NewRun();
            double before = run.Medals;
            Assert.AreEqual(15, run.ConvertToMedals("cracked_mirror"));
            Assert.AreEqual(7, run.ConvertToMedals("cracked_mirror", 0.5)); // 切り捨て
            Assert.AreEqual(before + 22, run.Medals);

            run.Player(0).Perks.AddXp(SymbolTag.Gamble, 1000); // Lv5 → +50%
            Assert.AreEqual(22, run.MedalValueFor("cracked_mirror"));
        }

        [Test]
        public void DeeperFloorsPayMoreMedalsForTheSameScrap()
        {
            var run = NewRun();
            double floor1 = run.MedalValueFor("cracked_mirror");
            CompleteObjective(run);
            run.AdvanceFloor();
            CompleteObjective(run);
            run.AdvanceFloor();
            Assert.AreEqual(System.Math.Floor(15 * (1 + 2 * run.Config.MedalValueFloorGrowth)), run.MedalValueFor("cracked_mirror"));
            Assert.Greater(run.MedalValueFor("cracked_mirror"), floor1);
        }

        [Test]
        public void FloorsGoNormalNormalBoss()
        {
            var run = NewRun();
            Assert.IsFalse(run.IsBossFloor);
            CompleteObjective(run);
            run.AdvanceFloor();
            Assert.IsFalse(run.IsBossFloor);
            Assert.AreEqual(run.Config.KillTargetFor(2), run.Objective.Target, "深いほど多く倒す");
            CompleteObjective(run);
            run.AdvanceFloor();
            Assert.IsTrue(run.IsBossFloor);
            Assert.AreEqual(FloorObjectiveKind.DefeatBoss, run.Objective.Kind);
            Assert.AreEqual(1, run.Objective.Target);
        }

        [Test]
        public void CannotAdvanceUntilObjectiveIsCompleteAndSpinCostsReset()
        {
            var run = Rich();
            Assert.Throws<System.InvalidOperationException>(() => run.AdvanceFloor());
            run.TrySpin(MachineKind.Perk, 0, out _);
            CompleteObjective(run);
            run.AdvanceFloor();
            Assert.AreEqual(2, run.Floor);
            Assert.AreEqual(0, run.SpinsThisFloor(MachineKind.Perk));
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
            Assert.IsTrue(run.IsBossFloor, "試作の 3 階層目はボス");
            CompleteObjective(run);
            run.AdvanceFloor();
            Assert.IsTrue(run.IsCleared);
            Assert.IsFalse(run.CanSpin(MachineKind.Tool, 0));
        }

        [Test]
        public void GoingDownCostsALifeAndResetsObjectiveProgress()
        {
            var run = NewRun();
            run.ReportObjective(run.Objective.Kind, 3);
            run.OnDowned();
            Assert.AreEqual(2, run.Lives);
            Assert.AreEqual(0, run.Objective.Progress);
            run.OnDowned();
            run.OnDowned();
            Assert.IsTrue(run.IsFailed);
            Assert.IsFalse(run.ReportObjective(run.Objective.Kind, 1), "ランが終わったら進まない");
        }

        [Test]
        public void RunRecordsKillsAndSpins()
        {
            var run = Rich();
            Assert.IsFalse(run.ReportKill(false));
            run.TrySpin(MachineKind.Tool, 0, out var outcome);
            run.TrySpin(MachineKind.Perk, 0, out _);
            Assert.AreEqual(1, run.Kills);
            Assert.AreEqual(2, run.TotalSpins);
            Assert.GreaterOrEqual(run.BestTier, outcome.Reading.Tier);
            Assert.AreEqual(1, run.Objective.Progress, "倒した数が攻略条件に入る");
        }

        [Test]
        public void RemovingFromReelCostsFee()
        {
            var run = NewRun();
            var target = run.ReelOf(MachineKind.Perk)[0];
            double fee = run.RemovalFee;
            Assert.IsTrue(run.TryRemoveFromReel(MachineKind.Perk, target.InstanceId));
            Assert.AreEqual(4, run.ReelOf(MachineKind.Perk).Count);
            Assert.AreEqual(10 - fee, run.Medals);
        }

        [Test]
        public void BuyingRelicRemovesItFromOffers()
        {
            var run = Rich();
            string offer = run.RelicOffers[0];
            Assert.IsTrue(run.TryBuyRelic(offer));
            Assert.AreEqual(1, run.Relics.Count);
            CollectionAssert.DoesNotContain(run.RelicOffers, offer);
            Assert.IsFalse(run.TryBuyRelic(offer));
        }

        [Test]
        public void EnemyBonusIsConsumedOnce()
        {
            var config = new BalanceConfig { StartingMedals = 10000, StartingReel = { "mannequin_head", "mannequin_head", "mannequin_head" } };
            config.StartingReel.RemoveAll(id => id == "empty_can");
            var run = NewRun(config);
            run.TrySpin(MachineKind.Perk, 0, out var outcome);
            Assert.AreEqual(outcome.Reading.Result.EnemyBonus, run.PendingEnemyBonus);
            Assert.AreEqual(1, run.ConsumeEnemyBonus());
            Assert.AreEqual(0, run.ConsumeEnemyBonus());
        }

        [Test]
        public void DestroyedSymbolsLeaveTheReel()
        {
            var config = new BalanceConfig { StartingMedals = 10000 };
            config.StartingReel.Clear();
            for (int i = 0; i < 10; i++) config.StartingReel.Add("lottery_bundle");
            var run = NewRun(config);
            run.TrySpin(MachineKind.Tool, 0, out var outcome);
            Assert.AreEqual(10 - outcome.Reading.Result.DestroyedInstances.Count(), run.ReelOf(MachineKind.Tool).Count);
        }
    }
}
