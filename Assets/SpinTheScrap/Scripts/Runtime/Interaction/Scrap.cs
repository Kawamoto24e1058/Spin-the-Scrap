using System;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Interaction
{
    /// <summary>投げたガラクタが当たったときに反応する物（敵など）。</summary>
    public interface IScrapHittable
    {
        void OnHitByScrap(Scrap scrap, float impactSpeed);
    }

    /// <summary>
    /// 廃墟に落ちているガラクタ 1 個。持ち帰るとシンボル（SymbolId）としてリールに入れられる。
    /// 割れ物は落下や衝突で状態（Integrity）が下がり、強い衝撃で割れる（要件書 4.1）。
    /// </summary>
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
        float nextImpactSoundTime;

        public string SymbolId => symbolId;
        public ScrapSize Size => size;
        public bool Fragile => fragile;
        public float Integrity => integrity;
        public bool IsBroken => fragile && integrity <= 0f;
        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        /// <summary>誰かが手に持っている（Interactor が設定）。</summary>
        public bool IsHeld { get; set; }

        public event Action<Scrap> Broken;
        /// <summary>誰かが手に取った（持った・しまった・取り出した）。協力プレイでは NetScrap が持ち主を切り替える。</summary>
        public event Action<Scrap> Claimed;
        /// <summary>しまった（true）・取り出した（false）。協力プレイでは NetScrap がほかの人の画面でも隠す。</summary>
        public event Action<Scrap, bool> StowedChanged;

        /// <summary>協力プレイ：このマシンで物理を計算しているか（NetScrap が設定。なければ常に true）。</summary>
        public Func<bool> LocalPhysics { get; set; }
        public bool HasLocalPhysics => LocalPhysics == null || LocalPhysics();
        /// <summary>
        /// 協力プレイ：物理を計算しているマシンへ「つかんだ点（この物から見た位置）をどこへ引っ張りたいか」を送る。
        /// 特大を 2 人で持つと、ホストで 2 人分の力が足される。
        /// </summary>
        public Action<Vector3, Vector3> RemoteCarry { get; set; }

        /// <summary>インベントリにしまってある（見えず、当たり判定もない）。</summary>
        public bool IsStowed { get; private set; }

        public void NotifyClaimed() => Claimed?.Invoke(this);

        /// <summary>
        /// しまう・取り出す。オブジェクトは無効にせず、見た目・当たり判定・物理だけを止める
        /// （協力プレイで、しまっている間もネットワーク上の物として残すため）。
        /// </summary>
        public void SetStowed(bool stowed)
        {
            if (IsStowed == stowed) return;
            IsStowed = stowed;
            SetPresent(!stowed);
            Body.isKinematic = stowed;
            if (!stowed)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            StowedChanged?.Invoke(this, stowed);
        }

        /// <summary>見た目と当たり判定を出す・消す（ほかの人がしまったときにも使う）。</summary>
        public void SetPresent(bool present)
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = present;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = present;
        }

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
            nextImpactSoundTime = Time.time + 1f; // 潜行の開始時に置いた物が着地する音は鳴らさない
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
            if (!IsHeld)
            {
                World.NoiseEvents.Impact(transform.position, speed);
                PlayImpactSound(speed);
            }
            var hittable = collision.collider.GetComponentInParent<IScrapHittable>();
            if (hittable != null) hittable.OnHitByScrap(this, speed);

            if (!fragile || IsBroken) return;
            if (speed < crackSpeed) return;

            integrity = Mathf.Clamp01(integrity - (speed - crackSpeed) / (breakSpeed - crackSpeed));
            Tint(Color.Lerp(new Color(0.15f, 0.15f, 0.15f), Color.white, integrity));
            if (IsBroken)
            {
                World.NoiseEvents.Break(transform.position);
                Sfx.PlayAt(SfxClips.Shatter, transform.position, 0.9f, UnityEngine.Random.Range(0.9f, 1.1f));
                Broken?.Invoke(this);
            }
        }

        /// <summary>ぶつかった音。小さい物は金属音、大は鈍い音、特大は重い音。速いほど大きい。</summary>
        void PlayImpactSound(float speed)
        {
            const float minSpeed = 1.2f;
            if (speed < minSpeed || Time.time < nextImpactSoundTime) return;
            nextImpactSoundTime = Time.time + 0.08f; // 転がっているときに鳴りっぱなしにならないように
            float volume = Mathf.Clamp01((speed - minSpeed) / 8f) * 0.9f + 0.1f;
            float pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            switch (size)
            {
                case ScrapSize.Small: Sfx.PlayAt(Sfx.Pick(SfxClips.Clinks), transform.position, volume * 0.7f, pitch); break;
                case ScrapSize.Large: Sfx.PlayAt(SfxClips.Knock, transform.position, volume, pitch); break;
                default: Sfx.PlayAt(SfxClips.HeavyThud, transform.position, volume, pitch); break;
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
