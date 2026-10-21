using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.Enemies;
using SpinTheScrap.Interaction;
using SpinTheScrap.Session;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>
    /// 武器で戦う（docs/floor-design.md「戦闘」）。左クリックで攻撃し、押し続ければ武器の間隔で続けて攻撃する。
    /// ダメージ・会心・ひるみ・吸収・自傷の計算は CombatRules（Core）、ここは当たり判定と見た目・音だけ。
    /// 物を手に持っているときや台車を押しているときは手がふさがっていて攻撃できない。
    /// </summary>
    [RequireComponent(typeof(FirstPersonController), typeof(Interactor), typeof(PlayerHealth))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Tooltip("照準の先にいる敵を見つける距離 (m)")]
        [SerializeField] float targetRange = 40f;
        [Tooltip("連鎖が飛ぶ距離 (m)")]
        [SerializeField] float chainRange = 6f;
        [Tooltip("連鎖したときのダメージの割合")]
        [SerializeField, Range(0, 1)] float chainDamageRatio = 0.6f;
        [Tooltip("攻撃の音の半径 (m)。近接 / 遠距離")]
        [SerializeField] float meleeNoiseRadius = 4f;
        [SerializeField] float rangedNoiseRadius = 11f;
        [Tooltip("近接攻撃が当たる、正面の扇形の半分の角度（度）")]
        [SerializeField] float meleeHalfAngle = 75f;

        FirstPersonController controller;
        Interactor interactor;
        PlayerHealth health;
        RunSession session;
        readonly WeaponInstance fists = new WeaponInstance(CombatRules.Fists);
        readonly System.Random random = new System.Random();
        float nextAttackTime;
        LineRenderer tracer;
        float tracerUntil;
        Light muzzle;

        /// <summary>照準の先にいる敵（HUD で体力を出す）。</summary>
        public Enemy Target { get; private set; }
        /// <summary>直前に当てた時刻と、それが会心だったか（HUD の当たり表示）。</summary>
        public float LastHitTime { get; private set; } = -10f;
        public bool LastHitCritical { get; private set; }
        /// <summary>武器が壊れた・弾が切れた時刻（HUD の表示）。</summary>
        public float WeaponBrokeTime { get; private set; } = -10f;

        /// <summary>攻撃した（WeaponViewModel が振る・反動の動きを付ける）。</summary>
        public event System.Action<WeaponInstance> Attacked;

        public WeaponInstance CurrentWeapon => session != null ? session.Run.Player(session.LocalPlayerId).Weapon ?? fists : fists;
        bool HandsFull => interactor.Held != null || interactor.Pushed != null;

        public void Init(RunSession runSession) => session = runSession;

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            interactor = GetComponent<Interactor>();
            health = GetComponent<PlayerHealth>();

            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            tracer = go.AddComponent<LineRenderer>();
            tracer.positionCount = 2;
            tracer.startWidth = 0.03f;
            tracer.endWidth = 0.01f;
            tracer.sharedMaterial = MaterialUtil.Create(new Color(1f, 0.85f, 0.5f));
            tracer.enabled = false;
            tracer.useWorldSpace = true;
        }

        void Start()
        {
            muzzle = new GameObject("Muzzle flash").AddComponent<Light>();
            muzzle.transform.SetParent(controller.ViewCamera.transform, false);
            muzzle.transform.localPosition = new Vector3(0.2f, -0.15f, 0.6f);
            muzzle.type = LightType.Point;
            muzzle.range = 5f;
            muzzle.intensity = 0f;
            muzzle.color = new Color(1f, 0.8f, 0.5f);
        }

        void Update()
        {
            if (tracer.enabled && Time.time > tracerUntil) tracer.enabled = false;
            if (muzzle != null) muzzle.intensity = Mathf.MoveTowards(muzzle.intensity, 0f, 80f * Time.deltaTime);
            if (session == null || !controller.InputEnabled || health.IsDown)
            {
                Target = null;
                return;
            }
            UpdateTarget();
            if (GameInput.AttackHeld && !HandsFull && Time.time >= nextAttackTime) Attack();
        }

        void UpdateTarget()
        {
            var cam = controller.ViewCamera.transform;
            Target = null;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, targetRange, ~0, QueryTriggerInteraction.Ignore))
                Target = hit.collider.GetComponentInParent<Enemy>();
        }

        void Attack()
        {
            var weapon = CurrentWeapon;
            nextAttackTime = Time.time + (float)weapon.Interval;
            var stats = session.Run.StatsOf(session.LocalPlayerId);
            var cam = controller.ViewCamera.transform;
            bool melee = weapon.Definition.Kind == WeaponKind.Melee;
            Attacked?.Invoke(weapon);

            if (melee)
            {
                Sfx.Play2D(SfxClips.Whoosh, 0.35f, 1.4f);
                var enemy = FindMeleeTarget(cam, (float)weapon.Definition.Range);
                if (enemy != null) Hit(enemy, weapon, stats, enemy.transform.position);
                NoiseEvents.Emit(transform.position, meleeNoiseRadius);
            }
            else
            {
                FireRay(cam, weapon, stats);
                NoiseEvents.Emit(transform.position, rangedNoiseRadius);
                if (muzzle != null) muzzle.intensity = 6f;
            }

            if (weapon != fists && weapon.TryUse() && weapon.IsSpent)
            {
                session.Run.Player(session.LocalPlayerId).Weapon = null;
                WeaponBrokeTime = Time.time;
                Sfx.Play2D(SfxClips.Shatter, 0.5f, 0.6f);
            }
        }

        /// <summary>目の前の扇形（meleeHalfAngle）の中で、いちばん近い敵。</summary>
        Enemy FindMeleeTarget(Transform cam, float range)
        {
            var hits = Physics.OverlapSphere(cam.position, range + 0.4f, ~0, QueryTriggerInteraction.Ignore);
            Enemy best = null;
            float bestDistance = float.MaxValue;
            foreach (var c in hits)
            {
                var enemy = c.GetComponentInParent<Enemy>();
                if (enemy == null || enemy.IsDead) continue;
                var to = c.ClosestPoint(cam.position) - cam.position;
                if (to.magnitude > range + 0.1f || Vector3.Angle(cam.forward, to) > meleeHalfAngle && to.magnitude > 0.6f) continue;
                if (to.magnitude < bestDistance)
                {
                    bestDistance = to.magnitude;
                    best = enemy;
                }
            }
            return best;
        }

        void FireRay(Transform cam, WeaponInstance weapon, PlayerStats stats)
        {
            var def = weapon.Definition;
            float range = (float)def.Range;
            var hits = Physics.RaycastAll(cam.position, cam.forward, range, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider != controller.Controller && h.collider.attachedRigidbody == null || h.collider.GetComponentInParent<Enemy>() != null)
                .OrderBy(h => h.distance).ToList();

            var end = cam.position + cam.forward * range;
            var struck = new HashSet<Enemy>();
            foreach (var h in hits)
            {
                var enemy = h.collider.GetComponentInParent<Enemy>();
                end = h.point;
                if (enemy == null) break; // 壁や物に当たって止まる
                if (enemy.IsDead || !struck.Add(enemy)) continue;
                Hit(enemy, weapon, stats, h.point);
                if (!def.Pierce) break;
            }

            if (def.ExplosionRadius > 0) Explode(end, weapon, stats, struck);
            ShowTracer(cam.position + cam.right * 0.2f - cam.up * 0.15f, end, def.Family == SymbolTag.Electric ? new Color(0.5f, 0.8f, 1f) : new Color(1f, 0.85f, 0.5f));

            switch (def.Family)
            {
                case SymbolTag.Electric: Sfx.Play2D(SfxClips.Shatter, 0.3f, 2f); break;
                case SymbolTag.Toy: Sfx.Play2D(SfxClips.Whoosh, 0.5f, 0.7f); break;
                default: Sfx.Play2D(Sfx.Pick(SfxClips.Clinks), 0.35f, 1.6f); break;
            }
        }

        void Hit(Enemy enemy, WeaponInstance weapon, PlayerStats stats, Vector3 point)
        {
            var result = CombatRules.Hit(weapon, stats, random.NextDouble(), random.NextDouble());
            enemy.TakeDamage(result.Damage, result.Stun);
            if (result.Heal > 0) health.Heal(result.Heal);
            if (result.SelfDamage > 0) health.Damage(result.SelfDamage);
            LastHitTime = Time.time;
            LastHitCritical = result.Critical;
            Sfx.PlayAt(SfxClips.Knock, point, result.Critical ? 0.9f : 0.6f, result.Critical ? 0.8f : 1.1f);

            // 連鎖：近くの敵へ弱めのダメージ
            int chain = weapon.Definition.Chain;
            if (chain <= 0) return;
            var from = enemy.transform.position;
            var nearby = Physics.OverlapSphere(from, chainRange, ~0, QueryTriggerInteraction.Ignore)
                .Select(c => c.GetComponentInParent<Enemy>())
                .Where(e => e != null && e != enemy && !e.IsDead).Distinct()
                .OrderBy(e => (e.transform.position - from).sqrMagnitude).Take(chain).ToList();
            foreach (var other in nearby)
            {
                var link = CombatRules.Hit(weapon, stats, random.NextDouble(), random.NextDouble());
                other.TakeDamage(link.Damage * chainDamageRatio, link.Stun);
                ShowTracer(from, other.transform.position, new Color(0.5f, 0.8f, 1f));
                from = other.transform.position;
            }
        }

        void Explode(Vector3 point, WeaponInstance weapon, PlayerStats stats, HashSet<Enemy> alreadyHit)
        {
            float radius = (float)(weapon.Definition.ExplosionRadius * stats.ExplosionRadiusMultiplier);
            Sfx.PlayAt(SfxClips.Bang, point, 1f, 1.2f);
            NoiseEvents.Emit(point, radius * 4f);
            var victims = Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore)
                .Select(c => c.GetComponentInParent<Enemy>()).Where(e => e != null && !e.IsDead).Distinct().ToList();
            foreach (var enemy in victims)
            {
                if (alreadyHit.Contains(enemy)) continue;
                float falloff = 1f - Mathf.Clamp01(Vector3.Distance(point, enemy.transform.position) / radius) * 0.5f;
                var result = CombatRules.Hit(weapon, stats, random.NextDouble(), random.NextDouble());
                enemy.TakeDamage(result.Damage * falloff, result.Stun);
            }
            var flash = new GameObject("Explosion light").AddComponent<Light>();
            flash.transform.position = point;
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.6f, 0.25f);
            flash.range = radius * 3f;
            flash.intensity = 10f;
            Destroy(flash.gameObject, 0.15f);
        }

        void ShowTracer(Vector3 from, Vector3 to, Color color)
        {
            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            MaterialUtil.SetColor(tracer.sharedMaterial, color);
            tracer.enabled = true;
            tracerUntil = Time.time + 0.06f;
        }
    }
}
