using System;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using SpinTheScrap.Player;
using SpinTheScrap.World;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;
using Random = UnityEngine.Random;

namespace SpinTheScrap.Enemies
{
    /// <summary>
    /// 敵（這う者・徘徊者・目の見えない巨体）。判断は EnemyBrain（Core）が行い、ここは知覚・移動・体力・見た目だけを受け持つ。
    /// 種類ごとの速さや攻撃は EnemyRules.Profile、体力と攻撃力は階層で決まる。
    /// 部屋をまたぐときは間取り（FacilityLayout.Path）に沿ってドアを順に抜ける。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class Enemy : MonoBehaviour, IScrapHittable, IEnemyTarget
    {
        const float TurnSpeed = 360f;
        const float StuckCheckSeconds = 1.5f;
        const float StuckDistance = 0.3f;
        const float FlashSeconds = 0.12f;

        EnemyKind kind;
        EnemyRules rules;
        EnemyBrain brain;
        HealthState health;
        BuiltFacility facility;
        Rng rng;
        PlayerTarget target;
        CharacterController body;
        Transform visual;
        Light eyeLight;
        Renderer[] renderers;
        Color[] baseColors;
        float headHeight;

        Vector3? heardNoise;
        float heardNoiseDistance;
        Vector3? wanderTarget;
        Vector3 stuckCheckPosition;
        float stuckCheckTime;
        float unstickUntil;
        Vector3 unstickDirection;
        AudioSource drone;
        EnemyState lastState = EnemyState.Wander;
        float nextStepTime;
        float flashUntil;
        bool dead;
        Light coreLight;
        float nextSpecialTime;
        float dischargeAt = -1f;
        readonly System.Collections.Generic.List<Enemy> minions = new System.Collections.Generic.List<Enemy>();

        public EnemyKind Kind => kind;
        public EnemyState State => brain != null ? brain.State : EnemyState.Wander;
        public HealthState Health => health;
        public bool IsDead => dead;
        public double CurrentHealth => health != null ? health.Current : 0;
        public double MaxHealth => health != null ? health.Max : 0;
        public Transform Transform => transform;
        public void ApplyHit(double damage, bool stun, SymbolTag weaponFamily, bool explosive) => TakeDamage(damage, stun, weaponFamily, explosive);

        /// <summary>どれかの敵が倒された（攻略条件・ガラクタを落とす・倒したときの回復に使う）。</summary>
        public static event Action<Enemy> AnyDied;
        /// <summary>群れを呼ぶ人形が這う者を呼びたい（Phase0Bootstrap が呼び出して、呼んだ敵を返す）。</summary>
        public static Func<Enemy, Vector3, Enemy> SummonRequested;
        /// <summary>プレイヤーの見つかりにくさ（人形の強化）。Phase0Bootstrap が強化から設定する。</summary>
        public static double PlayerStealth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AnyDied = null;
            SummonRequested = null;
            PlayerStealth = 0;
        }

        /// <summary>見た目（仮素材）と当たり判定を組み立てて置く。</summary>
        /// <param name="root">協力プレイのホストでは、ネットワークで配る敵のプレハブ（NetEnemy）のインスタンスを渡す。</param>
        public static Enemy Spawn(EnemyKind kind, int floor, Transform parent, Vector3 position, EnemyRules enemyRules,
            BuiltFacility builtFacility, ulong seed, GameObject root = null)
        {
            var go = root != null ? root : new GameObject();
            go.name = kind.ToString();
            if (parent != null) go.transform.SetParent(parent, true);
            var cc = go.AddComponent<CharacterController>();
            var visual = BuildBody(kind, go.transform, out float height, out float radius, out float head);
            cc.height = height;
            cc.radius = radius;
            cc.stepOffset = Mathf.Min(0.3f, cc.height * 0.3f);
            go.transform.position = position + Vector3.up * (cc.height / 2f + 0.05f);
            return Setup(go, cc, visual, kind, floor, enemyRules, builtFacility, seed, head);
        }

