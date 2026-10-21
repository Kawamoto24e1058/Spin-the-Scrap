using System;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>
    /// 次の階層へのエレベーター。攻略条件を満たすと明かりが赤から緑に変わり、見て E で下りる。
    /// </summary>
    public sealed class Elevator : MonoBehaviour, IInteractable
    {
        static readonly Color Locked = new Color(1f, 0.2f, 0.15f);
        static readonly Color Ready = new Color(0.3f, 1f, 0.4f);

        Func<bool> isReady;
        Func<StringTable, string> readyPrompt;
        Action onUse;
        Light lamp;
        Renderer lampRenderer;
        bool? shownReady;

        public bool IsReady => isReady != null && isReady();
        /// <summary>入口の外の呼び出しボタン。</summary>
        public Transform CallButton { get; private set; }

        /// <summary>position は床の中心、forward は扉の向き（プレイヤーが乗り込む側）。</summary>
        public static Elevator Create(Transform parent, Vector3 position, Vector3 forward, Func<bool> ready,
            Func<StringTable, string> readyPrompt, Action onUse)
        {
            var go = new GameObject("Elevator");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            var frame = new Color(0.4f, 0.42f, 0.45f);
            // 奥と左右の壁・天井だけで正面は開いている。中の壁か、入口の外の呼び出しボタンを見て E で乗る
            FacilityBuilder.Block(go.transform, "Back", new Vector3(0f, 1.5f, -1.2f), new Vector3(2.6f, 3f, 0.15f), frame);
            FacilityBuilder.Block(go.transform, "Left", new Vector3(-1.25f, 1.5f, 0f), new Vector3(0.15f, 3f, 2.4f), frame);
            FacilityBuilder.Block(go.transform, "Right", new Vector3(1.25f, 1.5f, 0f), new Vector3(0.15f, 3f, 2.4f), frame);
            FacilityBuilder.Block(go.transform, "Roof", new Vector3(0f, 3.05f, 0f), new Vector3(2.6f, 0.1f, 2.4f), frame);
            FacilityBuilder.Block(go.transform, "Floor", new Vector3(0f, 0.03f, 0f), new Vector3(2.4f, 0.06f, 2.3f), new Color(0.25f, 0.25f, 0.27f));
            FacilityBuilder.Block(go.transform, "Panel", new Vector3(0.9f, 1.3f, -1.1f), new Vector3(0.3f, 0.5f, 0.06f), new Color(0.15f, 0.15f, 0.17f));
            // 入口の外の呼び出しボタン（正面から見て E で使える）
            var button = FacilityBuilder.Block(go.transform, "Call button", new Vector3(1.55f, 1.25f, 1.15f), new Vector3(0.3f, 0.45f, 0.2f), new Color(0.2f, 0.2f, 0.22f));
            var lampBlock = FacilityBuilder.Block(go.transform, "Lamp", new Vector3(0f, 2.8f, 1.15f), new Vector3(0.6f, 0.15f, 0.1f), Locked);

            var lamp = new GameObject("Lamp light").AddComponent<Light>();
            lamp.transform.SetParent(go.transform, false);
            lamp.transform.localPosition = new Vector3(0f, 2.5f, 0.5f);
            lamp.type = LightType.Point;
            lamp.range = 5f;
            lamp.intensity = 3f;

            var elevator = go.AddComponent<Elevator>();
            elevator.isReady = ready;
            elevator.readyPrompt = readyPrompt;
            elevator.onUse = onUse;
            elevator.lamp = lamp;
            elevator.lampRenderer = lampBlock.GetComponent<Renderer>();
            elevator.CallButton = button;
            elevator.UpdateLamp();
            return elevator;
        }

        void Update() => UpdateLamp();

        void UpdateLamp()
        {
            bool ready = IsReady;
            if (shownReady == ready) return;
            shownReady = ready;
            var color = ready ? Ready : Locked;
            lamp.color = color;
            MaterialUtil.SetColor(lampRenderer.sharedMaterial, color);
        }

        public string Prompt(StringTable strings) => IsReady ? readyPrompt(strings) : strings.Get("hud.elevator_locked");

        public void Interact(Interactor who)
        {
            if (IsReady) onUse?.Invoke();
        }
    }
}
