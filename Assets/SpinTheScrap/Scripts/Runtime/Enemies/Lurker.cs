using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using SpinTheScrap.Player;
using SpinTheScrap.World;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace SpinTheScrap.Enemies
{
    /// <summary>
    /// 徘徊者（敵 1 種類目）。判断は EnemyBrain（Core）が行い、ここは知覚・移動・見た目だけを受け持つ。
    /// 部屋をまたぐときは間取り（FacilityLayout.Path）に沿ってドアを順に抜ける。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class Lurker : MonoBehaviour, IScrapHittable
    {
        const float HeadHeight = 1.7f;
        const float TurnSpeed = 360f;
        const float StuckCheckSeconds = 1.5f;
        const float StuckDistance = 0.3f;

        EnemyRules rules;
        EnemyBrain brain;
        BuiltFacility facility;
        FirstPersonController player;
        PlayerHealth playerHealth;
        Flashlight flashlight;
        Rng rng;
        CharacterController body;
        Transform visual;
        Light eyeLight;

        Vector3? heardNoise;
        float heardNoiseDistance;
        Vector3? wanderTarget;
        Vector3 stuckCheckPosition;
        float stuckCheckTime;
        float unstickUntil;
        Vector3 unstickDirection;

        public EnemyState State => brain != null ? brain.State : EnemyState.Wander;

        /// <summary>見た目（仮素材）と当たり判定を組み立てて置く。</summary>
        public static Lurker Spawn(Transform parent, Vector3 position, EnemyRules enemyRules, BuiltFacility builtFacility,
            FirstPersonController fpc, PlayerHealth health, Flashlight light, ulong seed)
        {
            var go = new GameObject("Lurker");
            go.transform.SetParent(parent, true);
            go.transform.position = position + Vector3.up * 1.05f;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 2.1f;
            cc.radius = 0.35f;
            cc.stepOffset = 0.3f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            Part(visual, PrimitiveType.Capsule, new Vector3(0f, -0.1f, 0f), new Vector3(0.55f, 0.95f, 0.45f), new Color(0.06f, 0.06f, 0.07f));
            Part(visual, PrimitiveType.Sphere, new Vector3(0f, 0.85f, 0f), new Vector3(0.36f, 0.42f, 0.36f), new Color(0.85f, 0.83f, 0.78f));
            Part(visual, PrimitiveType.Sphere, new Vector3(-0.08f, 0.9f, 0.16f), Vector3.one * 0.06f, new Color(1f, 0.1f, 0.05f));
            Part(visual, PrimitiveType.Sphere, new Vector3(0.08f, 0.9f, 0.16f), Vector3.one * 0.06f, new Color(1f, 0.1f, 0.05f));

            var eye = new GameObject("Eye light").AddComponent<Light>();
            eye.transform.SetParent(visual, false);
            eye.transform.localPosition = new Vector3(0f, 0.9f, 0.3f);
            eye.type = LightType.Point;
            eye.color = new Color(1f, 0.15f, 0.1f);
            eye.range = 3f;
            eye.intensity = 2f;

            var lurker = go.AddComponent<Lurker>();
            lurker.rules = enemyRules;
            lurker.brain = new EnemyBrain(enemyRules);
            lurker.facility = builtFacility;
            lurker.player = fpc;
            lurker.playerHealth = health;
            lurker.flashlight = light;
            lurker.rng = new Rng(seed);
            lurker.visual = visual;
            lurker.eyeLight = eye;
            return lurker;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(color);
            Destroy(go.GetComponent<Collider>()); // 当たり判定は CharacterController だけ
        }

        void Awake()
        {
            body = GetComponent<CharacterController>();
            NoiseEvents.Emitted += OnNoise;
        }

        void OnDestroy() => NoiseEvents.Emitted -= OnNoise;

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
            if (brain != null) brain.OnHit(impactSpeed);
        }

        void Update()
        {
            if (brain == null || Time.deltaTime <= 0f) return; // 拠点メニューで時間が止まっている間は何もしない
            var perception = Perceive();
            heardNoise = null;

            if (wanderTarget == null || Flat(wanderTarget.Value - transform.position).magnitude < 1f) wanderTarget = PickWanderTarget();
            var decision = brain.Tick(Time.deltaTime, perception, ToN(wanderTarget.Value));

            if (decision.Attack && playerHealth != null) playerHealth.Damage(rules.AttackDamage);
            Move(decision);
            UpdateLook(decision.State);
        }

        EnemyPerception Perceive()
        {
            var cam = player.ViewCamera.transform;
            var head = transform.position + Vector3.up * (HeadHeight - body.height / 2f);
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
            eyeLight.enabled = !stunned;
            eyeLight.intensity = state == EnemyState.Chase ? 4f : 2f;
            var tilt = stunned ? Quaternion.Euler(25f, 0f, 15f) : Quaternion.identity;
            visual.localRotation = Quaternion.RotateTowards(visual.localRotation, tilt, 200f * Time.deltaTime);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static NVector3 ToN(Vector3 v) => new NVector3(v.x, v.y, v.z);
        static Vector3 ToUnity(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
