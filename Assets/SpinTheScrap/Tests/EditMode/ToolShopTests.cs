using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class ToolShopTests
    {
        static RunState NewRun(double money)
        {
            var config = new BalanceConfig { StartingMoney = money };
            return new RunState(MvpContent.Create(), config, 1);
        }

        [Test]
        public void BuyingSpendsMoneyAndAddsTool()
        {
            var run = NewRun(100);
            var price = run.ToolCatalog.Get(ToolIds.Bandage).Price;
            Assert.IsTrue(run.TryBuyTool(ToolIds.Bandage));
            Assert.AreEqual(100 - price, run.Money);
            Assert.AreEqual(1, run.ToolCount(ToolIds.Bandage));
        }

        [Test]
        public void CannotBuyWithoutMoney()
        {
            var run = NewRun(1);
            Assert.IsFalse(run.CanBuyTool(ToolIds.Cart));
            Assert.IsFalse(run.TryBuyTool(ToolIds.Cart));
            Assert.AreEqual(1, run.Money);
            Assert.IsFalse(run.HasTool(ToolIds.Cart));
        }

        [Test]
        public void PermanentToolsAreBoughtOnceAndNeverUsedUp()
        {
            var run = NewRun(100);
            Assert.IsTrue(run.TryBuyTool(ToolIds.Cart));
            Assert.IsFalse(run.TryBuyTool(ToolIds.Cart), "2 台目は買えない");
            Assert.IsTrue(run.TryUseTool(ToolIds.Cart));
            Assert.IsTrue(run.HasTool(ToolIds.Cart), "使っても減らない");
        }

        [Test]
        public void ConsumablesStackToLimitAndRunOut()
        {
            var run = NewRun(100);
            int max = run.ToolCatalog.Get(ToolIds.Firecracker).MaxCount;
            for (int i = 0; i < max; i++) Assert.IsTrue(run.TryBuyTool(ToolIds.Firecracker));
            Assert.IsFalse(run.TryBuyTool(ToolIds.Firecracker), "上限まで");
            for (int i = 0; i < max; i++) Assert.IsTrue(run.TryUseTool(ToolIds.Firecracker));
            Assert.IsFalse(run.TryUseTool(ToolIds.Firecracker), "使い切った");
        }

        [Test]
        public void FlashlightDrainsAndSwapsInSpareBattery()
        {
            var run = NewRun(100);
            double life = run.Config.FlashlightBatterySeconds;
            Assert.IsTrue(run.DrainFlashlight(life / 2));
            Assert.AreEqual(0.5, run.FlashlightCharge, 1e-9);
            Assert.IsFalse(run.DrainFlashlight(life), "予備がなければ切れる");
            Assert.AreEqual(0, run.FlashlightCharge);

            run.TryBuyTool(ToolIds.Battery);
            Assert.IsTrue(run.DrainFlashlight(0.1), "予備に交換して点く");
            Assert.AreEqual(1, run.FlashlightCharge, 1e-9);
            Assert.AreEqual(0, run.ToolCount(ToolIds.Battery));
        }

        [Test]
        public void InsulatedBootsReducePuddleDamage()
        {
            var rules = new HazardRules();
            Assert.AreEqual(rules.PuddleDamagePerSecond, rules.PuddleDamage(1, false));
            Assert.AreEqual(rules.PuddleDamagePerSecond * rules.InsulatedBootsPuddleFactor, rules.PuddleDamage(1, true), 1e-9);
        }

        [Test]
        public void BandageHealsUpToMaxButNotWhenDown()
        {
            var health = new HealthState(100);
            health.Damage(30);
            Assert.AreEqual(30, health.Heal(40), "最大までしか回復しない");
            Assert.AreEqual(100, health.Current);
            health.Damage(100);
            Assert.AreEqual(0, health.Heal(40), "倒れたら回復しない");
        }

        [Test]
        public void CatalogRejectsUnknownAndDuplicateIds()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => ToolCatalog.CreateMvp().Get("nope"));
            Assert.Throws<System.ArgumentException>(() => new ToolCatalog(new[]
            {
                new ToolDefinition("a", 1, true), new ToolDefinition("a", 2, false),
            }));
        }
    }
}
