using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.Session;
using UnityEngine;

namespace SpinTheScrap.UI
{
    /// <summary>
    /// スロット台のメニュー（IMGUI の仮 UI。docs/floor-design.md）。台の種類（武器・強化・改造・道具）を切り替え、
    /// 台に入れたガラクタを「換金する」か「この台のリールに入れる」か選び、回して強くなる。
    /// 攻略条件を満たしたら次の階層へ、倒れたら階層をやり直す。
    /// </summary>
    public sealed class BasePanel : MonoBehaviour
    {
        /// <summary>投入者の色の縁取り（要件書 5.1）。-1 は初期リール。</summary>
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
        GUIStyle cellStyle;
        // IMGUI はイベントごとに OnGUI が複数回呼ばれるため、描画中に一覧を変えると
        // レイアウトの整合が崩れる。ボタンの処理は次の Update で実行する。
        System.Action pending;
        // 説明の列の幅。画面が狭くても横にはみ出さないよう、列の幅から決める
        float labelWidth = 200f;
        float cellWidth = 96f;

        public bool Visible { get; set; }
        /// <summary>倒れて開いた（閉じられず、「やり直す」だけが押せる）。Phase0Bootstrap が設定する。</summary>
        public bool OnDownedScreen { get; set; }
        /// <summary>今操作している台の種類。台に近づいて開いたときにその種類になる。</summary>
        public MachineKind Machine { get; set; } = MachineKind.Weapon;

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
            var action = pending;
            pending = null;
            action?.Invoke();
        }

        void OnGUI()
        {
            if (!Visible || session == null) return;
            if (cellStyle == null) cellStyle = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 12 };

