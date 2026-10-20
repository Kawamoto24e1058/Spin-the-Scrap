using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>懐中電灯（要件書 4.2）。電池消費は道具システムと一緒に入れる。</summary>
    public sealed class Flashlight : MonoBehaviour
    {
        [SerializeField] float range = 18f;
        [SerializeField] float spotAngle = 55f;
        [Tooltip("URP は距離の 2 乗で減衰する。10m 先の壁がうっすら見える程度")]
        [SerializeField] float intensity = 14f;

        Light spot;

        public bool IsOn => spot != null && spot.enabled;
        /// <summary>光の広がり（円錐の全角、度）。敵が「照らされた」と判断するのに使う。</summary>
        public float SpotAngle => spotAngle;

        void Awake()
        {
            var go = new GameObject("Flashlight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0.2f, -0.15f, 0f);
            spot = go.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = range;
            spot.spotAngle = spotAngle;
            spot.intensity = intensity;
            spot.color = new Color(1f, 0.95f, 0.85f);
            spot.shadows = LightShadows.Soft;
        }

        public void Toggle() => spot.enabled = !spot.enabled;
        public void SetOn(bool on) => spot.enabled = on;
    }
}
