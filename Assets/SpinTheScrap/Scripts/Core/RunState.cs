using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// 1 ランの状態（docs/floor-design.md）。地下へ階層を下り、階層ごとの攻略条件を満たすと次へ進める。
    /// ホストだけが持ち、結果をクライアントへ配信する想定。メダルと道具はチームで共有する。
    /// </summary>
    public sealed class RunState
    {
        readonly ContentDatabase db;
        readonly BalanceConfig config;
        readonly Rng rng;
        readonly List<SymbolInstance> reel = new List<SymbolInstance>();
        readonly List<RelicDefinition> relics = new List<RelicDefinition>();
        readonly List<string> relicOffers = new List<string>();
        readonly ToolCatalog toolCatalog;
        readonly Dictionary<string, int> tools = new Dictionary<string, int>();
        int nextInstanceId = 1;

        public ContentDatabase Content => db;
        public BalanceConfig Config => config;
        public ToolCatalog ToolCatalog => toolCatalog;
        public ulong RngState => rng.State;

        /// <summary>スロットを回すためのゲーム内通貨。現実のお金とは関係しない。</summary>
        public double Medals { get; private set; }
        /// <summary>今いる階層（1 から）。</summary>
        public int Floor { get; private set; } = 1;
        public int Lives { get; private set; }
        public FloorObjective Objective { get; private set; }
        public int SpinsThisFloor { get; private set; }
        public bool IsFailed { get; private set; }
        /// <summary>最後の階層を攻略した。</summary>
        public bool IsCleared { get; private set; }
        /// <summary>次の階層で増える敵の数（マネキンの首・呪いのラインなど）。</summary>
        public int PendingEnemyBonus { get; private set; }
        /// <summary>懐中電灯の電池の残り（0〜1）。ランを通して持ち越す。</summary>
        public double FlashlightCharge { get; private set; } = 1;

        public IReadOnlyList<SymbolInstance> Reel => reel;
        public IReadOnlyList<RelicDefinition> Relics => relics;
        public IReadOnlyList<string> RelicOffers => relicOffers;

        public bool IsOver => IsFailed || IsCleared;
        public bool IsLastFloor => Floor >= config.FloorCount;
        public double RemovalFee => config.RemovalFeePerFloor * Floor;

        public double CurrentSpinCost
        {
            get
            {
                if (SpinsThisFloor == 0 && HasRelic(RelicKind.FirstSpinFreePerCycle)) return 0;
                double cost = config.SpinCostFor(Floor, SpinsThisFloor);
                foreach (var r in relics)
                    if (r.Kind == RelicKind.SpinCostMultiplier) cost *= r.Amount;
                return Math.Ceiling(cost);
            }
        }

        public bool CanSpin => !IsOver && Medals >= CurrentSpinCost;

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

        public RunState(ContentDatabase db, BalanceConfig config, ulong seed, ToolCatalog toolCatalog = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.toolCatalog = toolCatalog ?? ToolCatalog.CreateMvp();
            rng = new Rng(seed);
            Medals = config.StartingMedals;
            Lives = config.StartingLives;
            foreach (var id in config.StartingReel) AddToReel(id, -1);
            Objective = FloorObjective.Roll(Floor, rng);
            RollRelicOffers();
        }

        public bool HasRelic(RelicKind kind) => relics.Any(r => r.Kind == kind);

        // ---- スロット台 ----

        /// <summary>回す。当たったぶんのメダルが増え、ラインに応じた道具（rewards）が手に入る。</summary>
        public bool TrySpin(out SpinResult result, out List<SpinReward> rewards)
        {
            result = null;
            rewards = null;
            if (!CanSpin) return false;
            Medals -= CurrentSpinCost;
            var cells = SlotMachine.Draw(reel, rng);
            result = SlotMachine.Evaluate(cells, db, relics, rng);
            SpinsThisFloor++;
            Medals += result.Total;
            PendingEnemyBonus += result.EnemyBonus + SpinRewards.EnemyBonusFor(result);
            foreach (var dead in result.DestroyedInstances) reel.Remove(dead);

            rewards = SpinRewards.FromSpin(result, rng);
            foreach (var reward in rewards)
            {
                if (reward.IsMedals) Medals += reward.Medals;
                else AddTool(reward.ToolId, reward.Count);
            }
            return true;
        }

        /// <summary>ガラクタをメダルに換える。valueFactor は割れ物の破損などによる価値の割合。</summary>
        public double ConvertToMedals(string symbolId, double valueFactor = 1)
        {
            double medals = MedalValueFor(symbolId, valueFactor);
            Medals += medals;
            return medals;
        }

        /// <summary>メダルに換えたときの枚数（割れ物の状態とレリック「錆びた秤」などを含む）。</summary>
        public double MedalValueFor(string symbolId, double valueFactor = 1)
        {
            double value = db.GetSymbol(symbolId).SellPrice * Math.Max(0, valueFactor);
            foreach (var r in relics)
                if (r.Kind == RelicKind.SellPriceMultiplier) value *= r.Amount;
            return Math.Floor(value);
        }

        public SymbolInstance AddToReel(string symbolId, int ownerId)
        {
            db.GetSymbol(symbolId); // 存在チェック
            var inst = new SymbolInstance(nextInstanceId++, symbolId, ownerId);
            reel.Add(inst);
            return inst;
        }

        /// <summary>手数料を払ってシンボルを廃棄する（リールの「濃さ」の調整）。</summary>
        public bool TryRemoveFromReel(int instanceId)
        {
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

        public bool TryDiscardRelic(string relicId)
        {
            int index = relics.FindIndex(r => r.Id == relicId);
            if (index < 0) return false;
            relics.RemoveAt(index);
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
            var previous = Objective.Kind;
            Floor++;
            SpinsThisFloor = 0;
            Objective = FloorObjective.Roll(Floor, rng, previous);
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

        void RollRelicOffers()
        {
            relicOffers.Clear();
            var candidates = db.Relics.Where(r => relics.All(owned => owned.Id != r.Id)).Select(r => r.Id).ToList();
            rng.Shuffle(candidates);
            relicOffers.AddRange(candidates.Take(config.RelicOffersPerFloor));
        }
    }
}
