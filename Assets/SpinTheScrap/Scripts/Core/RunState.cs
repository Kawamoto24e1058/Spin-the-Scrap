using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    /// <summary>プレイヤー 1 人分の、ランを通して持ち越す物（強化と手に持っている武器）。</summary>
    public sealed class PlayerLoadout
    {
        public int Id { get; }
        public PerkState Perks { get; } = new PerkState();
        WeaponInstance weapon;

        /// <summary>手に持っている武器（なければ素手）。</summary>
        public WeaponInstance Weapon
        {
            get => weapon;
            set { weapon = value; WeaponSerial++; }
        }

        /// <summary>武器を持ち替えた・改造した回数（協力プレイで「同じ武器のままか」を見分ける）。</summary>
        public int WeaponSerial { get; set; }

        public PlayerLoadout(int id) => Id = id;
    }

    /// <summary>
    /// 1 ランの状態（docs/floor-design.md）。戦って階層を下り、拾ったガラクタを換金してスロット台で強くなる。
    /// ホストだけが持ち、結果をクライアントへ配信する想定。メダル・道具・リールはチームで共有し、強化と武器はプレイヤーごと。
    /// </summary>
    public sealed class RunState
    {
        readonly ContentDatabase db;
        readonly BalanceConfig config;
        readonly MachineRules machineRules;
        readonly WeaponCatalog weaponCatalog;
        readonly ToolCatalog toolCatalog;
        readonly Rng rng;
        readonly Dictionary<MachineKind, List<SymbolInstance>> reels = new Dictionary<MachineKind, List<SymbolInstance>>();
        readonly Dictionary<MachineKind, int> spinsThisFloor = new Dictionary<MachineKind, int>();
        readonly List<RelicDefinition> relics = new List<RelicDefinition>();
        readonly List<string> relicOffers = new List<string>();
        readonly Dictionary<string, int> tools = new Dictionary<string, int>();
        readonly Dictionary<int, PlayerLoadout> players = new Dictionary<int, PlayerLoadout>();
        int nextInstanceId = 1;

        public static readonly MachineKind[] AllMachines = { MachineKind.Weapon, MachineKind.Perk, MachineKind.Mod, MachineKind.Tool };

        public ContentDatabase Content => db;
        public BalanceConfig Config => config;
        public MachineRules MachineRules => machineRules;
        public WeaponCatalog Weapons => weaponCatalog;
        public ToolCatalog ToolCatalog => toolCatalog;
        public ulong RngState => rng.State;

        /// <summary>スロットを回すためのゲーム内通貨。現実のお金とは関係しない。</summary>
        public double Medals { get; private set; }
        /// <summary>今いる階層（1 から）。</summary>
        public int Floor { get; private set; } = 1;
        public int Lives { get; private set; }
        public FloorObjective Objective { get; private set; }
        public bool IsFailed { get; private set; }
        /// <summary>最後の階層を攻略した。</summary>
        public bool IsCleared { get; private set; }
        /// <summary>次の階層で増える敵の数（マネキンの首など）。</summary>
        public int PendingEnemyBonus { get; private set; }
        /// <summary>ランの記録（結果画面）。</summary>
        public int Kills { get; private set; }
        public int TotalSpins { get; private set; }
        public int Jackpots { get; private set; }
        public int BestTier { get; private set; }
        /// <summary>懐中電灯の電池の残り（0〜1）。ランを通して持ち越す。</summary>
        public double FlashlightCharge { get; private set; } = 1;

        public IReadOnlyList<RelicDefinition> Relics => relics;
        public IReadOnlyList<string> RelicOffers => relicOffers;

        public bool IsOver => IsFailed || IsCleared;
        public bool IsLastFloor => Floor >= config.FloorCount;
        public bool IsBossFloor => config.IsBossFloor(Floor);
        public double RemovalFee => config.RemovalFeePerFloor * Floor;

        /// <summary>割れた割れ物に残る価値の割合（レリックがなければ 0）。</summary>
        public double BrokenFragileValueRatio
        {
            get
            {
                double ratio = 0;
                foreach (var r in relics)
                    if (r.Kind == RelicKind.BrokenFragileKeepsValue) ratio = Math.Max(ratio, r.Amount);
                return ratio;
            }
        }

        public RunState(ContentDatabase db, BalanceConfig config, ulong seed,
            ToolCatalog toolCatalog = null, WeaponCatalog weaponCatalog = null, MachineRules machineRules = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.toolCatalog = toolCatalog ?? ToolCatalog.CreateMvp();
            this.weaponCatalog = weaponCatalog ?? WeaponCatalog.CreateMvp();
            this.machineRules = machineRules ?? new MachineRules();
            rng = new Rng(seed);
            Medals = config.StartingMedals;
            Lives = config.StartingLives;
            foreach (var kind in AllMachines)
            {
                reels[kind] = new List<SymbolInstance>();
                spinsThisFloor[kind] = 0;
                foreach (var id in config.StartingReel) AddToReel(kind, id, -1);
            }
            Objective = FloorObjective.For(Floor, config);
            RollRelicOffers();
        }

        public bool HasRelic(RelicKind kind) => relics.Any(r => r.Kind == kind);

        public PlayerLoadout Player(int id)
        {
            if (!players.TryGetValue(id, out var loadout)) players[id] = loadout = new PlayerLoadout(id);
            return loadout;
        }

        public IEnumerable<PlayerLoadout> Players => players.Values;

        public PlayerStats StatsOf(int playerId) => Player(playerId).Perks.Stats(config.BasePlayerHealth);

        // ---- スロット台 ----

        public IReadOnlyList<SymbolInstance> ReelOf(MachineKind kind) => reels[kind];
        public int SpinsThisFloor(MachineKind kind) => spinsThisFloor[kind];

        public double CostOf(MachineKind kind)
        {
            if (spinsThisFloor[kind] == 0 && HasRelic(RelicKind.FirstSpinFreePerCycle)) return 0;
            double cost = machineRules.CostFor(kind, Floor, spinsThisFloor[kind]);
            foreach (var r in relics)
                if (r.Kind == RelicKind.SpinCostMultiplier) cost *= r.Amount;
            return Math.Ceiling(cost);
        }

        /// <summary>回せるか（メダルが足りて、改造スロットなら改造できる武器を持っている）。</summary>
        public bool CanSpin(MachineKind kind, int playerId)
        {
            if (IsOver || Medals < CostOf(kind)) return false;
            if (kind == MachineKind.Mod)
            {
                var weapon = Player(playerId).Weapon;
                return weapon != null && weapon.CanTakeMod;
            }
            return true;
        }

        /// <summary>
        /// 台を回す。盤面を読み取り、台の種類に応じた物をレバーを引いたプレイヤー（playerId）に渡す。
        /// 大当たりなら全員に強化スロット 1 回分の経験値が入る。
        /// </summary>
        public bool TrySpin(MachineKind kind, int playerId, out MachineOutcome outcome)
        {
            outcome = null;
            if (!CanSpin(kind, playerId)) return false;
            double cost = CostOf(kind);
            Medals -= cost;
            var reel = reels[kind];
            var cells = SlotMachine.Draw(reel, rng);
            var result = SlotMachine.Evaluate(cells, db, relics, rng);
            spinsThisFloor[kind]++;
            PendingEnemyBonus += result.EnemyBonus;
            foreach (var dead in result.DestroyedInstances) reel.Remove(dead);

            var reading = SpinReading.Read(result, db, machineRules);
            TotalSpins++;
            if (reading.Jackpot) Jackpots++;
            BestTier = Math.Max(BestTier, reading.Tier);
            outcome = new MachineOutcome { Kind = kind, Reading = reading, Cost = cost };
            var player = Player(playerId);
            switch (kind)
            {
                case MachineKind.Weapon:
                    outcome.Weapon = new WeaponInstance(weaponCatalog.For(reading.Family, reading.Tier));
                    player.Weapon = outcome.Weapon;
                    break;
                case MachineKind.Perk:
                    GrantPerkXp(player, reading, outcome.LevelUps);
                    break;
                case MachineKind.Mod:
                    var mod = WeaponInstance.ModFor(reading.Family);
                    if (player.Weapon.AddMod(mod))
                    {
                        outcome.Mod = mod;
                        player.WeaponSerial++; // 弾数の改造で補充されるので、別の武器として扱う
                    }
                    break;
                case MachineKind.Tool:
                    GrantTool(reading, outcome);
                    break;
            }

            if (reading.Jackpot)
                foreach (var other in players.Values)
                    if (kind != MachineKind.Perk || other != player)
                        GrantPerkXp(other, reading, other == player ? outcome.LevelUps : new List<SymbolTag>());
            return true;
        }

        static void GrantPerkXp(PlayerLoadout player, SpinReading reading, List<SymbolTag> levelUps)
        {
            foreach (var pair in reading.TagTotals)
                if (player.Perks.AddXp(pair.Key, pair.Value) && !levelUps.Contains(pair.Key)) levelUps.Add(pair.Key);
        }

        void GrantTool(SpinReading reading, MachineOutcome outcome)
        {
            int tier = reading.Tier;
            switch (reading.Family)
            {
                case SymbolTag.Food:
                    outcome.ToolId = ToolIds.Bandage;
                    outcome.ToolCount = tier;
                    break;
                case SymbolTag.Toy:
                case SymbolTag.Doll:
                    outcome.ToolId = ToolIds.Firecracker;
                    outcome.ToolCount = tier;
                    break;
                case SymbolTag.Electric:
                    outcome.ToolId = tier >= 3 && !HasTool(ToolIds.InsulatedBoots) ? ToolIds.InsulatedBoots : ToolIds.Battery;
                    outcome.ToolCount = 1;
                    break;
                case SymbolTag.Metal when tier >= 3 && !HasTool(ToolIds.Cart):
                    outcome.ToolId = ToolIds.Cart;
                    outcome.ToolCount = 1;
                    break;
                default:
                    outcome.Medals = Math.Ceiling(reading.Result.Total);
                    Medals += outcome.Medals;
                    return;
            }
            AddTool(outcome.ToolId, outcome.ToolCount);
        }

        /// <summary>ガラクタを換金する。valueFactor は割れ物の破損などによる価値の割合。換金した人のギャンブルの強化でレートが上がる。</summary>
        public double ConvertToMedals(string symbolId, double valueFactor = 1, int playerId = 0)
        {
            double medals = MedalValueFor(symbolId, valueFactor, playerId);
            Medals += medals;
            return medals;
        }

        /// <summary>換金したときの枚数（割れ物の状態・深さ・レリック「錆びた秤」・ギャンブルの強化を含む）。</summary>
        public double MedalValueFor(string symbolId, double valueFactor = 1, int playerId = 0)
        {
            double depthBonus = 1 + config.MedalValueFloorGrowth * (Floor - 1);
            double value = db.GetSymbol(symbolId).SellPrice * Math.Max(0, valueFactor) * StatsOf(playerId).MedalRateMultiplier * depthBonus;
            foreach (var r in relics)
                if (r.Kind == RelicKind.SellPriceMultiplier) value *= r.Amount;
            return Math.Floor(value);
        }

        public SymbolInstance AddToReel(MachineKind kind, string symbolId, int ownerId)
        {
            db.GetSymbol(symbolId); // 存在チェック
            var inst = new SymbolInstance(nextInstanceId++, symbolId, ownerId);
            reels[kind].Add(inst);
            return inst;
        }

        /// <summary>手数料を払ってシンボルを廃棄する（リールの「濃さ」の調整）。</summary>
        public bool TryRemoveFromReel(MachineKind kind, int instanceId)
        {
            var reel = reels[kind];
            int index = reel.FindIndex(s => s.InstanceId == instanceId);
            if (index < 0 || Medals < RemovalFee) return false;
            Medals -= RemovalFee;
            reel.RemoveAt(index);
            return true;
        }

        public bool TryBuyRelic(string relicId)
        {
            if (!relicOffers.Contains(relicId) || relics.Count >= config.MaxRelics) return false;
            var def = db.GetRelic(relicId);
            if (Medals < def.Price) return false;
            Medals -= def.Price;
            relics.Add(def);
            relicOffers.Remove(relicId);
            return true;
        }

        // ---- 道具 ----

        public int ToolCount(string toolId) => tools.TryGetValue(toolId, out int n) ? n : 0;
        public bool HasTool(string toolId) => ToolCount(toolId) > 0;

        /// <summary>道具を足す。持てる数を超えた分は捨てる。実際に増えた数を返す。</summary>
        public int AddTool(string toolId, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var def = toolCatalog.Get(toolId);
            int before = ToolCount(toolId);
            int after = Math.Min(def.MaxCount, before + count);
            tools[toolId] = after;
            return after - before;
        }

        /// <summary>道具を 1 回使う。永続の道具は減らない（使えたかどうかだけ返す）。</summary>
        public bool TryUseTool(string toolId)
        {
            if (!HasTool(toolId)) return false;
            if (!toolCatalog.Get(toolId).Permanent) tools[toolId]--;
            return true;
        }

        /// <summary>
        /// 懐中電灯を seconds 秒点けた分だけ電池を減らす。切れたら予備の電池に自動で交換する。
        /// まだ点けていられるなら true（予備もなく切れたら false）。
        /// </summary>
        public bool DrainFlashlight(double seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            FlashlightCharge = Math.Max(0, FlashlightCharge - seconds / config.FlashlightBatterySeconds);
            if (FlashlightCharge > 0) return true;
            if (!TryUseTool(ToolIds.Battery)) return false;
            FlashlightCharge = 1;
            return true;
        }

        // ---- 階層の進行 ----

        /// <summary>敵を倒した（記録と、通常の階の攻略条件）。ボスなら isBoss。達成したら true。</summary>
        public bool ReportKill(bool isBoss)
        {
            if (IsOver) return false;
            Kills++;
            return ReportObjective(isBoss ? FloorObjectiveKind.DefeatBoss : FloorObjectiveKind.DefeatEnemies);
        }

        /// <summary>攻略条件の進み具合を報告する。達成したら true（エレベーターが動く）。</summary>
        public bool ReportObjective(FloorObjectiveKind kind, double amount = 1)
        {
            if (IsOver) return false;
            return Objective.Report(kind, amount);
        }

        /// <summary>攻略条件を満たした状態でエレベーターに乗った。最後の階層ならクリア。</summary>
        public void AdvanceFloor()
        {
            if (IsOver) throw new InvalidOperationException("Run is over");
            if (!Objective.IsComplete) throw new InvalidOperationException("Floor objective is not complete");
            if (IsLastFloor)
            {
                IsCleared = true;
                return;
            }
            Floor++;
            foreach (var kind in AllMachines) spinsThisFloor[kind] = 0;
            Objective = FloorObjective.For(Floor, config);
            RollRelicOffers();
        }

        /// <summary>倒れた。残機が 1 減り、攻略条件をやり直す。残機が 0 ならラン終了。</summary>
        public void OnDowned()
        {
            if (IsOver) return;
            Lives = Math.Max(0, Lives - 1);
            if (Lives == 0)
            {
                IsFailed = true;
                return;
            }
            Objective.ResetProgress();
        }

        /// <summary>階層の開始時に呼び、増えた敵の数を受け取る。</summary>
        public int ConsumeEnemyBonus()
        {
            int bonus = PendingEnemyBonus;
            PendingEnemyBonus = 0;
            return bonus;
        }

        // ---- 状態の保存と復元（協力プレイでホストの状態をクライアントへ写す） ----

        /// <summary>ランの状態をすべて文字列にする。同じ内容の RunState に Load すれば、同じ乱数で同じ結果になる。</summary>
        public string Save()
        {
            var w = new SnapshotWriter();
            w.Write(rng.State).Write(nextInstanceId).Write(Medals).Write(Floor).Write(Lives);
            w.Write(IsFailed).Write(IsCleared).Write(PendingEnemyBonus);
            w.Write(Kills).Write(TotalSpins).Write(Jackpots).Write(BestTier).Write(FlashlightCharge);
            w.Write((int)Objective.Kind).Write(Objective.Target).Write(Objective.Progress);
            foreach (var kind in AllMachines)
            {
                w.Write(spinsThisFloor[kind]).Write(reels[kind].Count);
                foreach (var s in reels[kind]) w.Write(s.InstanceId).Write(s.SymbolId).Write(s.OwnerId);
            }
            w.Write(relics.Count);
            foreach (var r in relics) w.Write(r.Id);
            w.Write(relicOffers.Count);
            foreach (var id in relicOffers) w.Write(id);
            w.Write(tools.Count);
            foreach (var pair in tools.OrderBy(p => p.Key, StringComparer.Ordinal)) w.Write(pair.Key).Write(pair.Value);
            w.Write(players.Count);
            foreach (var p in players.Values.OrderBy(p => p.Id))
            {
                w.Write(p.Id).Write(p.WeaponSerial);
                var xp = p.Perks.AllXp.OrderBy(x => (int)x.Key).ToList();
                w.Write(xp.Count);
                foreach (var x in xp) w.Write((int)x.Key).Write(x.Value);
                w.Write(p.Weapon != null);
                if (p.Weapon == null) continue;
                w.Write(p.Weapon.Definition.Id).Write(p.Weapon.Mods.Count);
                foreach (var m in p.Weapon.Mods) w.Write((int)m);
                w.Write(p.Weapon.Remaining);
            }
            return w.ToString();
        }

        /// <summary>Save で作った文字列から状態を戻す（同じ内容・設定で作った RunState に対して）。</summary>
        public void Load(string snapshot)
        {
            var r = new SnapshotReader(snapshot);
            rng.State = r.ULong();
            nextInstanceId = r.Int();
            Medals = r.Double();
            Floor = r.Int();
            Lives = r.Int();
            IsFailed = r.Bool();
            IsCleared = r.Bool();
            PendingEnemyBonus = r.Int();
            Kills = r.Int();
            TotalSpins = r.Int();
            Jackpots = r.Int();
            BestTier = r.Int();
            FlashlightCharge = r.Double();
            var objectiveKind = (FloorObjectiveKind)r.Int();
            double target = r.Double();
            Objective = new FloorObjective(objectiveKind, target);
            Objective.Report(objectiveKind, r.Double());
            foreach (var kind in AllMachines)
            {
                spinsThisFloor[kind] = r.Int();
                var reel = reels[kind];
                reel.Clear();
                int count = r.Int();
                for (int i = 0; i < count; i++) reel.Add(new SymbolInstance(r.Int(), r.String(), r.Int()));
            }
            relics.Clear();
            int relicCount = r.Int();
            for (int i = 0; i < relicCount; i++) relics.Add(db.GetRelic(r.String()));
            relicOffers.Clear();
            int offerCount = r.Int();
            for (int i = 0; i < offerCount; i++) relicOffers.Add(r.String());
            tools.Clear();
            int toolCount = r.Int();
            for (int i = 0; i < toolCount; i++) tools[r.String()] = r.Int();
            players.Clear();
            int playerCount = r.Int();
            for (int i = 0; i < playerCount; i++)
            {
                var p = Player(r.Int());
                int serial = r.Int();
                int xpCount = r.Int();
                for (int j = 0; j < xpCount; j++) p.Perks.SetXp((SymbolTag)r.Int(), r.Double());
                if (r.Bool())
                {
                    var weapon = new WeaponInstance(weaponCatalog.Get(r.String()));
                    int modCount = r.Int();
                    for (int j = 0; j < modCount; j++) weapon.AddMod((WeaponModKind)r.Int());
                    weapon.SetRemaining(r.Int());
                    p.Weapon = weapon;
                }
                p.WeaponSerial = serial;
            }
        }

        void RollRelicOffers()
        {
            relicOffers.Clear();
            var candidates = db.Relics.Where(r => relics.All(owned => owned.Id != r.Id)).Select(r => r.Id).ToList();
            rng.Shuffle(candidates);
            relicOffers.AddRange(candidates.Take(config.RelicOffersPerFloor));
        }
    }
}
