using System;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Interaction
{
    /// <summary>
    /// 廃墟に落ちているガラクタ 1 個。持ち帰るとシンボル（SymbolId）としてリールに入れられる。
    /// 割れ物は落下や衝突で状態（Integrity）が下がり、強い衝撃で割れる（要件書 4.1）。
    /// </summary>
    /// <summary>投げたガラクタが当たったときに反応する物（敵など）。</summary>
    public interface IScrapHittable
    {
        void OnHitByScrap(Scrap scrap, float impactSpeed);
    }

    [RequireComponent(typeof(Rigidbody))]
    public sealed class Scrap : MonoBehaviour
    {
        [SerializeField] string symbolId = "empty_can";
        [SerializeField] ScrapSize size = ScrapSize.Small;
        [SerializeField] bool fragile;
        [SerializeField, Range(0, 1)] float integrity = 1f;

        [Header("Fragile")]
        [Tooltip("これ未満の衝突速度では傷つかない (m/s)")]
        [SerializeField] float crackSpeed = 3f;
        [Tooltip("この衝突速度で一撃で割れる (m/s)")]
        [SerializeField] float breakSpeed = 9f;

        Rigidbody body;
        Renderer[] renderers;
        Color[] originalColors;

        public string SymbolId => symbolId;
        public ScrapSize Size => size;
        public bool Fragile => fragile;
        public float Integrity => integrity;
        public bool IsBroken => fragile && integrity <= 0f;
        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        /// <summary>誰かが手に持っている（Interactor が設定）。</summary>
        public bool IsHeld { get; set; }

        public event Action<Scrap> Broken;

        public static float MassFor(ScrapSize size)
        {
            switch (size)
            {
                case ScrapSize.Small: return 1f;
                case ScrapSize.Large: return 8f;
                case ScrapSize.Huge: return 40f;
                default: throw new ArgumentOutOfRangeException(nameof(size), size, null);
            }
        }

        /// <summary>コードから生成したときの初期化。質量も区分に合わせる。</summary>
        public void Configure(string id, ScrapSize scrapSize, bool isFragile)
        {
            symbolId = id;
            size = scrapSize;
            fragile = isFragile;
            integrity = 1f;
            ApplyPhysicsDefaults();
        }

        /// <summary>売値に掛ける割合。brokenRatio は割れたときに残る割合（レリック「緩衝材」で 0.5）。</summary>
        public double ValueFactor(double brokenRatio)
        {
            if (!fragile) return 1;
            return IsBroken ? brokenRatio : integrity;
        }

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            ApplyPhysicsDefaults();
        }

        void ApplyPhysicsDefaults()
        {
            var rb = Body;
            rb.mass = MassFor(size);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = size == ScrapSize.Small
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.Continuous;
        }

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            // 手に持って運んでいる間の小さな接触は音にしない
            if (!IsHeld) World.NoiseEvents.Impact(transform.position, speed);
            var hittable = collision.collider.GetComponentInParent<IScrapHittable>();
            if (hittable != null) hittable.OnHitByScrap(this, speed);

            if (!fragile || IsBroken) return;
            if (speed < crackSpeed) return;

            integrity = Mathf.Clamp01(integrity - (speed - crackSpeed) / (breakSpeed - crackSpeed));
            Tint(Color.Lerp(new Color(0.15f, 0.15f, 0.15f), Color.white, integrity));
            if (IsBroken)
            {
                World.NoiseEvents.Break(transform.position);
                Broken?.Invoke(this);
            }
        }

        void Tint(Color multiplier)
        {
            if (renderers == null) return;
            if (originalColors == null)
            {
                originalColors = new Color[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) originalColors[i] = MaterialUtil.GetColor(renderers[i].sharedMaterial);
            }
            // material はインスタンス化される（プロトタイプなので許容）
            for (int i = 0; i < renderers.Length; i++) MaterialUtil.SetColor(renderers[i].material, originalColors[i] * multiplier);
        }
    }
}
