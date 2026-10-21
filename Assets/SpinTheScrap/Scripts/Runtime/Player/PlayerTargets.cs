using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>敵が狙えるプレイヤー 1 人分（このマシンのプレイヤーか、協力プレイのほかの人の分身）。</summary>
    public sealed class PlayerTarget
    {
        /// <summary>足元（体の中心）の位置。</summary>
        public Transform Body;
        /// <summary>目の高さ（視線の判定）。</summary>
        public Func<Vector3> Eye;
        /// <summary>視線を通すときに無視するコライダー（自分の体）。</summary>
        public Collider Collider;
        public Func<bool> IsDown;
        public Action<double> Damage;
        /// <summary>懐中電灯を点けているか・向き（このマシンのプレイヤーだけ）。</summary>
        public Flashlight Flashlight;
        public Transform View;
    }

    /// <summary>敵が狙えるプレイヤーの一覧。ホスト（またはひとり）の上で敵が近い人を選ぶ。</summary>
    public static class PlayerTargets
    {
        public static readonly List<PlayerTarget> All = new List<PlayerTarget>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        /// <summary>倒れていない中でいちばん近い人。いなければ null。</summary>
        public static PlayerTarget Nearest(Vector3 from)
        {
            PlayerTarget best = null;
            float bestDistance = float.MaxValue;
            foreach (var t in All)
            {
                if (t.Body == null || (t.IsDown != null && t.IsDown())) continue;
                float d = (t.Body.position - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }
            return best;
        }
    }
}
