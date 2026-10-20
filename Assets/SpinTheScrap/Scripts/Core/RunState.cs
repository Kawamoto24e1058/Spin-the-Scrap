using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    public enum PaydayOutcome
    {
        /// <summary>支払って次のサイクルへ。</summary>
        Paid,
        /// <summary>最終サイクルを支払った（完済）。以降は任意でエンドレス。</summary>
        Cleared,
        /// <summary>ノルマ未満でラン終了。</summary>
        Failed,
    }

    /// <summary>
    /// 1 ランの状態（要件書 8.3 RunState）。ホストだけが持ち、結果をクライアントへ配信する想定。
    /// 所持金は共有財布。
    /// </summary>
    public sealed class RunState
    {
        readonly ContentDatabase db;
        readonly BalanceConfig config;
        readonly Rng rng;
        readonly List<SymbolInstance> reel = new List<SymbolInstance>();
        readonly List<RelicDefinition> relics = new List<RelicDefinition>();
        readonly List<string> relicOffers = new List<string>();
        int nextInstanceId = 1;

        public ContentDatabase Content => db;
        public BalanceConfig Config => config;
        public double Money { get; private set; }
        public int Cycle { get; private set; } = 1;
        public int DivesCompletedInCycle { get; private set; }
        public int SpinsThisCycle { get; private set; }
        public bool IsFailed { get; private set; }
        public bool IsCleared { get; private set; }
        /// <summary>これまでに支払った最大のノルマ（エンドレスのスコア）。</summary>
        public double HighestPaidQuota { get; private set; }
        /// <summary>次の潜行で増える敵の数（マネキンの首など）。</summary>
        public int PendingEnemyBonus { get; private set; }
        public ulong RngState => rng.State;

        public IReadOnlyList<SymbolInstance> Reel => reel;
        public IReadOnlyList<RelicDefinition> Relics => relics;
        public IReadOnlyList<string> RelicOffers => relicOffers;

        public bool IsEndless => Cycle > config.CycleCount;
        public double Quota => config.QuotaFor(Cycle);
        public bool IsPaydayDue => DivesCompletedInCycle >= config.DivesPerCycle;
        public double RemovalFee => config.RemovalFeePerCycle * Cycle;

        public double CurrentSpinCost
        {
            get
            {
                if (SpinsThisCycle == 0 && HasRelic(RelicKind.FirstSpinFreePerCycle)) return 0;
                double cost = config.SpinCostFor(Cycle, SpinsThisCycle);
                foreach (var r in relics)
                    if (r.Kind == RelicKind.SpinCostMultiplier) cost *= r.Amount;
                return Math.Ceiling(cost);
            }
        }

        public bool CanSpin => !IsFailed && Money >= CurrentSpinCost;

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

        public RunState(ContentDatabase db, BalanceConfig config, ulong seed)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            rng = new Rng(seed);
            Money = config.StartingMoney;
            foreach (var id in config.StartingReel) AddToReel(id, -1);
            RollRelicOffers();
        }

        public bool HasRelic(RelicKind kind) => relics.Any(r => r.Kind == kind);

        public bool TrySpin(out SpinResult result)
        {
            result = null;
            if (!CanSpin) return false;
            Money -= CurrentSpinCost;
            var cells = SlotMachine.Draw(reel, rng);
            result = SlotMachine.Evaluate(cells, db, relics, rng);
            SpinsThisCycle++;
            Money += result.Total;
            PendingEnemyBonus += result.EnemyBonus;
            foreach (var dead in result.DestroyedInstances) reel.Remove(dead);
            return true;
        }

        /// <summary>店で即売する。valueFactor は割れ物の破損などによる価値の割合。</summary>
        public double SellScrap(string symbolId, double valueFactor = 1)
        {
            var def = db.GetSymbol(symbolId);
            double price = Math.Floor(def.SellPrice * Math.Max(0, valueFactor));
            Money += price;
            return price;
        }

        public SymbolInstance AddToReel(string symbolId, int ownerId)
        {
            db.GetSymbol(symbolId); // 存在チェック
            var inst = new SymbolInstance(nextInstanceId++, symbolId, ownerId);
            reel.Add(inst);
            return inst;
        }

        /// <summary>闇市で手数料を払ってシンボルを廃棄する（リールの「濃さ」の調整）。</summary>
        public bool TryRemoveFromReel(int instanceId)
        {
            int index = reel.FindIndex(s => s.InstanceId == instanceId);
            if (index < 0 || Money < RemovalFee) return false;
            Money -= RemovalFee;
            reel.RemoveAt(index);
            return true;
        }

        public bool TryBuyRelic(string relicId)
        {
            if (!relicOffers.Contains(relicId) || relics.Count >= config.MaxRelics) return false;
            var def = db.GetRelic(relicId);
            if (Money < def.Price) return false;
            Money -= def.Price;
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

        /// <summary>潜行から戻った。返済日が来ていたら先に ResolvePayday を呼ぶ必要がある。</summary>
        public void CompleteDive()
        {
            if (IsFailed) throw new InvalidOperationException("Run is over");
            if (IsPaydayDue) throw new InvalidOperationException("Payday must be resolved first");
            DivesCompletedInCycle++;
        }

        /// <summary>次の潜行の開始時に呼び、増えた敵の数を受け取る。</summary>
        public int ConsumeEnemyBonus()
        {
            int bonus = PendingEnemyBonus;
            PendingEnemyBonus = 0;
            return bonus;
        }

        public PaydayOutcome ResolvePayday()
        {
            if (IsFailed) throw new InvalidOperationException("Run is over");
            if (!IsPaydayDue) throw new InvalidOperationException("Payday is not due yet");
            double quota = Quota;
            if (Money < quota)
            {
                IsFailed = true;
                return PaydayOutcome.Failed;
            }

            Money -= quota;
            HighestPaidQuota = Math.Max(HighestPaidQuota, quota);
            bool wasFinal = Cycle == config.CycleCount;
            Cycle++;
            DivesCompletedInCycle = 0;
            SpinsThisCycle = 0;
            RollRelicOffers();
            if (wasFinal)
            {
                IsCleared = true;
                return PaydayOutcome.Cleared;
            }
            return PaydayOutcome.Paid;
        }

        void RollRelicOffers()
        {
            relicOffers.Clear();
            var candidates = db.Relics.Where(r => relics.All(owned => owned.Id != r.Id)).Select(r => r.Id).ToList();
            rng.Shuffle(candidates);
            relicOffers.AddRange(candidates.Take(config.RelicOffersPerCycle));
        }
    }
}
