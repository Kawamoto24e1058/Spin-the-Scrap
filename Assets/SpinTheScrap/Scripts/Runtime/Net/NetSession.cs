using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// 協力プレイで全員が共有するランの状態（ホスト権威型。要件書 8.2）。ホストが値を決めて配り、
    /// クライアントは同じシード・階層・やり直し回数から同じ間取りを組み立てる。
    /// 敵・ガラクタ・スロット台の結果の同期は次の段階（docs/floor-design.md「協力プレイ」）。
    /// </summary>
    public sealed class NetSession : NetworkBehaviour
    {
        readonly SyncVar<ulong> runSeed = new SyncVar<ulong>();
        readonly SyncVar<int> floor = new SyncVar<int>();
        readonly SyncVar<int> attempt = new SyncVar<int>();
        readonly SyncVar<float> medals = new SyncVar<float>();
        readonly SyncVar<int> lives = new SyncVar<int>();
        readonly SyncVar<int> objectiveKind = new SyncVar<int>();
        readonly SyncVar<int> objectiveProgress = new SyncVar<int>();
        readonly SyncVar<int> objectiveTarget = new SyncVar<int>();

        public static NetSession Instance { get; private set; }

        /// <summary>値が変わった（クライアントはこれを見て階層を組み立て直す）。</summary>
        public static event Action Changed;

        public ulong RunSeed => runSeed.Value;
        public int Floor => floor.Value;
        public int Attempt => attempt.Value;
        public double Medals => medals.Value;
        public int Lives => lives.Value;
        public Core.FloorObjectiveKind ObjectiveKind => (Core.FloorObjectiveKind)objectiveKind.Value;
        public int ObjectiveProgress => objectiveProgress.Value;
        public int ObjectiveTarget => objectiveTarget.Value;
        /// <summary>このマシンはホストの値を受け取る側か（HUD はこちらの値を出す）。</summary>
        public static bool IsRemoteView => Instance != null && !Instance.IsServerStarted;

        void Awake()
        {
            runSeed.OnChange += (prev, next, asServer) => Changed?.Invoke();
            floor.OnChange += (prev, next, asServer) => Changed?.Invoke();
            attempt.OnChange += (prev, next, asServer) => Changed?.Invoke();
        }

        public override void OnStartNetwork() => Instance = this;

        public override void OnStopNetwork()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>ホスト：メダル・残機・攻略条件の進み具合を配る（変わったときだけ送られる）。</summary>
        [Server]
        public void PublishProgress(Core.RunState run)
        {
            medals.Value = (float)run.Medals;
            lives.Value = run.Lives;
            objectiveKind.Value = (int)run.Objective.Kind;
            objectiveProgress.Value = (int)run.Objective.Progress;
            objectiveTarget.Value = (int)run.Objective.Target;
        }

        /// <summary>ホストが今のランと階層を配る。</summary>
        [Server]
        public void Publish(ulong seed, int currentFloor, int currentAttempt)
        {
            runSeed.Value = seed;
            floor.Value = currentFloor;
            attempt.Value = currentAttempt;
        }
    }
}
