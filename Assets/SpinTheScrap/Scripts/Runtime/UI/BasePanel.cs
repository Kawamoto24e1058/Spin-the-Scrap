using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.Session;
using UnityEngine;

namespace SpinTheScrap.UI
{
    /// <summary>
    /// スロット台のメニュー（IMGUI の仮 UI）。台に入れたガラクタを「リールに入れる」か「メダルに換える」か選び、
    /// 回して道具を出し、レリックの購入・シンボルの廃棄、次の階層への移動をまとめて操作する（docs/floor-design.md）。
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

        public void Init(RunSession runSession) => session = runSession;

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

            GUILayout.Label(s.Get("base.title"));
            GUILayout.Label(s.Format("base.status", run.Floor, run.Config.FloorCount, run.Lives, session.Medals(run.Medals)));
            GUILayout.Label(ObjectiveText.Describe(session));
            if (run.PendingEnemyBonus > 0) GUILayout.Label(s.Format("base.enemy_bonus", run.PendingEnemyBonus));
            if (!string.IsNullOrEmpty(session.Message)) GUILayout.Label(session.Message);

            GUILayout.BeginHorizontal();
            float columnWidth = (area.width - 40) / 2f;
            labelWidth = columnWidth * 0.5f;
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
            DrawTools();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawHaul()
        {
            var s = session.Strings;
            GUILayout.Label("■ " + s.Get("base.haul"));
            foreach (var item in session.Haul.ToList())
            {
                GUILayout.BeginHorizontal();
                string condition = item.ValueFactor < 1 ? " (" + Mathf.RoundToInt((float)item.ValueFactor * 100) + "%)" : "";
                GUILayout.Label(session.SymbolName(item.SymbolId) + condition, GUILayout.Width(labelWidth * 0.6f));
                if (GUILayout.Button(s.Format("base.to_medals", session.Medals(session.MedalValueOf(item))))) pending = () => { session.ConvertToMedals(item); Sfx.Play2D(SfxClips.Coin, 0.6f); };
                GUI.enabled = session.CanAddToReel(item);
                if (GUILayout.Button(s.Get("base.to_reel"))) pending = () => { session.AddToReel(item); Sfx.Play2D(SfxClips.Knock, 0.5f, 1.3f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        void DrawReel()
        {
            var s = session.Strings;
            var run = session.Run;
            GUILayout.Label("■ " + s.Format("base.reel", run.Reel.Count));
            // 同じシンボルはまとめて表示し、廃棄は 1 個ずつ（最後に入れた物から）
            foreach (var group in run.Reel.GroupBy(r => r.SymbolId).ToList())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(session.SymbolName(group.Key) + " ×" + group.Count(), GUILayout.Width(labelWidth));
                GUI.enabled = run.Medals >= run.RemovalFee;
                int instanceId = group.Last().InstanceId;
                if (GUILayout.Button(s.Format("base.remove", session.Medals(run.RemovalFee))))
                    pending = () => run.TryRemoveFromReel(instanceId);
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

            GUILayout.Label("■ " + s.Get("base.black_market"));
            foreach (var id in run.RelicOffers.ToList())
            {
                var def = session.Content.GetRelic(id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Get(def.NameKey) + "：" + s.Get(def.DescriptionKey), GUILayout.Width(labelWidth));
                GUI.enabled = run.Medals >= def.Price && run.Relics.Count < run.Config.MaxRelics;
                if (GUILayout.Button(s.Format("base.buy", session.Medals(def.Price)))) pending = () => { if (run.TryBuyRelic(id)) Sfx.Play2D(SfxClips.Coin, 0.6f, 0.8f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>持ち物（スロットで出た道具・武器）と、直前のスピンで出た物。</summary>
        void DrawTools()
        {
            var s = session.Strings;
            var run = session.Run;
            if (session.LastRewards != null && session.LastRewards.Count > 0)
            {
                GUILayout.Label("■ " + s.Get("base.last_rewards"));
                foreach (var reward in session.LastRewards)
                    GUILayout.Label("・" + RewardText(reward));
                GUILayout.Space(8);
            }
            GUILayout.Label("■ " + s.Get("base.inventory"));
            foreach (var tool in run.ToolCatalog.Tools)
            {
                int owned = run.ToolCount(tool.Id);
                if (owned <= 0) continue;
                string count = tool.Permanent ? "" : "  " + s.Format("base.tool_count", owned, tool.MaxCount);
                GUILayout.Label(s.Get(tool.NameKey) + count + "：" + s.Get(tool.DescriptionKey));
            }
        }

        string RewardText(SpinReward reward)
        {
            var s = session.Strings;
            if (reward.IsMedals) return s.Format("base.reward_medals", session.Medals(reward.Medals));
            var def = session.Run.ToolCatalog.Get(reward.ToolId);
            return def.Permanent ? s.Get(def.NameKey) : s.Format("base.reward_tool", s.Get(def.NameKey), reward.Count);
        }

        void DrawSlot()
        {
            var s = session.Strings;
            var run = session.Run;
            GUI.enabled = run.CanSpin;
            if (GUILayout.Button(s.Format("base.spin", session.Medals(run.CurrentSpinCost)), GUILayout.Height(40))) pending = SpinWithSound;
            GUI.enabled = true;

            var spin = session.LastSpin;
            if (spin == null) return;

            GUILayout.Label(s.Format("base.last_spin", session.Medals(spin.Total), spin.Lines.Count, spin.BoardMultiplier));
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

        void SpinWithSound()
        {
            if (!session.Spin()) return;
            Sfx.Play2D(SfxClips.Spin, 0.7f);
            // 道具が出たらファンファーレ
            if (session.LastRewards != null && session.LastRewards.Count > 0) Sfx.Play2D(SfxClips.Win, 0.6f);
        }

        void DrawFlow()
        {
            var s = session.Strings;
            var run = session.Run;
            if (run.IsOver)
            {
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

        string TagNames(SymbolTag tags)
        {
            var names = new List<string>();
            foreach (SymbolTag t in System.Enum.GetValues(typeof(SymbolTag)))
                if (t != SymbolTag.None && tags.HasAny(t)) names.Add(session.Strings.Get("tag." + t));
            return string.Join("・", names);
        }
    }
}
