using System;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Net
{
    public enum NetMode
    {
        Solo,
        Host,
        Client,
    }

    /// <summary>
    /// 始める前の画面（IMGUI の仮 UI）：ひとりで遊ぶ・ホストになる・参加する（相手の IP アドレス）。
    /// 起動オプション -solo / -host / -join アドレス でも選べる（自動テスト・2 窓での確認用）。
    /// 今はローカルネットワーク（FishNet の Tugboat）でつなぐ。Steam 経由（FishySteamworks）は次の段階。
    /// </summary>
    public sealed class NetLobby : MonoBehaviour
    {
        StringTable strings;
        Action<NetMode, string> onChoose;
        string address = "127.0.0.1";
        GUIStyle title;

        public bool Visible { get; private set; } = true;

        public void Init(StringTable table, Action<NetMode, string> choose)
        {
            strings = table;
            onChoose = choose;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-solo") Choose(NetMode.Solo);
                else if (args[i] == "-host") Choose(NetMode.Host);
                else if (args[i] == "-join") Choose(NetMode.Client, i + 1 < args.Length ? args[i + 1] : address);
            }
        }

        public void Choose(NetMode mode, string to = null)
        {
            if (!Visible) return;
            Visible = false;
            onChoose?.Invoke(mode, to ?? address);
        }

        void OnGUI()
        {
            if (!Visible || strings == null) return;
            if (title == null) title = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            var area = new Rect(Screen.width / 2f - 220, Screen.height / 2f - 170, 440, 340);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, area.width - 40, area.height - 32));
            GUILayout.Label("Spin the Scrap", title, GUILayout.Height(48));
            GUILayout.Space(8);
            if (GUILayout.Button(strings.Get("lobby.solo"), GUILayout.Height(40))) Choose(NetMode.Solo);
            GUILayout.Space(6);
            if (GUILayout.Button(strings.Get("lobby.host"), GUILayout.Height(40))) Choose(NetMode.Host);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, GUILayout.Height(36), GUILayout.Width(200));
            if (GUILayout.Button(strings.Get("lobby.join"), GUILayout.Height(36))) Choose(NetMode.Client);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label(strings.Get("lobby.note"));
            GUILayout.EndArea();
        }
    }
}
