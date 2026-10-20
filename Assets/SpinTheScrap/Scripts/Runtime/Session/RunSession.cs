using System;
using System.Collections.Generic;
using SpinTheScrap.Core;
using SpinTheScrap.Data;
using SpinTheScrap.Interaction;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Session
{
    /// <summary>持ち帰ったが、まだ「売る」か「リールに入れる」かを決めていないガラクタ。</summary>
    public sealed class HaulItem
    {
        public string SymbolId;
        public double ValueFactor;
        public int OwnerId;
    }

    /// <summary>
    /// 1 ランの進行を Unity 側から操作する窓口。ルールそのものは SpinTheScrap.Core.RunState が持つ。
    /// フェーズ 1 ではこのコンポーネントをホスト上だけで動かし、結果を同期する想定。
    /// </summary>
    public sealed class RunSession : MonoBehaviour
    {
        public const string StringsResourcePath = "Localization/strings";

        [Tooltip("未設定なら MvpContent（コード定義）を使う")]
        [SerializeField] ContentDatabaseAsset contentAsset;
        [Tooltip("MVP（2 サイクル）の数値で始める")]
        [SerializeField] bool mvpBalance = true;
        [Tooltip("0 ならランダム")]
        [SerializeField] int seed;
        [SerializeField] Language language = Language.Japanese;

        readonly List<HaulItem> haul = new List<HaulItem>();

        public ContentDatabase Content { get; private set; }
        public StringTable Strings { get; private set; }
        public RunState Run { get; private set; }
        public HazardRules Hazards { get; } = new HazardRules();
        public EnemyRules Enemies { get; } = new EnemyRules();
        public ToolRules Tools { get; } = new ToolRules();
        public IReadOnlyList<HaulItem> Haul => haul;
        public SpinResult LastSpin { get; private set; }
        /// <summary>直前の操作の結果メッセージ（返済結果など）。</summary>
        public string Message { get; private set; }

        /// <summary>新しい潜行が始まった。引数は増えた敵の数。</summary>
        public event Action<int> DiveStarted;

        void Awake()
        {
            Content = contentAsset != null ? contentAsset.Build() : MvpContent.Create();
            var csv = Resources.Load<TextAsset>(StringsResourcePath);
            Strings = csv != null ? StringTable.FromCsv(csv.text) : StringTable.Empty();
            Strings.Language = language;
            NewRun();
        }

        public void NewRun()
        {
            ulong runSeed = seed != 0 ? (ulong)seed : (ulong)DateTime.UtcNow.Ticks;
            Run = new RunState(Content, mvpBalance ? BalanceConfig.CreateMvp() : new BalanceConfig(), runSeed);
            haul.Clear();
            LastSpin = null;
            Message = null;
            DiveStarted?.Invoke(0);
        }

        /// <summary>脱出地点の中のガラクタと、プレイヤーの持ち物をすべて運び出す。</summary>
        public int Extract(ExtractionZone zone, Inventory inventory, bool playerInZone, int ownerId)
        {
            var scraps = zone.ScrapsInside();
            if (playerInZone) scraps.AddRange(inventory.TakeAll());

            double brokenRatio = Run.BrokenFragileValueRatio;
            foreach (var scrap in scraps)
            {
                haul.Add(new HaulItem { SymbolId = scrap.SymbolId, ValueFactor = scrap.ValueFactor(brokenRatio), OwnerId = ownerId });
                Destroy(scrap.gameObject);
            }
            return scraps.Count;
        }

        /// <summary>潜行中に倒れた。持っていた物は失い、運び出し済みの物（Haul）は残る。</summary>
        public void ReportDowned(int lostCount)
        {
            Message = Strings.Format("base.downed", lostCount);
        }

        public double SellPriceOf(HaulItem item) => Math.Floor(Content.GetSymbol(item.SymbolId).SellPrice * item.ValueFactor);

        public void Sell(HaulItem item)
        {
            if (!haul.Remove(item)) return;
            Run.SellScrap(item.SymbolId, item.ValueFactor);
        }

        /// <summary>割れて価値のない物はリールに入れられない。</summary>
        public bool CanAddToReel(HaulItem item) => item.ValueFactor > 0;

        public void AddToReel(HaulItem item)
        {
            if (!CanAddToReel(item) || !haul.Remove(item)) return;
            Run.AddToReel(item.SymbolId, item.OwnerId);
        }

        public bool Spin()
        {
            if (!Run.TrySpin(out var result)) return false;
            LastSpin = result;
            return true;
        }

        /// <summary>今の潜行を終える。返済日でなければすぐ次の潜行を始める。</summary>
        public void FinishDive()
        {
            Message = null;
            Run.CompleteDive();
            if (!Run.IsPaydayDue) StartDive();
        }

        public void ResolvePayday()
        {
            var outcome = Run.ResolvePayday();
            switch (outcome)
            {
                case PaydayOutcome.Paid:
                    Message = Strings.Get("base.paid");
                    StartDive();
                    break;
                case PaydayOutcome.Cleared:
                    Message = Strings.Get("base.cleared");
                    StartDive();
                    break;
                case PaydayOutcome.Failed:
                    Message = Strings.Get("base.failed");
                    break;
            }
        }

        void StartDive() => DiveStarted?.Invoke(Run.ConsumeEnemyBonus());

        public string Money(double value) => NumberFormatter.Format(value, Strings.Language);
        public string SymbolName(string symbolId) => Strings.Get(Content.GetSymbol(symbolId).NameKey);
        public string RelicName(string relicId) => Strings.Get(Content.GetRelic(relicId).NameKey);
    }
}
