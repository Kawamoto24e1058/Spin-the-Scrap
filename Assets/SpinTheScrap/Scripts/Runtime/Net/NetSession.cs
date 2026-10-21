using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// 協力プレイで全員が共有するランの状態（ホスト権威型。要件書 8.2）。ホストが値を決めて配り、
    /// クライアントは同じシード・階層・やり直し回数から同じ間取りを組み立てる。
    /// ランの中身（メダル・リール・強化・武器・台に入れたガラクタ）は文字列にまとめて配る。
    /// </summary>
    public sealed class NetSession : NetworkBehaviour
    {
        readonly SyncVar<ulong> runSeed = new SyncVar<ulong>();
        readonly SyncVar<int> floor = new SyncVar<int>();
        readonly SyncVar<int> attempt = new SyncVar<int>();
        /// <summary>ランの状態すべて（RunSession.SaveState）。クライアントはこれを写して、メニュー・HUD・自分の武器と強化に使う。</summary>
        readonly SyncVar<string> state = new SyncVar<string>();
        float nextStateTime;

        public static NetSession Instance { get; private set; }

        /// <summary>値が変わった（クライアントはこれを見て階層を組み立て直す）。</summary>
        public static event Action Changed;

        public ulong RunSeed => runSeed.Value;
        public int Floor => floor.Value;
        public int Attempt => attempt.Value;
        /// <summary>このマシンのラン（Phase0Bootstrap が設定する）。ホストでは正、クライアントでは写し。</summary>
        public static Session.RunSession LocalSession { get; set; }
        /// <summary>このマシンはホストの値を受け取る側か（HUD はこちらの値を出す）。</summary>
        public static bool IsRemoteView => Instance != null && !Instance.IsServerStarted;

        void Awake()
        {
            runSeed.OnChange += (prev, next, asServer) => Changed?.Invoke();
            floor.OnChange += (prev, next, asServer) => Changed?.Invoke();
            attempt.OnChange += (prev, next, asServer) => Changed?.Invoke();
            state.OnChange += (prev, next, asServer) =>
            {
                if (!asServer && !IsServerStarted && LocalSession != null) LocalSession.LoadState(next);
            };
        }

        public override void OnStartNetwork() => Instance = this;

        public override void OnStartClient()
        {
            if (!IsServerStarted && LocalSession != null) LocalSession.LoadState(state.Value);
        }

        public override void OnStopNetwork()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// ホスト：ランの状態を配る（変わったときだけ送られる）。immediately でなければ 0.1 秒おき
        /// （敵を倒した・電池が減ったなど、毎フレーム変わる物のため）。
        /// </summary>
        [Server]
        public void PublishState(Session.RunSession session, bool immediately = false)
        {
            if (!immediately && UnityEngine.Time.unscaledTime < nextStateTime) return;
            nextStateTime = UnityEngine.Time.unscaledTime + 0.1f;
            state.Value = session.SaveState();
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
