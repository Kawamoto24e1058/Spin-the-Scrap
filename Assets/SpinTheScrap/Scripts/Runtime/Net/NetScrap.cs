using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// ネットワークで配るガラクタ（ホストが置く）。位置は NetworkTransform（持ち主の権威）。
    /// 誰も持っていない間はホストが物理を計算し、拾った人が持ち主になってその人のマシンで動かす。
    /// しまった物は、ほかの人の画面でも見えなくする。台に入れた物はホストが消す。
    /// </summary>
    public sealed class NetScrap : NetworkBehaviour
    {
        readonly SyncVar<string> symbolId = new SyncVar<string>();
        readonly SyncVar<bool> stowed = new SyncVar<bool>();

        Scrap scrap;

        /// <summary>シンボルの定義を引く（Phase0Bootstrap が設定する）。</summary>
        public static Func<string, SymbolDefinition> Lookup { get; set; }

        public Scrap Scrap => scrap;

        /// <summary>この物の物理をこのマシンで計算するか（持ち主、誰も持っていなければホスト）。</summary>
        bool HasPhysicsAuthority => Owner.IsValid ? IsOwner : IsServerStarted;

        /// <summary>ホスト：作ったガラクタを結び付ける（ServerManager.Spawn の前に呼ぶ）。</summary>
        public void BindHost(Scrap hostScrap)
        {
            scrap = hostScrap;
            symbolId.Value = hostScrap.SymbolId;
            Hook();
        }

        void Awake()
        {
            stowed.OnChange += (prev, next, asServer) => ApplyStowed();
        }

        public override void OnStartClient()
        {
            if (scrap == null && Lookup != null && !string.IsNullOrEmpty(symbolId.Value))
            {
                scrap = ScrapFactory.CreateOn(gameObject, Lookup(symbolId.Value), transform.position, transform.rotation);
                Hook();
            }
            ApplyStowed();
        }

        void OnDestroy()
        {
            if (scrap == null) return;
            scrap.Claimed -= OnClaimed;
            scrap.StowedChanged -= OnStowedChanged;
        }

        void Hook()
        {
            scrap.Claimed += OnClaimed;
            scrap.StowedChanged += OnStowedChanged;
        }

        void Update()
        {
            if (scrap == null || !IsSpawned) return;
            if (IsServerStarted && Owner.IsValid && !Owner.IsActive)
            {
                // 持っていた人が抜けた：ホストの物に戻して、しまっていた物はその場に出す
                RemoveOwnership();
                stowed.Value = false;
            }
            bool kinematic = scrap.IsStowed || !HasPhysicsAuthority;
            if (scrap.Body.isKinematic != kinematic) scrap.Body.isKinematic = kinematic;
        }

        /// <summary>このマシンの人が手に取った：持ち主になる（ホストなら誰の物でもなくす＝ホストが動かす）。</summary>
        void OnClaimed(Scrap _)
        {
            if (!IsSpawned) return;
            if (IsServerStarted)
            {
                if (Owner.IsValid && !Owner.IsLocalClient) RemoveOwnership();
                return;
            }
            if (!IsOwner) ClaimRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        void ClaimRpc(NetworkConnection caller = null)
        {
            if (caller != null && caller != Owner) GiveOwnership(caller);
        }

        void OnStowedChanged(Scrap _, bool value)
        {
            if (!IsSpawned) return;
            if (IsServerStarted) stowed.Value = value;
            else SetStowedRpc(value);
        }

        [ServerRpc(RequireOwnership = false)]
        void SetStowedRpc(bool value) => stowed.Value = value;

        /// <summary>しまった人以外の画面では、見た目と当たり判定を消す。</summary>
        void ApplyStowed()
        {
            if (scrap == null || scrap.IsStowed) return; // しまった本人は Scrap が隠している
            scrap.SetPresent(!stowed.Value);
        }

        /// <summary>消す（台に入れた・倒れて失った）。ホストならその場で、クライアントならホストに頼む。</summary>
        public void Discard()
        {
            if (!IsSpawned) return;
            scrap?.SetPresent(false);
            if (IsServerStarted) Despawn();
            else DiscardRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        void DiscardRpc()
        {
            if (IsSpawned) Despawn();
        }

        /// <summary>ホスト：台に入れた物を記録して消す（クライアントが入れた物）。</summary>
        [Server]
        public void FeedOnHost(Session.RunSession session, double valueFactor, int ownerId)
        {
            if (!IsSpawned) return;
            session.AddToHaul(symbolId.Value, valueFactor, ownerId);
            Despawn();
        }
    }
}
