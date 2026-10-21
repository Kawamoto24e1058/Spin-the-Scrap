using System.Collections.Generic;
using UnityEngine;

namespace SpinTheScrap.UI
{
    /// <summary>
    /// 画面の見た目の共通設定（IMGUI）：日本語の OS フォント、配色、角丸の背景、ボタン。
    /// 画像素材は使わず、角丸などのテクスチャもコードで作る。
    /// 画面の文字は IMGUI の既定のフォント（日本語は OS が補う）。3D の文字（TextMesh）は WorldFont を使う
    /// （同じフォントを両方で使うと、文字の画像が作り直されたときに IMGUI の文字が化ける）。
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Ink = new Color(0.94f, 0.93f, 0.89f);
        public static readonly Color Muted = new Color(0.64f, 0.64f, 0.62f);
        public static readonly Color PanelColor = new Color(0.06f, 0.065f, 0.07f, 0.93f);
        public static readonly Color CardColor = new Color(0.13f, 0.135f, 0.145f, 0.96f);
        public static readonly Color Gold = new Color(1f, 0.8f, 0.28f);
        public static readonly Color Danger = new Color(0.95f, 0.36f, 0.3f);
        public static readonly Color Good = new Color(0.45f, 0.9f, 0.5f);

        /// <summary>日本語が出る OS のフォント（macOS・Windows・Linux の順に探す）。</summary>
        static readonly string[] FontNames =
        {
            "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Yu Gothic UI", "Meiryo UI", "Meiryo",
            "Noto Sans CJK JP", "Noto Sans JP", "Arial Unicode MS",
        };

        static Font font;
        static Texture2D rounded;
        static Texture2D roundedSmall;
        static readonly Dictionary<Color, GUIStyle> accentButtons = new Dictionary<Color, GUIStyle>();

        /// <summary>3D の文字（台の看板など）用。</summary>
        public static Font WorldFont
        {
            get
            {
                if (font == null) font = Font.CreateDynamicFontFromOSFont(FontNames, 64);
                return font;
            }
        }

        public static GUIStyle Title { get; private set; }
        public static GUIStyle Heading { get; private set; }
        public static GUIStyle Body { get; private set; }
        public static GUIStyle Small { get; private set; }
        public static GUIStyle Big { get; private set; }
        /// <summary>見出しの横の数字（折り返さない）。</summary>
        public static GUIStyle Stat { get; private set; }
        public static GUIStyle Button { get; private set; }
        public static GUIStyle Tab { get; private set; }
        public static GUIStyle TabActive { get; private set; }
        public static GUIStyle Card { get; private set; }
        public static GUIStyle Panel { get; private set; }
        public static GUIStyle HudLabel { get; private set; }
        public static GUIStyle HudCenter { get; private set; }

        /// <summary>OnGUI の中で最初に呼ぶ。</summary>
        public static void Ensure()
        {
            if (Title != null && rounded != null) return;
            rounded = MakeRounded(32, 10, 0.12f);
            roundedSmall = MakeRounded(16, 5, 0.18f);

            Body = Text(15, Ink);
            Body.wordWrap = true;
            Small = Text(13, Muted);
            Small.wordWrap = true;
            Heading = Text(17, Gold, FontStyle.Bold);
            Title = Text(26, Ink, FontStyle.Bold);
            Big = Text(22, Ink, FontStyle.Bold);
            Big.wordWrap = true;
            Stat = Text(19, Ink, FontStyle.Bold);
            Stat.alignment = TextAnchor.MiddleRight;
            Stat.padding = new RectOffset(2, 2, 8, 2);

            Panel = new GUIStyle { normal = { background = rounded }, border = new RectOffset(10, 10, 10, 10), padding = new RectOffset(18, 18, 16, 16) };
            Card = new GUIStyle(Panel) { padding = new RectOffset(14, 14, 10, 12), margin = new RectOffset(0, 0, 4, 8) };

            Button = MakeButton(new Color(0.24f, 0.25f, 0.27f), Ink);
            Tab = MakeButton(new Color(0.16f, 0.165f, 0.18f), Muted);
            Tab.fontSize = 14;
            Tab.wordWrap = false;
            TabActive = MakeButton(new Color(0.3f, 0.3f, 0.32f), Ink);
            TabActive.fontSize = 14;
            TabActive.fontStyle = FontStyle.Bold;

            HudLabel = Text(16, Color.white);
            HudCenter = new GUIStyle(HudLabel) { alignment = TextAnchor.MiddleCenter };
        }

