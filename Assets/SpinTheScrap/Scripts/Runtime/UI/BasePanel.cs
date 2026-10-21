using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.Session;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.UI
{
    /// <summary>
    /// スロット台のメニュー（IMGUI。見た目は UiTheme）。台の種類（武器・強化・改造・道具）を切り替え、
    /// 台に入れたガラクタを「換金する」か「この台のリールに入れる」か選び、回して強くなる。
    /// 台の前で開いたときは画面の右に寄せ（Docked）、左に映る台のリールで結果を見せる。
    /// 攻略条件を満たしたら次の階層へ、倒れたら階層をやり直す。
    /// </summary>
    public sealed class BasePanel : MonoBehaviour
    {
        /// <summary>投入者の色（要件書 5.1）。-1 は初期リール。</summary>
        static readonly Color[] OwnerColors =
        {
            new Color(1f, 0.35f, 0.7f),
            new Color(0.3f, 0.9f, 1f),
            new Color(1f, 0.9f, 0.3f),
            new Color(0.5f, 1f, 0.5f),
        };

        RunSession session;
        Vector2 leftScroll;
        Vector2 rightScroll;
        // IMGUI はイベントごとに OnGUI が複数回呼ばれるため、描画中に一覧を変えると
        // レイアウトの整合が崩れる。ボタンの処理は次の Update で実行する。
        System.Action pending;
        float outcomeShownAt = -100f;
        MachineOutcome shownOutcome;

        public bool Visible { get; set; }
        /// <summary>メニューが開いている（HUD が「クリックで戻る」の案内を出すかどうかに使う）。</summary>
        public static bool IsOpen { get; private set; }
        /// <summary>倒れて開いた（閉じられず、「やり直す」だけが押せる）。Phase0Bootstrap が設定する。</summary>
        public bool OnDownedScreen { get; set; }
        /// <summary>今操作している台の種類。台に近づいて開いたときにその種類になる。</summary>
        public MachineKind Machine { get; set; } = MachineKind.Weapon;
        /// <summary>台の前で開いた：画面の右に寄せる（左に台が映る）。</summary>
        public bool Docked { get; set; }
        /// <summary>前に立っている台の種類（台から離れて開いたときは null）。回せるのはこの台だけ。</summary>
        public MachineKind? CabinetKind { get; set; }
        /// <summary>台のリールがまだ回っている（結果をまだ見せない）。</summary>
        public System.Func<bool> OutcomeHidden { get; set; }

        Color Accent => MachineCabinet.ColorOf(Machine);

        public void Init(RunSession runSession)
        {
            session = runSession;
            session.Spun += PlaySpinSound; // 協力プレイのクライアントでは、ホストの結果が届いてから鳴る
        }

        void OnDestroy()
        {
            if (session != null) session.Spun -= PlaySpinSound;
        }

        void Update()
        {
            IsOpen = Visible;
            var action = pending;
            pending = null;
            action?.Invoke();
        }

        void OnGUI()
        {
            if (!Visible || session == null) return;
            UiTheme.Ensure();
            var run = session.Run;

            bool wide = !Docked && Screen.width >= 1000;
            float width = Docked ? Mathf.Clamp(Screen.width * 0.46f, 380f, 720f) : Mathf.Min(Screen.width - 40f, 1180f);
            float x = Docked ? Screen.width - width - 20f : (Screen.width - width) / 2f;
            var area = new Rect(x, 20f, width, Screen.height - 40f);
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = UiTheme.PanelColor;
            GUI.Box(area, GUIContent.none, UiTheme.Panel);
            GUI.backgroundColor = prev;
            // 台の色の帯
            UiTheme.Fill(new Rect(area.x, area.y, area.width, 5f), Accent, true);

            GUILayout.BeginArea(new Rect(area.x + 18, area.y + 14, area.width - 36, area.height - 28));
            if (Docked && !run.IsOver && !OnDownedScreen)
            {
                DrawDocked(area.width - 36f - 20f);
                GUILayout.EndArea();
                return;
            }
            DrawHeader();
            if (run.IsOver)
            {
                leftScroll = Scroll(leftScroll);
                DrawResult();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            DrawMachineTabs();

            if (wide)
            {
                float column = (area.width - 36f - 16f) / 2f;
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(column));
                leftScroll = Scroll(leftScroll);
                DrawSlot(true);
                DrawHaul();
                DrawReel();
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                GUILayout.Space(16);
                GUILayout.BeginVertical(GUILayout.Width(column));
                rightScroll = Scroll(rightScroll);
                DrawFlow();
                DrawLoadout();
                DrawRelics();
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            else
            {
                leftScroll = Scroll(leftScroll);
                GUILayout.BeginVertical(GUILayout.Width(area.width - 36f - 20f)); // スクロールバーの分を空ける
                DrawSlot(!Docked);
                DrawHaul();
                DrawFlow();
                DrawLoadout();
                DrawReel();
                DrawRelics();
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        // ---- 台の前で開いたとき：この台だけを、手順どおりに見せる ----

        // 遊び方は最初だけ開いておき、1 回回したらたたむ（「開く」でいつでも見られる）
        bool showHelp = true;

        void DrawDocked(float width)
        {
            var s = session.Strings;
            var run = session.Run;
            // 台の名前と、閉じるボタン
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiTheme.Colored(s.Get("machine." + Machine), Accent), UiTheme.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(s.Get("panel.close"), UiTheme.Button, GUILayout.Width(150), GUILayout.Height(34))) Visible = false;
            GUILayout.EndHorizontal();
            GUILayout.Label(UiTheme.Colored(s.Format("panel.medals", session.Medals(run.Medals)), UiTheme.Gold) + "    " +
                            UiTheme.Colored(s.Format("panel.lives", run.Lives), UiTheme.Danger) + "    " +
                            s.Format("panel.floor", run.Floor, run.Config.FloorCount), UiTheme.Body);
            if (!string.IsNullOrEmpty(session.Message)) GUILayout.Label(session.Message, UiTheme.Small);
            GUILayout.Space(4);

            leftScroll = Scroll(leftScroll);
            GUILayout.BeginVertical(GUILayout.Width(width));
            DrawHelp();
            DrawDockedHaul();
            DrawDockedSpin();
            DrawReelSummary();
            DrawLoadout();
            DrawFlow();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        /// <summary>遊び方（たたんでおける）。</summary>
        void DrawHelp()
        {
            var s = session.Strings;
            UiTheme.BeginCard(new Color(0.1f, 0.12f, 0.16f, 0.96f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(s.Get("help.title"), UiTheme.Heading);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(s.Get(showHelp ? "help.hide" : "help.show"), UiTheme.Tab, GUILayout.Width(90), GUILayout.Height(26))) showHelp = !showHelp;
            GUILayout.EndHorizontal();
            if (showHelp)
            {
                GUILayout.Label(s.Get("help.step1"), UiTheme.Body);
                GUILayout.Label(s.Get("help.step2"), UiTheme.Body);
                GUILayout.Label(s.Get("help.step3"), UiTheme.Body);
                GUILayout.Label(UiTheme.Colored(s.Get("help.lines"), UiTheme.Muted), UiTheme.Small);
                GUILayout.Label(UiTheme.Colored(s.Get("help." + Machine), Accent), UiTheme.Body);
            }
            UiTheme.EndCard();
        }

        /// <summary>① 台に入れたガラクタ：換金するか、この台のリールに入れるか。</summary>
        void DrawDockedHaul()
        {
            var s = session.Strings;
            var haul = session.Haul.ToList();
            UiTheme.BeginCard();
            GUILayout.Label(s.Format("panel.step_haul", haul.Count), UiTheme.Heading);
            if (haul.Count == 0) GUILayout.Label(s.Get("panel.haul_empty"), UiTheme.Small);
            foreach (var item in haul)
            {
                string condition = item.ValueFactor < 1 ? UiTheme.Colored(" " + s.Format("panel.condition", Mathf.RoundToInt((float)item.ValueFactor * 100)), UiTheme.Danger) : "";
                GUILayout.Label(session.SymbolName(item.SymbolId) + condition + "   " + TagChips(session.Content.GetSymbol(item.SymbolId).Tags), UiTheme.Body);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(UiTheme.Colored(s.Format("panel.cash_in", session.Medals(session.MedalValueOf(item))), UiTheme.Gold), UiTheme.Button,
                        GUILayout.Height(32), GUILayout.MinWidth(60)))
                    pending = () => { session.ConvertToMedals(item); Sfx.Play2D(SfxClips.Coin, 0.6f); };
                GUI.enabled = session.CanAddToReel(item);
                var kind = Machine;
                if (GUILayout.Button(s.Get("panel.add_to_reel"), UiTheme.Button, GUILayout.Height(32), GUILayout.MinWidth(60)))
                    pending = () => { session.AddToReel(item, kind); Sfx.Play2D(SfxClips.Knock, 0.5f, 1.3f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            UiTheme.EndCard();
        }

        /// <summary>② レバーを引く：費用と、引けない理由、出た物。</summary>
        void DrawDockedSpin()
        {
            var s = session.Strings;
            var run = session.Run;
            int player = session.LocalPlayerId;
            double cost = run.CostOf(Machine);
            UiTheme.BeginCard();
            GUILayout.Label(s.Get("panel.step_spin"), UiTheme.Heading);
            bool spinning = OutcomeHidden != null && OutcomeHidden();
            GUI.enabled = run.CanSpin(Machine, player) && !spinning;
            if (GUILayout.Button(s.Format("panel.pull", session.Medals(cost)), UiTheme.AccentButton(Accent), GUILayout.Height(52)))
                pending = SpinWithSound;
            GUI.enabled = true;
            string reason = null;
            if (run.Medals < cost) reason = s.Format("panel.need_medals", session.Medals(cost - run.Medals));
            else if (Machine == MachineKind.Mod && (run.Player(player).Weapon == null || !run.Player(player).Weapon.CanTakeMod)) reason = s.Get("base.mod_needs_weapon");
            if (reason != null) GUILayout.Label(UiTheme.Colored(reason, UiTheme.Danger), UiTheme.Small);
            else GUILayout.Label(UiTheme.Colored(s.Get("panel.spin_note"), UiTheme.Muted), UiTheme.Small);
            DrawLastOutcome(false);
            UiTheme.EndCard();
        }

        /// <summary>③ リールの中身：多いタグと、それで何が出やすいか。</summary>
        void DrawReelSummary()
        {
            var s = session.Strings;
            var run = session.Run;
            var reel = run.ReelOf(Machine);
            UiTheme.BeginCard();
            GUILayout.Label(s.Format("panel.step_reel", reel.Count), UiTheme.Heading);
            var counts = new Dictionary<SymbolTag, int>();
            foreach (var inst in reel)
            {
                var tags = session.Content.GetSymbol(inst.SymbolId).Tags;
                foreach (SymbolTag t in System.Enum.GetValues(typeof(SymbolTag)))
                    if (t != SymbolTag.None && tags.HasAny(t)) counts[t] = (counts.TryGetValue(t, out int n) ? n : 0) + 1;
            }
            int total = Mathf.Max(1, reel.Count);
            foreach (var pair in counts.OrderByDescending(p => p.Value).Take(4))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Get("tag." + pair.Key) + UiTheme.Colored("  " + pair.Value, UiTheme.Muted), UiTheme.Body, GUILayout.Width(120));
                var bar = GUILayoutUtility.GetRect(40f, 10f, GUILayout.Width(70));
                bar.y += 8f;
                UiTheme.Bar(bar, (float)pair.Value / total, Accent);
                GUILayout.Label(UiTheme.Colored(RewardFor(Machine, pair.Key), UiTheme.Ink), UiTheme.Small);
                GUILayout.EndHorizontal();
            }
            if (counts.Count == 0) GUILayout.Label(s.Get("panel.reel_empty"), UiTheme.Small);
            GUILayout.Label(UiTheme.Colored(s.Get("panel.reel_note"), UiTheme.Muted), UiTheme.Small);
            showReelList = GUILayout.Toggle(showReelList, " " + s.Get("panel.reel_list"), GUILayout.Height(22));
            if (showReelList) DrawReelList();
            UiTheme.EndCard();
        }

        bool showReelList;

        /// <summary>この台で、このタグが多いと何が出やすいか。</summary>
        string RewardFor(MachineKind kind, SymbolTag tag)
        {
            var s = session.Strings;
            switch (kind)
            {
                case MachineKind.Weapon:
                    var family = tag == SymbolTag.Doll ? SymbolTag.Toy : tag;
                    var names = session.Run.Weapons.Weapons.Where(w => w.Family == family).OrderBy(w => w.Tier).Select(w => s.Get(w.NameKey)).ToList();
                    return names.Count > 0 ? "→ " + string.Join("・", names) : "→ " + s.Get("reward.weapon_default");
                case MachineKind.Mod:
                    return "→ " + s.Get("mod." + WeaponInstance.ModFor(tag));
                case MachineKind.Perk:
                    return "→ " + s.Get("reward.perk." + tag);
                default:
                    return "→ " + s.Get("reward.tool." + tag);
            }
        }

        /// <summary>タグを［金属］［電化製品］のように並べる。</summary>
        string TagChips(SymbolTag tags)
        {
            var parts = new List<string>();
            foreach (SymbolTag t in System.Enum.GetValues(typeof(SymbolTag)))
                if (t != SymbolTag.None && tags.HasAny(t)) parts.Add("［" + session.Strings.Get("tag." + t) + "］");
            return UiTheme.Colored(string.Join("", parts), UiTheme.Muted);
        }

        /// <summary>縦だけのスクロール。スクロールバーの幅を空けて、中身が右にはみ出さないようにする。</summary>
        static Vector2 Scroll(Vector2 position) =>
            GUILayout.BeginScrollView(position, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);

        // ---- 見出し ----

        void DrawHeader()
        {
            var s = session.Strings;
            var run = session.Run;
            GUILayout.BeginHorizontal();
            GUILayout.Label(s.Format("panel.floor", run.Floor, run.Config.FloorCount), UiTheme.Title);
            GUILayout.FlexibleSpace();
            GUILayout.Label(UiTheme.Colored(s.Format("panel.lives", run.Lives), UiTheme.Danger) + "   " +
                            UiTheme.Colored(s.Format("panel.medals", session.Medals(run.Medals)), UiTheme.Gold), UiTheme.Stat);
            GUILayout.EndHorizontal();
            GUILayout.Label(ObjectiveText.Describe(session), UiTheme.Body);
            if (run.PendingEnemyBonus > 0) GUILayout.Label(UiTheme.Colored(s.Format("base.enemy_bonus", run.PendingEnemyBonus), UiTheme.Danger), UiTheme.Small);
            if (!string.IsNullOrEmpty(session.Message)) GUILayout.Label(session.Message, UiTheme.Body);
            GUILayout.Space(6);
        }

        void DrawMachineTabs()
        {
            var s = session.Strings;
            GUILayout.BeginHorizontal();
            foreach (var kind in RunState.AllMachines)
            {
                bool active = kind == Machine;
                if (GUILayout.Button(s.Get("machine." + kind + ".short"), active ? UiTheme.TabAccent(MachineCabinet.ColorOf(kind)) : UiTheme.Tab,
                        GUILayout.Height(32)))
                    Machine = kind;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(s.Get("machine." + Machine + ".desc"), UiTheme.Small);
            GUILayout.Space(4);
        }

        // ---- 回す ----

        void DrawSlot(bool showBoard)
        {
            var s = session.Strings;
            var run = session.Run;
            int player = session.LocalPlayerId;
            UiTheme.BeginCard();
            // 回せるのは、いま前に立っている台だけ（ほかの台はリールの中身を見る・入れるだけ）
            bool here = CabinetKind.HasValue && CabinetKind.Value == Machine;
            if (!here)
            {
                GUILayout.Label(UiTheme.Colored(s.Format("panel.spin_at_machine", s.Get("machine." + Machine)), UiTheme.Muted), UiTheme.Body);
                DrawLastOutcome(showBoard);
                UiTheme.EndCard();
                return;
            }
            GUI.enabled = run.CanSpin(Machine, player) && (OutcomeHidden == null || !OutcomeHidden());
            if (GUILayout.Button(s.Format("base.spin", session.Medals(run.CostOf(Machine))), UiTheme.AccentButton(Accent), GUILayout.Height(52)))
                pending = SpinWithSound;
            GUI.enabled = true;
            if (Machine == MachineKind.Mod && (run.Player(player).Weapon == null || !run.Player(player).Weapon.CanTakeMod))
                GUILayout.Label(UiTheme.Colored(s.Get("base.mod_needs_weapon"), UiTheme.Danger), UiTheme.Small);

            DrawLastOutcome(showBoard);
            UiTheme.EndCard();
        }

        void DrawLastOutcome(bool showBoard)
        {
            var s = session.Strings;
            var outcome = session.LastOutcome;
            if (outcome != null && outcome.Kind == Machine)
            {
                GUILayout.Space(6);
                if (OutcomeHidden != null && OutcomeHidden())
                {
                    GUILayout.Label(s.Get("panel.spinning"), UiTheme.Big);
                }
                else
                {
                    if (shownOutcome != outcome)
                    {
                        shownOutcome = outcome;
                        outcomeShownAt = Time.unscaledTime;
                    }
                    DrawOutcome(outcome);
                    if (showBoard) DrawBoard(outcome.Reading.Result);
                }
            }
        }

        void DrawOutcome(MachineOutcome outcome)
        {
            var s = session.Strings;
            var reading = outcome.Reading;
            var spin = reading.Result;
            // 出た直後は少し大きく光らせる
            float fresh = Mathf.Clamp01(1f - (Time.unscaledTime - outcomeShownAt) / 1.2f);
            var big = new GUIStyle(UiTheme.Big) { fontSize = Mathf.RoundToInt(22 + 6 * fresh) };
            if (reading.Jackpot) GUILayout.Label(UiTheme.Colored(s.Get("base.jackpot"), UiTheme.Gold), UiTheme.Body);
            GUILayout.Label(UiTheme.Colored(OutcomeText(outcome), Color.Lerp(UiTheme.Ink, UiTheme.Gold, 0.4f + 0.6f * fresh)), big);
            string family = reading.Family == SymbolTag.None ? "-" : s.Get("tag." + reading.Family);
            GUILayout.Label(s.Format("panel.reading", session.Medals(spin.Total), spin.Lines.Count, family, Stars(reading.Tier)), UiTheme.Body);
            foreach (var line in spin.Lines)
                GUILayout.Label(s.Format("panel.line", line.LineIndex + 1, line.Length, TagNames(line.SharedTags), session.Medals(line.Bonus)), UiTheme.Small);
            if (spin.CornerBonus > 0) GUILayout.Label(s.Format("panel.corners", session.Medals(spin.CornerBonus)), UiTheme.Small);
        }

        static string Stars(int tier)
        {
            tier = Mathf.Clamp(tier, 0, 5);
            return UiTheme.Colored(new string('★', tier), UiTheme.Gold) + UiTheme.Colored(new string('☆', 5 - tier), UiTheme.Muted);
        }

        /// <summary>盤面（台から離れて開いたとき用。台の前ではリールに出る）。</summary>
        void DrawBoard(SpinResult spin)
        {
            GUILayout.Space(6);
            float cell = 64f;
            var rect = GUILayoutUtility.GetRect(Board.Columns * (cell + 4f), Board.Rows * (cell * 0.75f + 4f));
            float cellW = Mathf.Min(cell, rect.width / Board.Columns - 4f);
            float cellH = cellW * 0.75f;
            for (int row = 0; row < Board.Rows; row++)
            for (int col = 0; col < Board.Columns; col++)
            {
                var c = spin.Cells[Board.Index(row, col)];
                var r = new Rect(rect.x + col * (cellW + 4f), rect.y + row * (cellH + 4f), cellW, cellH);
                var tint = c.IsEmpty ? new Color(0.1f, 0.1f, 0.1f) : c.Destroyed ? new Color(0.35f, 0.1f, 0.1f) : new Color(0.2f, 0.2f, 0.22f);
                UiTheme.Fill(r, tint, true);
                if (c.IsEmpty) continue;
                int owner = c.Instance.OwnerId;
                if (owner >= 0) UiTheme.Fill(new Rect(r.x, r.yMax - 3f, r.width, 3f), OwnerColors[owner % OwnerColors.Length], true);
                string value = c.Destroyed ? UiTheme.Colored("×", UiTheme.Danger) : UiTheme.Colored("+" + session.Medals(c.Value), UiTheme.Gold);
                GUI.Label(r, session.SymbolName(c.Instance.SymbolId) + "\n" + value + (c.Triggers > 1 ? " ↻" + c.Triggers : ""),
                    new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 11, normal = { textColor = UiTheme.Ink } });
            }
        }

        string OutcomeText(MachineOutcome outcome)
        {
            var s = session.Strings;
            switch (outcome.Kind)
            {
                case MachineKind.Weapon:
                    return s.Format("outcome.weapon", s.Get(outcome.Weapon.Definition.NameKey));
                case MachineKind.Mod:
                    return outcome.Mod.HasValue ? s.Format("outcome.mod", s.Get("mod." + outcome.Mod.Value)) : s.Get("outcome.mod_full");
                case MachineKind.Perk:
                    var gains = outcome.Reading.TagTotals.Where(p => p.Value > 0).OrderByDescending(p => p.Value)
                        .Select(p => s.Get("tag." + p.Key) + " +" + session.Medals(p.Value));
                    string text = s.Format("outcome.perk", string.Join("  ", gains));
                    if (outcome.LevelUps.Count > 0)
                        text += "  " + s.Format("outcome.level_up", string.Join("・", outcome.LevelUps.Select(t => s.Get("tag." + t))));
                    return text;
                default:
                    if (outcome.ToolId == null) return s.Format("outcome.medals", session.Medals(outcome.Medals));
                    var def = session.Run.ToolCatalog.Get(outcome.ToolId);
                    return s.Format("outcome.tool", s.Get(def.NameKey), outcome.ToolCount);
            }
        }

        void SpinWithSound() => session.Spin(Machine);

        void PlaySpinSound(MachineOutcome outcome)
        {
            Sfx.Play2D(SfxClips.Spin, 0.7f);
            showHelp = false;
        }

        // ---- 台に入れた物・リール・レリック ----

        void DrawHaul()
        {
            var s = session.Strings;
            var haul = session.Haul.ToList();
            UiTheme.BeginCard();
            GUILayout.Label(s.Get("base.haul") + "  " + UiTheme.Colored(haul.Count.ToString(), UiTheme.Muted), UiTheme.Heading);
            if (haul.Count == 0) GUILayout.Label(s.Get("panel.haul_empty"), UiTheme.Small);
            foreach (var item in haul)
            {
                string condition = item.ValueFactor < 1 ? UiTheme.Colored(" " + Mathf.RoundToInt((float)item.ValueFactor * 100) + "%", UiTheme.Danger) : "";
                GUILayout.Label(session.SymbolName(item.SymbolId) + condition, UiTheme.Body);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(UiTheme.Colored(s.Format("base.to_medals", session.Medals(session.MedalValueOf(item))), UiTheme.Gold), UiTheme.Button,
                        GUILayout.Height(30), GUILayout.MinWidth(60)))
                    pending = () => { session.ConvertToMedals(item); Sfx.Play2D(SfxClips.Coin, 0.6f); };
                GUI.enabled = session.CanAddToReel(item);
                var kind = Machine;
                if (GUILayout.Button(s.Get("base.to_reel"), UiTheme.Button, GUILayout.Height(30), GUILayout.MinWidth(60)))
                    pending = () => { session.AddToReel(item, kind); Sfx.Play2D(SfxClips.Knock, 0.5f, 1.3f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            UiTheme.EndCard();
        }

        void DrawReel()
        {
            var s = session.Strings;
            UiTheme.BeginCard();
            GUILayout.Label(s.Format("base.reel", session.Run.ReelOf(Machine).Count), UiTheme.Heading);
            DrawReelList();
            UiTheme.EndCard();
        }

        /// <summary>リールのシンボル（同じ物はまとめて）と廃棄ボタン。</summary>
        void DrawReelList()
        {
            var s = session.Strings;
            var run = session.Run;
            var reel = run.ReelOf(Machine);
            // 同じシンボルはまとめて表示し、廃棄は 1 個ずつ（最後に入れた物から）
            foreach (var group in reel.GroupBy(r => r.SymbolId).ToList())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(session.SymbolName(group.Key) + UiTheme.Colored("  ×" + group.Count(), UiTheme.Muted), UiTheme.Body);
                GUILayout.FlexibleSpace();
                GUI.enabled = run.Medals >= run.RemovalFee;
                int instanceId = group.Last().InstanceId;
                var kind = Machine;
                if (GUILayout.Button(s.Format("base.remove", session.Medals(run.RemovalFee)), UiTheme.Button, GUILayout.Width(110), GUILayout.Height(26)))
                    pending = () => session.RemoveFromReel(kind, instanceId);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        void DrawRelics()
        {
            var s = session.Strings;
            var run = session.Run;
            UiTheme.BeginCard();
            GUILayout.Label(s.Format("base.relics", run.Relics.Count, run.Config.MaxRelics), UiTheme.Heading);
            foreach (var relic in run.Relics)
                GUILayout.Label(UiTheme.Colored(s.Get(relic.NameKey), UiTheme.Gold) + "  " + s.Get(relic.DescriptionKey), UiTheme.Body);
            foreach (var id in run.RelicOffers.ToList())
            {
                var def = session.Content.GetRelic(id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Get(def.NameKey) + "\n" + UiTheme.Colored(s.Get(def.DescriptionKey), UiTheme.Muted), UiTheme.Body);
                GUILayout.FlexibleSpace();
                GUI.enabled = run.Medals >= def.Price && run.Relics.Count < run.Config.MaxRelics;
                if (GUILayout.Button(s.Format("base.buy", session.Medals(def.Price)), UiTheme.Button, GUILayout.Width(110), GUILayout.Height(30)))
                    pending = () => { session.BuyRelic(id); Sfx.Play2D(SfxClips.Coin, 0.6f, 0.8f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            UiTheme.EndCard();
        }

        // ---- 装備・進行 ----

        /// <summary>手に持っている武器、強化のレベル、道具。</summary>
        void DrawLoadout()
        {
            var s = session.Strings;
            var run = session.Run;
            var me = run.Player(session.LocalPlayerId);
            UiTheme.BeginCard();
            GUILayout.Label(s.Get("base.loadout"), UiTheme.Heading);
            if (me.Weapon != null && !me.Weapon.IsSpent)
            {
                GUILayout.Label(s.Format("base.weapon", s.Get(me.Weapon.Definition.NameKey), me.Weapon.Remaining, me.Weapon.Capacity), UiTheme.Body);
                var bar = GUILayoutUtility.GetRect(100f, 6f, GUILayout.ExpandWidth(true));
                UiTheme.Bar(bar, (float)me.Weapon.Remaining / Mathf.Max(1, me.Weapon.Capacity), MachineCabinet.ColorOf(MachineKind.Weapon));
                if (me.Weapon.Mods.Count > 0)
                    GUILayout.Label(UiTheme.Colored(string.Join("・", me.Weapon.Mods.Select(m => s.Get("mod." + m))), MachineCabinet.ColorOf(MachineKind.Mod)),
                        UiTheme.Small);
            }
            else GUILayout.Label(s.Get("base.no_weapon"), UiTheme.Body);

            // 強化：レベルと次のレベルまでのゲージ
            foreach (SymbolTag tag in System.Enum.GetValues(typeof(SymbolTag)))
            {
                if (tag == SymbolTag.None) continue;
                double xp = me.Perks.Xp(tag);
                if (xp <= 0) continue;
                int level = me.Perks.Level(tag);
                double from = level == 0 ? 0 : PerkState.Thresholds[level - 1];
                double to = level < PerkState.Thresholds.Length ? PerkState.Thresholds[level] : from;
                float ratio = to > from ? (float)((xp - from) / (to - from)) : 1f;
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Get("tag." + tag) + "  " + UiTheme.Colored("Lv" + level, UiTheme.Gold), UiTheme.Body, GUILayout.Width(140));
                var bar = GUILayoutUtility.GetRect(60f, 8f, GUILayout.ExpandWidth(true));
                bar.y += 8f;
                UiTheme.Bar(bar, ratio, MachineCabinet.ColorOf(MachineKind.Perk));
                GUILayout.EndHorizontal();
            }

            var tools = new List<string>();
            foreach (var tool in run.ToolCatalog.Tools)
            {
                int owned = run.ToolCount(tool.Id);
                if (owned <= 0) continue;
                tools.Add(s.Get(tool.NameKey) + (tool.Permanent ? "" : " " + s.Format("base.tool_count", owned, tool.MaxCount)));
            }
            if (tools.Count > 0) GUILayout.Label(UiTheme.Colored(string.Join("   ", tools), MachineCabinet.ColorOf(MachineKind.Tool)), UiTheme.Small);
            UiTheme.EndCard();
        }

        void DrawFlow()
        {
            var s = session.Strings;
            var run = session.Run;
            if (OnDownedScreen && session.CoopRespawn)
            {
                // 協力プレイ：仲間に起こしてもらうか、あきらめて入口で起き上がる
                UiTheme.BeginCard(new Color(0.3f, 0.08f, 0.08f, 0.95f));
                GUILayout.Label(s.Get("base.wait_rescue"), UiTheme.Body);
                if (GUILayout.Button(s.Format("base.give_up", run.Lives), UiTheme.AccentButton(UiTheme.Danger), GUILayout.Height(40)))
                    pending = () => { session.RestartFloor(); Visible = false; };
                UiTheme.EndCard();
                return;
            }
            if (OnDownedScreen)
            {
                UiTheme.BeginCard(new Color(0.3f, 0.08f, 0.08f, 0.95f));
                if (GUILayout.Button(s.Format("base.retry_floor", run.Floor), UiTheme.AccentButton(UiTheme.Danger), GUILayout.Height(40)))
                    pending = () => { session.RestartFloor(); Visible = false; };
                UiTheme.EndCard();
                return;
            }
            if (!run.Objective.IsComplete) return; // 攻略条件は見出しに出ている
            UiTheme.BeginCard();
            string label = run.IsLastFloor ? s.Get("base.finish_run") : s.Format("base.next_floor", run.Floor + 1);
            if (GUILayout.Button(label, UiTheme.AccentButton(UiTheme.Good), GUILayout.Height(40)))
                pending = () => { if (session.TryAdvanceFloor() && !session.Run.IsOver) Visible = false; };
            UiTheme.EndCard();
        }

        /// <summary>ランの結果（クリア・ゲームオーバー）。</summary>
        void DrawResult()
        {
            var s = session.Strings;
            var run = session.Run;
            UiTheme.BeginCard();
            GUILayout.Label(UiTheme.Colored(s.Get(run.IsCleared ? "result.cleared" : "result.failed"), run.IsCleared ? UiTheme.Gold : UiTheme.Danger),
                UiTheme.Title);
            GUILayout.Space(8);
            int seconds = Mathf.RoundToInt(Time.realtimeSinceStartup - session.RunStartRealtime);
            GUILayout.Label(s.Format("result.floor", run.Floor, run.Config.FloorCount), UiTheme.Big);
            GUILayout.Label(s.Format("result.kills", run.Kills), UiTheme.Body);
            GUILayout.Label(s.Format("result.spins", run.TotalSpins, run.Jackpots, run.BestTier), UiTheme.Body);
            GUILayout.Label(s.Format("result.time", seconds / 60, (seconds % 60).ToString("00")), UiTheme.Body);
            GUILayout.Space(10);
            if (GUILayout.Button(s.Get("base.new_run"), UiTheme.AccentButton(UiTheme.Good), GUILayout.Height(44))) pending = session.NewRun;
            UiTheme.EndCard();
        }

        string TagNames(SymbolTag tags)
        {
            var names = new List<string>();
            foreach (SymbolTag t in System.Enum.GetValues(typeof(SymbolTag)))
                if (t != SymbolTag.None && tags.HasAny(t)) names.Add(session.Strings.Get("tag." + t));
            return string.Join("・", names);
        }
    }
}
