using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    /// <summary>台の読み取り・武器・改造・強化・戦闘・敵の強さ・道具（docs/floor-design.md）。</summary>
    public class FloorAndRewardTests
    {
        static readonly ContentDatabase Db = MvpContent.Create();
        static readonly MachineRules Rules = new MachineRules();
        int nextId = 1;

        SymbolInstance[] Board(params (int row, int col, string id)[] placements)
        {
            var cells = new SymbolInstance[Core.Board.CellCount];
            foreach (var (row, col, id) in placements)
                cells[Core.Board.Index(row, col)] = new SymbolInstance(nextId++, id, 0);
            return cells;
        }

        static SpinReading Read(SymbolInstance[] cells) =>
            SpinReading.Read(SlotMachine.Evaluate(cells, Db, new RelicDefinition[0], new Rng(1)), Db, Rules);

        // ---- 読み取り ----

        [Test]
        public void FamilyIsTheTagWithTheMostValue()
        {
            var r = Read(Board((0, 0, "old_calculator"), (2, 4, "old_calculator"), (1, 2, "empty_can")));
            Assert.AreEqual(SymbolTag.Electric, r.Family, "電卓 2 つ（互いに +2）が空き缶 1 つより大きい");
            Assert.Greater(r.TotalOf(SymbolTag.Electric), r.TotalOf(SymbolTag.Metal));
        }

        [Test]
        public void EmptyBoardHasNoFamilyAndTierOne()
        {
            var r = Read(new SymbolInstance[Core.Board.CellCount]);
            Assert.AreEqual(SymbolTag.None, r.Family);
            Assert.AreEqual(1, r.Tier);
            Assert.IsFalse(r.Jackpot);
        }

        [Test]
        public void TierComesFromBoardTotalAndLuckyLines()
        {
            Assert.AreEqual(1, Rules.TierFor(Rules.Tier2Value - 1, false));
            Assert.AreEqual(2, Rules.TierFor(Rules.Tier2Value, false));
            Assert.AreEqual(3, Rules.TierFor(Rules.Tier3Value, false));
            Assert.AreEqual(2, Rules.TierFor(0, true), "幸運のラインで 1 段上がる");
            Assert.AreEqual(3, Rules.TierFor(Rules.Tier3Value, true), "3 が上限");
        }

        [Test]
        public void SynergyRaisesTier()
        {
            // 同じ 3 マスでも、ネジ（隣接する金属で伸びる）で固めると値が大きくなり、質が上がる
            var plain = Read(Board((0, 0, "empty_can"), (0, 1, "empty_can"), (0, 2, "empty_can")));
            var screws = Read(Board((0, 0, "rusty_screw"), (0, 1, "rusty_screw"), (0, 2, "rusty_screw"),
                (1, 0, "rusty_screw"), (1, 1, "rusty_screw"), (1, 2, "rusty_screw")));
            Assert.Greater(screws.Result.Total, plain.Result.Total);
            Assert.GreaterOrEqual(screws.Tier, plain.Tier);
            Assert.Greater(screws.Tier, 1);
        }

        // ---- 武器・改造 ----

        [Test]
        public void WeaponTableFollowsFamilyAndTier()
        {
            var weapons = WeaponCatalog.CreateMvp();
            Assert.AreEqual("pipe", weapons.For(SymbolTag.Metal, 1).Id);
            Assert.AreEqual("nail_gun", weapons.For(SymbolTag.Metal, 2).Id);
            Assert.AreEqual("rivet_cannon", weapons.For(SymbolTag.Metal, 3).Id);
            Assert.AreEqual("arc_gun_mk2", weapons.For(SymbolTag.Electric, 3).Id);
            Assert.AreEqual("firework_tube", weapons.For(SymbolTag.Doll, 2).Id, "人形はおもちゃと同じ");
            Assert.AreEqual("cursed_cleaver_mk2", weapons.For(SymbolTag.Curse, 2).Id);
            Assert.AreEqual("nail_gun", weapons.For(SymbolTag.Paper, 2).Id, "武器のない系統は金属");
        }

        [Test]
        public void HigherTierWeaponsAreStronger()
        {
            var weapons = WeaponCatalog.CreateMvp();
            foreach (var family in new[] { SymbolTag.Metal, SymbolTag.Electric, SymbolTag.Curse })
            {
                double DamagePerSecond(int tier) { var w = weapons.For(family, tier); return w.Damage / w.Interval; }
                Assert.Less(DamagePerSecond(1), DamagePerSecond(3), family.ToString());
            }
        }

        [Test]
        public void ModsChangeWeaponNumbersAndCapAtThree()
        {
            var weapon = new WeaponInstance(WeaponCatalog.CreateMvp().Get("nail_gun"));
            double damage = weapon.Damage, interval = weapon.Interval;
            int capacity = weapon.Capacity;
            Assert.IsTrue(weapon.AddMod(WeaponModKind.Damage));
            Assert.AreEqual(damage * 1.3, weapon.Damage, 1e-9);
            Assert.IsTrue(weapon.AddMod(WeaponModKind.FireRate));
            Assert.AreEqual(interval * 0.8, weapon.Interval, 1e-9);
            Assert.IsTrue(weapon.AddMod(WeaponModKind.Capacity));
            Assert.AreEqual((int)System.Math.Round(capacity * 1.5), weapon.Capacity);
            Assert.AreEqual(weapon.Capacity, weapon.Remaining, "弾数の改造で補充される");
            Assert.IsFalse(weapon.AddMod(WeaponModKind.Stun), "3 つまで");
        }

        [Test]
        public void WeaponsWearOut()
        {
            var pipe = new WeaponInstance(WeaponCatalog.CreateMvp().Get("pipe"));
            for (int i = 0; i < pipe.Capacity; i++) Assert.IsTrue(pipe.TryUse());
            Assert.IsTrue(pipe.IsSpent);
            Assert.IsFalse(pipe.TryUse());
        }

        // ---- 強化・戦闘 ----

        [Test]
        public void PerkLevelsFollowThresholds()
        {
            var perks = new PerkState();
            Assert.AreEqual(0, perks.Level(SymbolTag.Metal));
            Assert.IsTrue(perks.AddXp(SymbolTag.Metal, PerkState.Thresholds[0]));
            Assert.AreEqual(1, perks.Level(SymbolTag.Metal));
            Assert.IsFalse(perks.AddXp(SymbolTag.Metal, 1), "まだ Lv2 に届かない");
            perks.AddXp(SymbolTag.Metal, 1000);
            Assert.AreEqual(PerkState.Thresholds.Length, perks.Level(SymbolTag.Metal), "上限");
        }

        [Test]
        public void PerksShapeStats()
        {
            var perks = new PerkState();
            perks.AddXp(SymbolTag.Metal, PerkState.Thresholds[1]);   // Lv2
            perks.AddXp(SymbolTag.Food, PerkState.Thresholds[2]);    // Lv3
            perks.AddXp(SymbolTag.Luck, PerkState.Thresholds[0]);    // Lv1
            perks.AddXp(SymbolTag.Curse, PerkState.Thresholds[0]);   // Lv1
            var stats = perks.Stats(100);
            Assert.AreEqual(1 + 0.15 * 2 + 0.3, stats.DamageMultiplier, 1e-9);
            Assert.AreEqual(145, stats.MaxHealth);
            Assert.AreEqual(5, stats.HealOnKill, "食べ物 Lv3 から倒すたびに回復");
            Assert.AreEqual(0.06, stats.CritChance, 1e-9);
            Assert.AreEqual(1.1, stats.DamageTakenMultiplier, 1e-9, "呪いは被ダメージも増える");
        }

        [Test]
        public void PaperRevealsAndDollHidesFromSight()
        {
            var perks = new PerkState();
            perks.AddXp(SymbolTag.Paper, PerkState.Thresholds[1]);   // Lv2
            perks.AddXp(SymbolTag.Doll, PerkState.Thresholds[2]);    // Lv3
            var stats = perks.Stats(100);
            Assert.AreEqual(16, stats.RevealRange);
            Assert.AreEqual(0.24, stats.Stealth, 1e-9);

            var rules = new EnemyRules();
            var brain = new EnemyBrain(rules);
            var p = new EnemyPerception
            {
                Position = System.Numerics.Vector3.Zero,
                Forward = new System.Numerics.Vector3(0, 0, 1),
                PlayerPosition = new System.Numerics.Vector3(0, 0, (float)(rules.SightRange * 0.9)),
                HasLineOfSight = true,
            };
            Assert.IsTrue(brain.CanSee(p));
            p.Stealth = stats.Stealth;
            Assert.IsFalse(brain.CanSee(p), "見つかりにくいと、同じ距離でも気づかれない");
        }

        [Test]
        public void HitCombinesWeaponAndStats()
        {
            var weapon = new WeaponInstance(WeaponCatalog.CreateMvp().Get("pipe"));
            var stats = new PerkState().Stats(100);
            stats.CritChance = 0.5;
            var normal = CombatRules.Hit(weapon, stats, critRoll: 0.9, stunRoll: 0.9);
            var crit = CombatRules.Hit(weapon, stats, critRoll: 0.1, stunRoll: 0.9);
            Assert.AreEqual(20, normal.Damage);
            Assert.IsFalse(normal.Critical);
            Assert.AreEqual(40, crit.Damage);
            Assert.IsTrue(crit.Critical);
            Assert.IsFalse(normal.Stun);
        }

        [Test]
        public void CursedWeaponsHurtTheUserAndLifestealHeals()
        {
            var weapons = WeaponCatalog.CreateMvp();
            var stats = new PerkState().Stats(100);
            var cleaver = new WeaponInstance(weapons.Get("cursed_cleaver"));
            Assert.AreEqual(3, CombatRules.Hit(cleaver, stats, 0.9, 0.9).SelfDamage);
            var pipe = new WeaponInstance(weapons.Get("pipe"));
            pipe.AddMod(WeaponModKind.Lifesteal);
            Assert.AreEqual(2, CombatRules.Hit(pipe, stats, 0.9, 0.9).Heal, 1e-9);
        }

        // ---- 敵・階層 ----

        [Test]
        public void EnemiesScaleWithFloor()
        {
            var enemies = new EnemyRules();
            Assert.AreEqual(40, enemies.HealthFor(EnemyKind.Crawler, 1));
            Assert.AreEqual(System.Math.Round(40 * 1.25 * 1.25), enemies.HealthFor(EnemyKind.Crawler, 3));
            Assert.Greater(enemies.DamageFor(EnemyKind.Lurker, 5), enemies.DamageFor(EnemyKind.Lurker, 1));
            Assert.Greater(enemies.HealthFor(EnemyKind.Brute, 3), enemies.HealthFor(EnemyKind.Lurker, 3));
        }

        [Test]
        public void ProfilesTuneEachEnemyKind()
        {
            var rules = new EnemyRules();
            var crawler = rules.Profile(EnemyKind.Crawler, 1);
            var brute = rules.Profile(EnemyKind.Brute, 3);
            Assert.Less(crawler.ChaseSpeed, 4.5, "這う者は走れば逃げられる");
            Assert.AreEqual(rules.DamageFor(EnemyKind.Crawler, 1), crawler.AttackDamage);
            Assert.AreEqual(0, brute.SightRange, "巨体は目が見えない");
            Assert.AreEqual(rules.DamageFor(EnemyKind.Brute, 3), brute.AttackDamage);
            Assert.AreEqual(rules.RunNoiseRadius, brute.RunNoiseRadius, "音の大きさは共通");
            Assert.AreNotSame(rules, crawler);

            // 目の見えない巨体は、正面にいても離れていれば気づかない
            var brain = new EnemyBrain(brute);
            var p = new EnemyPerception
            {
                Position = System.Numerics.Vector3.Zero,
                Forward = new System.Numerics.Vector3(0, 0, 1),
                PlayerPosition = new System.Numerics.Vector3(0, 0, 6),
                HasLineOfSight = true,
            };
            Assert.IsFalse(brain.CanSee(p));
        }

        [Test]
        public void ThreeBossesInOrder()
        {
            var config = new BalanceConfig();
            Assert.AreEqual(EnemyKind.Brute, config.BossFor(3));
            Assert.AreEqual(EnemyKind.Dynamo, config.BossFor(6));
            Assert.AreEqual(EnemyKind.Puppeteer, config.BossFor(9));
            Assert.Throws<System.ArgumentException>(() => config.BossFor(4));
            Assert.IsTrue(EnemyRules.IsBoss(EnemyKind.Dynamo));
            Assert.IsFalse(EnemyRules.IsBoss(EnemyKind.Lurker));
        }

        [Test]
        public void DynamoResistsAllButElectricOrStunAndPuppeteerFearsExplosions()
        {
            var rules = new EnemyRules();
            Assert.AreEqual(rules.DynamoResistance, rules.DamageTakenMultiplier(EnemyKind.Dynamo, SymbolTag.Metal, false, false));
            Assert.AreEqual(1, rules.DamageTakenMultiplier(EnemyKind.Dynamo, SymbolTag.Electric, false, false));
            Assert.AreEqual(1, rules.DamageTakenMultiplier(EnemyKind.Dynamo, SymbolTag.Metal, false, true), "ひるんでいる間は効く");
            Assert.AreEqual(rules.PuppeteerExplosionWeakness, rules.DamageTakenMultiplier(EnemyKind.Puppeteer, SymbolTag.Toy, true, false));
            Assert.AreEqual(1, rules.DamageTakenMultiplier(EnemyKind.Puppeteer, SymbolTag.Metal, false, false));
            Assert.AreEqual(1, rules.DamageTakenMultiplier(EnemyKind.Crawler, SymbolTag.Metal, false, false));
            Assert.Greater(rules.HealthFor(EnemyKind.Dynamo, 6), rules.HealthFor(EnemyKind.Brute, 3));
        }

        [Test]
        public void RaisingMaxHealthAlsoRaisesCurrent()
        {
            var health = new HealthState(100);
            health.Damage(30);
            health.SetMax(130);
            Assert.AreEqual(100, health.Current);
            health.SetMax(80);
            Assert.AreEqual(80, health.Current, "上限を下げたら合わせる");
        }

        [Test]
        public void StayingLongerMakesCrawlersReturnFaster()
        {
            var enemies = new EnemyRules();
            Assert.Greater(enemies.CrawlerRespawnSeconds(0), enemies.CrawlerRespawnSeconds(240));
            Assert.GreaterOrEqual(enemies.CrawlerRespawnSeconds(100000), 4);
            Assert.Less(enemies.CrawlerPopulationFor(1), enemies.CrawlerPopulationFor(6));
        }

        [Test]
        public void BossEveryThirdFloorAndKillTargetsGrow()
        {
            var config = new BalanceConfig();
            Assert.IsFalse(config.IsBossFloor(1));
            Assert.IsFalse(config.IsBossFloor(2));
            Assert.IsTrue(config.IsBossFloor(3));
            Assert.IsTrue(config.IsBossFloor(9));
            Assert.Less(config.KillTargetFor(1), config.KillTargetFor(4));
            Assert.AreEqual(FloorObjectiveKind.DefeatBoss, FloorObjective.For(6, config).Kind);
        }

        [Test]
        public void ObjectiveCompletesAtTargetAndIgnoresOtherKinds()
        {
            var objective = new FloorObjective(FloorObjectiveKind.DefeatEnemies, 2);
            Assert.IsFalse(objective.Report(FloorObjectiveKind.DefeatBoss));
            Assert.IsFalse(objective.Report(FloorObjectiveKind.DefeatEnemies));
            Assert.IsTrue(objective.Report(FloorObjectiveKind.DefeatEnemies));
            Assert.IsFalse(objective.Report(FloorObjectiveKind.DefeatEnemies), "達成後は true を返さない");
        }

        [Test]
        public void RoomsAndLurkersGrowWithFloor()
        {
            var config = new BalanceConfig();
            Assert.Less(config.RoomCountFor(1), config.RoomCountFor(9));
            var enemies = new EnemyRules();
            Assert.AreEqual(1, enemies.CountFor(1, 0, 4));
            Assert.AreEqual(2, enemies.CountFor(3, 0, 5));
            Assert.AreEqual(3, enemies.CountFor(1, 9, 4), "入口以外の部屋数まで");
        }

        // ---- 道具 ----

        [Test]
        public void ToolsCapAtMaxAndPermanentToolsStay()
        {
            var run = new RunState(Db, new BalanceConfig(), 1);
            int max = run.ToolCatalog.Get(ToolIds.Firecracker).MaxCount;
            Assert.AreEqual(max, run.AddTool(ToolIds.Firecracker, max + 5));
            Assert.AreEqual(0, run.AddTool(ToolIds.Firecracker, 1));
            Assert.AreEqual(1, run.AddTool(ToolIds.Cart, 3), "永続は 1 つ");
            Assert.IsTrue(run.TryUseTool(ToolIds.Cart));
            Assert.IsTrue(run.HasTool(ToolIds.Cart));
        }

        [Test]
        public void FlashlightDrainsAndSwapsInSpareBattery()
        {
            var run = new RunState(Db, new BalanceConfig(), 1);
            double life = run.Config.FlashlightBatterySeconds;
            Assert.IsTrue(run.DrainFlashlight(life / 2));
            Assert.IsFalse(run.DrainFlashlight(life));
            run.AddTool(ToolIds.Battery, 1);
            Assert.IsTrue(run.DrainFlashlight(0.1));
            Assert.AreEqual(0, run.ToolCount(ToolIds.Battery));
        }

        [Test]
        public void InsulatedBootsReducePuddleDamageAndBandagesHeal()
        {
            var rules = new HazardRules();
            Assert.AreEqual(rules.PuddleDamagePerSecond * rules.InsulatedBootsPuddleFactor, rules.PuddleDamage(1, true), 1e-9);
            var health = new HealthState(100);
            health.Damage(30);
            Assert.AreEqual(30, health.Heal(40));
        }
    }
}
