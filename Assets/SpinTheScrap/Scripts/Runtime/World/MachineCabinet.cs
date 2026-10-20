using System;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>
    /// 施設に置くスロット台（docs/floor-design.md「スロット台」）。見て E で、持っている物と台の前に置いた物を入れてメニューを開く。
    /// 台の前の床（Hopper）に置いた大きい物・台車に載せた物も一緒に入る。
    /// </summary>
    public sealed class MachineCabinet : MonoBehaviour, IInteractable
    {
        static readonly Color Body = new Color(0.18f, 0.18f, 0.2f);

        Action<MachineCabinet, Interactor> onUse;

        public MachineKind Kind { get; private set; }
        /// <summary>台の前の、物を置いて入れる範囲。</summary>
        public ExtractionZone Hopper { get; private set; }

        public static Color ColorOf(MachineKind kind)
        {
            switch (kind)
            {
                case MachineKind.Weapon: return new Color(0.9f, 0.3f, 0.2f);
                case MachineKind.Perk: return new Color(0.3f, 0.75f, 1f);
                case MachineKind.Mod: return new Color(0.75f, 0.4f, 1f);
                default: return new Color(0.45f, 0.9f, 0.4f);
            }
        }

        /// <summary>position は台の足元、forward は台の正面（プレイヤーが立つ側）。</summary>
        public static MachineCabinet Create(Transform parent, Vector3 position, Vector3 forward, MachineKind kind, Action<MachineCabinet, Interactor> onUse)
        {
            var go = new GameObject("Slot machine " + kind);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            var color = ColorOf(kind);

            FacilityBuilder.Block(go.transform, "Cabinet", new Vector3(0f, 0.95f, 0f), new Vector3(1.1f, 1.9f, 0.8f), Body);
            FacilityBuilder.Block(go.transform, "Screen", new Vector3(0f, 1.35f, 0.41f), new Vector3(0.85f, 0.55f, 0.02f), color * 0.9f);
            FacilityBuilder.Block(go.transform, "Marquee", new Vector3(0f, 1.95f, 0.15f), new Vector3(1.15f, 0.25f, 0.5f), color);
            FacilityBuilder.Block(go.transform, "Tray", new Vector3(0f, 0.6f, 0.5f), new Vector3(0.8f, 0.1f, 0.25f), Body * 1.5f);
            var lever = FacilityBuilder.Block(go.transform, "Lever", new Vector3(0.65f, 1.2f, 0.1f), new Vector3(0.06f, 0.5f, 0.06f), new Color(0.7f, 0.7f, 0.7f));
            lever.localRotation = Quaternion.Euler(-15f, 0f, 0f);
            FacilityBuilder.Block(go.transform, "Knob", new Vector3(0.65f, 1.47f, 0.17f), Vector3.one * 0.12f, new Color(0.9f, 0.15f, 0.1f));

            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.5f, 1f);
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = 4f;
            glow.intensity = 2.5f;

            // 台の前の床：ここに置いた物も一緒に入る
            var hopperGo = new GameObject("Hopper");
            hopperGo.transform.SetParent(go.transform, false);
            hopperGo.transform.localPosition = new Vector3(0f, 1f, 1.5f);
            var box = hopperGo.AddComponent<BoxCollider>();
            box.size = new Vector3(2.4f, 2f, 1.8f);
            var hopper = hopperGo.AddComponent<ExtractionZone>();
            var mark = FacilityBuilder.Block(go.transform, "Hopper mark", new Vector3(0f, 0.012f, 1.5f), new Vector3(2.4f, 0.02f, 1.8f), color * 0.35f);
            DestroyImmediate(mark.GetComponent<Collider>());

            var cabinet = go.AddComponent<MachineCabinet>();
            cabinet.Kind = kind;
            cabinet.Hopper = hopper;
            cabinet.onUse = onUse;
            return cabinet;
        }

        public string Prompt(StringTable strings) => strings.Format("hud.use_machine", strings.Get("machine." + Kind));

        public void Interact(Interactor who) => onUse?.Invoke(this, who);
    }
}
