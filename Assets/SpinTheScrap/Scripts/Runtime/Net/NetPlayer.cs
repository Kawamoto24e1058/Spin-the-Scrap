using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SpinTheScrap.Player;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// ネットワーク上のプレイヤー 1 人分の分身。持ち主は自分の一人称プレイヤー（LocalTarget）の位置を写し、
    /// NetworkTransform（クライアント権威）がほかの人へ配る。ほかの人の分身は仮素材の人形で見せる。
    /// ホスト権威型：クライアントの攻撃・音はホストへ送り、ホストの敵からのダメージはその人へ返す。
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

        /// <summary>このマシンの一人称プレイヤー（Phase0Bootstrap が設定する）。</summary>
        public static Transform LocalTarget { get; set; }
        /// <summary>このマシンのプレイヤーの体力（ホストの敵からのダメージを受ける）。</summary>
        public static PlayerHealth LocalHealth { get; set; }
        /// <summary>このマシンのプレイヤーの分身。</summary>
        public static NetPlayer Local { get; private set; }

        public bool IsDown => down.Value;

        public override void OnStartClient()
        {
            if (IsOwner)
            {
                Local = this;
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
            if (!IsOwner || LocalTarget == null) return;
            transform.SetPositionAndRotation(LocalTarget.position, Quaternion.Euler(0f, LocalTarget.eulerAngles.y, 0f));
            bool isDown = LocalHealth != null && LocalHealth.IsDown;
            if (isDown != down.Value) SetDownRpc(isDown);
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
}
