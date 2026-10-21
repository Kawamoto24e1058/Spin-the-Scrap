using FishNet.Object;
using FishNet.Object.Synchronizing;
using SpinTheScrap.Core;
using SpinTheScrap.Enemies;
using SpinTheScrap.Interaction;
using UnityEngine;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// ネットワークで配る敵（ホスト権威型）。ホストでは同じ物に Enemy（判断して動く本体）が付き、
    /// 種類・体力・ひるみを配る。位置は NetworkTransform（サーバー権威）。
    /// クライアントでは見た目と当たり判定だけを作り、当てた攻撃をホストへ送る。
    /// </summary>
    public sealed class NetEnemy : NetworkBehaviour, IEnemyTarget, IScrapHittable
    {
        readonly SyncVar<int> kind = new SyncVar<int>();
        readonly SyncVar<float> health = new SyncVar<float>();
        readonly SyncVar<float> maxHealth = new SyncVar<float>();
        readonly SyncVar<bool> stunned = new SyncVar<bool>();

        Enemy hostEnemy;
        Transform visual;
        bool registered;

        public EnemyKind Kind => (EnemyKind)kind.Value;
        public double CurrentHealth => health.Value;
        public double MaxHealth => maxHealth.Value;
        public bool IsDead => health.Value <= 0 && maxHealth.Value > 0;
        public Transform Transform => transform;

        /// <summary>ホスト：本体を結び付けて、種類を配る（ServerManager.Spawn の前に呼ぶ）。</summary>
        public void BindHost(Enemy enemy)
        {
            hostEnemy = enemy;
            kind.Value = (int)enemy.Kind;
            maxHealth.Value = (float)enemy.MaxHealth;
            health.Value = (float)enemy.CurrentHealth;
        }

        public override void OnStartClient()
        {
            if (IsServerStarted) return; // ホストは本体がある
            BuildProxy();
        }

        public override void OnStopClient()
        {
            if (registered) EnemyRegistry.All.Remove(this);
            registered = false;
        }

        void BuildProxy()
        {
            visual = Enemy.BuildBody(Kind, transform, out float height, out float radius, out _);
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.height = height;
            capsule.radius = radius;
            EnemyRegistry.All.Add(this);
            registered = true;
        }

        void Update()
        {
            if (IsServerStarted && hostEnemy != null)
            {
                health.Value = (float)hostEnemy.CurrentHealth;
                stunned.Value = hostEnemy.State == EnemyState.Stunned;
            }
            else if (visual != null)
            {
                var tilt = stunned.Value ? Quaternion.Euler(25f, 0f, 15f) : Quaternion.identity;
                visual.localRotation = Quaternion.RotateTowards(visual.localRotation, tilt, 200f * Time.deltaTime);
            }
        }

        /// <summary>クライアントで当てた攻撃をホストへ送る。</summary>
        public void ApplyHit(double damage, bool stun, SymbolTag weaponFamily, bool explosive)
        {
            if (hostEnemy != null)
            {
                hostEnemy.TakeDamage(damage, stun, weaponFamily, explosive);
                return;
            }
            if (NetPlayer.Local != null) NetPlayer.Local.SendHit(this, (float)damage, stun, (int)weaponFamily, explosive);
        }

        /// <summary>ホスト：クライアント（attacker）から届いた攻撃を本体に当てる。</summary>
        public void ReceiveHit(float damage, bool stun, int weaponFamily, bool explosive, int attacker)
        {
            if (hostEnemy == null) return;
            Enemy.CurrentAttacker = attacker;
            try { hostEnemy.TakeDamage(damage, stun, (SymbolTag)weaponFamily, explosive); }
            finally { Enemy.CurrentAttacker = -1; }
        }

        /// <summary>投げたガラクタが当たった。クライアントで投げた物（その人のマシンで動いている）はホストへ伝える。</summary>
        public void OnHitByScrap(Scrap scrap, float impactSpeed)
        {
            if (hostEnemy != null)
            {
                hostEnemy.OnHitByScrap(scrap, impactSpeed);
                return;
            }
            if (NetPlayer.Local != null) NetPlayer.Local.SendScrapHit(this, impactSpeed);
        }

        /// <summary>ホスト：クライアントが投げたガラクタが当たった。</summary>
        public void ReceiveScrapHit(float impactSpeed, int attacker)
        {
            if (hostEnemy == null) return;
            Enemy.CurrentAttacker = attacker;
            try { hostEnemy.OnHitByScrap(null, impactSpeed); }
            finally { Enemy.CurrentAttacker = -1; }
        }
    }
}
