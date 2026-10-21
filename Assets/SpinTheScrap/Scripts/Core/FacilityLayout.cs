using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    public enum RoomKind
    {
        /// <summary>脱出地点がある部屋。潜行はここから始まる。</summary>
        Entrance,
        /// <summary>柱が並ぶ広間。</summary>
        Hall,
        /// <summary>棚とカウンターが並ぶ倉庫。物を置ける高い場所が多い。</summary>
        Storage,
        /// <summary>中 2 階と階段・坂がある部屋。重い物を持って上り下りする。</summary>
        Loft,
        /// <summary>ボス階層の戦場。太い柱と低い壁が並び、巨体を柱にぶつけて隙を作る。</summary>
        Arena,
        /// <summary>ボス階層のいちばん奥。ボスがいて、奥に最後のスロット台がある。</summary>
        BossLair,
    }

    public enum Direction
    {
        North,
        East,
        South,
        West,
    }

    /// <summary>施設の部屋 1 つ。格子の座標（X, Y）と、隣の部屋へのドアを持つ。</summary>
    public sealed class RoomPlan
    {
        readonly bool[] doors = new bool[4];

        public int X { get; }
        public int Y { get; }
        public RoomKind Kind { get; internal set; }
        /// <summary>入口から何部屋目か（入口は 0）。奥ほど珍しいガラクタが出る。</summary>
        public int Depth { get; internal set; }

        public RoomPlan(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool HasDoor(Direction direction) => doors[(int)direction];
        internal void SetDoor(Direction direction) => doors[(int)direction] = true;
    }

    /// <summary>
    /// 1 回の潜行の施設の間取り。部屋を格子上で木の形につなぐ（ループなし、入口から全部屋へ行ける）。
    /// シードだけで決まるので、フェーズ 1 ではホストがシードを配れば全員が同じ間取りを組み立てられる。
    /// 入口は Y = 0 の行にあり、施設は Y が増える方向（北）へ広がる。
    /// </summary>
    public sealed class FacilityLayout
    {
        static readonly int[] Dx = { 0, 1, 0, -1 };
        static readonly int[] Dy = { 1, 0, -1, 0 };

        readonly List<RoomPlan> rooms = new List<RoomPlan>();
        readonly Dictionary<long, RoomPlan> byCell = new Dictionary<long, RoomPlan>();

        public IReadOnlyList<RoomPlan> Rooms => rooms;
        public RoomPlan Entrance => rooms[0];
        public int MaxDepth { get; private set; }

        FacilityLayout() { }

        public RoomPlan At(int x, int y) => byCell.TryGetValue(Key(x, y), out var room) ? room : null;

        public RoomPlan Neighbor(RoomPlan room, Direction direction) =>
            At(room.X + Dx[(int)direction], room.Y + Dy[(int)direction]);

        public static Direction Opposite(Direction direction) => (Direction)(((int)direction + 2) % 4);

        /// <summary>
        /// from から to までドアを通ってたどる部屋の列（両端を含む）。間取りは木なので道は 1 通り。
        /// 敵が部屋をまたいで移動するときに使う。
        /// </summary>
        public List<RoomPlan> Path(RoomPlan from, RoomPlan to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            var previous = new Dictionary<RoomPlan, RoomPlan> { { from, null } };
            var queue = new Queue<RoomPlan>();
            queue.Enqueue(from);
            while (queue.Count > 0 && !previous.ContainsKey(to))
            {
                var room = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!room.HasDoor((Direction)d)) continue;
                    var next = Neighbor(room, (Direction)d);
                    if (next == null || previous.ContainsKey(next)) continue;
                    previous[next] = room;
                    queue.Enqueue(next);
                }
            }
            var path = new List<RoomPlan>();
            if (!previous.ContainsKey(to)) return path;
            for (var r = to; r != null; r = previous[r]) path.Add(r);
            path.Reverse();
            return path;
        }

        /// <summary>隣り合う 2 部屋の間のドアの向き（a から見て）。ドアがなければ null。</summary>
        public static Direction? DoorBetween(RoomPlan a, RoomPlan b)
        {
            for (int d = 0; d < 4; d++)
                if (a.X + Dx[d] == b.X && a.Y + Dy[d] == b.Y && a.HasDoor((Direction)d)) return (Direction)d;
            return null;
        }

        /// <summary>ランのシード・階層・やり直し回数から、その階層の間取りのシードを決める（全員が同じ値になる）。</summary>
        public static ulong SeedFor(ulong runSeed, int floor, int attempt)
        {
            var rng = new Rng(runSeed ^ ((ulong)floor * 0x9E3779B97F4A7C15UL) ^ ((ulong)(attempt + 1) * 0xC2B2AE3D27D4EB4FUL));
            return rng.NextULong();
        }

        public static FacilityLayout Generate(ulong seed, int roomCount)
        {
            if (roomCount < 1) throw new ArgumentOutOfRangeException(nameof(roomCount));
            var rng = new Rng(seed);
            var layout = new FacilityLayout();
            var entrance = layout.Add(0, 0, RoomKind.Entrance, 0);

            while (layout.rooms.Count < roomCount)
            {
                // 空いている隣のマスを持つ部屋からランダムに選んで広げる。南（入口より手前）には広げない
                var candidates = new List<(RoomPlan room, Direction dir)>();
                foreach (var room in layout.rooms)
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = room.X + Dx[d], ny = room.Y + Dy[d];
                        if (ny < 0 || layout.At(nx, ny) != null) continue;
                        candidates.Add((room, (Direction)d));
                    }

                var pick = candidates[rng.Next(candidates.Count)];
                int px = pick.room.X + Dx[(int)pick.dir], py = pick.room.Y + Dy[(int)pick.dir];
                var added = layout.Add(px, py, RoomKind.Hall, pick.room.Depth + 1);
                pick.room.SetDoor(pick.dir);
                added.SetDoor(Opposite(pick.dir));
            }

            layout.AssignKinds(rng);
            return layout;
        }

        /// <summary>
        /// ボス階層の決まった間取り：入口 → 中央の戦場 → 奥のボスの部屋。中央の左右にも戦場がある。
        /// </summary>
        public static FacilityLayout CreateBossFloor()
        {
            var layout = new FacilityLayout();
            var entrance = layout.Add(0, 0, RoomKind.Entrance, 0);
            var center = layout.Add(0, 1, RoomKind.Arena, 1);
            var west = layout.Add(-1, 1, RoomKind.Arena, 2);
            var east = layout.Add(1, 1, RoomKind.Arena, 2);
            var lair = layout.Add(0, 2, RoomKind.BossLair, 2);
            Connect(entrance, center, Direction.North);
            Connect(center, west, Direction.West);
            Connect(center, east, Direction.East);
            Connect(center, lair, Direction.North);
            return layout;
        }

        static void Connect(RoomPlan from, RoomPlan to, Direction dir)
        {
            from.SetDoor(dir);
            to.SetDoor(Opposite(dir));
        }

        RoomPlan Add(int x, int y, RoomKind kind, int depth)
        {
            var room = new RoomPlan(x, y) { Kind = kind, Depth = depth };
            rooms.Add(room);
            byCell[Key(x, y)] = room;
            if (depth > MaxDepth) MaxDepth = depth;
            return room;
        }

        /// <summary>入口以外に種類を割り当てる。3 部屋以上なら中 2 階を必ず 1 つ、なるべく奥に置く。</summary>
        void AssignKinds(Rng rng)
        {
            var others = rooms.GetRange(1, rooms.Count - 1);
            if (others.Count == 0) return;

            if (rooms.Count >= 3)
            {
                var deepest = others.FindAll(r => r.Depth == MaxDepth);
                deepest[rng.Next(deepest.Count)].Kind = RoomKind.Loft;
            }
            foreach (var room in others)
                if (room.Kind != RoomKind.Loft)
                    room.Kind = rng.Next(2) == 0 ? RoomKind.Hall : RoomKind.Storage;
        }

        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    }

    /// <summary>潜行で落ちているガラクタの出やすさ（要件書 4.3「奥ほど価値が高い」）。</summary>
    public static class LootTable
    {
        /// <summary>部屋の深さに応じた出やすさの重み。深いほど珍しい物の割合が増える。</summary>
        public static double Weight(Rarity rarity, int depth)
        {
            if (depth < 0) throw new ArgumentOutOfRangeException(nameof(depth));
            switch (rarity)
            {
                case Rarity.Common: return 6;
                case Rarity.Uncommon: return 3 + depth;
                case Rarity.Rare: return 0.5 + depth;
                default: throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null);
            }
        }

        /// <summary>部屋に置くガラクタの数。入口は少なく、奥の部屋ほど多い。</summary>
        public static int ScrapCountFor(RoomKind kind, int depth)
        {
            if (kind == RoomKind.Entrance) return 2;
            // ボス階層では、ガラクタはほとんど拾えない
            if (kind == RoomKind.Arena || kind == RoomKind.BossLair) return 1;
            return Math.Min(4 + depth, 7);
        }
    }
}
