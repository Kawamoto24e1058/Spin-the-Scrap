using System.Collections.Generic;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Enemies
{
    /// <summary>
    /// 攻撃できる敵。ひとり・ホストでは Enemy（判断して動く本体）、協力プレイのクライアントでは NetEnemy（ホストの敵の写し）。
    /// クライアントで当てた攻撃は NetEnemy がホストへ送り、ホストの Enemy が受ける。
    /// </summary>
    public interface IEnemyTarget
    {
        EnemyKind Kind { get; }
        double CurrentHealth { get; }
        double MaxHealth { get; }
        bool IsDead { get; }
        Transform Transform { get; }
        void ApplyHit(double damage, bool stun, SymbolTag weaponFamily, bool explosive);
    }

    /// <summary>生きている敵の一覧（HUD の紙の強化の位置表示、照準の対象探し）。</summary>
    public static class EnemyRegistry
    {
        public static readonly List<IEnemyTarget> All = new List<IEnemyTarget>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        /// <summary>当たったコライダーの敵（同じ物にホストの本体と写しの両方があれば本体）。</summary>
        public static IEnemyTarget FromCollider(Component c)
        {
            if (c == null) return null;
            var enemy = c.GetComponentInParent<Enemy>();
            if (enemy != null && enemy.enabled) return enemy;
            return c.GetComponentInParent<IEnemyTarget>();
        }
    }
}