        static GUIStyle Text(int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var s = new GUIStyle { fontSize = size, fontStyle = style, richText = true, padding = new RectOffset(2, 2, 2, 2) };
            s.normal.textColor = color;
            return s;
        }

        static GUIStyle MakeButton(Color background, Color text)
        {
            var s = new GUIStyle
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(5, 5, 5, 5),
                padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(3, 3, 3, 3),
                wordWrap = true,
            };
            s.normal.background = Tinted(roundedSmall, background);
            s.hover.background = Tinted(roundedSmall, background * 1.35f);
            s.active.background = Tinted(roundedSmall, background * 0.75f);
            s.normal.textColor = text;
            s.hover.textColor = Color.white;
            s.active.textColor = Color.white;
            return s;
        }

        static readonly Dictionary<Color, GUIStyle> accentTabs = new Dictionary<Color, GUIStyle>();

        /// <summary>選んでいるタブ（台の色）。</summary>
        public static GUIStyle TabAccent(Color color)
        {
            Ensure();
            if (accentTabs.TryGetValue(color, out var style)) return style;
            style = MakeButton(color * 0.75f, Color.white);
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 14;
            style.wordWrap = false;
            accentTabs[color] = style;
            return style;
        }

        /// <summary>台の色のボタン（回す・換金など、押してほしいボタン）。</summary>
        public static GUIStyle AccentButton(Color color)
        {
            Ensure();
            if (accentButtons.TryGetValue(color, out var style)) return style;
            style = MakeButton(color * 0.8f, Color.white);
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 17;
            accentButtons[color] = style;
            return style;
        }

        /// <summary>色付きの角丸の四角（カード・ゲージの背景）。</summary>
        public static void Fill(Rect rect, Color color, bool small = false)
        {
            Ensure();
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, small ? 3f : 8f);
        }

        /// <summary>横長のゲージ。</summary>
        public static void Bar(Rect rect, float ratio, Color color)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.55f), true);
            if (ratio > 0f) Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), color, true);
        }

        /// <summary>明るい物の手前でも読めるよう、黒い影を付けて描く。</summary>
        public static void ShadowLabel(Rect rect, string text, GUIStyle style)
        {
            var color = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x - 1f, rect.y + 1f, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        /// <summary>色を IMGUI のリッチテキストにする。</summary>
        public static string Colored(string text, Color color) => "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";

        /// <summary>角丸の白い四角（ふちを少し明るく）。GUI の背景に使う。</summary>
        static Texture2D MakeRounded(int size, int radius, float borderBoost)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - d + 0.5f);
                float edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                float shade = edge < 1.5f || d > radius - 1.5f ? 1f : 1f - borderBoost;
                pixels[y * size + x] = new Color(shade, shade, shade, alpha);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.DontSave;
            return tex;
        }

        static Texture2D Tinted(Texture2D source, Color color)
        {
            var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = source.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(pixels[i].r * color.r, pixels[i].g * color.g, pixels[i].b * color.b, pixels[i].a * Mathf.Max(color.a, 0.01f));
            tex.SetPixels(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.DontSave;
            return tex;
        }

        /// <summary>カード（角丸の背景）を描いて、その中で縦に並べ始める。</summary>
        public static void BeginCard(Color? tint = null, params GUILayoutOption[] options)
        {
            Ensure();
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = tint ?? CardColor;
            GUILayout.BeginVertical(Card, options);
            GUI.backgroundColor = prev;
        }

        public static void EndCard() => GUILayout.EndVertical();
    }
}
