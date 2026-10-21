using System.Collections.Generic;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Interaction
{
    /// <summary>
    /// 台車（要件書 4.2 道具）。荷台に大きい物をまとめて載せ、取っ手を押して運ぶ。
    /// 車輪は見た目だけで、底の当たり判定を摩擦なしにして転がる感触を出す。荷台は摩擦ありで、載せた物がずれにくい。
    /// 押す力は Interactor がかける。台車は積んだ重さを数え、速く押すとガタガタ鳴って敵を呼ぶ。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Cart : MonoBehaviour
    {
        const float RattleInterval = 0.4f;
        static readonly Vector3 HandleLocal = new Vector3(0f, 0.95f, -0.5f);
        static readonly Vector3 LoadAreaCenter = new Vector3(0f, 0.8f, 0f);
        static readonly Vector3 LoadAreaHalf = new Vector3(0.62f, 0.32f, 0.42f);

        static PhysicsMaterial wheelMaterial;
        static PhysicsMaterial bedMaterial;

        ToolRules rules;
        Rigidbody body;
        Collider[] ownColliders;
        readonly Collider[] overlap = new Collider[32];
        readonly HashSet<Rigidbody> counted = new HashSet<Rigidbody>();
        float nextRattleTime;
        AudioSource rattle;

        public Rigidbody Body => body;
        public Collider[] Colliders => ownColliders;
        /// <summary>荷台に載っている物の重さの合計 (kg)。</summary>
        public float LoadKg { get; private set; }
        /// <summary>荷台に載っているガラクタの数。</summary>
        public int LoadCount { get; private set; }
        /// <summary>誰かが押している（Interactor が設定）。</summary>
        public bool IsPushed { get; set; }
        public Vector3 HandlePoint => transform.TransformPoint(HandleLocal);
        public float SpeedFactor => rules != null ? (float)rules.CartSpeedFactor(LoadKg) : 1f;

        /// <summary>協力プレイ：このマシンで物理を計算しているか（NetCart が設定。なければ常に true）。</summary>
        public System.Func<bool> LocalPhysics { get; set; }
        public bool HasLocalPhysics => LocalPhysics == null || LocalPhysics();
        /// <summary>協力プレイ：物理を計算しているホストへ「押している人の位置と向き」を送る。</summary>
        public System.Action<Vector3, Vector3> RemotePush { get; set; }

        public static Cart Spawn(Transform parent, Vector3 position, Quaternion rotation, ToolRules toolRules)
        {
            var go = new GameObject("Cart");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(position, rotation);
            return Build(go, toolRules);
        }

        /// <summary>渡した物（協力プレイでは NetCart のプレハブ）を台車にする。</summary>
        public static Cart Build(GameObject go, ToolRules toolRules)
        {
            EnsureMaterials();

            var frame = new Color(0.35f, 0.38f, 0.42f);
            var bed = new Color(0.45f, 0.36f, 0.25f);
            // 底（摩擦なし）・荷台・縁・取っ手
            Part(go.transform, "Chassis", new Vector3(0f, 0.24f, 0f), new Vector3(1.2f, 0.3f, 0.7f), frame, wheelMaterial, visible: false);
            Part(go.transform, "Bed", new Vector3(0f, 0.43f, 0f), new Vector3(1.3f, 0.08f, 0.85f), bed, bedMaterial);
            Part(go.transform, "Rail L", new Vector3(-0.63f, 0.55f, 0f), new Vector3(0.04f, 0.16f, 0.85f), frame, bedMaterial);
            Part(go.transform, "Rail R", new Vector3(0.63f, 0.55f, 0f), new Vector3(0.04f, 0.16f, 0.85f), frame, bedMaterial);
            Part(go.transform, "Rail F", new Vector3(0f, 0.55f, 0.41f), new Vector3(1.3f, 0.16f, 0.04f), frame, bedMaterial);
            Part(go.transform, "Rail B", new Vector3(0f, 0.55f, -0.41f), new Vector3(1.3f, 0.16f, 0.04f), frame, bedMaterial);
            Part(go.transform, "Post L", new Vector3(-0.35f, 0.72f, -0.47f), new Vector3(0.04f, 0.5f, 0.04f), frame, null);
            Part(go.transform, "Post R", new Vector3(0.35f, 0.72f, -0.47f), new Vector3(0.04f, 0.5f, 0.04f), frame, null);
            Part(go.transform, "Handle", HandleLocal, new Vector3(0.8f, 0.05f, 0.05f), new Color(0.15f, 0.15f, 0.15f), null);
            foreach (float x in new[] { -0.5f, 0.5f })
                foreach (float z in new[] { -0.3f, 0.3f })
                    Wheel(go.transform, new Vector3(x, 0.1f, z));

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = (float)toolRules.CartMass;
            rb.linearDamping = 1.2f; // 転がり抵抗。手を離すとすぐ止まる
            rb.angularDamping = 3f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var cart = go.AddComponent<Cart>();
            cart.rules = toolRules;
            cart.body = rb;
            cart.ownColliders = go.GetComponentsInChildren<Collider>();
            cart.rattle = Sfx.CreateLoop(go, SfxClips.RattleLoop, 0f, 20f);
            return cart;
        }

        static void EnsureMaterials()
        {
            if (wheelMaterial != null) return;
            wheelMaterial = new PhysicsMaterial("Cart wheels")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
            bedMaterial = new PhysicsMaterial("Cart bed")
            {
                dynamicFriction = 0.9f,
                staticFriction = 1f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
            };
        }

        static void Part(Transform parent, string name, Vector3 localPosition, Vector3 size, Color color, PhysicsMaterial material, bool visible = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>();
            if (visible) renderer.sharedMaterial = MaterialUtil.Create(color);
            else DestroyImmediate(renderer);
            var collider = go.GetComponent<Collider>();
            if (material != null) collider.sharedMaterial = material;
            else DestroyImmediate(collider); // 取っ手などは当たり判定なし（つかむときは荷台を狙えばよい）
        }

        static void Wheel(Transform parent, Vector3 localPosition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Wheel";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            go.transform.localScale = new Vector3(0.2f, 0.04f, 0.2f);
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(new Color(0.08f, 0.08f, 0.08f));
            DestroyImmediate(go.GetComponent<Collider>());
        }

        void FixedUpdate()
        {
            CountLoad();
            var v = body.linearVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (rattle != null && rules != null)
            {
                // ゆっくりなら小さくコロコロ、速いとガタガタ鳴る（敵に聞こえるのは速いときだけ）
                float loud = speed > rules.CartRattleSpeed ? 0.6f : 0.12f;
                rattle.volume = speed < 0.3f ? 0f : loud * Mathf.Clamp01(speed / 3f) + 0.05f;
                rattle.pitch = 0.8f + Mathf.Clamp01(speed / 4f) * 0.5f;
            }
            if (rules == null || Time.time < nextRattleTime) return;
            float radius = (float)rules.CartRattleRadius(speed, LoadKg);
            if (radius <= 0f) return;
            nextRattleTime = Time.time + RattleInterval;
            NoiseEvents.Emit(transform.position, radius);
        }

        /// <summary>荷台の上の空間にあるガラクタを数える（手に持っている物は数えない）。</summary>
        void CountLoad()
        {
            var center = transform.TransformPoint(LoadAreaCenter);
            int n = Physics.OverlapBoxNonAlloc(center, LoadAreaHalf, overlap, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            counted.Clear();
            float load = 0f;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                var rb = overlap[i].attachedRigidbody;
                if (rb == null || rb == body || !counted.Add(rb)) continue;
                var scrap = rb.GetComponent<Scrap>();
                if (scrap == null || scrap.IsHeld) continue;
                load += rb.mass;
                count++;
            }
            LoadKg = load;
            LoadCount = count;
        }
    }
}
