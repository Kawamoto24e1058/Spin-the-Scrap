using System;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>
    /// 敵に聞こえる音の通知（要件書 4.2「音に反応する敵」）。音の大きさは EnemyRules（Core）で決める。
    /// 音を出す側（足音・ガラクタの衝突）はここに知らせるだけで、敵の存在を知らない。
    /// </summary>
    public static class NoiseEvents
    {
        /// <summary>音が鳴った。位置と、聞こえる半径 (m)。</summary>
        public static event Action<Vector3, float> Emitted;

        /// <summary>音の大きさの決め方。Phase0Bootstrap が RunSession の値を入れる。</summary>
        public static EnemyRules Rules { get; set; }

        public static void Emit(Vector3 position, float radius)
        {
            if (radius > 0f) Emitted?.Invoke(position, radius);
        }

        /// <summary>ガラクタが何かにぶつかった。速さに応じた音を出す。</summary>
        public static void Impact(Vector3 position, float speed)
        {
            if (Rules != null) Emit(position, (float)Rules.ImpactNoiseRadius(speed));
        }

        /// <summary>割れ物が割れた。</summary>
        public static void Break(Vector3 position)
        {
            if (Rules != null) Emit(position, (float)Rules.BreakNoiseRadius);
        }

        // Enter Play Mode Options でドメインのリロードを切っても、前の Play の購読が残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Emitted = null;
            Rules = null;
        }
    }
}
