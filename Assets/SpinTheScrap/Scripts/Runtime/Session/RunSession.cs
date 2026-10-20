using System;
using System.Collections.Generic;
using SpinTheScrap.Core;
using SpinTheScrap.Data;
using SpinTheScrap.Interaction;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Session
{
    /// <summary>スロット台に入れたが、まだ「メダルに換える」か「リールに入れる」かを決めていないガラクタ。</summary>
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
        [Tooltip("MVP（5 階層）の数値で始める")]
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
        /// <summary>直前に回した結果（台の種類・出た物）。</summary>
        public MachineOutcome LastOutcome { get; private set; }
        /// <summary>このマシンで遊んでいるプレイヤー（フェーズ 1 で 2 人目が入るまでは 0）。</summary>
        public int LocalPlayerId { get; set; }
        /// <summary>直前の操作の結果メッセージ（倒れた・次の階層へ など）。</summary>
        public string Message { get; private set; }

        /// <summary>階層が始まった（次の階層へ進んだ・やり直した・新しいラン）。引数は増えた敵の数。</summary>
        public event Action<int> FloorStarted;

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
            LastOutcome = null;
            Message = null;
            FloorStarted?.Invoke(0);
        }

        /// <summary>
        /// スロット台の投入口の中のガラクタと、プレイヤーの持ち物をすべて台に入れる。
        /// 入れた物は「換金する」か「どの台のリールに入れるか」を台のメニューで選ぶ。
        /// </summary>
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

        /// <summary>
        /// 倒れた。残機が 1 減る。持っていた物は失い、台に入れた物（Haul）は残る。
        /// 残機があれば「やり直す」でこの階層を最初から、なければラン終了。
        /// </summary>
        public void ReportDowned(int lostCount)
        {
            Run.OnDowned();
            Message = Run.IsFailed
                ? Strings.Format("base.downed_over", lostCount)
                : Strings.Format("base.downed", lostCount, Run.Lives);
        }

        public double MedalValueOf(HaulItem item) => Run.MedalValueFor(item.SymbolId, item.ValueFactor, LocalPlayerId);

        public void ConvertToMedals(HaulItem item)
        {
            if (!haul.Remove(item)) return;
            Run.ConvertToMedals(item.SymbolId, item.ValueFactor, LocalPlayerId);
        }

        /// <summary>割れて価値のない物はリールに入れられない。</summary>
        public bool CanAddToReel(HaulItem item) => item.ValueFactor > 0;

        public void AddToReel(HaulItem item, MachineKind kind)
        {
            if (!CanAddToReel(item) || !haul.Remove(item)) return;
            Run.AddToReel(kind, item.SymbolId, item.OwnerId);
        }

        /// <summary>台を回す。出た物はレバーを引いたプレイヤー（LocalPlayerId）に入る。</summary>
        public bool Spin(MachineKind kind)
        {
            if (!Run.TrySpin(kind, LocalPlayerId, out var outcome)) return false;
            LastSpin = outcome.Reading.Result;
            LastOutcome = outcome;
            Spun?.Invoke(outcome);
            return true;
        }

        /// <summary>台を回した（音を鳴らす・武器を持ち替えるなど、世界の側の処理用）。</summary>
        public event Action<MachineOutcome> Spun;

        /// <summary>攻略条件を満たした（HUD とメニューに知らせる）。</summary>
        public void NotifyObjectiveComplete() => Message = Strings.Get("base.objective_done");

        /// <summary>攻略条件を満たしていれば次の階層へ（最後の階層ならクリア）。進んだら true。</summary>
        public bool TryAdvanceFloor()
        {
            if (Run.IsOver || !Run.Objective.IsComplete) return false;
            Run.AdvanceFloor();
            if (Run.IsCleared)
            {
                Message = Strings.Get("base.cleared");
                return true;
            }
            Message = Strings.Format("base.next_floor_message", Run.Floor);
            StartFloor();
            return true;
        }

        /// <summary>倒れたあと、この階層を最初からやり直す（間取りも作り直す）。</summary>
        public void RestartFloor()
        {
            if (Run.IsOver) return;
            Message = null;
            StartFloor();
        }

        void StartFloor() => FloorStarted?.Invoke(Run.ConsumeEnemyBonus());

        public string Medals(double value) => NumberFormatter.Format(value, Strings.Language);
        public string SymbolName(string symbolId) => Strings.Get(Content.GetSymbol(symbolId).NameKey);
        public string RelicName(string relicId) => Strings.Get(Content.GetRelic(relicId).NameKey);
    }
}
