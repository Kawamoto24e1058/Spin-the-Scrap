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

        /// <summary>見た目と動き（リール・電飾・レバー）。</summary>
        public SlotCabinetView View { get; private set; }

        /// <summary>position は台の足元、forward は台の正面（プレイヤーが立つ側）。</summary>
        public static MachineCabinet Create(Transform parent, Vector3 position, Vector3 forward, MachineKind kind, CabinetContext context,
            Action<MachineCabinet, Interactor> onUse)
        {
            var go = new GameObject("Slot machine " + kind);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            var color = ColorOf(kind);
            var view = SlotCabinetView.Build(go, kind, color, context);

            // 台の前の床：ここに置いた物も一緒に入る
            var hopperGo = new GameObject("Hopper");
            hopperGo.transform.SetParent(go.transform, false);
            hopperGo.transform.localPosition = new Vector3(0f, 1f, 1.5f);
            var box = hopperGo.AddComponent<BoxCollider>();
            box.size = new Vector3(2.4f, 2f, 1.8f);
            var hopper = hopperGo.AddComponent<ExtractionZone>();
            var mark = FacilityBuilder.Block(go.transform, "Hopper mark", new Vector3(0f, 0.012f, 1.5f), new Vector3(2.4f, 0.02f, 1.8f), color * 0.35f);
            DestroyImmediate(mark.GetComponent<Collider>());
            mark.GetComponent<Renderer>().sharedMaterial = MaterialUtil.CreateEmissive(color * 0.35f, 0.4f);

            var cabinet = go.AddComponent<MachineCabinet>();
            cabinet.Kind = kind;
            cabinet.View = view;
            cabinet.Hopper = hopper;
            cabinet.onUse = onUse;
            return cabinet;
        }

        public string Prompt(StringTable strings) => strings.Format("hud.use_machine", strings.Get("machine." + Kind));

        public void Interact(Interactor who) => onUse?.Invoke(this, who);
    }
}
