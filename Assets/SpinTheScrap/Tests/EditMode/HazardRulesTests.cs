using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class HazardRulesTests
    {
        [Test]
        public void NoFallDamageFromMezzanineHeight()
        {
            var rules = new HazardRules();
            // プレイヤーの重力は 20m/s²。2m から落ちたときの着地速度は √(2 × 20 × 2) ≈ 8.9m/s
            Assert.AreEqual(0, rules.FallDamage(System.Math.Sqrt(2 * 20 * 2)));
            Assert.Greater(rules.FallDamage(System.Math.Sqrt(2 * 20 * 4)), 0, "4m から落ちると傷つく");
        }

        [Test]
        public void FallDamageGrowsWithLandingSpeed()
        {
            var rules = new HazardRules();
            double small = rules.FallDamage(11);
            double big = rules.FallDamage(14);
            Assert.Greater(small, 0);
            Assert.Greater(big, small);
            Assert.AreEqual(System.Math.Ceiling((14 - rules.FallDamageStartSpeed) * rules.FallDamagePerSpeed), big);
        }

        [Test]
        public void OnlyMetalShortsOutPuddle()
        {
            var rules = new HazardRules();
            Assert.IsTrue(rules.CanShortCircuit(SymbolTag.Metal));
            Assert.IsTrue(rules.CanShortCircuit(SymbolTag.Metal | SymbolTag.Curse));
            Assert.IsFalse(rules.CanShortCircuit(SymbolTag.Electric));
            Assert.IsFalse(rules.CanShortCircuit(SymbolTag.None));
        }

        [Test]
        public void PuddlesNeverInEntranceAndMoreLikelyDeeper()
        {
            var rules = new HazardRules();
            Assert.AreEqual(0, rules.PuddleChance(RoomKind.Entrance, 0));
            Assert.Less(rules.PuddleChance(RoomKind.Hall, 1), rules.PuddleChance(RoomKind.Hall, 3));
            Assert.LessOrEqual(rules.PuddleChance(RoomKind.Storage, 20), 0.8);
        }

        [Test]
        public void HealthGoesDownOnceAndResets()
        {
            var health = new HealthState(100);
            Assert.IsFalse(health.Damage(60));
            Assert.AreEqual(40, health.Current);
            Assert.IsTrue(health.Damage(60), "この呼び出しで倒れる");
            Assert.AreEqual(0, health.Current);
            Assert.IsFalse(health.Damage(10), "倒れた後は true を返さない");
            health.Reset();
            Assert.AreEqual(100, health.Current);
            Assert.IsFalse(health.IsDown);
        }
    }
}
