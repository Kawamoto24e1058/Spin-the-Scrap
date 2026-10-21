using FishNet.Object;
using UnityEngine;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// ネットワーク上のプレイヤー 1 人分の分身。持ち主は自分の一人称プレイヤー（LocalTarget）の位置を写し、
    /// NetworkTransform（クライアント権威）がほかの人へ配る。ほかの人の分身は仮素材の人形で見せる。
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

        /// <summary>このマシンの一人称プレイヤー（Phase0Bootstrap が設定する）。</summary>
        public static Transform LocalTarget { get; set; }

        public override void OnStartClient()
        {
            if (IsOwner) return;
            BuildAvatar(Colors[Mathf.Abs(OwnerId) % Colors.Length]);
        }

        void Update()
        {
            if (!IsOwner || LocalTarget == null) return;
            transform.SetPositionAndRotation(LocalTarget.position, Quaternion.Euler(0f, LocalTarget.eulerAngles.y, 0f));
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
