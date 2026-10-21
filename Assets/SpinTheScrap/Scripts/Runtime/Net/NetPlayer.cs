using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using SpinTheScrap.Player;
using SpinTheScrap.Session;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// ネットワーク上のプレイヤー 1 人分の分身。持ち主は自分の一人称プレイヤー（LocalTarget）の位置を写し、
    /// NetworkTransform（クライアント権威）がほかの人へ配る。ほかの人の分身は仮素材の人形で見せる。
    /// ホスト権威型：クライアントの攻撃・音はホストへ送り、ホストの敵からのダメージはその人へ返す。
    /// スロット台の操作（台に入れる・回す・換金など）もここからホストへ送り、ホストの RunSession が行う。
    /// </summary>
    public sealed class NetPlayer : NetworkBehaviour
    {
        static readonly Color[] Colors =
        {
            new Color(1f, 0.35f, 0.7f),
            new Color(0.3f, 0.9f, 1f),
            new Color(1f, 0.9f, 0.3f),
            new Color(0.5f, 1f, 0.5f),
        };

        readonly SyncVar<bool> down = new SyncVar<bool>();
        PlayerTarget hostTarget;
        CapsuleCollider hitBox;
        bool forwardingNoise;
        int sentWeaponSerial = -1;
        int sentWeaponRemaining = -1;
        double pendingDrain;
        float nextDrainTime;

        static RunSession Run => NetSession.LocalSession;

        /// <summary>このマシンの一人称プレイヤー（Phase0Bootstrap が設定する）。</summary>
        public static Transform LocalTarget { get; set; }
        /// <summary>このマシンのプレイヤーの体力（ホストの敵からのダメージを受ける）。</summary>
        public static PlayerHealth LocalHealth { get; set; }
        /// <summary>このマシンのプレイヤーの分身。</summary>
        public static NetPlayer Local { get; private set; }

        public bool IsDown => down.Value;

        /// <summary>このマシンのプレイヤーが仲間に起こされた。</summary>
        public static event Action Revived;
        /// <summary>このマシンの倒れているプレイヤーが、仲間に引きずられている（行き先）。</summary>
        public static event Action<Vector3> DraggedTo;

        Transform avatar;

        public override void OnStartClient()
        {
            if (IsOwner)
            {
                Local = this;
                if (Run != null) Run.LocalPlayerId = OwnerId; // 武器と強化はこの番号で持つ
                // クライアントで鳴った音（足音・攻撃・衝突）をホストの敵にも聞かせる
                if (!IsServerStarted)
                {
                    NoiseEvents.Emitted += ForwardNoise;
                    forwardingNoise = true;
                }
                return;
            }
            BuildAvatar(Colors[Mathf.Abs(OwnerId) % Colors.Length]);
        }

        public override void OnStartServer()
        {
            // ホスト：ほかの人の分身を、敵が狙える相手として登録する
            if (Owner.IsLocalClient) return;
            hitBox = gameObject.AddComponent<CapsuleCollider>();
            hitBox.height = 1.8f;
            hitBox.radius = 0.35f;
            hostTarget = new PlayerTarget
            {
                Body = transform,
                Eye = () => transform.position + Vector3.up * 0.65f,
                Collider = hitBox,
                IsDown = () => down.Value,
                Damage = amount => TakeDamageRpc(Owner, (float)amount),
            };
            PlayerTargets.All.Add(hostTarget);
        }

        public override void OnStopServer()
        {
            if (hostTarget != null) PlayerTargets.All.Remove(hostTarget);
            hostTarget = null;
        }

        public override void OnStopClient()
        {
            if (forwardingNoise) NoiseEvents.Emitted -= ForwardNoise;
            forwardingNoise = false;
            if (Local == this) Local = null;
        }

        void Update()
        {
            if (!IsOwner)
            {
                // 倒れている仲間は横になって見える
                if (avatar != null)
                {
                    avatar.localRotation = Quaternion.RotateTowards(avatar.localRotation,
                        down.Value ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity, 240f * Time.deltaTime);
                    avatar.localPosition = Vector3.MoveTowards(avatar.localPosition,
                        down.Value ? new Vector3(0f, -0.6f, 0f) : Vector3.zero, 2f * Time.deltaTime);
                }
                return;
            }
            if (LocalTarget == null) return;
            transform.SetPositionAndRotation(LocalTarget.position, Quaternion.Euler(0f, LocalTarget.eulerAngles.y, 0f));
            bool isDown = LocalHealth != null && LocalHealth.IsDown;
            if (isDown != down.Value) SetDownRpc(isDown);
            if (!IsServerStarted) ReportLocalState();
        }

        /// <summary>クライアント：自分で減らした武器の耐久・弾数と懐中電灯の電池をホストへ伝える。</summary>
        void ReportLocalState()
        {
            if (Run == null) return;
            var me = Run.Run.Player(Run.LocalPlayerId);
            int remaining = me.Weapon != null ? me.Weapon.Remaining : 0;
            if (me.WeaponSerial != sentWeaponSerial || remaining != sentWeaponRemaining)
            {
                sentWeaponSerial = me.WeaponSerial;
                sentWeaponRemaining = remaining;
                if (me.Weapon != null) WeaponStateRpc(me.WeaponSerial, remaining);
            }
            if (pendingDrain > 0 && Time.unscaledTime >= nextDrainTime)
            {
                DrainRpc(pendingDrain);
                pendingDrain = 0;
                nextDrainTime = Time.unscaledTime + 0.5f;
            }
        }

        // ---- スロット台の操作（クライアント → ホスト） ----

        /// <summary>ホスト：操作を受けたら、すぐ状態を配る。</summary>
        static void Push()
        {
            if (NetSession.Instance != null && Run != null) NetSession.Instance.PublishState(Run, immediately: true);
        }

        PlayerLoadout Loadout => Run.Run.Player(OwnerId);

        public void Feed(List<Scrap> scraps, double brokenRatio)
        {
            var items = new List<NetScrap>();
            var factors = new List<double>();
            foreach (var scrap in scraps)
            {
                var net = scrap.GetComponent<NetScrap>();
                if (net == null) continue;
                scrap.SetPresent(false); // ホストが消すまでの間も見えないように
                items.Add(net);
                factors.Add(scrap.ValueFactor(brokenRatio));
            }
            if (items.Count > 0) FeedRpc(items.ToArray(), factors.ToArray());
        }

        [ServerRpc]
        void FeedRpc(NetScrap[] items, double[] factors)
        {
            if (Run == null || items == null || factors == null) return;
            for (int i = 0; i < items.Length && i < factors.Length; i++)
                if (items[i] != null) items[i].FeedOnHost(Run, factors[i], OwnerId);
            Push();
        }

        public void Spin(MachineKind kind) => SpinRpc((int)kind, Run != null ? Run.CurrentCabinetIndex : -1);

        [ServerRpc]
        void SpinRpc(int kind, int cabinetIndex)
        {
            if (Run == null) return;
            string before = Run.SpinFor((MachineKind)kind, OwnerId);
            if (before == null) return;
            ReplaySpinRpc(Owner, before, kind); // 状態より先に届く（同じ順で送られる）
            if (NetSession.Instance != null) NetSession.Instance.ShowSpin(cabinetIndex, before, kind, OwnerId);
            Push();
        }

        /// <summary>ホストが回す直前の状態。同じように回して、出た物を見せる。</summary>
        [TargetRpc]
        void ReplaySpinRpc(NetworkConnection target, string before, int kind)
        {
            if (Run != null) Run.ReplaySpin(before, (MachineKind)kind);
        }

        public void ConvertToMedals(int haulId) => ConvertRpc(haulId);

        [ServerRpc]
        void ConvertRpc(int haulId)
        {
            var item = Run?.FindHaul(haulId);
            if (item == null) return;
            Run.ConvertToMedals(item, OwnerId);
            Push();
        }

        public void AddToReel(int haulId, MachineKind kind) => AddToReelRpc(haulId, (int)kind);

        [ServerRpc]
        void AddToReelRpc(int haulId, int kind)
        {
            var item = Run?.FindHaul(haulId);
            if (item == null) return;
            Run.AddToReel(item, (MachineKind)kind);
            Push();
        }

        public void RemoveFromReel(MachineKind kind, int instanceId) => RemoveFromReelRpc((int)kind, instanceId);

        [ServerRpc]
        void RemoveFromReelRpc(int kind, int instanceId)
        {
            if (Run == null) return;
            Run.RemoveFromReel((MachineKind)kind, instanceId);
            Push();
        }

        public void BuyRelic(string relicId) => BuyRelicRpc(relicId);

        [ServerRpc]
        void BuyRelicRpc(string relicId)
        {
            if (Run == null) return;
            Run.BuyRelic(relicId);
            Push();
        }

        public void AdvanceFloor() => AdvanceFloorRpc();

        [ServerRpc]
        void AdvanceFloorRpc()
        {
            if (Run == null) return;
            Run.TryAdvanceFloor();
            Push();
        }

        public void NewRun() => NewRunRpc();

        [ServerRpc]
        void NewRunRpc()
        {
            if (Run == null || !Run.Run.IsOver) return; // ランの途中では始め直さない
            Run.NewRun();
            Push();
        }

        public void ReportDowned(int lostCount) => DownedRpc(lostCount);

        [ServerRpc]
        void DownedRpc(int lostCount)
        {
            if (Run == null) return;
            Run.ReportDowned(lostCount);
            Push();
        }

        public void UseTool(string toolId) => UseToolRpc(toolId);

        [ServerRpc]
        void UseToolRpc(string toolId)
        {
            if (Run == null) return;
            Run.Run.TryUseTool(toolId);
            Push();
        }

        public void DrainFlashlight(double seconds) => pendingDrain += seconds;

        [ServerRpc]
        void DrainRpc(double seconds)
        {
            if (Run != null && seconds > 0) Run.Run.DrainFlashlight(seconds);
        }

        public void DiscardSpentWeapon(int weaponSerial) => DiscardWeaponRpc(weaponSerial);

        [ServerRpc]
        void DiscardWeaponRpc(int weaponSerial)
        {
            if (Run == null || Loadout.WeaponSerial != weaponSerial) return;
            Loadout.Weapon = null;
            Push();
        }

        [ServerRpc]
        void WeaponStateRpc(int weaponSerial, int remaining)
        {
            if (Run == null || Loadout.WeaponSerial != weaponSerial || Loadout.Weapon == null) return;
            Loadout.Weapon.SetRemaining(remaining);
        }

        void ForwardNoise(Vector3 position, float radius) => NoiseRpc(position, radius);

        [ServerRpc]
        void NoiseRpc(Vector3 position, float radius) => NoiseEvents.Emit(position, radius);

        [ServerRpc]
        void SetDownRpc(bool value) => down.Value = value;

        /// <summary>クライアントで敵に当てた攻撃をホストへ送る。</summary>
        public void SendHit(NetEnemy enemy, float damage, bool stun, int weaponFamily, bool explosive) =>
            HitRpc(enemy, damage, stun, weaponFamily, explosive);

        [ServerRpc]
        void HitRpc(NetEnemy enemy, float damage, bool stun, int weaponFamily, bool explosive)
        {
            if (enemy != null) enemy.ReceiveHit(damage, stun, weaponFamily, explosive);
        }

        // ---- 倒れた仲間を起こす・引きずる ----

        /// <summary>倒れている仲間（target）を起こす。</summary>
        public void Revive(NetPlayer target) => ReviveRpc(target);

        [ServerRpc]
        void ReviveRpc(NetPlayer target)
        {
            if (target != null && target.down.Value) target.RevivedRpc(target.Owner);
        }

        [TargetRpc]
        void RevivedRpc(NetworkConnection conn) => Revived?.Invoke();

        /// <summary>倒れている仲間（target）を position へ引きずる。</summary>
        public void Drag(NetPlayer target, Vector3 position) => DragRpc(target, position);

        [ServerRpc]
        void DragRpc(NetPlayer target, Vector3 position)
        {
            if (target != null && target.down.Value) target.DraggedRpc(target.Owner, position);
        }

        [TargetRpc]
        void DraggedRpc(NetworkConnection conn, Vector3 position) => DraggedTo?.Invoke(position);

        /// <summary>ホストの敵の攻撃がこの人に当たった。</summary>
        [TargetRpc]
        void TakeDamageRpc(NetworkConnection target, float amount)
        {
            if (LocalHealth != null) LocalHealth.Damage(amount);
        }

        void BuildAvatar(Color color)
        {
            var root = new GameObject("Avatar").transform;
            root.SetParent(transform, false);
            avatar = root;
            Part(root, PrimitiveType.Capsule, Vector3.zero, new Vector3(0.7f, 0.9f, 0.7f), color);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.6f, 0.3f), new Vector3(0.5f, 0.15f, 0.1f), new Color(0.1f, 0.1f, 0.1f));
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.transform.SetParent(root, false);
            lamp.transform.localPosition = new Vector3(0f, 0.6f, 0.4f);
            lamp.type = LightType.Spot;
            lamp.spotAngle = 55f;
            lamp.range = 14f;
            lamp.intensity = 8f;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(color);
            Destroy(go.GetComponent<Collider>());
        }
    }

    /// <summary>クライアントの RunSession.Remote。操作を自分の分身（NetPlayer.Local）からホストへ送る。</summary>
    public sealed class RemoteRun : IRunAuthority
    {
        static NetPlayer P => NetPlayer.Local;

        public void Feed(List<Scrap> scraps, double brokenRatio) => P?.Feed(scraps, brokenRatio);
        public void Spin(MachineKind kind) => P?.Spin(kind);
        public void ConvertToMedals(int haulId) => P?.ConvertToMedals(haulId);
        public void AddToReel(int haulId, MachineKind kind) => P?.AddToReel(haulId, kind);
        public void RemoveFromReel(MachineKind kind, int instanceId) => P?.RemoveFromReel(kind, instanceId);
        public void BuyRelic(string relicId) => P?.BuyRelic(relicId);
        public void AdvanceFloor() => P?.AdvanceFloor();
        public void NewRun() => P?.NewRun();
        public void ReportDowned(int lostCount) => P?.ReportDowned(lostCount);
        public void UseTool(string toolId) => P?.UseTool(toolId);
        public void DrainFlashlight(double seconds) => P?.DrainFlashlight(seconds);
        public void DiscardSpentWeapon(int weaponSerial) => P?.DiscardSpentWeapon(weaponSerial);
    }
}
