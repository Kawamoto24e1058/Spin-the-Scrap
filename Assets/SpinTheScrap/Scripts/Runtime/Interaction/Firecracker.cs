using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Interaction
{
    /// <summary>
    /// 爆竹（道具屋の消耗品）。投げると少しして大きな音で鳴り、敵をその場所へおびき寄せる。
    /// 音の届く距離と鳴るまでの時間は EnemyRules（Core）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Firecracker : MonoBehaviour
    {
        const float ThrowSpeed = 11f;

        EnemyRules rules;
        float fuseEnd;
        bool exploded;
        Light flash;
        Renderer body;

        public static Firecracker Throw(Transform parent, Vector3 position, Vector3 direction, EnemyRules enemyRules)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Firecracker";
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.06f, 0.08f, 0.06f);
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(new Color(0.85f, 0.1f, 0.08f));
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = (direction.normalized + Vector3.up * 0.15f).normalized * ThrowSpeed;
            rb.angularVelocity = Random.insideUnitSphere * 10f;

            var fc = go.AddComponent<Firecracker>();
            fc.rules = enemyRules;
            fc.fuseEnd = Time.time + (float)enemyRules.FirecrackerFuseSeconds;
            fc.body = go.GetComponent<Renderer>();
            // 導火線の火花
            var spark = new GameObject("Spark").AddComponent<Light>();
            spark.transform.SetParent(go.transform, false);
            spark.type = LightType.Point;
            spark.color = new Color(1f, 0.6f, 0.2f);
            spark.range = 2.5f;
            spark.intensity = 1.5f;
            fc.flash = spark;
            Sfx.Play2D(SfxClips.Whoosh, 0.3f, 1.3f);
            return fc;
        }

        void Update()
        {
            if (exploded)
            {
                flash.intensity = Mathf.MoveTowards(flash.intensity, 0f, 60f * Time.deltaTime);
                return;
            }
            flash.intensity = 1f + Mathf.PerlinNoise(Time.time * 20f, 0f) * 1.5f;
            if (Time.time < fuseEnd) return;

            exploded = true;
            NoiseEvents.Emit(transform.position, (float)rules.FirecrackerNoiseRadius);
            Sfx.PlayAt(SfxClips.Bang, transform.position, 1f);
            flash.range = 8f;
            flash.intensity = 12f;
            body.enabled = false;
            Destroy(gameObject, 0.6f);
        }
    }
}
