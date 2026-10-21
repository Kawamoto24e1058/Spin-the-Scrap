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
    public sealed class Enemy : MonoBehaviour, IScrapHittable
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
        FirstPersonController player;
        PlayerHealth playerHealth;
        Flashlight flashlight;
        Rng rng;
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

        public EnemyKind Kind => kind;
        public EnemyState State => brain != null ? brain.State : EnemyState.Wander;
        public HealthState Health => health;
        public bool IsDead => dead;

        /// <summary>どれかの敵が倒された（攻略条件・ガラクタを落とす・倒したときの回復に使う）。</summary>
        public static event Action<Enemy> AnyDied;
        /// <summary>生きている敵（HUD で紙の強化の位置表示に使う）。</summary>
        public static readonly System.Collections.Generic.List<Enemy> All = new System.Collections.Generic.List<Enemy>();
        /// <summary>プレイヤーの見つかりにくさ（人形の強化）。Phase0Bootstrap が強化から設定する。</summary>
        public static double PlayerStealth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AnyDied = null;
            All.Clear();
            PlayerStealth = 0;
        }

        /// <summary>見た目（仮素材）と当たり判定を組み立てて置く。</summary>
        public static Enemy Spawn(EnemyKind kind, int floor, Transform parent, Vector3 position, EnemyRules enemyRules,
            BuiltFacility builtFacility, FirstPersonController fpc, PlayerHealth health, Flashlight light, ulong seed)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, true);
            var cc = go.AddComponent<CharacterController>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            float head;
            switch (kind)
            {
                case EnemyKind.Crawler:
                    cc.height = 0.9f;
                    cc.radius = 0.4f;
                    head = 0.6f;
                    // 低く這う、赤黒い塊
                    Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.1f, 0f), new Vector3(0.7f, 0.45f, 1.1f), new Color(0.25f, 0.05f, 0.05f), Quaternion.Euler(90f, 0f, 0f));
                    Part(visual, PrimitiveType.Sphere, new Vector3(0f, 0.05f, 0.5f), new Vector3(0.4f, 0.32f, 0.4f), new Color(0.35f, 0.1f, 0.08f));
                    Eyes(visual, new Vector3(0f, 0.12f, 0.68f), 0.08f, 0.05f);
                    break;
                case EnemyKind.Brute:
                    cc.height = 2.9f;
                    cc.radius = 0.75f;
                    head = 2.6f;
                    // 灰色の巨体。目はない
                    Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.1f, 0f), new Vector3(1.4f, 1.35f, 1.1f), new Color(0.32f, 0.31f, 0.3f));
                    Part(visual, PrimitiveType.Sphere, new Vector3(0f, 1.25f, 0.15f), new Vector3(0.7f, 0.6f, 0.7f), new Color(0.45f, 0.42f, 0.4f));
                    Part(visual, PrimitiveType.Cube, new Vector3(-0.85f, 0.2f, 0.2f), new Vector3(0.35f, 1.4f, 0.35f), new Color(0.3f, 0.29f, 0.28f));
                    Part(visual, PrimitiveType.Cube, new Vector3(0.85f, 0.2f, 0.2f), new Vector3(0.35f, 1.4f, 0.35f), new Color(0.3f, 0.29f, 0.28f));
                    break;
                default:
                    cc.height = 2.1f;
                    cc.radius = 0.35f;
                    head = 1.7f;
                    Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.1f, 0f), new Vector3(0.55f, 0.95f, 0.45f), new Color(0.06f, 0.06f, 0.07f));
                    Part(visual, PrimitiveType.Sphere, new Vector3(0f, 0.85f, 0f), new Vector3(0.36f, 0.42f, 0.36f), new Color(0.85f, 0.83f, 0.78f));
                    Eyes(visual, new Vector3(0f, 0.9f, 0.16f), 0.08f, 0.06f);
                    break;
            }
            cc.stepOffset = Mathf.Min(0.3f, cc.height * 0.3f);
            go.transform.position = position + Vector3.up * (cc.height / 2f + 0.05f);

            Light eye = null;
            if (kind != EnemyKind.Brute)
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
            enemy.player = fpc;
            enemy.playerHealth = health;
            enemy.flashlight = light;
            enemy.rng = new Rng(seed);
            enemy.visual = visual;
            enemy.eyeLight = eye;
            enemy.headHeight = head;
            enemy.renderers = visual.GetComponentsInChildren<Renderer>();
            enemy.baseColors = new Color[enemy.renderers.Length];
            for (int i = 0; i < enemy.renderers.Length; i++) enemy.baseColors[i] = MaterialUtil.GetColor(enemy.renderers[i].sharedMaterial);
            if (kind != EnemyKind.Crawler)
                enemy.drone = Sfx.CreateLoop(go, SfxClips.DroneLoop, 0.5f, kind == EnemyKind.Brute ? 30f : 18f);
            return enemy;
        }

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
            All.Add(this);
        }

        void OnDestroy()
        {
            NoiseEvents.Emitted -= OnNoise;
            All.Remove(this);
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

        /// <summary>武器などでダメージを受ける。stun が true ならひるむ。倒れたら true。</summary>
        public bool TakeDamage(double amount, bool stun)
        {
            if (dead || health == null) return false;
            flashUntil = Time.time + FlashSeconds;
            if (stun) brain.OnHit(double.MaxValue);
            if (!health.Damage(amount)) return false;
            Die();
            return true;
        }

        void Die()
        {
            dead = true;
            All.Remove(this);
            Sfx.PlayAt(SfxClips.Groan, transform.position, 0.9f, kind == EnemyKind.Crawler ? 1.6f : kind == EnemyKind.Brute ? 0.6f : 1f);
            AnyDied?.Invoke(this);
            Destroy(gameObject);
        }

        void Update()
        {
            if (brain == null || dead || Time.deltaTime <= 0f) return; // 拠点メニューで時間が止まっている間は何もしない
            var perception = Perceive();
            heardNoise = null;

            if (wanderTarget == null || Flat(wanderTarget.Value - transform.position).magnitude < 1f) wanderTarget = PickWanderTarget();
            var decision = brain.Tick(Time.deltaTime, perception, ToN(wanderTarget.Value));

            if (decision.Attack && playerHealth != null) playerHealth.Damage(rules.AttackDamage);
            Move(decision);
            UpdateLook(decision.State);
            UpdateSound(decision);
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
            var cam = player.ViewCamera.transform;
            var head = transform.position + Vector3.up * (headHeight - body.height / 2f);
            bool lineOfSight = !Physics.Linecast(head, cam.position, out var hit, ~0, QueryTriggerInteraction.Ignore)
                               || hit.collider == player.Controller;

            return new EnemyPerception
            {
                Position = ToN(transform.position),
                Forward = ToN(Flat(transform.forward).normalized),
                PlayerPosition = ToN(player.transform.position),
                HasLineOfSight = lineOfSight,
                PlayerDown = playerHealth != null && playerHealth.IsDown,
                HeardNoise = heardNoise.HasValue ? ToN(heardNoise.Value) : (NVector3?)null,
                LitByFlashlight = lineOfSight && IsLitByFlashlight(cam),
                Stealth = PlayerStealth,
            };
        }

        bool IsLitByFlashlight(Transform cam)
        {
            if (flashlight == null || !flashlight.IsOn) return false;
            var to = transform.position - cam.position;
            if (to.magnitude > rules.FlashlightNoticeRange) return false;
            return Vector3.Angle(cam.forward, to) <= flashlight.SpotAngle / 2f;
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
                var face = Flat(player.transform.position - transform.position);
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
