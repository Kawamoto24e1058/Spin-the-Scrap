using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using UnityEngine;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// ネットワークで配る台車（ホストが置く）。物理はいつもホストが計算し（荷台のガラクタと一緒に動かすため）、
    /// 位置は NetworkTransform（サーバー権威）。クライアントが押すと、押している位置と向きをホストへ送り、ホストが押す力をかける。
    /// </summary>
    public sealed class NetCart : NetworkBehaviour
    {
        const float PushTimeout = 0.25f;
        const float SendInterval = 0.05f;

        sealed class PushCommand
        {
            public Vector3 Position;
            public Vector3 Forward;
            public float Time;
        }

        readonly Dictionary<int, PushCommand> pushers = new Dictionary<int, PushCommand>();
        Cart cart;
        float nextSendTime;

        /// <summary>台車の重さなどの決まり（Phase0Bootstrap が設定する）。</summary>
        public static ToolRules Rules { get; set; }

        /// <summary>ホスト：作った台車を結び付ける（ServerManager.Spawn の前に呼ぶ）。</summary>
        public void BindHost(Cart hostCart)
        {
            cart = hostCart;
            Hook();
        }

        public override void OnStartClient()
        {
            if (cart != null || Rules == null) return;
            cart = Cart.Build(gameObject, Rules);
            Hook();
        }

        void Hook()
        {
            cart.LocalPhysics = () => !IsSpawned || IsServerStarted;
            cart.RemotePush = SendPush;
        }

        void Update()
        {
            if (cart == null || !IsSpawned) return;
            bool kinematic = !IsServerStarted;
            if (cart.Body.isKinematic != kinematic) cart.Body.isKinematic = kinematic;
        }

        void SendPush(Vector3 position, Vector3 forward)
        {
            if (!IsSpawned || Time.unscaledTime < nextSendTime) return;
            nextSendTime = Time.unscaledTime + SendInterval;
            PushRpc(position, forward);
        }

        [ServerRpc(RequireOwnership = false)]
        void PushRpc(Vector3 position, Vector3 forward, NetworkConnection caller = null)
        {
            if (caller == null) return;
            if (!pushers.TryGetValue(caller.ClientId, out var command)) pushers[caller.ClientId] = command = new PushCommand();
            command.Position = position;
            command.Forward = forward;
            command.Time = Time.time;
        }

        void FixedUpdate()
        {
            if (pushers.Count == 0 || cart == null || !IsServerStarted || Interactor.Local == null) return;
            foreach (var command in pushers.Values)
                if (Time.time - command.Time <= PushTimeout) Interactor.Local.PushCart(cart, command.Position, command.Forward);
        }

        /// <summary>ホスト：いまほかの人が押している数（確認用）。</summary>
        public int RemotePushers
        {
            get
            {
                int count = 0;
                foreach (var command in pushers.Values)
                    if (Time.time - command.Time <= PushTimeout) count++;
                return count;
            }
        }
    }
}
