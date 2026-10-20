using System.Linq;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using SpinTheScrap.Player;
using SpinTheScrap.Session;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.UI
{
    /// <summary>
    /// フェーズ 0 用の潜行 HUD（IMGUI）。見た目は仮。縦切り版（フェーズ 3）で uGUI / UI Toolkit に置き換える。
    /// </summary>
    public sealed class Phase0Hud : MonoBehaviour
    {
        RunSession session;
        FirstPersonController player;
        Interactor interactor;
        ExtractionZone zone;
        GUIStyle label;
        GUIStyle center;
        int extractableCount;

        public void Init(RunSession runSession, FirstPersonController fpc, Interactor playerInteractor, ExtractionZone extractionZone)
        {
            session = runSession;
            player = fpc;
            interactor = playerInteractor;
            zone = extractionZone;
        }

        void Update()
        {
            if (zone == null) return;
            // OverlapBox を OnGUI で毎回呼ばないよう、フレームに 1 回だけ数える
            extractableCount = zone.ScrapsInside().Count;
            if (zone.Contains(player.transform.position)) extractableCount += interactor.Inventory.Items.Count();
        }

        void OnGUI()
        {
            if (session == null || !player.InputEnabled) return;
            EnsureStyles();
            var s = session.Strings;
            var run = session.Run;

            // 照準
            GUI.Label(new Rect(Screen.width / 2f - 10, Screen.height / 2f - 12, 20, 24), "+", center);

            // 左上：ランの状況
            string status = s.Format("base.status", run.Cycle, run.DivesCompletedInCycle + 1, run.Config.DivesPerCycle,
                session.Money(run.Money), session.Money(run.Quota));
            GUI.Label(new Rect(16, 12, 900, 24), status, label);
            GUI.Label(new Rect(16, 36, 400, 24), s.Format("hud.load", player.LoadKg.ToString("0")), label);
            GUI.Label(new Rect(16, 60, 400, 24), s.Format("hud.haul", session.Haul.Count), label);

            // 照準の下：見ている物・持っている物
            float y = Screen.height / 2f + 24;
            if (interactor.Held != null)
            {
                DescribeScrap(interactor.Held, ref y);
                Line(s.Get("hud.holding"), ref y);
            }
            else if (interactor.Hovered != null)
            {
                var scrap = interactor.Hovered;
                DescribeScrap(scrap, ref y);
                string key = scrap.Size == ScrapSize.Huge ? "hud.grab_huge" : scrap.Size == ScrapSize.Large ? "hud.grab" : "hud.pickup";
                Line(s.Get(key), ref y);
            }
            if (interactor.InventoryFullFeedback) Line(s.Get("hud.inventory_full"), ref y);

            if (extractableCount > 0) Line(s.Format("hud.extract", extractableCount), ref y);

            // 下：インベントリ枠
            var inv = interactor.Inventory;
            const float slotW = 130, slotH = 44;
            float x0 = Screen.width / 2f - inv.SlotCount * slotW / 2f;
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var item = inv[i];
                string text = (i + 1) + "  " + (item != null ? session.SymbolName(item.SymbolId) : "-");
                var prev = GUI.color;
                if (i == inv.SelectedIndex) GUI.color = new Color(1f, 0.9f, 0.4f);
                GUI.Box(new Rect(x0 + i * slotW, Screen.height - slotH - 40, slotW - 6, slotH), text);
                GUI.color = prev;
            }

            GUI.Label(new Rect(16, Screen.height - 28, Screen.width - 32, 24), s.Get("hud.controls"), label);
        }

        void DescribeScrap(Scrap scrap, ref float y)
        {
            var s = session.Strings;
            var def = session.Content.GetSymbol(scrap.SymbolId);
            Line(session.SymbolName(scrap.SymbolId) + "  [" + s.Get("size." + scrap.Size) + "]", ref y);
            double factor = scrap.ValueFactor(session.Run.BrokenFragileValueRatio);
            Line(s.Format("hud.value", session.Money(System.Math.Floor(def.SellPrice * factor))), ref y);
            if (scrap.Fragile)
                Line(scrap.IsBroken ? s.Get("hud.broken") : s.Format("hud.integrity", Mathf.RoundToInt(scrap.Integrity * 100)), ref y);
            Line(s.Get(def.DescriptionKey), ref y);
        }

        void Line(string text, ref float y)
        {
            GUI.Label(new Rect(Screen.width / 2f - 300, y, 600, 24), text, center);
            y += 22;
        }

        void EnsureStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            label.normal.textColor = Color.white;
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
