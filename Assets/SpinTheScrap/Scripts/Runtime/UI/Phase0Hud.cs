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
        PlayerHealth health;
        PlayerCombat combat;
        GUIStyle label;
        GUIStyle center;

        public void Init(RunSession runSession, FirstPersonController fpc, Interactor playerInteractor, PlayerHealth playerHealth)
        {
            session = runSession;
            player = fpc;
            interactor = playerInteractor;
            health = playerHealth;
            combat = fpc.GetComponent<PlayerCombat>();
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
            DrawCrosshair();
            DrawRevealedEnemies();

            DrawStatusCard();
            DrawVitals();

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

            if (interactor.HoveredInteractable != null) Line(interactor.HoveredInteractable.Prompt(s), ref y);
            var assist = Net.TeammateAssist.Instance;
            if (assist != null && assist.Prompt != null) Line(assist.Prompt, ref y);
            if (combat != null && Time.time - combat.WeaponBrokeTime < 2f) Line(s.Get("hud.weapon_broke"), ref y);
            if (run.Objective.IsComplete && !run.IsOver) Line(s.Get("hud.objective_done"), ref y);
            DrawWeapon();

            DrawInventory();

            // 操作の説明：ランの最初だけはっきり、あとは薄く
            float age = Time.realtimeSinceStartup - session.RunStartRealtime;
            var controls = new GUIStyle(UiTheme.Small) { alignment = TextAnchor.LowerCenter, wordWrap = true };
            controls.normal.textColor = new Color(1f, 1f, 1f, age < 60f ? 0.75f : 0.3f);
            UiTheme.ShadowLabel(new Rect(16, Screen.height - 40, Screen.width - 32, 34), s.Get("hud.controls"), controls);
        }

        /// <summary>左上：階層・残機・メダル、攻略条件とその進み具合。</summary>
        void DrawStatusCard()
        {
            var s = session.Strings;
            var run = session.Run;
            var card = new Rect(16, 14, 340, 96);
            UiTheme.Fill(card, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(card.x + 12, card.y + 6, 200, 28), s.Format("panel.floor", run.Floor, run.Config.FloorCount), UiTheme.Big);
            var stat = new GUIStyle(UiTheme.Stat) { fontSize = 16 };
            GUI.Label(new Rect(card.x + 12, card.y + 4, card.width - 24, 28),
                UiTheme.Colored(s.Format("panel.lives", run.Lives), UiTheme.Danger) + "   " +
                UiTheme.Colored(s.Format("panel.medals", session.Medals(run.Medals)), UiTheme.Gold), stat);
            var objective = run.Objective;
            GUI.Label(new Rect(card.x + 12, card.y + 38, card.width - 24, 22), ObjectiveText.Describe(session), UiTheme.Body);
            var color = objective.IsComplete ? UiTheme.Good : UiTheme.Gold;
            UiTheme.Bar(new Rect(card.x + 12, card.y + 62, card.width - 24, 6), (float)(objective.Progress / objective.Target), color);
            GUI.Label(new Rect(card.x + 12, card.y + 70, card.width - 24, 22),
                s.Format("hud.load", player.LoadKg.ToString("0")) + "    " + s.Format("hud.haul", session.Haul.Count), UiTheme.Small);
        }

        /// <summary>左下・右下のカードの幅（狭い画面では縮めて、真ん中のインベントリ枠を空ける）。</summary>
        static float SideCardWidth => Mathf.Clamp(Screen.width * 0.24f, 200f, 300f);

        /// <summary>左下：体力と道具（電池の残り、包帯・爆竹の数）。</summary>
        void DrawVitals()
        {
            var s = session.Strings;
            var run = session.Run;
            if (health == null || health.State == null) return;
            var hp = health.State;
            float ratio = (float)(hp.Current / hp.Max);
            var area = new Rect(16, Screen.height - 128, SideCardWidth, 76);
            UiTheme.Fill(area, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(area.x + 12, area.y + 6, area.width - 24, 22),
                s.Format("hud.health", Mathf.CeilToInt((float)hp.Current), Mathf.RoundToInt((float)hp.Max)), UiTheme.Body);
            UiTheme.Bar(new Rect(area.x + 12, area.y + 30, area.width - 24, 12), ratio,
                Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.45f, 0.9f, 0.5f), ratio));
            GUI.Label(new Rect(area.x + 12, area.y + 48, area.width - 24, 26), s.Format("hud.tools", Mathf.RoundToInt((float)run.FlashlightCharge * 100),
                run.ToolCount(ToolIds.Battery), run.ToolCount(ToolIds.Bandage), run.ToolCount(ToolIds.Firecracker)), UiTheme.Small);
        }

        /// <summary>下：インベントリ枠。選んでいる枠は金色のふち。</summary>
        void DrawInventory()
        {
            var inv = interactor.Inventory;
            const float slotH = 42, gap = 6;
            float sideCards = 2 * (SideCardWidth + 28f);
            // 左右のカード（体力・武器）の間に並べる。狭い画面では枠を縮め、それでも入らなければカードの上に出す
            float room = Screen.width - sideCards;
            float slotW = Mathf.Clamp((room - gap * (inv.SlotCount - 1)) / inv.SlotCount, 84f, 128f);
            float total = inv.SlotCount * (slotW + gap) - gap;
            float x0 = Screen.width / 2f - total / 2f;
            float y0 = total <= room ? Screen.height - slotH - 46 : Screen.height - 128 - slotH - 10;
            var text = new GUIStyle(UiTheme.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var item = inv[i];
                var rect = new Rect(x0 + i * (slotW + gap), y0, slotW, slotH);
                bool selected = i == inv.SelectedIndex;
                if (selected) UiTheme.Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), UiTheme.Gold);
                UiTheme.Fill(rect, new Color(0.05f, 0.05f, 0.06f, selected ? 0.9f : 0.6f));
                GUI.Label(new Rect(rect.x + 6, rect.y + 2, 20, 16), (i + 1).ToString(), UiTheme.Small);
                text.normal.textColor = item != null ? UiTheme.Ink : UiTheme.Muted;
                GUI.Label(rect, item != null ? session.SymbolName(item.SymbolId) : "-", text);
            }
        }

        /// <summary>照準。当てた直後は ×（会心は黄色）にし、狙っている敵の体力を上に出す。</summary>
        void DrawCrosshair()
        {
            var rect = new Rect(Screen.width / 2f - 10, Screen.height / 2f - 12, 20, 24);
            bool hit = combat != null && Time.time - combat.LastHitTime < 0.15f;
            var prev = center.normal.textColor;
            if (hit) center.normal.textColor = combat.LastHitCritical ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.4f, 0.3f);
            ShadowLabel(rect, hit ? "×" : "+", center);
            center.normal.textColor = prev;

            var target = combat != null ? combat.Target : null;
            if (target == null || target.MaxHealth <= 0) return;
            var bar = new Rect(Screen.width / 2f - 60, Screen.height / 2f - 34, 120, 6);
            float ratio = (float)(target.CurrentHealth / target.MaxHealth);
            var c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.25f, 0.2f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), Texture2D.whiteTexture);
            GUI.color = c;
            ShadowLabel(new Rect(Screen.width / 2f - 150, Screen.height / 2f - 58, 300, 24), session.Strings.Get("enemy." + target.Kind), center);
        }

        /// <summary>紙の強化：近くの敵の位置を壁越しに印で出す。</summary>
        void DrawRevealedEnemies()
        {
            double range = session.Run.StatsOf(session.LocalPlayerId).RevealRange;
            if (range <= 0) return;
            var cam = player.ViewCamera;
            var prev = center.normal.textColor;
            center.normal.textColor = new Color(1f, 0.35f, 0.3f, 0.85f);
            foreach (var enemy in Enemies.EnemyRegistry.All)
            {
                if (enemy == null || enemy.IsDead || enemy.Transform == null) continue;
                float distance = Vector3.Distance(enemy.Transform.position, player.transform.position);
                if (distance > range) continue;
                var screen = cam.WorldToScreenPoint(enemy.Transform.position);
                if (screen.z <= 0) continue; // 後ろ
                var rect = new Rect(screen.x - 30, Screen.height - screen.y - 12, 60, 24);
                ShadowLabel(rect, "◆ " + Mathf.RoundToInt(distance) + "m", center);
            }
            center.normal.textColor = prev;
        }

        /// <summary>右下：手に持っている武器と残りの耐久・弾数。</summary>
        void DrawWeapon()
        {
            if (combat == null) return;
            var s = session.Strings;
            var weapon = session.Run.Player(session.LocalPlayerId).Weapon;
            bool bare = weapon == null || weapon.IsSpent;
            var area = new Rect(Screen.width - 16 - SideCardWidth, Screen.height - 128, SideCardWidth, 76);
            UiTheme.Fill(area, new Color(0f, 0f, 0f, 0.45f));
            var name = new GUIStyle(UiTheme.Big) { alignment = TextAnchor.MiddleRight, wordWrap = false, fontSize = 19 };
            GUI.Label(new Rect(area.x + 12, area.y + 6, area.width - 24, 28), bare ? s.Get("hud.fists") : s.Get(weapon.Definition.NameKey), name);
            if (bare) return;
            float ratio = (float)weapon.Remaining / Mathf.Max(1, weapon.Capacity);
            UiTheme.Bar(new Rect(area.x + 12, area.y + 40, area.width - 24, 8), ratio, ratio < 0.25f ? UiTheme.Danger : MachineCabinet.ColorOf(MachineKind.Weapon));
            var count = new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleRight };
            string mods = weapon.Mods.Count > 0 ? string.Join("・", weapon.Mods.Select(m => s.Get("mod." + m))) + "    " : "";
            GUI.Label(new Rect(area.x + 12, area.y + 50, area.width - 24, 22), mods + weapon.Remaining + " / " + weapon.Capacity, count);
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
            Line(s.Format("hud.value", session.Medals(session.Run.MedalValueFor(def.Id, factor))), ref y);
            if (scrap.Fragile)
                Line(scrap.IsBroken ? s.Get("hud.broken") : s.Format("hud.integrity", Mathf.RoundToInt(scrap.Integrity * 100)), ref y);
            Line(s.Get(def.DescriptionKey), ref y);
        }

        /// <summary>照準の下の案内。読みやすいよう、文字の幅だけ暗い帯を敷く。</summary>
        void Line(string text, ref float y)
        {
            float width = Mathf.Min(620f, center.CalcSize(new GUIContent(text)).x + 24f);
            UiTheme.Fill(new Rect(Screen.width / 2f - width / 2f, y + 1, width, 22), new Color(0f, 0f, 0f, 0.4f), true);
            ShadowLabel(new Rect(Screen.width / 2f - 310, y, 620, 24), text, center);
            y += 25;
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
            UiTheme.Ensure();
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            label.normal.textColor = Color.white;
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
