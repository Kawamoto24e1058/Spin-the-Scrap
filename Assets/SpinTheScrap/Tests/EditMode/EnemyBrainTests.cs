using System.Linq;
using System.Numerics;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class EnemyBrainTests
    {
        static readonly Vector3 Origin = Vector3.Zero;
        static readonly Vector3 Forward = new Vector3(0, 0, 1);
        static readonly Vector3 Far = new Vector3(0, 0, 50);

        static EnemyPerception See(Vector3 player, bool lineOfSight = true) => new EnemyPerception
        {
            Position = Origin, Forward = Forward, PlayerPosition = player, HasLineOfSight = lineOfSight,
        };

        [Test]
        public void WandersWhenNothingHappens()
        {
            var brain = new EnemyBrain(new EnemyRules());
            var wander = new Vector3(5, 0, 5);
            var d = brain.Tick(0.1, See(Far, false), wander);
            Assert.AreEqual(EnemyState.Wander, d.State);
            Assert.AreEqual(wander, d.MoveTarget);
            Assert.AreEqual(new EnemyRules().WanderSpeed, d.Speed);
        }

        [Test]
        public void SeesPlayerInFrontButNotBehindUnlessClose()
        {
            var brain = new EnemyBrain(new EnemyRules());
            Assert.IsTrue(brain.CanSee(See(new Vector3(0, 0, 8))));
            Assert.IsFalse(brain.CanSee(See(new Vector3(0, 0, -8))), "後ろは見えない");
            Assert.IsTrue(brain.CanSee(See(new Vector3(0, 0, -2))), "すぐ後ろなら気づく");
            Assert.IsFalse(brain.CanSee(See(new Vector3(0, 0, 8), lineOfSight: false)), "壁越しは見えない");
            Assert.IsFalse(brain.CanSee(See(new Vector3(0, 0, 20))), "遠すぎる");
        }

        [Test]
        public void InvestigatesNoiseThenLooksAroundThenWanders()
        {
            var rules = new EnemyRules();
            var brain = new EnemyBrain(rules);
            var noise = new Vector3(6, 0, 0);
            var p = See(Far, false);
            p.HeardNoise = noise;
            var d = brain.Tick(0.1, p, null);
            Assert.AreEqual(EnemyState.Investigate, d.State);
            Assert.AreEqual(noise, d.MoveTarget);
            Assert.AreEqual(rules.InvestigateSpeed, d.Speed);

            // 音の場所に着いたら見回し、時間が過ぎたら徘徊に戻る
            var arrived = See(Far, false);
            arrived.Position = noise;
            d = brain.Tick(0.1, arrived, null);
            Assert.AreEqual(EnemyState.Investigate, d.State);
            Assert.IsNull(d.MoveTarget);
            d = brain.Tick(rules.LookAroundSeconds + 0.1, arrived, null);
            Assert.AreEqual(EnemyState.Wander, d.State);
        }

        [Test]
        public void ChasesThenInvestigatesLastSeenAfterLosingSight()
        {
            var rules = new EnemyRules();
            var brain = new EnemyBrain(rules);
            var player = new Vector3(0, 0, 6);
            var d = brain.Tick(0.1, See(player), null);
            Assert.AreEqual(EnemyState.Chase, d.State);
            Assert.AreEqual(rules.ChaseSpeed, d.Speed);

            // 見失ってもしばらくは最後に見た場所へ向かって追い続ける
            d = brain.Tick(1, See(Far, false), null);
            Assert.AreEqual(EnemyState.Chase, d.State);
            Assert.AreEqual(player, d.MoveTarget);
            d = brain.Tick(rules.LoseSightSeconds, See(Far, false), null);
            Assert.AreEqual(EnemyState.Investigate, d.State);
            Assert.AreEqual(player, d.MoveTarget);
        }

        [Test]
        public void AttacksInReachWithCooldown()
        {
            var rules = new EnemyRules();
            var brain = new EnemyBrain(rules);
            var close = new Vector3(0, 0, 1);
            Assert.IsTrue(brain.Tick(0.1, See(close), null).Attack);
            Assert.IsFalse(brain.Tick(0.1, See(close), null).Attack, "クールダウン中");
            Assert.IsTrue(brain.Tick(rules.AttackCooldown, See(close), null).Attack);
        }

        [Test]
        public void HardThrowStunsSoftThrowDoesNot()
        {
            var rules = new EnemyRules();
            var brain = new EnemyBrain(rules);
            Assert.IsFalse(brain.OnHit(rules.StunHitSpeed - 1));
            Assert.IsTrue(brain.OnHit(rules.StunHitSpeed + 1));
            var d = brain.Tick(0.1, See(new Vector3(0, 0, 1)), null);
            Assert.AreEqual(EnemyState.Stunned, d.State);
            Assert.IsFalse(d.Attack, "ひるんでいる間は攻撃しない");
            Assert.IsNull(d.MoveTarget);
            d = brain.Tick(rules.StunSeconds, See(new Vector3(0, 0, 1)), null);
            Assert.AreEqual(EnemyState.Chase, d.State, "ひるみが解けて見えていれば追う");
        }

        [Test]
        public void FlashlightDrawsAttention()
        {
            var brain = new EnemyBrain(new EnemyRules());
            var player = new Vector3(0, 0, -10); // 後ろなので見えていない
            var p = See(player);
            p.LitByFlashlight = true;
            var d = brain.Tick(0.1, p, null);
            Assert.AreEqual(EnemyState.Investigate, d.State);
            Assert.AreEqual(player, d.MoveTarget);
        }

        [Test]
        public void IgnoresDownedPlayer()
        {
            var brain = new EnemyBrain(new EnemyRules());
            var p = See(new Vector3(0, 0, 1));
            p.PlayerDown = true;
            var d = brain.Tick(0.1, p, null);
            Assert.AreNotEqual(EnemyState.Chase, d.State);
            Assert.IsFalse(d.Attack);
        }

        [Test]
        public void EnemyCountGrowsWithCycleAndBonusButFitsRooms()
        {
            var rules = new EnemyRules();
            Assert.AreEqual(1, rules.CountFor(1, 0, 4));
            Assert.AreEqual(2, rules.CountFor(2, 0, 5));
            Assert.AreEqual(3, rules.CountFor(1, 2, 5));
            Assert.AreEqual(3, rules.CountFor(4, 3, 4), "入口以外の部屋数まで");
        }

        [Test]
        public void ImpactNoiseScalesWithSpeedAndIsCapped()
        {
            var rules = new EnemyRules();
            Assert.AreEqual(0, rules.ImpactNoiseRadius(1));
            Assert.Less(rules.ImpactNoiseRadius(4), rules.ImpactNoiseRadius(8));
            Assert.AreEqual(rules.MaxImpactNoiseRadius, rules.ImpactNoiseRadius(100));
        }

        [Test]
        public void PathFollowsDoorsBetweenRooms()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var layout = FacilityLayout.Generate(seed, 7);
                var from = layout.Entrance;
                foreach (var to in layout.Rooms)
                {
                    var path = layout.Path(from, to);
                    Assert.AreEqual(from, path.First());
                    Assert.AreEqual(to, path.Last());
                    Assert.AreEqual(to.Depth + 1, path.Count, "入口からの道の長さは深さ + 1");
                    for (int i = 0; i + 1 < path.Count; i++)
                        Assert.IsNotNull(FacilityLayout.DoorBetween(path[i], path[i + 1]));
                }
            }
        }
    }
}