            var s = session.Strings;
            var run = session.Run;
            var area = new Rect(20, 20, Screen.width - 40, Screen.height - 40);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10, area.y + 10, area.width - 20, area.height - 20));

            GUILayout.Label(s.Format("base.status", run.Floor, run.Config.FloorCount, run.Lives, session.Medals(run.Medals))
                            + "    " + ObjectiveText.Describe(session));
            if (run.PendingEnemyBonus > 0) GUILayout.Label(s.Format("base.enemy_bonus", run.PendingEnemyBonus));
            if (!string.IsNullOrEmpty(session.Message)) GUILayout.Label(session.Message);
            DrawMachineTabs();

            GUILayout.BeginHorizontal();
            float columnWidth = (area.width - 40) / 2f;
            labelWidth = columnWidth * 0.45f;
            cellWidth = Mathf.Min(96f, (columnWidth - 40f) / Board.Columns);

            GUILayout.BeginVertical(GUILayout.Width(columnWidth));
            leftScroll = GUILayout.BeginScrollView(leftScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            DrawHaul();
            GUILayout.Space(12);
            DrawReel();
            GUILayout.Space(12);
            DrawRelics();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(columnWidth));
            rightScroll = GUILayout.BeginScrollView(rightScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            DrawSlot();
            GUILayout.Space(16);
            DrawFlow();
            GUILayout.Space(16);
            DrawLoadout();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawMachineTabs()
        {
            var s = session.Strings;
            GUILayout.BeginHorizontal();
            foreach (var kind in RunState.AllMachines)
            {
                var prev = GUI.color;
                if (kind == Machine) GUI.color = new Color(1f, 0.9f, 0.4f);
                if (GUILayout.Button(s.Get("machine." + kind), GUILayout.Height(28))) Machine = kind;
                GUI.color = prev;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(s.Get("machine." + Machine + ".desc"));
        }

        void DrawHaul()
        {
            var s = session.Strings;
            GUILayout.Label("■ " + s.Get("base.haul"));
            foreach (var item in session.Haul.ToList())
            {
                GUILayout.BeginHorizontal();
                string condition = item.ValueFactor < 1 ? " (" + Mathf.RoundToInt((float)item.ValueFactor * 100) + "%)" : "";
                GUILayout.Label(session.SymbolName(item.SymbolId) + condition, GUILayout.Width(labelWidth * 0.7f));
                if (GUILayout.Button(s.Format("base.to_medals", session.Medals(session.MedalValueOf(item)))))
                    pending = () => { session.ConvertToMedals(item); Sfx.Play2D(SfxClips.Coin, 0.6f); };
                GUI.enabled = session.CanAddToReel(item);
                var kind = Machine;
                if (GUILayout.Button(s.Get("base.to_reel"))) pending = () => { session.AddToReel(item, kind); Sfx.Play2D(SfxClips.Knock, 0.5f, 1.3f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        void DrawReel()
        {
            var s = session.Strings;
            var run = session.Run;
            var reel = run.ReelOf(Machine);
            GUILayout.Label("■ " + s.Format("base.reel", reel.Count));
            // 同じシンボルはまとめて表示し、廃棄は 1 個ずつ（最後に入れた物から）
            foreach (var group in reel.GroupBy(r => r.SymbolId).ToList())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(session.SymbolName(group.Key) + " ×" + group.Count(), GUILayout.Width(labelWidth));
                GUI.enabled = run.Medals >= run.RemovalFee;
                int instanceId = group.Last().InstanceId;
                var kind = Machine;
                if (GUILayout.Button(s.Format("base.remove", session.Medals(run.RemovalFee))))
                    pending = () => session.RemoveFromReel(kind, instanceId);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        void DrawRelics()
        {
            var s = session.Strings;
            var run = session.Run;
            GUILayout.Label("■ " + s.Format("base.relics", run.Relics.Count, run.Config.MaxRelics));
            foreach (var relic in run.Relics)
                GUILayout.Label("・" + s.Get(relic.NameKey) + "：" + s.Get(relic.DescriptionKey));
            foreach (var id in run.RelicOffers.ToList())
            {
                var def = session.Content.GetRelic(id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Get(def.NameKey) + "：" + s.Get(def.DescriptionKey), GUILayout.Width(labelWidth));
                GUI.enabled = run.Medals >= def.Price && run.Relics.Count < run.Config.MaxRelics;
                if (GUILayout.Button(s.Format("base.buy", session.Medals(def.Price))))
                    pending = () => { session.BuyRelic(id); Sfx.Play2D(SfxClips.Coin, 0.6f, 0.8f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        void DrawSlot()
        {
            var s = session.Strings;
            var run = session.Run;
            int player = session.LocalPlayerId;
            GUI.enabled = run.CanSpin(Machine, player);
            if (GUILayout.Button(s.Format("base.spin", session.Medals(run.CostOf(Machine))), GUILayout.Height(40))) pending = SpinWithSound;
            GUI.enabled = true;
            if (Machine == MachineKind.Mod && (run.Player(player).Weapon == null || !run.Player(player).Weapon.CanTakeMod))
                GUILayout.Label(s.Get("base.mod_needs_weapon"));

            var outcome = session.LastOutcome;
            if (outcome == null) return;
            var spin = outcome.Reading.Result;
            GUILayout.Label(s.Format("base.last_spin", s.Get("machine." + outcome.Kind), session.Medals(spin.Total), spin.Lines.Count,
                outcome.Reading.Family == SymbolTag.None ? "-" : s.Get("tag." + outcome.Reading.Family), outcome.Reading.Tier));
            if (outcome.Reading.Jackpot) GUILayout.Label(s.Get("base.jackpot"));
            GUILayout.Label("→ " + OutcomeText(outcome));

            for (int row = 0; row < Board.Rows; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < Board.Columns; col++)
                {
                    var cell = spin.Cells[Board.Index(row, col)];
                    var prev = GUI.color;
                    string text = "";
                    if (!cell.IsEmpty)
                    {
                        int owner = cell.Instance.OwnerId;
                        if (owner >= 0) GUI.color = OwnerColors[owner % OwnerColors.Length];
                        text = session.SymbolName(cell.Instance.SymbolId) + "\n" +
                               (cell.Destroyed ? "×" : "+" + session.Medals(cell.Value)) +
                               (cell.Triggers > 1 ? " ↻" + cell.Triggers : "");
                    }
                    GUILayout.Box(text, cellStyle, GUILayout.Width(cellWidth), GUILayout.Height(52));
                    GUI.color = prev;
                }
                GUILayout.EndHorizontal();
            }
            foreach (var line in spin.Lines)
                GUILayout.Label("Line " + (line.LineIndex + 1) + "  ×" + line.Length + "  " + TagNames(line.SharedTags) + "  +" + session.Medals(line.Bonus));
            if (spin.CornerBonus > 0) GUILayout.Label("Corners  +" + session.Medals(spin.CornerBonus));
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

        static void PlaySpinSound(MachineOutcome outcome)
        {
            Sfx.Play2D(SfxClips.Spin, 0.7f);
            if (outcome.Reading.Jackpot || outcome.Reading.Tier >= 3 || outcome.LevelUps.Count > 0) Sfx.Play2D(SfxClips.Win, 0.6f);
        }

        /// <summary>手に持っている武器、強化のレベル、道具。</summary>
        void DrawLoadout()
        {
            var s = session.Strings;
            var run = session.Run;
            var me = run.Player(session.LocalPlayerId);
            GUILayout.Label("■ " + s.Get("base.loadout"));
            if (me.Weapon != null)
            {
                string mods = me.Weapon.Mods.Count > 0 ? "  [" + string.Join("・", me.Weapon.Mods.Select(m => s.Get("mod." + m))) + "]" : "";
                GUILayout.Label(s.Format("base.weapon", s.Get(me.Weapon.Definition.NameKey), me.Weapon.Remaining, me.Weapon.Capacity) + mods);
            }
            else GUILayout.Label(s.Get("base.no_weapon"));

            var levels = System.Enum.GetValues(typeof(SymbolTag)).Cast<SymbolTag>()
                .Where(t => t != SymbolTag.None && me.Perks.Level(t) > 0)
                .Select(t => s.Get("tag." + t) + " Lv" + me.Perks.Level(t));
            string perkText = string.Join("  ", levels);
            if (!string.IsNullOrEmpty(perkText)) GUILayout.Label(s.Format("base.perks", perkText));

            foreach (var tool in run.ToolCatalog.Tools)
            {
                int owned = run.ToolCount(tool.Id);
                if (owned <= 0) continue;
                string count = tool.Permanent ? "" : "  " + s.Format("base.tool_count", owned, tool.MaxCount);
                GUILayout.Label("・" + s.Get(tool.NameKey) + count);
            }
        }

        void DrawFlow()
        {
            var s = session.Strings;
            var run = session.Run;
            if (run.IsOver)
            {
                DrawResult();
                if (GUILayout.Button(s.Get("base.new_run"), GUILayout.Height(36))) pending = session.NewRun;
                return;
            }
            if (OnDownedScreen)
            {
                if (GUILayout.Button(s.Format("base.retry_floor", run.Floor), GUILayout.Height(36)))
                    pending = () => { session.RestartFloor(); Visible = false; };
                return;
            }
            GUI.enabled = run.Objective.IsComplete;
            string label = run.Objective.IsComplete
                ? (run.IsLastFloor ? s.Get("base.finish_run") : s.Format("base.next_floor", run.Floor + 1))
                : s.Get("base.objective_pending");
            if (GUILayout.Button(label, GUILayout.Height(36)))
                pending = () => { if (session.TryAdvanceFloor() && !session.Run.IsOver) Visible = false; };
            GUI.enabled = true;
        }

        /// <summary>ランの結果（クリア・ゲームオーバー）。</summary>
        void DrawResult()
        {
            var s = session.Strings;
            var run = session.Run;
            GUILayout.Label(s.Get(run.IsCleared ? "result.cleared" : "result.failed"));
            int seconds = Mathf.RoundToInt(Time.realtimeSinceStartup - session.RunStartRealtime);
            GUILayout.Label(s.Format("result.floor", run.Floor, run.Config.FloorCount));
            GUILayout.Label(s.Format("result.kills", run.Kills));
            GUILayout.Label(s.Format("result.spins", run.TotalSpins, run.Jackpots, run.BestTier));
            GUILayout.Label(s.Format("result.time", seconds / 60, (seconds % 60).ToString("00")));
            GUILayout.Space(8);
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
