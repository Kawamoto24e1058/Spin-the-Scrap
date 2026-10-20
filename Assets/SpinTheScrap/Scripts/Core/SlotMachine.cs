using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>3 行 × 5 列の盤面（要件書 5.1）。インデックスは row * Columns + column。</summary>
    public static class Board
    {
        public const int Rows = 3;
        public const int Columns = 5;
        public const int CellCount = Rows * Columns;

        public static int Index(int row, int column) => row * Columns + column;
        public static int RowOf(int index) => index / Columns;
        public static int ColumnOf(int index) => index % Columns;

        /// <summary>判定ライン：横 3 本 + V 字 + 逆 V 字。左端から連続する同タグを数える。</summary>
        public static readonly int[][] Paylines =
        {
            new[] { Index(0, 0), Index(0, 1), Index(0, 2), Index(0, 3), Index(0, 4) },
            new[] { Index(1, 0), Index(1, 1), Index(1, 2), Index(1, 3), Index(1, 4) },
            new[] { Index(2, 0), Index(2, 1), Index(2, 2), Index(2, 3), Index(2, 4) },
            new[] { Index(0, 0), Index(1, 1), Index(2, 2), Index(1, 3), Index(0, 4) },
            new[] { Index(2, 0), Index(1, 1), Index(0, 2), Index(1, 3), Index(2, 4) },
        };

        public static readonly int[] Corners = { Index(0, 0), Index(0, 4), Index(2, 0), Index(2, 4) };
    }

    public sealed class CellResult
    {
        public SymbolInstance Instance { get; internal set; }
        public double Value { get; internal set; }
        public bool Destroyed { get; internal set; }
        /// <summary>効果の発動回数（1 + 再発動された回数）。</summary>
        public int Triggers { get; internal set; }
        public bool IsEmpty => Instance == null;
    }

    public sealed class LineHit
    {
        public int LineIndex { get; }
        public int Length { get; }
        public SymbolTag SharedTags { get; }
        public double Bonus { get; }

        public LineHit(int lineIndex, int length, SymbolTag sharedTags, double bonus)
        {
            LineIndex = lineIndex;
            Length = length;
            SharedTags = sharedTags;
            Bonus = bonus;
        }
    }

    public sealed class SpinResult
    {
        public CellResult[] Cells { get; } = new CellResult[Board.CellCount];
        public List<LineHit> Lines { get; } = new List<LineHit>();
        public double CornerBonus { get; internal set; }
        public double BoardMultiplier { get; internal set; } = 1;
        public int LineThreshold { get; internal set; }
        public double Total { get; internal set; }
        public int EnemyBonus { get; internal set; }

        public IEnumerable<SymbolInstance> DestroyedInstances
        {
            get
            {
                foreach (var c in Cells)
                    if (c.Destroyed && c.Instance != null) yield return c.Instance;
            }
        }

        /// <summary>投入者ごとのシンボル値の合計（盤面倍率適用後）。「誰のガラクタで当たったか」の表示用。</summary>
        public Dictionary<int, double> ContributionByOwner()
        {
            var map = new Dictionary<int, double>();
            foreach (var c in Cells)
            {
                if (c.Instance == null) continue;
                map.TryGetValue(c.Instance.OwnerId, out var v);
                map[c.Instance.OwnerId] = v + c.Value * BoardMultiplier;
            }
            return map;
        }
    }

    /// <summary>スロットの抽選と役判定。状態を持たない純粋なロジック。</summary>
    public static class SlotMachine
    {
        public const int BaseLineThreshold = 3;
        public const int MinLineThreshold = 2;
        public const double CornerMultiplier = 2;

        /// <summary>ライン長ごとのボーナス係数（ライン上の値合計に掛けて加算）。</summary>
        public static double LineFactor(int length)
        {
            switch (length)
            {
                case 2: return 0.5;
                case 3: return 1;
                case 4: return 2;
                case 5: return 4;
                default: return 0;
            }
        }

        /// <summary>リールの束から最大 15 個を抽選し、盤面のランダムな位置に置く。足りない分は空きマス（null）。</summary>
        public static SymbolInstance[] Draw(IReadOnlyList<SymbolInstance> reel, Rng rng)
        {
            var pool = new List<SymbolInstance>(reel);
            rng.Shuffle(pool);
            var positions = new List<int>(Board.CellCount);
            for (int i = 0; i < Board.CellCount; i++) positions.Add(i);
            rng.Shuffle(positions);

            var cells = new SymbolInstance[Board.CellCount];
            int take = Math.Min(pool.Count, Board.CellCount);
            for (int k = 0; k < take; k++) cells[positions[k]] = pool[k];
            return cells;
        }

        public static SpinResult Evaluate(SymbolInstance[] cells, ContentDatabase db, IReadOnlyList<RelicDefinition> relics, Rng rng)
        {
            if (cells == null || cells.Length != Board.CellCount)
                throw new ArgumentException("cells must have " + Board.CellCount + " entries", nameof(cells));
            relics = relics ?? Array.Empty<RelicDefinition>();

            int n = Board.CellCount;
            var defs = new SymbolDefinition[n];
            var tags = new SymbolTag[n];
            var add = new double[n];
            var mul = new double[n];
            var destroyed = new bool[n];
            var triggers = new int[n];
            for (int i = 0; i < n; i++)
            {
                mul[i] = 1;
                if (cells[i] == null) continue;
                defs[i] = db.GetSymbol(cells[i].SymbolId);
                tags[i] = defs[i].Tags;
                triggers[i] = 1;
            }

            // 1) 再発動の割り当て。盤面の初期状態だけで決め、連鎖させない。
            for (int i = 0; i < n; i++)
            {
                if (defs[i] == null) continue;
                foreach (var e in defs[i].Effects)
                {
                    if (e.Op != EffectOp.RetriggerSameColumnTag) continue;
                    if (CountFor(e.Condition, i, tags, rng) < e.Condition.MinCount) continue;
                    int col = Board.ColumnOf(i);
                    for (int r = 0; r < Board.Rows; r++)
                    {
                        int j = Board.Index(r, col);
                        if (j != i && tags[j].HasAny(e.TargetTag)) triggers[j]++;
                    }
                }
            }

            // 2) 効果の適用。加算・乗算・盤面倍率を別々に集計する。
            double boardMul = 1;
            int widen = 0;
            int enemies = 0;
            var firedOnce = new HashSet<EffectDefinition>();
            for (int i = 0; i < n; i++)
            {
                if (defs[i] == null) continue;
                foreach (var e in defs[i].Effects)
                {
                    if (e.Op == EffectOp.RetriggerSameColumnTag) continue;
                    for (int t = 0; t < triggers[i]; t++)
                    {
                        if (e.OncePerSpin && firedOnce.Contains(e)) break;
                        int count = CountFor(e.Condition, i, tags, rng);
                        bool pass = count >= e.Condition.MinCount;
                        var op = pass ? e.Op : e.ElseOp;
                        if (pass && e.OncePerSpin) firedOnce.Add(e);
                        Apply(op, e.Amount, pass ? count : 1, i, add, mul, destroyed, ref boardMul, ref widen, ref enemies);
                    }
                }
            }

            // 3) レリックによるタグ補正。
            foreach (var relic in relics)
            {
                for (int i = 0; i < n; i++)
                {
                    if (defs[i] == null || !tags[i].HasAny(relic.Tag)) continue;
                    if (relic.Kind == RelicKind.TagFlatBonus) add[i] += relic.Amount;
                    else if (relic.Kind == RelicKind.TagMultiplier) mul[i] *= relic.Amount;
                }
            }

            var result = new SpinResult();
            double sum = 0;
            var values = new double[n];
            for (int i = 0; i < n; i++)
            {
                double v = 0;
                if (defs[i] != null && !destroyed[i]) v = (defs[i].BaseValue + add[i]) * mul[i];
                values[i] = v;
                sum += v;
                result.Cells[i] = new CellResult
                {
                    Instance = cells[i],
                    Value = v,
                    Destroyed = destroyed[i],
                    Triggers = triggers[i],
                };
            }

            // 4) ライン判定。左端から、全セルが共通して持つタグが残る限り伸ばす。
            int threshold = Math.Max(MinLineThreshold, BaseLineThreshold - widen);
            result.LineThreshold = threshold;
            double lineTotal = 0;
            for (int li = 0; li < Board.Paylines.Length; li++)
            {
                var line = Board.Paylines[li];
                SymbolTag shared = tags[line[0]];
                if (shared == SymbolTag.None) continue;
                int len = 1;
                double runSum = values[line[0]];
                for (int k = 1; k < line.Length; k++)
                {
                    var next = shared & tags[line[k]];
                    if (next == SymbolTag.None) break;
                    shared = next;
                    runSum += values[line[k]];
                    len++;
                }
                if (len < threshold) continue;
                double bonus = runSum * LineFactor(len);
                lineTotal += bonus;
                result.Lines.Add(new LineHit(li, len, shared, bonus));
            }

            // 5) 四隅：4 つとも共通タグを持てばボーナス。
            SymbolTag cornerShared = ~SymbolTag.None;
            double cornerSum = 0;
            foreach (var c in Board.Corners)
            {
                cornerShared &= tags[c];
                cornerSum += values[c];
            }
            if (cornerShared != SymbolTag.None) result.CornerBonus = cornerSum * CornerMultiplier;

            result.BoardMultiplier = boardMul;
            result.EnemyBonus = enemies;
            result.Total = (sum + lineTotal + result.CornerBonus) * boardMul;
            return result;
        }

        static int CountFor(EffectCondition cond, int self, SymbolTag[] tags, Rng rng)
        {
            switch (cond.Kind)
            {
                case ConditionKind.Always:
                    return 1;
                case ConditionKind.Chance:
                    return rng.NextDouble() < cond.Probability ? 1 : 0;
                case ConditionKind.AdjacentTagCount:
                {
                    int row = Board.RowOf(self), col = Board.ColumnOf(self), count = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int r = row + dr, c = col + dc;
                        if (r < 0 || r >= Board.Rows || c < 0 || c >= Board.Columns) continue;
                        if (tags[Board.Index(r, c)].HasAny(cond.Tag)) count++;
                    }
                    return count;
                }
                case ConditionKind.SameColumnTagCount:
                {
                    int col = Board.ColumnOf(self), count = 0;
                    for (int r = 0; r < Board.Rows; r++)
                    {
                        int j = Board.Index(r, col);
                        if (j != self && tags[j].HasAny(cond.Tag)) count++;
                    }
                    return count;
                }
                case ConditionKind.BoardTagCount:
                {
                    int count = 0;
                    for (int j = 0; j < tags.Length; j++)
                        if (j != self && tags[j].HasAny(cond.Tag)) count++;
                    return count;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(cond), cond.Kind, null);
            }
        }

        static void Apply(EffectOp op, double amount, int count, int i, double[] add, double[] mul, bool[] destroyed,
            ref double boardMul, ref int widen, ref int enemies)
        {
            switch (op)
            {
                case EffectOp.None:
                case EffectOp.RetriggerSameColumnTag:
                    break;
                case EffectOp.AddFlat:
                    add[i] += amount * count;
                    break;
                case EffectOp.MultiplySelf:
                    mul[i] *= amount;
                    break;
                case EffectOp.MultiplyBoard:
                    boardMul *= amount;
                    break;
                case EffectOp.WidenLines:
                    widen = Math.Max(widen, (int)amount);
                    break;
                case EffectOp.DestroySelf:
                    destroyed[i] = true;
                    break;
                case EffectOp.SpawnEnemies:
                    enemies += (int)amount;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(op), op, null);
            }
        }
    }
}
