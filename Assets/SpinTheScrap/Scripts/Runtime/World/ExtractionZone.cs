using System.Collections.Generic;
using SpinTheScrap.Interaction;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>
    /// 脱出地点。範囲内に置いたガラクタと、中に立っているプレイヤーの持ち物を運び出せる。
    /// 範囲は BoxCollider（トリガー）で決め、判定は OverlapBox で都度行う
    /// （インベントリに入れて非アクティブになった物でも取りこぼさないため）。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ExtractionZone : MonoBehaviour
    {
        BoxCollider box;
        readonly Collider[] overlapBuffer = new Collider[128];

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        public bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition) - box.center;
            var half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        public List<Scrap> ScrapsInside()
        {
            var center = transform.TransformPoint(box.center);
            var half = Vector3.Scale(box.size * 0.5f, transform.lossyScale);
            int count = Physics.OverlapBoxNonAlloc(center, half, overlapBuffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

            var result = new List<Scrap>();
            for (int i = 0; i < count; i++)
            {
                var body = overlapBuffer[i].attachedRigidbody;
                if (body == null) continue;
                var scrap = body.GetComponent<Scrap>();
                if (scrap != null && !result.Contains(scrap)) result.Add(scrap);
            }
            return result;
        }
    }
}
