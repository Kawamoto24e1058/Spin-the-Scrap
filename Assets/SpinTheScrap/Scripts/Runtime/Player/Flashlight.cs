using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>懐中電灯（要件書 4.2）。電池消費は道具システムと一緒に入れる。</summary>
    public sealed class Flashlight : MonoBehaviour
    {
        [SerializeField] float range = 18f;
        [SerializeField] float spotAngle = 55f;
        [SerializeField] float intensity = 3f;

        Light spot;

        public bool IsOn => spot != null && spot.enabled;

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
    }
}
