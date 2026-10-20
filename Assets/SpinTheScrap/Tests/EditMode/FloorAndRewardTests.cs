using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    /// <summary>階層の攻略条件、スロットから出る物、道具の数（docs/floor-design.md）。</summary>
    public class FloorAndRewardTests
    {
        static RunState NewRun(double medals = 100) =>
            new RunState(MvpContent.Create(), new BalanceConfig { StartingMedals = medals }, 1);

        static LineHit Line(int length, SymbolTag tags, double bonus = 10) => new LineHit(0, length, tags, bonus);

        // ---- 攻略条件 ----

        [Test]
        public void ObjectiveCompletesAtTargetAndIgnoresOtherKinds()
        {
            var objective = new FloorObjective(FloorObjectiveKind.DefeatEnemies, 3);
            Assert.IsFalse(objective.Report(FloorObjectiveKind.FindKey));
            Assert.IsFalse(objective.Report(FloorObjectiveKind.DefeatEnemies));
            Assert.IsFalse(objective.Report(FloorObjectiveKind.DefeatEnemies));
            Assert.IsTrue(objective.Report(FloorObjectiveKind.DefeatEnemies), "3 体目で達成");
            Assert.IsFalse(objective.Report(FloorObjectiveKind.DefeatEnemies), "達成後は true を返さない");
            Assert.AreEqual(3, objective.Progress);
        }

        [Test]
        public void ObjectivesVaryAndNeverRepeatBackToBack()
        {
            var rng = new Rng(5);
            FloorObjectiveKind? previous = null;
            var seen = new System.Collections.Generic.HashSet<FloorObjectiveKind>();
            for (int floor = 1; floor <= 40; floor++)
            {
                var o = FloorObjective.Roll(floor, rng, previous);
                Assert.AreNotEqual(previous, o.Kind);
                seen.Add(o.Kind);
                previous = o.Kind;
            }
            Assert.AreEqual(4, seen.Count, "4 種類とも出る");
        }

        [Test]
        public void DeeperFloorsNeedMore()
        {
            Assert.Less(FloorObjective.TargetFor(FloorObjectiveKind.DefeatEnemies, 1), FloorObjective.TargetFor(FloorObjectiveKind.DefeatEnemies, 9));
            Assert.Less(FloorObjective.TargetFor(FloorObjectiveKind.PowerGenerator, 1), FloorObjective.TargetFor(FloorObjectiveKind.PowerGenerator, 5));
            Assert.AreEqual(1, FloorObjective.TargetFor(FloorObjectiveKind.FindKey, 7));
        }

        [Test]
        public void RoomsAndEnemiesGrowWithFloor()
        {
            var config = new BalanceConfig();
            Assert.Less(config.RoomCountFor(1), config.RoomCountFor(9));
            Assert.LessOrEqual(config.RoomCountFor(100), config.MaxRoomCount);
            var enemies = new EnemyRules();
            Assert.AreEqual(1, enemies.CountFor(1, 0, 4));
            Assert.AreEqual(2, enemies.CountFor(3, 0, 5));
            Assert.AreEqual(3, enemies.CountFor(1, 9, 4), "入口以外の部屋数まで");
        }

        // ---- 出る物 ----

        [Test]
        public void LongerLinesGiveBetterTier()
        {
            Assert.AreEqual(1, SpinRewards.TierFor(Line(3, SymbolTag.Metal)));
            Assert.AreEqual(2, SpinRewards.TierFor(Line(4, SymbolTag.Metal)));
            Assert.AreEqual(3, SpinRewards.TierFor(Line(5, SymbolTag.Metal)));
            Assert.AreEqual(2, SpinRewards.TierFor(Line(3, SymbolTag.Metal | SymbolTag.Luck)), "幸運で 1 段上がる");
            Assert.AreEqual(3, SpinRewards.TierFor(Line(5, SymbolTag.Metal | SymbolTag.Luck)), "3 が上限");
        }

        [Test]
        public void MetalLinesGiveWeapons()
        {
            var rng = new Rng(1);
            Assert.AreEqual(ToolIds.Pipe, SpinRewards.FromLine(Line(3, SymbolTag.Metal), rng).Single().ToolId);
            Assert.AreEqual(ToolIds.NailGun, SpinRewards.FromLine(Line(4, SymbolTag.Metal), rng).Single().ToolId);
            var top = SpinRewards.FromLine(Line(5, SymbolTag.Metal), rng);
            CollectionAssert.AreEquivalent(new[] { ToolIds.NailGun, ToolIds.Cart }, top.Select(r => r.ToolId));
        }

        [Test]
        public void EachTagFamilyHasItsReward()
        {
            var rng = new Rng(1);
            Assert.AreEqual(ToolIds.Taser, SpinRewards.FromLine(Line(4, SymbolTag.Electric), rng).First().ToolId);
            Assert.AreEqual(ToolIds.Bandage, SpinRewards.FromLine(Line(3, SymbolTag.Food), rng).Single().ToolId);
            Assert.AreEqual(ToolIds.Firecracker, SpinRewards.FromLine(Line(3, SymbolTag.Toy), rng).Single().ToolId);
            var paper = SpinRewards.FromLine(Line(3, SymbolTag.Paper, bonus: 7.2), rng).Single();
            Assert.IsTrue(paper.IsMedals);
            Assert.AreEqual(8, paper.Medals);
        }

        [Test]
        public void CurseOutranksOtherTagsAndAddsEnemies()
        {
            var rng = new Rng(1);
            var reward = SpinRewards.FromLine(Line(3, SymbolTag.Curse | SymbolTag.Metal), rng).Single();
            Assert.AreEqual(ToolIds.NailGun, reward.ToolId);
            Assert.AreEqual(12, reward.Count);
        }

        [Test]
        public void GambleLinesPickAnotherFamily()
        {
            var ids = Enumerable.Range(1, 50)
                .Select(s => SpinRewards.FromLine(Line(3, SymbolTag.Gamble), new Rng((ulong)s)).First().ToolId)
                .Distinct().ToList();
            Assert.Greater(ids.Count, 1, "毎回同じ物にはならない");
            Assert.IsFalse(ids.Contains(null), "ギャンブルはメダルではなく道具になる");
        }

        // ---- 道具の数 ----

        [Test]
        public void ToolsCapAtMaxAndPermanentToolsStay()
        {
            var run = NewRun();
            int max = run.ToolCatalog.Get(ToolIds.Firecracker).MaxCount;
            Assert.AreEqual(max, run.AddTool(ToolIds.Firecracker, max + 5), "上限を超えた分は捨てる");
            Assert.AreEqual(0, run.AddTool(ToolIds.Firecracker, 1));
            Assert.AreEqual(1, run.AddTool(ToolIds.Cart, 3), "永続は 1 つ");
            Assert.IsTrue(run.TryUseTool(ToolIds.Cart));
            Assert.IsTrue(run.HasTool(ToolIds.Cart), "使っても減らない");
            for (int i = 0; i < max; i++) Assert.IsTrue(run.TryUseTool(ToolIds.Firecracker));
            Assert.IsFalse(run.TryUseTool(ToolIds.Firecracker), "使い切った");
        }

        [Test]
        public void FlashlightDrainsAndSwapsInSpareBattery()
        {
            var run = NewRun();
            double life = run.Config.FlashlightBatterySeconds;
            Assert.IsTrue(run.DrainFlashlight(life / 2));
            Assert.AreEqual(0.5, run.FlashlightCharge, 1e-9);
            Assert.IsFalse(run.DrainFlashlight(life), "予備がなければ切れる");
            run.AddTool(ToolIds.Battery, 1);
            Assert.IsTrue(run.DrainFlashlight(0.1), "予備に交換して点く");
            Assert.AreEqual(1, run.FlashlightCharge, 1e-9);
            Assert.AreEqual(0, run.ToolCount(ToolIds.Battery));
        }

        [Test]
        public void InsulatedBootsReducePuddleDamage()
        {
            var rules = new HazardRules();
            Assert.AreEqual(rules.PuddleDamagePerSecond * rules.InsulatedBootsPuddleFactor, rules.PuddleDamage(1, true), 1e-9);
        }

        [Test]
        public void BandageHealsUpToMaxButNotWhenDown()
        {
            var health = new HealthState(100);
            health.Damage(30);
            Assert.AreEqual(30, health.Heal(40));
            health.Damage(100);
            Assert.AreEqual(0, health.Heal(40));
        }
    }
}
