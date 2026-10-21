using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class ToolRulesTests
    {
        [Test]
        public void CartSlowsDownWithLoadAndCapsAtFull()
        {
            var rules = new ToolRules();
            Assert.AreEqual(rules.CartEmptySpeedFactor, rules.CartSpeedFactor(0), 1e-9);
            Assert.Greater(rules.CartSpeedFactor(10), rules.CartSpeedFactor(40));
            Assert.AreEqual(rules.CartFullSpeedFactor, rules.CartSpeedFactor(rules.CartFullLoadKg), 1e-9);
            Assert.AreEqual(rules.CartFullSpeedFactor, rules.CartSpeedFactor(500), 1e-9, "上限より積んでもそれ以上は遅くならない");
        }

        [Test]
        public void CartIsQuietWhenPushedSlowly()
        {
            var rules = new ToolRules();
            Assert.AreEqual(0, rules.CartRattleRadius(rules.CartRattleSpeed, 30));
            Assert.Greater(rules.CartRattleRadius(rules.CartRattleSpeed + 1, 0), 0);
        }

        [Test]
        public void HeavierLoadRattlesLouderUpToCap()
        {
            var rules = new ToolRules();
            Assert.Less(rules.CartRattleRadius(3, 0), rules.CartRattleRadius(3, 40));
            Assert.AreEqual(rules.CartMaxRattleNoiseRadius, rules.CartRattleRadius(3, 10000));
        }
    }
}
