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
        /// <summary>協力プレイでクライアントがどれを選んだかをホストに伝える番号。</summary>
        public int Id;
        public string SymbolId;
        public double ValueFactor;
        public int OwnerId;
    }

    /// <summary>
    /// 協力プレイのクライアントで、ランを変える操作をホストへ送る先（NetPlayer が実装する）。
    /// ホストが RunState を変え、その状態がクライアントに写される。
    /// </summary>
    public interface IRunAuthority
    {
        void Feed(List<Scrap> scraps, double brokenRatio);
        void Spin(MachineKind kind);
        void ConvertToMedals(int haulId);
        void AddToReel(int haulId, MachineKind kind);
        void RemoveFromReel(MachineKind kind, int instanceId);
        void BuyRelic(string relicId);
        void AdvanceFloor();
        void NewRun();
        void ReportDowned(int lostCount);
        void UseTool(string toolId);
        void DrainFlashlight(double seconds);
        void DiscardSpentWeapon(int weaponSerial);
    }

    /// <summary>
    /// 1 ランの進行を Unity 側から操作する窓口。ルールそのものは SpinTheScrap.Core.RunState が持つ。
    /// 協力プレイではホストの RunState が正で、クライアントは Remote（ホストへ送る先）を通して操作し、
    /// ホストから届いた状態（SaveState の文字列）を LoadState で写す。
    /// </summary>
    public sealed class RunSession : MonoBehaviour
    {
        public const string StringsResourcePath = "Localization/strings";

        [Tooltip("未設定なら MvpContent（コード定義）を使う")]
        [SerializeField] ContentDatabaseAsset contentAsset;
        [Tooltip("試作（3 階層）の数値で始める。オフなら本番の 9 階層（ボス 3 体）")]
        [SerializeField] bool mvpBalance;
        [Tooltip("0 ならランダム")]
        [SerializeField] int seed;
        [SerializeField] Language language = Language.Japanese;

        readonly List<HaulItem> haul = new List<HaulItem>();
        int nextHaulId = 1;

        /// <summary>協力プレイのクライアントでだけ設定する。設定されていると、ランを変える操作はホストへ送る。</summary>
        public IRunAuthority Remote { get; set; }
        /// <summary>台に入れたガラクタを片付ける（協力プレイのホストではネットワークから消す）。</summary>
        public Action<Scrap> DisposeScrap { get; set; } = scrap => Destroy(scrap.gameObject);
        /// <summary>状態がホストから届いた（クライアント）。</summary>
        public event Action StateLoaded;

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

        /// <summary>ランのシード。協力プレイではホストがこれを配り、全員が同じ間取りを組み立てる。</summary>
        public ulong RunSeed { get; private set; }
        /// <summary>今の階層をやり直した回数（やり直すと間取りが変わる）。</summary>
        public int Attempt { get; private set; }

        /// <summary>ランを始めた時刻（結果画面のプレイ時間。メニューで止めている間も数える）。</summary>
        public float RunStartRealtime { get; private set; }

        public void NewRun()
        {
            if (Remote != null)
            {
                Remote.NewRun();
                return;
            }
            RunStartRealtime = Time.realtimeSinceStartup;
            ulong runSeed = seed != 0 ? (ulong)seed : (ulong)DateTime.UtcNow.Ticks;
            RunSeed = runSeed;
            Attempt = 0;
            Run = new RunState(Content, mvpBalance ? BalanceConfig.CreateMvp() : new BalanceConfig(), runSeed);
            haul.Clear();
            nextHaulId = 1;
            LastSpin = null;
            LastOutcome = null;
            Message = null;
            FloorStarted?.Invoke(0);
        }

        /// <summary>
        /// ガラクタをスロット台に入れる（持っている物・台の前に置いた物）。入れた物は台のメニューで
        /// 「換金する」か「どの台のリールに入れるか」を選ぶ。入れた数を返す。
        /// </summary>
        public int Feed(List<Scrap> scraps, int ownerId)
        {
            double brokenRatio = Run.BrokenFragileValueRatio;
            if (Remote != null)
            {
                Remote.Feed(scraps, brokenRatio);
                return scraps.Count;
            }
            foreach (var scrap in scraps)
            {
                AddToHaul(scrap.SymbolId, scrap.ValueFactor(brokenRatio), ownerId);
                DisposeScrap(scrap);
            }
            return scraps.Count;
        }

        /// <summary>ホスト：台に入れたガラクタを記録する（クライアントが入れた物も）。</summary>
        public void AddToHaul(string symbolId, double valueFactor, int ownerId) =>
            haul.Add(new HaulItem { Id = nextHaulId++, SymbolId = symbolId, ValueFactor = valueFactor, OwnerId = ownerId });

        public HaulItem FindHaul(int id) => haul.Find(h => h.Id == id);

        /// <summary>
        /// 倒れた。残機が 1 減る。持っていた物は失い、台に入れた物（Haul）は残る。
        /// 残機があれば「やり直す」でこの階層を最初から、なければラン終了。
        /// </summary>
        public void ReportDowned(int lostCount)
        {
            if (Remote != null)
            {
                Remote.ReportDowned(lostCount);
                Message = Strings.Format("base.downed", lostCount, Math.Max(0, Run.Lives - 1));
                return;
            }
            Run.OnDowned();
            Message = Run.IsFailed
                ? Strings.Format("base.downed_over", lostCount)
                : Strings.Format("base.downed", lostCount, Run.Lives);
        }

        public double MedalValueOf(HaulItem item) => Run.MedalValueFor(item.SymbolId, item.ValueFactor, LocalPlayerId);

        public void ConvertToMedals(HaulItem item) => ConvertToMedals(item, LocalPlayerId);

        /// <summary>換金する。レートは換金した人（playerId）のギャンブルの強化で決まる。</summary>
        public void ConvertToMedals(HaulItem item, int playerId)
        {
            if (Remote != null)
            {
                Remote.ConvertToMedals(item.Id);
                return;
            }
            if (!haul.Remove(item)) return;
            Run.ConvertToMedals(item.SymbolId, item.ValueFactor, playerId);
        }

        /// <summary>割れて価値のない物はリールに入れられない。</summary>
        public bool CanAddToReel(HaulItem item) => item.ValueFactor > 0;

        public void AddToReel(HaulItem item, MachineKind kind)
        {
            if (!CanAddToReel(item)) return;
            if (Remote != null)
            {
                Remote.AddToReel(item.Id, kind);
                return;
            }
            if (!haul.Remove(item)) return;
            Run.AddToReel(kind, item.SymbolId, item.OwnerId);
        }

        public void RemoveFromReel(MachineKind kind, int instanceId)
        {
            if (Remote != null) Remote.RemoveFromReel(kind, instanceId);
            else Run.TryRemoveFromReel(kind, instanceId);
        }

        /// <summary>レリックを買う。買えたら true（クライアントでは送っただけなので false）。</summary>
        public bool BuyRelic(string relicId)
        {
            if (Remote == null) return Run.TryBuyRelic(relicId);
            Remote.BuyRelic(relicId);
            return false;
        }

        /// <summary>道具を 1 回使う。クライアントではその場で減らし、ホストにも伝える。</summary>
        public bool TryUseTool(string toolId)
        {
            if (!Run.TryUseTool(toolId)) return false;
            Remote?.UseTool(toolId);
            return true;
        }

        /// <summary>懐中電灯の電池を減らす（電池はチームで共有）。</summary>
        public bool DrainFlashlight(double seconds)
        {
            Remote?.DrainFlashlight(seconds);
            return Run.DrainFlashlight(seconds);
        }

        /// <summary>手に持っている武器が壊れた（耐久・弾がなくなった）。</summary>
        public void DiscardSpentWeapon()
        {
            var me = Run.Player(LocalPlayerId);
            if (Remote != null) Remote.DiscardSpentWeapon(me.WeaponSerial); // ホストが捨てるまでは素手として扱う
            else me.Weapon = null;
        }

        /// <summary>
        /// 台を回す。出た物はレバーを引いたプレイヤー（LocalPlayerId）に入る。
        /// クライアントではホストへ送り、ホストが回す直前の状態が ReplaySpin で届く。
        /// </summary>
        public bool Spin(MachineKind kind)
        {
            if (Remote != null)
            {
                if (Run.CanSpin(kind, LocalPlayerId)) Remote.Spin(kind);
                return false;
            }
            if (!Run.TrySpin(kind, LocalPlayerId, out var outcome)) return false;
            ShowOutcome(outcome);
            return true;
        }

        /// <summary>ホスト：ほかの人（playerId）の代わりに回す。回せたら回す直前の状態（RunState.Save）を返す。</summary>
        public string SpinFor(MachineKind kind, int playerId)
        {
            if (!Run.CanSpin(kind, playerId)) return null;
            string before = Run.Save();
            Run.TrySpin(kind, playerId, out _);
            return before;
        }

        /// <summary>
        /// クライアント：ホストが回す直前の状態に戻して同じように回す。乱数の状態も同じなので、同じ結果になる
        /// （出た物の表示と、回した音・武器の持ち替えのため）。
        /// </summary>
        public void ReplaySpin(string before, MachineKind kind)
        {
            LoadRun(before);
            if (Run.TrySpin(kind, LocalPlayerId, out var outcome)) ShowOutcome(outcome);
        }

        void ShowOutcome(MachineOutcome outcome)
        {
            LastSpin = outcome.Reading.Result;
            LastOutcome = outcome;
            Spun?.Invoke(outcome);
        }

        /// <summary>台を回した（音を鳴らす・武器を持ち替えるなど、世界の側の処理用）。</summary>
        public event Action<MachineOutcome> Spun;

        /// <summary>攻略条件を満たした（HUD とメニューに知らせる）。</summary>
        public void NotifyObjectiveComplete() => Message = Strings.Get("base.objective_done");

        /// <summary>攻略条件を満たしていれば次の階層へ（最後の階層ならクリア）。進んだら true。</summary>
        public bool TryAdvanceFloor()
        {
            if (Run.IsOver || !Run.Objective.IsComplete) return false;
            if (Remote != null)
            {
                Remote.AdvanceFloor(); // 間取りはホストが配った階層で組み立て直す
                return false;
            }
            Run.AdvanceFloor();
            if (Run.IsCleared)
            {
                Message = Strings.Get("base.cleared");
                return true;
            }
            Message = Strings.Format("base.next_floor_message", Run.Floor);
            Attempt = 0;
            StartFloor();
            return true;
        }

        /// <summary>協力プレイ：倒れても階層はやり直さず、その人だけ入口で起き上がる（RespawnRequested）。</summary>
        public bool CoopRespawn { get; set; }
        public event Action RespawnRequested;

        /// <summary>倒れたあと、この階層を最初からやり直す（間取りも作り直す）。協力プレイでは入口で起き上がる。</summary>
        public void RestartFloor()
        {
            if (Run.IsOver) return;
            if (CoopRespawn)
            {
                Message = null;
                RespawnRequested?.Invoke();
                return;
            }
            Attempt++;
            Message = null;
            StartFloor();
        }

        void StartFloor() => FloorStarted?.Invoke(Run.ConsumeEnemyBonus());

        // ---- 協力プレイ：ホストの状態をクライアントへ写す ----

        /// <summary>ホスト：ランの状態（RunState・台に入れたガラクタ・やり直し回数・メッセージ）を文字列にする。</summary>
        public string SaveState()
        {
            var w = new SnapshotWriter();
            w.Write(Attempt).Write((Message ?? "").Replace('\n', ' ').Replace('\t', ' ')).Write(nextHaulId).Write(haul.Count);
            foreach (var h in haul) w.Write(h.Id).Write(h.SymbolId).Write(h.ValueFactor).Write(h.OwnerId);
            return w.ToString() + "\n" + Run.Save();
        }

        /// <summary>クライアント：ホストから届いた状態を写す。</summary>
        public void LoadState(string state)
        {
            if (string.IsNullOrEmpty(state)) return;
            int split = state.IndexOf('\n');
            var r = new SnapshotReader(state.Substring(0, split));
            Attempt = r.Int();
            string message = r.String();
            if (message.Length > 0) Message = message;
            nextHaulId = r.Int();
            haul.Clear();
            int count = r.Int();
            for (int i = 0; i < count; i++)
                haul.Add(new HaulItem { Id = r.Int(), SymbolId = r.String(), ValueFactor = r.Double(), OwnerId = r.Int() });
            LoadRun(state.Substring(split + 1));
            StateLoaded?.Invoke();
        }

        /// <summary>
        /// RunState を写す。自分の武器の耐久・弾数はこのマシンで減らしているので、同じ武器のままなら少ない方を残す
        /// （ホストには NetPlayer が少し遅れて伝える）。
        /// </summary>
        void LoadRun(string snapshot)
        {
            var mine = Run.Player(LocalPlayerId);
            int serial = mine.WeaponSerial;
            int remaining = mine.Weapon != null ? mine.Weapon.Remaining : 0;
            Run.Load(snapshot);
            mine = Run.Player(LocalPlayerId);
            if (mine.Weapon != null && mine.WeaponSerial == serial && remaining < mine.Weapon.Remaining)
                mine.Weapon.SetRemaining(remaining);
        }

        public string Medals(double value) => NumberFormatter.Format(value, Strings.Language);
        public string SymbolName(string symbolId) => Strings.Get(Content.GetSymbol(symbolId).NameKey);
        public string RelicName(string relicId) => Strings.Get(Content.GetRelic(relicId).NameKey);
    }
}
