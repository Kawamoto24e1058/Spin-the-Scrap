using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.Session;
using UnityEngine;

namespace SpinTheScrap.UI
{
    /// <summary>
    /// 拠点メニュー（IMGUI の仮 UI）。店（即売）・リールへの投入・闇市（レリック / 廃棄）・スロット・返済日をまとめて操作する。
    /// フェーズ 0 では「潜って運んで賭ける」ループを 1 人で通すための検証用。
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

        public bool Visible { get; set; }

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

            GUILayout.Label(s.Get("base.title") + (run.IsEndless ? "  [" + s.Get("base.endless") + "]" : ""));
            GUILayout.Label(s.Format("base.status", run.Cycle, run.DivesCompletedInCycle + (run.IsPaydayDue ? 0 : 1),
                run.Config.DivesPerCycle, session.Money(run.Money), session.Money(run.Quota)));
            if (run.PendingEnemyBonus > 0) GUILayout.Label(s.Format("base.enemy_bonus", run.PendingEnemyBonus));
            if (!string.IsNullOrEmpty(session.Message)) GUILayout.Label(session.Message);

            GUILayout.BeginHorizontal();
            float columnWidth = (area.width - 40) / 2f;

            GUILayout.BeginVertical(GUILayout.Width(columnWidth));
            leftScroll = GUILayout.BeginScrollView(leftScroll);
            DrawHaul();
            GUILayout.Space(12);
            DrawReel();
            GUILayout.Space(12);
            DrawRelics();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(columnWidth));
            rightScroll = GUILayout.BeginScrollView(rightScroll);
            DrawSlot();
            GUILayout.Space(16);
            DrawFlow();
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
                GUILayout.Label(session.SymbolName(item.SymbolId) + condition, GUILayout.Width(200));
                if (GUILayout.Button(s.Format("base.sell", session.Money(session.SellPriceOf(item))))) pending = () => { session.Sell(item); Sfx.Play2D(SfxClips.Coin, 0.6f); };
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
                GUILayout.Label(session.SymbolName(group.Key) + " ×" + group.Count(), GUILayout.Width(200));
                GUI.enabled = run.Money >= run.RemovalFee;
                int instanceId = group.Last().InstanceId;
                if (GUILayout.Button(s.Format("base.remove", session.Money(run.RemovalFee))))
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
                GUILayout.Label(s.Get(def.NameKey) + "：" + s.Get(def.DescriptionKey), GUILayout.Width(280));
                GUI.enabled = run.Money >= def.Price && run.Relics.Count < run.Config.MaxRelics;
                if (GUILayout.Button(s.Format("base.buy", session.Money(def.Price)))) pending = () => { if (run.TryBuyRelic(id)) Sfx.Play2D(SfxClips.Coin, 0.6f, 0.8f); };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        void DrawSlot()
        {
            var s = session.Strings;
            var run = session.Run;
            GUI.enabled = run.CanSpin;
            if (GUILayout.Button(s.Format("base.spin", session.Money(run.CurrentSpinCost)), GUILayout.Height(40))) pending = SpinWithSound;
            GUI.enabled = true;

            var spin = session.LastSpin;
            if (spin == null) return;

            GUILayout.Label(s.Format("base.last_spin", session.Money(spin.Total), spin.Lines.Count, spin.BoardMultiplier));
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
                               (cell.Destroyed ? "×" : "+" + session.Money(cell.Value)) +
                               (cell.Triggers > 1 ? " ↻" + cell.Triggers : "");
                    }
                    GUILayout.Box(text, cellStyle, GUILayout.Width(96), GUILayout.Height(52));
                    GUI.color = prev;
                }
                GUILayout.EndHorizontal();
            }
            foreach (var line in spin.Lines)
                GUILayout.Label("Line " + (line.LineIndex + 1) + "  ×" + line.Length + "  " + TagNames(line.SharedTags) + "  +" + session.Money(line.Bonus));
            if (spin.CornerBonus > 0) GUILayout.Label("Corners  +" + session.Money(spin.CornerBonus));
        }

        void SpinWithSound()
        {
            if (!session.Spin()) return;
            Sfx.Play2D(SfxClips.Spin, 0.7f);
            var spin = session.LastSpin;
            // 回す費用より多く当たったらファンファーレ
            if (spin.Total >= session.Run.CurrentSpinCost * 2) Sfx.Play2D(SfxClips.Win, 0.6f);
        }

        void DrawFlow()
        {
            var s = session.Strings;
            var run = session.Run;
            if (run.IsFailed)
            {
                if (GUILayout.Button(s.Get("base.new_run"), GUILayout.Height(36))) pending = session.NewRun;
                return;
            }
            if (run.IsPaydayDue)
            {
                if (GUILayout.Button(s.Format("base.payday", session.Money(run.Quota)), GUILayout.Height(36))) pending = session.ResolvePayday;
                return;
            }
            if (GUILayout.Button(s.Get("base.next_dive"), GUILayout.Height(36)))
            {
                pending = () =>
                {
                    session.FinishDive();
                    if (!run.IsPaydayDue) Visible = false;
                };
            }
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
