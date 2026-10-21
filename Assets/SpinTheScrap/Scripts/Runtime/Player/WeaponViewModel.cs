using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>
    /// 手に持っている武器の見た目と動き（仮素材のプリミティブ）。武器が変わると作り直す。
    /// 近接は振る、遠距離は反動、歩くと揺れる。物を持っているときや台車を押しているときは下げる。
    /// 当たり判定やダメージは PlayerCombat、ここは見た目だけ。
    /// </summary>
    [RequireComponent(typeof(PlayerCombat), typeof(FirstPersonController), typeof(Interactor))]
    public sealed class WeaponViewModel : MonoBehaviour
    {
        static readonly Vector3 RestPosition = new Vector3(0.28f, -0.3f, 0.5f);

        PlayerCombat combat;
        FirstPersonController controller;
        Interactor interactor;
        Transform pivot;
        Transform model;
        string modelId;
        bool melee;
        float swingStart = -10f;
        float swingDuration = 0.3f;
        float recoil;
        float lowered;
        float bobPhase;

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            controller = GetComponent<FirstPersonController>();
            interactor = GetComponent<Interactor>();
            combat.Attacked += OnAttacked;
        }

        void OnDestroy()
        {
            if (combat != null) combat.Attacked -= OnAttacked;
        }

        void Start()
        {
            pivot = new GameObject("Weapon pivot").transform;
            pivot.SetParent(controller.ViewCamera.transform, false);
            pivot.localPosition = RestPosition;
        }

        void OnAttacked(WeaponInstance weapon)
        {
            if (weapon.Definition.Kind == WeaponKind.Melee)
            {
                swingStart = Time.time;
                swingDuration = Mathf.Clamp((float)weapon.Interval * 0.8f, 0.18f, 0.4f);
            }
            else
            {
                recoil = 1f;
            }
        }

        void LateUpdate()
        {
            if (pivot == null) return;
            var weapon = combat.CurrentWeapon;
            if (weapon.Definition.Id != modelId) Rebuild(weapon.Definition);

            // 物を持っている・台車を押している・メニューを開いている間は下げる
            bool handsBusy = interactor.Held != null || interactor.Pushed != null || !controller.InputEnabled;
            lowered = Mathf.MoveTowards(lowered, handsBusy ? 1f : 0f, Time.deltaTime * 4f);

            // 歩くと揺れる
            var v = controller.Controller.velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (controller.Controller.isGrounded) bobPhase += Time.deltaTime * Mathf.Lerp(0f, 10f, Mathf.Clamp01(speed / 4f));
            float bob = Mathf.Clamp01(speed / 4f);
            var bobOffset = new Vector3(Mathf.Cos(bobPhase * 0.5f) * 0.012f, Mathf.Sin(bobPhase) * 0.012f, 0f) * bob;

            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 8f);
            var position = RestPosition + bobOffset + new Vector3(0f, -0.45f * lowered, -0.07f * recoil);
            var rotation = Quaternion.Euler(-12f * recoil + 25f * lowered, 0f, 0f);

            // 近接：右上から左下へ振り抜く
            float t = (Time.time - swingStart) / swingDuration;
            if (melee && t >= 0f && t <= 1f)
            {
                float s = Mathf.Sin(t * Mathf.PI);
                float sweep = Mathf.Lerp(1f, -1f, t);
                rotation *= Quaternion.Euler(35f * s, 55f * sweep * s, -40f * s);
                position += new Vector3(-0.12f * s, 0.05f * s, 0.12f * s);
            }

            pivot.localPosition = position;
            pivot.localRotation = rotation;
        }

        void Rebuild(WeaponDefinition def)
        {
            if (model != null) Destroy(model.gameObject);
            modelId = def.Id;
            melee = def.Kind == WeaponKind.Melee;
            model = new GameObject("Weapon " + def.Id).transform;
            model.SetParent(pivot, false);

            var metal = new Color(0.55f, 0.56f, 0.58f);
            var dark = new Color(0.12f, 0.12f, 0.13f);
            switch (def.Id)
            {
                case "fists":
                    Part(PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.09f, 0.08f, 0.11f), new Color(0.8f, 0.65f, 0.55f));
                    break;
                case "pipe":
                    Part(PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0.05f), new Vector3(0.035f, 0.28f, 0.035f), metal, Quaternion.Euler(20f, 0f, 0f));
                    break;
                case "taser":
                    Part(PrimitiveType.Cube, new Vector3(0f, 0f, 0.04f), new Vector3(0.06f, 0.05f, 0.18f), new Color(0.85f, 0.75f, 0.15f));
                    Part(PrimitiveType.Cube, new Vector3(-0.015f, 0f, 0.15f), new Vector3(0.008f, 0.008f, 0.05f), metal);
                    Part(PrimitiveType.Cube, new Vector3(0.015f, 0f, 0.15f), new Vector3(0.008f, 0.008f, 0.05f), metal);
                    break;
                case "nail_gun":
                case "rivet_cannon":
                    float size = def.Id == "rivet_cannon" ? 1.4f : 1f;
                    var body = def.Id == "rivet_cannon" ? new Color(0.3f, 0.32f, 0.35f) : new Color(0.9f, 0.45f, 0.1f);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.05f), new Vector3(0.08f, 0.1f, 0.26f) * size, body);
                    Part(PrimitiveType.Cube, new Vector3(0f, -0.08f, -0.03f), new Vector3(0.05f, 0.12f, 0.06f) * size, dark);
                    Part(PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.2f * size), new Vector3(0.03f, 0.05f, 0.03f) * size, metal, Quaternion.Euler(90f, 0f, 0f));
                    Part(PrimitiveType.Cube, new Vector3(0f, -0.02f, 0.08f), new Vector3(0.03f, 0.06f, 0.1f) * size, dark);
                    break;
                case "arc_gun":
                case "arc_gun_mk2":
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.01f, 0.04f), new Vector3(0.09f, 0.09f, 0.22f), new Color(0.2f, 0.3f, 0.45f));
                    Part(PrimitiveType.Cube, new Vector3(0f, -0.08f, -0.03f), new Vector3(0.05f, 0.11f, 0.06f), dark);
                    int coils = def.Id == "arc_gun_mk2" ? 3 : 2;
                    for (int i = 0; i < coils; i++)
                        Part(PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0.16f + i * 0.04f), new Vector3(0.07f, 0.01f, 0.07f), new Color(0.5f, 0.85f, 1f), Quaternion.Euler(90f, 0f, 0f));
                    break;
                case "firework_tube":
                case "firework_tube_mk2":
                    float width = def.Id == "firework_tube_mk2" ? 0.11f : 0.085f;
                    Part(PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.08f), new Vector3(width, 0.2f, width), new Color(0.85f, 0.15f, 0.12f), Quaternion.Euler(90f, 0f, 0f));
                    Part(PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.27f), new Vector3(width * 1.05f, 0.015f, width * 1.05f), new Color(0.95f, 0.85f, 0.2f), Quaternion.Euler(90f, 0f, 0f));
                    Part(PrimitiveType.Cube, new Vector3(0f, -0.06f, 0.02f), new Vector3(0.04f, 0.09f, 0.05f), dark);
                    break;
                default:
                    // 呪いの包丁：柄と幅のある刃。強いほど長い
                    float length = def.Tier >= 3 ? 0.45f : def.Tier == 2 ? 0.38f : 0.32f;
                    Part(PrimitiveType.Cube, new Vector3(0f, -0.02f, -0.04f), new Vector3(0.03f, 0.03f, 0.12f), new Color(0.25f, 0.12f, 0.08f));
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.03f + length / 2f), new Vector3(0.012f, 0.1f, length), new Color(0.45f, 0.08f, 0.08f));
                    break;
            }
        }

        void Part(PrimitiveType type, Vector3 localPosition, Vector3 scale, Color color, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "Part";
            go.transform.SetParent(model, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            if (rotation.HasValue) go.transform.localRotation = rotation.Value;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialUtil.Create(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 手元の影が床に落ちないように
            DestroyImmediate(go.GetComponent<Collider>());
        }
    }
}
