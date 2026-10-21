using System;
using System.Collections.Generic;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.UI;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>スロット台の見た目に必要な情報（Phase0Bootstrap が作って渡す）。</summary>
    public sealed class CabinetContext
    {
        public StringTable Strings;
        public Func<string, SymbolDefinition> Symbol;
        public Func<MachineKind, IReadOnlyList<SymbolInstance>> Reel;
        /// <summary>台の下の小さな表示（1 回の費用など）。</summary>
        public Func<MachineKind, string> Status;
    }

    /// <summary>
    /// スロット台の見た目と動き（仮素材のプリミティブ）。リールの窓には、リールに入っているガラクタの小さな模型が並び、
    /// 回すと列ごとに流れて左から止まる。止まったら、そろったラインを光らせ、壊れた物に × を出し、合計を数え上げる。
    /// 電飾は普段はゆっくり、回している間は速く、大当たりでは全部が点滅する。メニューで時間を止めていても動く（実時間）。
    /// </summary>
    public sealed class SlotCabinetView : MonoBehaviour
    {
        const float CellStep = 0.18f;
        const float CellFit = 0.13f;
        const float SpinSpeed = 16f;
        const float FirstStop = 0.6f;
        const float StopGap = 0.2f;
        const int StripLength = Board.Rows + 1;

        static readonly Color BodyColor = new Color(0.12f, 0.12f, 0.135f);
        static readonly Color Chrome = new Color(0.7f, 0.7f, 0.72f);
        static readonly Color BulbOn = new Color(1f, 0.82f, 0.45f);
        static readonly Color[] LineColors =
        {
            new Color(1f, 0.85f, 0.25f), new Color(0.35f, 0.9f, 1f), new Color(1f, 0.45f, 0.8f),
            new Color(0.5f, 1f, 0.45f), new Color(1f, 0.55f, 0.25f),
        };
        static readonly Dictionary<PrimitiveType, Mesh> Meshes = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<Color, Material> CellMaterials = new Dictionary<Color, Material>();

        sealed class Cell
        {
            public Transform Root;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public string SymbolId;
            public Vector3 Scale;
            public float Spin;
        }

        MachineKind kind;
        CabinetContext context;
        Color color;
        Transform window;
        readonly Cell[,] strips = new Cell[Board.Columns, StripLength];
        readonly float[] position = new float[Board.Columns];
        readonly int[] steps = new int[Board.Columns];
        readonly bool[] stopped = new bool[Board.Columns];
        readonly float[] bounce = new float[Board.Columns];
        readonly List<GameObject> overlays = new List<GameObject>();
        readonly List<Material> bulbs = new List<Material>();
        SpinResult result;
        MachineOutcome outcome;
        float spinStart = -100f;
        float revealTime = -100f;
        bool revealed = true;
        Transform leverPivot;
        Light glow;
        Light windowLight;
        TextMesh marquee;
        TextMesh led;
        Material marqueeFace;
        string marqueeText;
        float nextStatusTime;
        System.Random random = new System.Random();

        /// <summary>リールが回っている（結果をまだ見せていない）。</summary>
        public bool IsSpinning => !revealed;

        public static SlotCabinetView Build(GameObject root, MachineKind machineKind, Color machineColor, CabinetContext ctx)
        {
            var view = root.AddComponent<SlotCabinetView>();
            view.kind = machineKind;
            view.color = machineColor;
            view.context = ctx;
            view.BuildBody();
            view.ShowIdle();
            return view;
        }

        // ---- 組み立て ----

        void BuildBody()
        {
            var t = transform;
            var trim = color;
            // 台座・本体・側面の光る縁
            Part(t, "Cabinet", new Vector3(0f, 0.95f, 0f), new Vector3(1.16f, 1.9f, 0.82f), BodyColor, keepCollider: true);
            Part(t, "Plinth", new Vector3(0f, 0.08f, 0.02f), new Vector3(1.26f, 0.16f, 0.9f), BodyColor * 0.6f);
            foreach (float x in new[] { -0.6f, 0.6f })
            {
                Glow(t, "Side light", new Vector3(x, 1.05f, 0.36f), new Vector3(0.03f, 1.55f, 0.03f), trim, 2.2f);
                Part(t, "Side panel", new Vector3(x * 0.99f, 1.05f, 0f), new Vector3(0.04f, 1.7f, 0.8f), trim * 0.45f);
            }
            // 上の看板：光る面に機種名、周りに電球
            Part(t, "Marquee box", new Vector3(0f, 2.08f, 0.05f), new Vector3(1.24f, 0.44f, 0.64f), BodyColor);
            marqueeFace = MaterialUtil.CreateEmissive(trim, 1.6f);
            var face = Part(t, "Marquee face", new Vector3(0f, 2.08f, 0.375f), new Vector3(1.08f, 0.3f, 0.02f), trim);
            face.GetComponent<Renderer>().sharedMaterial = marqueeFace;
            marqueeText = context.Strings.Get("machine." + kind);
            marquee = Text(t, new Vector3(0f, 2.08f, 0.39f), 0.021f, new Color(0.06f, 0.05f, 0.05f), marqueeText);
            BuildBulbs(t, new Vector3(0f, 2.08f, 0.39f), 1.18f, 0.38f);

            // リールの窓：枠・クリーム色の背景・列の仕切り・中を照らす灯り
            var windowRoot = new GameObject("Reel window").transform;
            windowRoot.SetParent(t, false);
            windowRoot.localPosition = new Vector3(0f, 1.42f, 0.42f);
            window = windowRoot;
            float w = Board.Columns * CellStep, h = Board.Rows * CellStep;
            Part(window, "Frame", new Vector3(0f, 0f, -0.01f), new Vector3(w + 0.1f, h + 0.1f, 0.04f), Chrome * 0.55f);
            // 背景は少し暗めのクリーム色（明るすぎると模型が白く飛ぶ）
            var back = Part(window, "Back", new Vector3(0f, 0f, 0.012f), new Vector3(w, h, 0.01f), new Color(0.62f, 0.58f, 0.5f));
            back.GetComponent<Renderer>().sharedMaterial = MaterialUtil.CreateEmissive(new Color(0.62f, 0.58f, 0.5f), 0.08f);
            for (int c = 1; c < Board.Columns; c++)
                Part(window, "Divider", new Vector3(-w / 2f + c * CellStep, 0f, 0.03f), new Vector3(0.008f, h, 0.01f), BodyColor);
            windowLight = new GameObject("Window light").AddComponent<Light>();
            windowLight.transform.SetParent(window, false);
            windowLight.transform.localPosition = new Vector3(0f, -0.05f, 0.45f);
            windowLight.type = LightType.Point;
            windowLight.range = 0.9f;
            windowLight.intensity = 0.3f;
            windowLight.color = new Color(1f, 0.95f, 0.85f);
            windowLight.shadows = LightShadows.None;

            for (int c = 0; c < Board.Columns; c++)
            for (int j = 0; j < StripLength; j++)
                strips[c, j] = MakeCell(c);

            // 下の表示（費用・合計）と、受け皿
            Part(t, "LED panel", new Vector3(0f, 1.06f, 0.415f), new Vector3(0.7f, 0.12f, 0.02f), new Color(0.02f, 0.02f, 0.02f));
            led = Text(t, new Vector3(0f, 1.06f, 0.43f), 0.011f, new Color(1f, 0.62f, 0.2f), "");
            Part(t, "Button deck", new Vector3(0f, 0.93f, 0.5f), new Vector3(1.0f, 0.06f, 0.24f), BodyColor * 1.4f);
            for (int i = 0; i < 3; i++)
                Glow(t, "Button", new Vector3(-0.25f + i * 0.25f, 0.97f, 0.52f), new Vector3(0.12f, 0.03f, 0.08f), i == 1 ? trim : BulbOn * 0.8f, 1.4f);
            Part(t, "Tray", new Vector3(0f, 0.42f, 0.46f), new Vector3(0.76f, 0.1f, 0.18f), Chrome * 0.5f);

            // レバー（台に向かって右側。回すと下に引かれる）。台の正面は +Z なので、向かって右は -X
            leverPivot = new GameObject("Lever pivot").transform;
            leverPivot.SetParent(t, false);
            leverPivot.localPosition = new Vector3(-0.66f, 1.25f, 0.05f);
            Part(t, "Lever base", new Vector3(-0.64f, 1.25f, 0.05f), new Vector3(0.1f, 0.16f, 0.16f), Chrome * 0.7f);
            Part(leverPivot, "Lever", new Vector3(-0.03f, 0.25f, 0f), new Vector3(0.04f, 0.5f, 0.04f), Chrome);
            Glow(leverPivot, "Knob", new Vector3(-0.03f, 0.52f, 0f), Vector3.one * 0.11f, new Color(0.95f, 0.15f, 0.1f), 1.2f, PrimitiveType.Sphere);

            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(t, false);
            glow.transform.localPosition = new Vector3(0f, 0.6f, 1.3f);
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = 4.5f;
            glow.intensity = 1.6f;
        }

        void BuildBulbs(Transform parent, Vector3 center, float width, float height)
        {
            const int perSide = 7;
            var points = new List<Vector3>();
            for (int i = 0; i < perSide; i++) points.Add(new Vector3(-width / 2f + width * i / (perSide - 1), height / 2f, 0f));
            points.Add(new Vector3(width / 2f, 0f, 0f));
            for (int i = 0; i < perSide; i++) points.Add(new Vector3(width / 2f - width * i / (perSide - 1), -height / 2f, 0f));
            points.Add(new Vector3(-width / 2f, 0f, 0f));
            foreach (var p in points)
            {
                var bulb = Glow(parent, "Bulb", center + p, Vector3.one * 0.05f, BulbOn, 3f, PrimitiveType.Sphere);
                bulbs.Add(bulb.GetComponent<Renderer>().sharedMaterial);
            }
        }

        Cell MakeCell(int column)
        {
            var go = new GameObject("Cell");
            go.transform.SetParent(window, false);
            var cell = new Cell
            {
                Root = go.transform,
                Filter = go.AddComponent<MeshFilter>(),
                Renderer = go.AddComponent<MeshRenderer>(),
                Spin = (float)random.NextDouble() * 360f,
            };
            cell.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return cell;
        }

        static Transform Part(Transform parent, string name, Vector3 position, Vector3 scale, Color c, bool keepCollider = false,
            PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(c);
            if (!keepCollider) DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        static Transform Glow(Transform parent, string name, Vector3 position, Vector3 scale, Color c, float intensity,
            PrimitiveType shape = PrimitiveType.Cube)
        {
            var part = Part(parent, name, position, scale, c, false, shape);
            part.GetComponent<Renderer>().sharedMaterial = MaterialUtil.CreateEmissive(c, intensity);
            return part;
        }

        /// <summary>正面から読める 3D の文字。</summary>
        static TextMesh Text(Transform parent, Vector3 position, float size, Color c, string text)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TextMesh は -Z 向きに読めるので、台の正面に向ける
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = UiTheme.WorldFont;
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = c;
            mesh.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = UiTheme.WorldFont.material;
            return mesh;
        }

        // ---- 表示するガラクタ ----

        void SetCell(Cell cell, string symbolId)
        {
            cell.SymbolId = symbolId;
            if (symbolId == null)
            {
                cell.Renderer.enabled = false;
                return;
            }
            var def = context.Symbol(symbolId);
            ScrapFactory.LookOf(def, out var shape, out var scale, out var c);
            cell.Filter.sharedMesh = MeshOf(shape);
            cell.Renderer.sharedMaterial = MaterialOf(c);
            cell.Renderer.enabled = true;
            // 形ごとの元の大きさ（円柱・カプセルは高さ 2）をそろえて、窓のマスに収める
            float heightFactor = shape == PrimitiveType.Cylinder || shape == PrimitiveType.Capsule ? 2f : 1f;
            float extent = Mathf.Max(scale.x, scale.y * heightFactor, scale.z * 0.7f);
            float fit = CellFit / Mathf.Max(0.001f, extent);
            cell.Scale = new Vector3(scale.x * fit, scale.y * fit, Mathf.Min(scale.z * fit, 0.1f));
        }

        static Mesh MeshOf(PrimitiveType shape)
        {
            if (Meshes.TryGetValue(shape, out var mesh) && mesh != null) return mesh;
            // 形の元のメッシュだけ取り出す（組み込みのメッシュなので、物を消しても残る）。その場で消して画面に出さない
            var probe = GameObject.CreatePrimitive(shape);
            mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(probe);
            Meshes[shape] = mesh;
            return mesh;
        }

        static Material MaterialOf(Color c)
        {
            if (CellMaterials.TryGetValue(c, out var mat) && mat != null) return mat;
            mat = MaterialUtil.Create(c);
            CellMaterials[c] = mat;
            return mat;
        }

        string RandomSymbol()
        {
            var reel = context.Reel(kind);
            return reel == null || reel.Count == 0 ? null : reel[random.Next(reel.Count)].SymbolId;
        }

        /// <summary>止まっている間：最後の結果、なければリールから選んだ物を並べる。</summary>
        void ShowIdle()
        {
            for (int c = 0; c < Board.Columns; c++)
            {
                position[c] = 0f;
                stopped[c] = true;
                for (int j = 0; j < StripLength; j++)
                    SetCell(strips[c, j], j == 0 ? RandomSymbol() : ResultSymbol(j - 1, c) ?? RandomSymbol());
            }
        }

        string ResultSymbol(int row, int column)
        {
            if (result == null) return null;
            var cell = result.Cells[Board.Index(row, column)];
            return cell.IsEmpty ? null : cell.Instance.SymbolId;
        }

        // ---- 回す ----

        /// <summary>回した結果を見せる（リールを流して、左の列から止める）。</summary>
        public void Play(MachineOutcome spun)
        {
            outcome = spun;
            result = spun.Reading.Result;
            ClearOverlays();
            spinStart = Time.unscaledTime;
            revealed = false;
            revealTime = spinStart + FirstStop + StopGap * (Board.Columns - 1) + 0.25f;
            for (int c = 0; c < Board.Columns; c++)
            {
                stopped[c] = false;
                steps[c] = Mathf.FloorToInt(position[c]);
            }
        }

        void Update()
        {
            float now = Time.unscaledTime;
            UpdateReels(now);
            UpdateLever(now);
            UpdateLights(now);
            if (!revealed && now >= revealTime) Reveal();
            if (revealed && now >= nextStatusTime && context.Status != null && now - revealTime > 6f)
            {
                nextStatusTime = now + 0.5f;
                led.text = context.Status(kind);
            }
        }

        void UpdateReels(float now)
        {
            for (int c = 0; c < Board.Columns; c++)
            {
                if (!stopped[c])
                {
                    float stopAt = spinStart + FirstStop + StopGap * c;
                    if (now >= stopAt)
                    {
                        // 止める：結果を窓に並べ、少し跳ねさせる
                        stopped[c] = true;
                        position[c] = 0f;
                        steps[c] = 0;
                        for (int j = 0; j < StripLength; j++) SetCell(strips[c, j], j == 0 ? RandomSymbol() : ResultSymbol(j - 1, c));
                        bounce[c] = 1f;
                        Sfx.Play2D(SfxClips.Knock, 0.45f, 1.6f + c * 0.06f);
                    }
                    else
                    {
                        float speed = SpinSpeed * Mathf.Clamp01((now - spinStart) / 0.15f);
                        position[c] += speed * Time.unscaledDeltaTime;
                        int step = Mathf.FloorToInt(position[c]);
                        while (steps[c] < step)
                        {
                            // 1 マス下がった：中身を下へずらし、上から新しい物を入れる
                            steps[c]++;
                            for (int j = StripLength - 1; j > 0; j--) SetCell(strips[c, j], strips[c, j - 1].SymbolId);
                            SetCell(strips[c, 0], RandomSymbol());
                        }
                    }
                }
                bounce[c] = Mathf.MoveTowards(bounce[c], 0f, Time.unscaledDeltaTime * 5f);
                float frac = position[c] - Mathf.Floor(position[c]);
                float kick = Mathf.Sin(bounce[c] * Mathf.PI * 2f) * bounce[c] * 0.025f;
                for (int j = 0; j < StripLength; j++)
                {
                    var cell = strips[c, j];
                    float row = j - 1 + frac; // 0〜2 が窓の中
                    float visible = Mathf.Clamp01(Mathf.Min(row + 1f, 3f - row));
                    float x = -Board.Columns * CellStep / 2f + (c + 0.5f) * CellStep;
                    float y = (1f - row) * CellStep - kick;
                    cell.Root.localPosition = new Vector3(x, y, 0.07f);
                    bool destroyed = revealed && j > 0 && IsDestroyed(j - 1, c);
                    cell.Root.localScale = cell.Scale * visible * (destroyed ? 0.6f : 1f);
                    if (stopped[c]) cell.Spin += Time.unscaledDeltaTime * 35f;
                    cell.Root.localRotation = stopped[c] ? Quaternion.Euler(12f, cell.Spin, 0f) : Quaternion.Euler(70f * frac, 0f, 0f);
                }
            }
        }

        bool IsDestroyed(int row, int column)
        {
            if (result == null) return false;
            var cell = result.Cells[Board.Index(row, column)];
            return !cell.IsEmpty && cell.Destroyed;
        }

        void UpdateLever(float now)
        {
            float t = (now - spinStart) / 0.45f;
            float pull = t >= 0f && t <= 1f ? Mathf.Sin(t * Mathf.PI) : 0f;
            leverPivot.localRotation = Quaternion.Euler(60f * pull, 0f, 0f);
        }

        void UpdateLights(float now)
        {
            bool jackpot = revealed && outcome != null && outcome.Reading.Jackpot && now - revealTime < 4f;
            bool spinning = !revealed;
            float rate = jackpot ? 14f : spinning ? 10f : 3f;
            int step = Mathf.FloorToInt(now * rate);
            for (int i = 0; i < bulbs.Count; i++)
            {
                bool on = jackpot ? step % 2 == 0 : (i + step) % 3 == 0;
                MaterialUtil.SetEmission(bulbs[i], BulbOn, on ? 3.2f : 0.12f);
            }
            float pulse = spinning ? 2.2f + Mathf.Sin(now * 18f) * 0.8f : 1.6f;
            glow.intensity = jackpot ? (step % 2 == 0 ? 4f : 0.8f) : pulse;
            glow.color = jackpot ? Color.HSVToRGB(now * 1.5f % 1f, 0.7f, 1f) : color;
            MaterialUtil.SetEmission(marqueeFace, color, jackpot ? (step % 2 == 0 ? 3f : 0.6f) : 1.6f);
            if (marquee != null)
            {
                string text = jackpot ? context.Strings.Get("cabinet.jackpot") : marqueeText;
                if (marquee.text != text) marquee.text = text;
            }
        }

        // ---- 止まったあと ----

        void Reveal()
        {
            revealed = true;
            if (result == null) return;
            // そろったライン：マスの中心を光る棒でつなぐ
            foreach (var line in result.Lines)
            {
                var cells = Board.Paylines[line.LineIndex];
                var lineColor = LineColors[line.LineIndex % LineColors.Length];
                for (int k = 0; k + 1 < line.Length; k++)
                    AddLineSegment(CellCenter(cells[k]), CellCenter(cells[k + 1]), lineColor);
                for (int k = 0; k < line.Length; k++) AddMarker(CellCenter(cells[k]), lineColor);
            }
            // それぞれのマスの値と、壊れた物の ×
            for (int i = 0; i < result.Cells.Length; i++)
            {
                var cell = result.Cells[i];
                if (cell.IsEmpty) continue;
                var center = CellCenter(i);
                if (cell.Destroyed)
                    overlays.Add(Text(window, center + new Vector3(0f, 0f, 0.13f), 0.012f, UiTheme.Danger, "×").gameObject);
                else
                    overlays.Add(Text(window, center + new Vector3(0f, -0.062f, 0.12f), 0.0075f, new Color(0.3f, 0.12f, 0.02f),
                        "+" + NumberFormatter.Format(cell.Value, context.Strings.Language)).gameObject);
            }
            led.text = context.Strings.Format("cabinet.total", NumberFormatter.Format(result.Total, context.Strings.Language));
            if (result.Lines.Count > 0) Sfx.Play2D(SfxClips.Coin, 0.5f, 1.1f);
            if (outcome.Reading.Jackpot || outcome.Reading.Tier >= 3 || outcome.LevelUps.Count > 0) Sfx.Play2D(SfxClips.Win, 0.6f);
        }

        Vector3 CellCenter(int index)
        {
            int row = Board.RowOf(index), column = Board.ColumnOf(index);
            return new Vector3(-Board.Columns * CellStep / 2f + (column + 0.5f) * CellStep, (1 - row) * CellStep, 0f);
        }

        void AddLineSegment(Vector3 a, Vector3 b, Color c)
        {
            var bar = Glow(window, "Line", (a + b) / 2f + new Vector3(0f, 0f, 0.115f), Vector3.one, c, 4f);
            var delta = b - a;
            bar.localScale = new Vector3(Vector3.Distance(a, b), 0.022f, 0.006f);
            bar.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            overlays.Add(bar.gameObject);
        }

        void AddMarker(Vector3 center, Color c)
        {
            var dot = Glow(window, "Line dot", center + new Vector3(0f, 0f, 0.115f), Vector3.one * 0.04f, c, 4f, PrimitiveType.Sphere);
            overlays.Add(dot.gameObject);
        }

        void ClearOverlays()
        {
            foreach (var o in overlays)
                if (o != null) Destroy(o);
            overlays.Clear();
            if (led != null) led.text = "";
        }
    }
}