        /// <summary>
        /// 種類ごとの見た目（仮素材）を組み立てる。当たり判定の大きさと目の高さも返す。
        /// 協力プレイのクライアントは NetEnemy から呼び、判断なしの見た目だけを作る。
        /// </summary>
        public static Transform BuildBody(EnemyKind kind, Transform parent, out float height, out float radius, out float head)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(parent, false);
            switch (kind)
            {
                case EnemyKind.Crawler:
                    height = 0.9f;
                    radius = 0.4f;
                    head = 0.6f;
                    // 低く這う、赤黒い塊
                    Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.1f, 0f), new Vector3(0.7f, 0.45f, 1.1f), new Color(0.25f, 0.05f, 0.05f), Quaternion.Euler(90f, 0f, 0f));
                    Part(visual, PrimitiveType.Sphere, new Vector3(0f, 0.05f, 0.5f), new Vector3(0.4f, 0.32f, 0.4f), new Color(0.35f, 0.1f, 0.08f));
                    Eyes(visual, new Vector3(0f, 0.12f, 0.68f), 0.08f, 0.05f);
                    break;
                case EnemyKind.Brute:
                    height = 2.9f;
                    radius = 0.75f;
                    head = 2.6f;
                    // 灰色の巨体。目はない
                    Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.1f, 0f), new Vector3(1.4f, 1.35f, 1.1f), new Color(0.32f, 0.31f, 0.3f));
                    Part(visual, PrimitiveType.Sphere, new Vector3(0f, 1.25f, 0.15f), new Vector3(0.7f, 0.6f, 0.7f), new Color(0.45f, 0.42f, 0.4f));
                    Part(visual, PrimitiveType.Cube, new Vector3(-0.85f, 0.2f, 0.2f), new Vector3(0.35f, 1.4f, 0.35f), new Color(0.3f, 0.29f, 0.28f));
                    Part(visual, PrimitiveType.Cube, new Vector3(0.85f, 0.2f, 0.2f), new Vector3(0.35f, 1.4f, 0.35f), new Color(0.3f, 0.29f, 0.28f));
                    break;
                case EnemyKind.Dynamo:
                    height = 2.6f;
                    radius = 0.9f;
                    head = 2.2f;
                    // 配電盤のような箱に、青く光るコイル
                    Part(visual, PrimitiveType.Cube, new Vector3(0f, -0.1f, 0f), new Vector3(1.7f, 2.2f, 1.3f), new Color(0.22f, 0.25f, 0.3f));
                    Part(visual, PrimitiveType.Cylinder, new Vector3(0f, 1.15f, 0f), new Vector3(0.9f, 0.15f, 0.9f), new Color(0.4f, 0.75f, 1f));
                    Part(visual, PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 0f), new Vector3(1.1f, 0.08f, 1.1f), new Color(0.4f, 0.75f, 1f));
                    Part(visual, PrimitiveType.Cube, new Vector3(0f, 0.3f, 0.66f), new Vector3(0.9f, 0.5f, 0.04f), new Color(0.1f, 0.1f, 0.12f));
                    break;
                case EnemyKind.Puppeteer:
                    height = 2.4f;
                    radius = 0.4f;
                    head = 2.1f;
                    // 細長い人形。大きな白い頭と、垂れた糸
                    Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.2f, 0f), new Vector3(0.45f, 0.9f, 0.35f), new Color(0.35f, 0.1f, 0.2f));
                    Part(visual, PrimitiveType.Sphere, new Vector3(0f, 0.95f, 0f), new Vector3(0.6f, 0.65f, 0.6f), new Color(0.95f, 0.93f, 0.9f));
                    Eyes(visual, new Vector3(0f, 1f, 0.28f), 0.12f, 0.08f);
                    foreach (float x in new[] { -0.5f, 0.5f })
                        Part(visual, PrimitiveType.Cube, new Vector3(x, 0.6f, 0f), new Vector3(0.01f, 1.6f, 0.01f), new Color(0.85f, 0.85f, 0.8f));
                    break;
                default:
                    height = 2.1f;
                    radius = 0.35f;
                    head = 1.7f;
                    Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.1f, 0f), new Vector3(0.55f, 0.95f, 0.45f), new Color(0.06f, 0.06f, 0.07f));
                    Part(visual, PrimitiveType.Sphere, new Vector3(0f, 0.85f, 0f), new Vector3(0.36f, 0.42f, 0.36f), new Color(0.85f, 0.83f, 0.78f));
                    Eyes(visual, new Vector3(0f, 0.9f, 0.16f), 0.08f, 0.06f);
                    break;
            }
            return visual;
        }

        static Enemy Setup(GameObject go, CharacterController cc, Transform visual, EnemyKind kind, int floor, EnemyRules enemyRules,
            BuiltFacility builtFacility, ulong seed, float head)
        {
            Light eye = null;
            if (kind != EnemyKind.Brute && kind != EnemyKind.Dynamo)
            {
                eye = new GameObject("Eye light").AddComponent<Light>();
                eye.transform.SetParent(visual, false);
                eye.transform.localPosition = new Vector3(0f, head - cc.height / 2f, 0.4f);
                eye.type = LightType.Point;
                eye.color = new Color(1f, 0.15f, 0.1f);
                eye.range = kind == EnemyKind.Crawler ? 1.8f : 3f;
                eye.intensity = 2f;
            }

            var enemy = go.AddComponent<Enemy>();
            enemy.kind = kind;
            enemy.rules = enemyRules.Profile(kind, floor);
            enemy.brain = new EnemyBrain(enemy.rules);
            enemy.health = new HealthState(enemyRules.HealthFor(kind, floor));
            enemy.facility = builtFacility;
            enemy.rng = new Rng(seed);
            enemy.visual = visual;
            enemy.eyeLight = eye;
            enemy.headHeight = head;
            enemy.renderers = visual.GetComponentsInChildren<Renderer>();
            enemy.baseColors = new Color[enemy.renderers.Length];
            for (int i = 0; i < enemy.renderers.Length; i++) enemy.baseColors[i] = MaterialUtil.GetColor(enemy.renderers[i].sharedMaterial);
            if (kind == EnemyKind.Dynamo)
            {
                enemy.drone = Sfx.CreateLoop(go, SfxClips.BuzzLoop, 0.6f, 25f);
                enemy.coreLight = new GameObject("Core light").AddComponent<Light>();
                enemy.coreLight.transform.SetParent(visual, false);
                enemy.coreLight.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                enemy.coreLight.type = LightType.Point;
                enemy.coreLight.color = new Color(0.45f, 0.75f, 1f);
                enemy.coreLight.range = 6f;
                enemy.coreLight.intensity = 2f;
                enemy.nextSpecialTime = Time.time + (float)enemy.rules.DynamoDischargeInterval;
            }
            else if (kind != EnemyKind.Crawler)
                enemy.drone = Sfx.CreateLoop(go, SfxClips.DroneLoop, 0.5f, IsBossKind(kind) ? 30f : 18f);
            if (kind == EnemyKind.Puppeteer) enemy.nextSpecialTime = Time.time + 2f;
            return enemy;
        }

        static bool IsBossKind(EnemyKind kind) => EnemyRules.IsBoss(kind);

        static void Part(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 scale, Color color, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            if (rotation.HasValue) go.transform.localRotation = rotation.Value;
            // 被弾で色を変えるので、敵ごとに別のマテリアルにする
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(color);
            DestroyImmediate(go.GetComponent<Collider>()); // 当たり判定は CharacterController だけ
        }

        static void Eyes(Transform parent, Vector3 center, float spacing, float size)
        {
            var red = new Color(1f, 0.1f, 0.05f);
            Part(parent, PrimitiveType.Sphere, center + Vector3.left * spacing, Vector3.one * size, red);
            Part(parent, PrimitiveType.Sphere, center + Vector3.right * spacing, Vector3.one * size, red);
        }

        void Awake()
        {
            body = GetComponent<CharacterController>();
            NoiseEvents.Emitted += OnNoise;
            EnemyRegistry.All.Add(this);
        }

        void OnDestroy()
        {
            NoiseEvents.Emitted -= OnNoise;
            EnemyRegistry.All.Remove(this);
        }

        void OnNoise(Vector3 position, float radius)
        {
            float distance = Vector3.Distance(position, transform.position);
            if (distance > radius) return;
            // 同じフレームに複数聞こえたら近い方を調べる
            if (heardNoise.HasValue && distance >= heardNoiseDistance) return;
            heardNoise = position;
            heardNoiseDistance = distance;
        }

        public void OnHitByScrap(Scrap scrap, float impactSpeed)
        {
            if (brain == null || dead) return;
            brain.OnHit(impactSpeed);
            // 投げた物も少し痛い
            if (impactSpeed >= rules.StunHitSpeed) TakeDamage(impactSpeed * 1.5f, false);
        }

        /// <summary>
        /// 武器などでダメージを受ける。stun が true ならひるむ。武器の系統と爆発かどうかで効き方が変わる（ボスの耐性・弱点）。
        /// 倒れたら true。
        /// </summary>
        public bool TakeDamage(double amount, bool stun, SymbolTag weaponFamily = SymbolTag.None, bool explosive = false)
        {
            if (dead || health == null) return false;
            flashUntil = Time.time + FlashSeconds;
            amount *= rules.DamageTakenMultiplier(kind, weaponFamily, explosive, brain.State == EnemyState.Stunned);
            if (stun) brain.OnHit(double.MaxValue);
            if (!health.Damage(amount)) return false;
            Die();
            return true;
        }

        void Die()
        {
            dead = true;
            EnemyRegistry.All.Remove(this);
            Sfx.PlayAt(SfxClips.Groan, transform.position, 0.9f, kind == EnemyKind.Crawler ? 1.6f : kind == EnemyKind.Brute ? 0.6f : 1f);
            AnyDied?.Invoke(this);
            // 協力プレイでネットワークに配っている敵は、ネットワークから消す
            var nob = GetComponent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned) FishNet.InstanceFinder.ServerManager.Despawn(gameObject);
            else Destroy(gameObject);
        }

        void Update()
        {
            if (brain == null || dead || Time.deltaTime <= 0f) return; // 拠点メニューで時間が止まっている間は何もしない
            target = PlayerTargets.Nearest(transform.position);
            var perception = Perceive();
            heardNoise = null;

            if (wanderTarget == null || Flat(wanderTarget.Value - transform.position).magnitude < 1f) wanderTarget = PickWanderTarget();
            var decision = brain.Tick(Time.deltaTime, perception, ToN(wanderTarget.Value));

            if (decision.Attack && target != null) target.Damage(rules.AttackDamage);
            if (kind == EnemyKind.Dynamo) UpdateDischarge(decision.State);
            if (kind == EnemyKind.Puppeteer)
            {
                UpdateSummon(decision.State);
                decision = KeepDistance(decision);
            }
            Move(decision);
            UpdateLook(decision.State);
            UpdateSound(decision);
        }

        /// <summary>電気を吸う機械：数秒ごとにコイルが強く光り、1 秒後に周りへ放電する。範囲の外か、物陰にいればよけられる。</summary>
        void UpdateDischarge(EnemyState state)
        {
            if (state == EnemyState.Stunned)
            {
                dischargeAt = -1f;
                coreLight.intensity = 0.5f;
                return;
            }
            if (dischargeAt < 0f && Time.time >= nextSpecialTime)
            {
                dischargeAt = Time.time + (float)rules.DynamoDischargeWarning;
                Sfx.PlayAt(SfxClips.Shatter, transform.position, 0.6f, 0.5f);
            }
            if (dischargeAt >= 0f)
            {
                coreLight.intensity = Mathf.Lerp(2f, 14f, 1f - (dischargeAt - Time.time) / (float)rules.DynamoDischargeWarning);
                coreLight.range = 6f + coreLight.intensity * 0.5f;
                if (Time.time < dischargeAt) return;
                dischargeAt = -1f;
                nextSpecialTime = Time.time + (float)rules.DynamoDischargeInterval;
                Sfx.PlayAt(SfxClips.Bang, transform.position, 1f, 1.8f);
                NoiseEvents.Emit(transform.position, 20f);
                // 範囲内で、物陰に隠れていない全員に当たる
                var center = transform.position + Vector3.up * 0.8f;
                foreach (var t in PlayerTargets.All)
                {
                    if (t.Body == null || (t.IsDown != null && t.IsDown())) continue;
                    if (Vector3.Distance(transform.position, t.Body.position) > rules.DynamoDischargeRadius) continue;
                    bool covered = Physics.Linecast(center, t.Eye(), out var hit, ~0, QueryTriggerInteraction.Ignore)
                                   && hit.collider != t.Collider && hit.collider != body;
                    if (!covered) t.Damage(rules.AttackDamage * 1.5);
                }
            }
            else coreLight.intensity = 2f;
        }

        /// <summary>群れを呼ぶ人形：見つけている間、数秒ごとに這う者を呼ぶ（同時に呼べる数に上限）。</summary>
        void UpdateSummon(EnemyState state)
        {
            if (state != EnemyState.Chase && state != EnemyState.Investigate) return;
            if (Time.time < nextSpecialTime) return;
            nextSpecialTime = Time.time + (float)rules.PuppeteerSummonInterval;
            minions.RemoveAll(m => m == null || m.IsDead);
            if (minions.Count >= rules.PuppeteerMaxMinions || SummonRequested == null) return;
            var offset = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 1.8f;
            var minion = SummonRequested(this, transform.position + offset);
            if (minion != null) minions.Add(minion);
            Sfx.PlayAt(SfxClips.Screech, transform.position, 0.6f, 1.6f);
        }

        /// <summary>群れを呼ぶ人形：近づかれすぎたら、プレイヤーから離れる方へ逃げる。</summary>
        EnemyDecision KeepDistance(EnemyDecision decision)
        {
            if (decision.State != EnemyState.Chase || target == null) return decision;
            var away = Flat(transform.position - target.Body.position);
            if (away.magnitude >= rules.PuppeteerKeepDistance) return new EnemyDecision { State = decision.State, Attack = decision.Attack };
            var fleeTo = transform.position + away.normalized * 4f;
            decision.MoveTarget = ToN(fleeTo);
            return decision;
        }

        /// <summary>巨体が音へ突進している最中に壁へぶつかると、ひるむ（爆竹でおびき寄せて叩くための隙）。</summary>
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (kind != EnemyKind.Brute || dead || brain.State != EnemyState.Investigate) return;
            if (hit.rigidbody != null || Mathf.Abs(hit.normal.y) > 0.5f) return;
            if (new Vector2(body.velocity.x, body.velocity.z).magnitude < (float)rules.InvestigateSpeed * 0.6f) return;
            brain.OnHit(double.MaxValue);
            Sfx.PlayAt(SfxClips.HeavyThud, transform.position, 1f, 0.7f);
            NoiseEvents.Emit(transform.position, 12f);
        }

        /// <summary>気配のうなり（追うときは大きく高い）、見つけた瞬間の叫び、ひるんだときのうめき、足音。</summary>
        void UpdateSound(EnemyDecision decision)
        {
            var state = decision.State;
            if (state != lastState)
            {
                if (state == EnemyState.Chase && kind != EnemyKind.Crawler)
                    Sfx.PlayAt(SfxClips.Screech, transform.position, 0.9f, kind == EnemyKind.Brute ? 0.6f : Random.Range(0.9f, 1.05f));
                else if (state == EnemyState.Stunned) Sfx.PlayAt(SfxClips.Groan, transform.position, 0.8f, kind == EnemyKind.Brute ? 0.7f : 1f);
                lastState = state;
            }
            if (drone != null)
            {
                drone.volume = state == EnemyState.Chase ? 0.85f : state == EnemyState.Stunned ? 0.15f : 0.45f;
                drone.pitch = (state == EnemyState.Chase ? 1.25f : 1f) * (kind == EnemyKind.Brute ? 0.7f : 1f);
            }

            var v = body.velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (speed > 0.5f && Time.time >= nextStepTime)
            {
                nextStepTime = Time.time + Mathf.Lerp(0.7f, 0.32f, speed / (float)rules.ChaseSpeed) * (kind == EnemyKind.Crawler ? 0.5f : 1f);
                switch (kind)
                {
                    case EnemyKind.Crawler: Sfx.PlayAt(SfxClips.Knock, transform.position, 0.25f, Random.Range(1.6f, 2f)); break;
                    case EnemyKind.Brute: Sfx.PlayAt(SfxClips.HeavyThud, transform.position, 0.6f, Random.Range(0.6f, 0.7f)); break;
                    default: Sfx.PlayAt(SfxClips.Land, transform.position, 0.35f, Random.Range(0.55f, 0.7f)); break;
                }
            }
        }

        EnemyPerception Perceive()
        {
            if (target == null)
                return new EnemyPerception { Position = ToN(transform.position), Forward = ToN(Flat(transform.forward).normalized), PlayerDown = true,
                    HeardNoise = heardNoise.HasValue ? ToN(heardNoise.Value) : (NVector3?)null };
            var eye = target.Eye();
            var head = transform.position + Vector3.up * (headHeight - body.height / 2f);
            bool lineOfSight = !Physics.Linecast(head, eye, out var hit, ~0, QueryTriggerInteraction.Ignore)
                               || hit.collider == target.Collider;

            return new EnemyPerception
            {
                Position = ToN(transform.position),
                Forward = ToN(Flat(transform.forward).normalized),
                PlayerPosition = ToN(target.Body.position),
                HasLineOfSight = lineOfSight,
                PlayerDown = target.IsDown != null && target.IsDown(),
                HeardNoise = heardNoise.HasValue ? ToN(heardNoise.Value) : (NVector3?)null,
                LitByFlashlight = lineOfSight && IsLitByFlashlight(),
                Stealth = PlayerStealth,
            };
        }

        bool IsLitByFlashlight()
        {
            if (target.Flashlight == null || !target.Flashlight.IsOn || target.View == null) return false;
            var cam = target.View;
            var to = transform.position - cam.position;
            if (to.magnitude > rules.FlashlightNoticeRange) return false;
            return Vector3.Angle(cam.forward, to) <= target.Flashlight.SpotAngle / 2f;
        }

        void Move(EnemyDecision decision)
        {
            var velocity = Vector3.zero;
            if (Time.time < unstickUntil)
            {
                velocity = unstickDirection * (float)rules.WanderSpeed;
            }
            else if (decision.MoveTarget.HasValue)
            {
                var waypoint = NextWaypoint(ToUnity(decision.MoveTarget.Value));
                var to = Flat(waypoint - transform.position);
                if (to.magnitude > 0.05f) velocity = to.normalized * (float)decision.Speed;
            }

            if (velocity.sqrMagnitude > 0.001f)
            {
                var look = Quaternion.LookRotation(velocity.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TurnSpeed * Time.deltaTime);
            }
            else if (decision.State == EnemyState.Chase)
            {
                // 止まっていても、追っている相手の方を向く
                var face = target != null ? Flat(target.Body.position - transform.position) : Vector3.zero;
                if (face.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), TurnSpeed * Time.deltaTime);
            }
            body.SimpleMove(velocity);
            CheckStuck(decision.MoveTarget.HasValue && velocity.sqrMagnitude > 0.001f);
        }

        /// <summary>目的地が別の部屋なら、今の部屋から次の部屋へのドアを経由する。</summary>
        Vector3 NextWaypoint(Vector3 target)
        {
            if (facility == null) return target;
            var current = facility.RoomAt(transform.position);
            var goal = facility.RoomAt(target);
            if (current == null || goal == null || current == goal) return target;

            var path = facility.Layout.Path(current, goal);
            if (path.Count < 2) return target;
            var dir = FacilityLayout.DoorBetween(current, path[1]);
            if (!dir.HasValue) return target;

            // ドアの正面に回り込んでから抜ける（斜めに向かうと壁の角に引っかかる）
            var approach = facility.DoorApproachPoint(current, dir.Value);
            var pass = facility.DoorPassPoint(current, dir.Value);
            var axis = FacilityBuilder.DirectionVector(dir.Value);
            var lateral = Flat(transform.position - approach) - axis * Vector3.Dot(Flat(transform.position - approach), axis);
            return lateral.magnitude < 0.8f ? pass : approach;
        }

        /// <summary>しばらくほとんど動けていなければ、横へずれて抜け出す。</summary>
        void CheckStuck(bool tryingToMove)
        {
            if (Time.time < stuckCheckTime) return;
            bool stuck = tryingToMove && Flat(transform.position - stuckCheckPosition).magnitude < StuckDistance;
            stuckCheckPosition = transform.position;
            stuckCheckTime = Time.time + StuckCheckSeconds;
            if (!stuck) return;
            float side = rng.Next(2) == 0 ? -1f : 1f;
            unstickDirection = (transform.right * side - transform.forward * 0.5f).normalized;
            unstickUntil = Time.time + 0.8f;
            wanderTarget = null;
        }

        Vector3 PickWanderTarget()
        {
            if (facility == null || facility.Rooms.Count == 0)
                return transform.position + new Vector3((float)(rng.NextDouble() * 16 - 8), 0f, (float)(rng.NextDouble() * 16 - 8));
            var rooms = facility.Rooms;
            var room = rooms[rng.Next(rooms.Count)];
            // 入口にはあまり行かない
            if (room.Plan.Kind == RoomKind.Entrance && rng.NextDouble() < 0.7) room = rooms[rng.Next(rooms.Count)];
            var center = FacilityBuilder.RoomCenter(room.Plan);
            return center + new Vector3((float)(rng.NextDouble() * 14 - 7), 0f, (float)(rng.NextDouble() * 14 - 7));
        }

        void UpdateLook(EnemyState state)
        {
            bool stunned = state == EnemyState.Stunned;
            if (eyeLight != null)
            {
                eyeLight.enabled = !stunned;
                eyeLight.intensity = state == EnemyState.Chase ? 4f : 2f;
            }
            var tilt = stunned ? Quaternion.Euler(25f, 0f, 15f) : Quaternion.identity;
            visual.localRotation = Quaternion.RotateTowards(visual.localRotation, tilt, 200f * Time.deltaTime);

            // 被弾した直後だけ白く光らせる
            bool flash = Time.time < flashUntil;
            for (int i = 0; i < renderers.Length; i++)
                MaterialUtil.SetColor(renderers[i].sharedMaterial, flash ? Color.white : baseColors[i]);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static NVector3 ToN(Vector3 v) => new NVector3(v.x, v.y, v.z);
        static Vector3 ToUnity(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
