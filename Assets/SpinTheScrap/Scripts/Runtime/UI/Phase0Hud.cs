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
        PlayerHealth health;
        GUIStyle label;
        GUIStyle center;
        int extractableCount;

        public void Init(RunSession runSession, FirstPersonController fpc, Interactor playerInteractor, ExtractionZone extractionZone, PlayerHealth playerHealth)
        {
            session = runSession;
            player = fpc;
            interactor = playerInteractor;
            zone = extractionZone;
            health = playerHealth;
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

            // ダメージを受けた直後は画面のふちを赤くする
            if (health != null && health.DamageFlash > 0f)
            {
                var prev = GUI.color;
                GUI.color = new Color(0.8f, 0f, 0f, 0.35f * health.DamageFlash);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            // 照準
            ShadowLabel(new Rect(Screen.width / 2f - 10, Screen.height / 2f - 12, 20, 24), "+", center);

            // 左上：ランの状況
            string status = s.Format("base.status", run.Cycle, run.DivesCompletedInCycle + 1, run.Config.DivesPerCycle,
                session.Money(run.Money), session.Money(run.Quota));
            ShadowLabel(new Rect(16, 12, 900, 24), status, label);
            ShadowLabel(new Rect(16, 36, 400, 24), s.Format("hud.load", player.LoadKg.ToString("0")), label);
            ShadowLabel(new Rect(16, 60, 400, 24), s.Format("hud.haul", session.Haul.Count), label);
            // 道具：電池の残り、包帯・爆竹の数
            ShadowLabel(new Rect(16, 116, 600, 24), s.Format("hud.tools", Mathf.RoundToInt((float)run.FlashlightCharge * 100),
                run.ToolCount(ToolIds.Battery), run.ToolCount(ToolIds.Bandage), run.ToolCount(ToolIds.Firecracker)), label);
            if (health != null && health.State != null)
            {
                var hp = health.State;
                ShadowLabel(new Rect(16, 84, 400, 24), s.Format("hud.health", Mathf.CeilToInt((float)hp.Current), Mathf.RoundToInt((float)hp.Max)), label);
                var bar = new Rect(16, 108, 160, 6);
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.5f, 0.9f, 0.5f), (float)(hp.Current / hp.Max));
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (float)(hp.Current / hp.Max), bar.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            // 照準の下：投げる力のゲージ、見ている物・持っている物
            float y = Screen.height / 2f + 24;
            if (interactor.IsChargingThrow)
            {
                DrawChargeBar(new Rect(Screen.width / 2f - 60, y - 8, 120, 6), interactor.ThrowCharge);
                y += 6;
            }
            if (interactor.Pushed != null)
            {
                var cart = interactor.Pushed;
                Line(s.Format("hud.cart_load", cart.LoadCount, cart.LoadKg.ToString("0")), ref y);
                Line(s.Get("hud.pushing_cart"), ref y);
            }
            else if (interactor.HoveredCart != null)
            {
                var cart = interactor.HoveredCart;
                Line(s.Format("hud.cart_load", cart.LoadCount, cart.LoadKg.ToString("0")), ref y);
                Line(s.Get("hud.push_cart"), ref y);
            }
            else if (interactor.Held != null)
            {
                DescribeScrap(interactor.Held, ref y);
                Line(s.Get(interactor.Held.Size == ScrapSize.Huge ? "hud.holding_huge" : "hud.holding"), ref y);
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

            ShadowLabel(new Rect(16, Screen.height - 28, Screen.width - 32, 24), s.Get("hud.controls"), label);
        }

        static void DrawChargeBar(Rect rect, float charge)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(1f, 0.4f, 0.2f), charge);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * charge, rect.height), Texture2D.whiteTexture);
            GUI.color = prev;
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
            ShadowLabel(new Rect(Screen.width / 2f - 300, y, 600, 24), text, center);
            y += 22;
        }

        /// <summary>明るい物の手前でも読めるよう、黒い影を付けて描く。</summary>
        void ShadowLabel(Rect rect, string text, GUIStyle style)
        {
            var color = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0) GUI.Label(new Rect(rect.x + dx * 1.5f, rect.y + dy * 1.5f, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
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
