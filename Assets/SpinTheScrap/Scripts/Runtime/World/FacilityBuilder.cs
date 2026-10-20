using System.Collections.Generic;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>ガラクタを置ける場所。Depth はその部屋の入口からの深さ。</summary>
    public struct ScrapSpot
    {
        public Vector3 Position;
        public int Depth;
        public bool Raised;
    }

    /// <summary>組み立てた施設。部屋ごとのガラクタ置き場を持つ。</summary>
    public sealed class BuiltFacility
    {
        public Transform Root;
        public readonly List<BuiltRoom> Rooms = new List<BuiltRoom>();
    }

    public sealed class BuiltRoom
    {
        public RoomPlan Plan;
        public readonly List<ScrapSpot> Spots = new List<ScrapSpot>();
        /// <summary>特大を置ける広い床（入口にはない）。</summary>
        public Vector3? HugeSpot;
    }

    /// <summary>
    /// FacilityLayout（Core）の間取りから、部屋をプリミティブで組み立てる。フェーズ 0〜2 の仮素材。
    /// 部屋の中心は (X, Y) × RoomSize。ドアは壁の中央にあり、ドアの前（十字の通路）には物を置かない。
    /// </summary>
    public static class FacilityBuilder
    {
        public const float RoomSize = 24f;
        public const float WallHeight = 4f;
        public const float DoorWidth = 3.2f;
        public const float DoorHeight = 3f;
        const float WallThickness = 0.3f;

        static readonly Color WallColor = new Color(0.22f, 0.25f, 0.21f);
        static readonly Color FloorColor = new Color(0.18f, 0.17f, 0.15f);
        static readonly Color PropColor = new Color(0.3f, 0.26f, 0.22f);
        static readonly Color EmergencyColor = new Color(0.5f, 1f, 0.6f);
        static readonly Color WarmColor = new Color(1f, 0.9f, 0.7f);

        public static Vector3 RoomCenter(RoomPlan room) => new Vector3(room.X * RoomSize, 0f, room.Y * RoomSize);

        public static BuiltFacility Build(FacilityLayout layout)
        {
            var facility = new BuiltFacility { Root = new GameObject("Facility").transform };
            foreach (var plan in layout.Rooms)
            {
                var root = new GameObject("Room " + plan.X + "," + plan.Y + " " + plan.Kind).transform;
                root.SetParent(facility.Root, false);
                root.localPosition = RoomCenter(plan);
                var room = new BuiltRoom { Plan = plan };
                BuildShell(root, layout, plan);
                switch (plan.Kind)
                {
                    case RoomKind.Entrance: BuildEntrance(root, room); break;
                    case RoomKind.Hall: BuildHall(root, room); break;
                    case RoomKind.Storage: BuildStorage(root, room); break;
                    case RoomKind.Loft: BuildLoft(root, room); break;
                }
                // 部屋ごとの置き場はローカル座標で書いているので、ワールド座標に直す
                for (int i = 0; i < room.Spots.Count; i++)
                {
                    var spot = room.Spots[i];
                    spot.Position = root.TransformPoint(spot.Position);
                    room.Spots[i] = spot;
                }
                if (room.HugeSpot.HasValue) room.HugeSpot = root.TransformPoint(room.HugeSpot.Value);
                facility.Rooms.Add(room);
            }
            return facility;
        }

        // ---- 床・天井・壁 ----

        static void BuildShell(Transform root, FacilityLayout layout, RoomPlan plan)
        {
            float half = RoomSize / 2f;
            Block(root, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(RoomSize, 0.2f, RoomSize), FloorColor);
            Block(root, "Ceiling", new Vector3(0f, WallHeight + 0.1f, 0f), new Vector3(RoomSize, 0.2f, RoomSize), WallColor);

            // 隣と共有する壁は二重にしないよう、南と西は常に自分で作り、北と東は隣がいないときだけ作る
            foreach (Direction dir in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
            {
                bool owns = dir == Direction.South || dir == Direction.West || layout.Neighbor(plan, dir) == null;
                if (!owns) continue;
                bool door = plan.HasDoor(dir) || (layout.Neighbor(plan, dir) is RoomPlan n && n.HasDoor(FacilityLayout.Opposite(dir)));
                Wall(root, dir, half, door);
            }
        }

        static void Wall(Transform root, Direction dir, float half, bool door)
        {
            bool alongX = dir == Direction.North || dir == Direction.South;
            float offset = dir == Direction.North || dir == Direction.East ? half : -half;
            Vector3 Pos(float along, float y) => alongX ? new Vector3(along, y, offset) : new Vector3(offset, y, along);
            Vector3 Size(float length, float height) => alongX ? new Vector3(length, height, WallThickness) : new Vector3(WallThickness, height, length);

            if (!door)
            {
                Block(root, "Wall " + dir, Pos(0f, WallHeight / 2f), Size(RoomSize + WallThickness, WallHeight), WallColor);
                return;
            }
            float side = (RoomSize + WallThickness - DoorWidth) / 2f;
            float sideCenter = DoorWidth / 2f + side / 2f;
            Block(root, "Wall " + dir + " L", Pos(-sideCenter, WallHeight / 2f), Size(side, WallHeight), WallColor);
            Block(root, "Wall " + dir + " R", Pos(sideCenter, WallHeight / 2f), Size(side, WallHeight), WallColor);
            Block(root, "Wall " + dir + " Top", Pos(0f, (DoorHeight + WallHeight) / 2f), Size(DoorWidth, WallHeight - DoorHeight), WallColor);
        }

        // ---- 部屋の中身（座標は部屋の中心からのローカル） ----

        static void BuildEntrance(Transform root, BuiltRoom room)
        {
            // 投げて越える低い壁と、物を置くカウンター
            Block(root, "Low wall", new Vector3(-4f, 0.6f, 1f), new Vector3(6f, 1.2f, 0.3f), WallColor);
            Block(root, "Counter A", new Vector3(-7f, 0.5f, 5f), new Vector3(4f, 1f, 1f), PropColor);
            Block(root, "Counter B", new Vector3(-9f, 0.5f, -2f), new Vector3(1f, 1f, 4f), PropColor);
            Block(root, "Pillar", new Vector3(-4f, 2f, 7f), new Vector3(0.6f, 4f, 0.6f), WallColor);

            PointLight(root, new Vector3(0f, 3.6f, -9.5f), WarmColor, 8f, 8f);
            PointLight(root, new Vector3(-6f, 3.6f, 6f), EmergencyColor, 9f, 6f);

            Floor(room, -6f, -6f, 5f, -7f);
            Raised(room, 1f, -8f, 5f, -9f, -1f, -9f, -3f);
        }

        static void BuildHall(Transform root, BuiltRoom room)
        {
            foreach (float x in new[] { -5f, 5f })
                foreach (float z in new[] { -5f, 5f })
                    Block(root, "Pillar", new Vector3(x, 2f, z), new Vector3(0.8f, 4f, 0.8f), WallColor);
            Block(root, "Debris A", new Vector3(-8f, 0.4f, -8f), new Vector3(1.6f, 0.8f, 1.6f), PropColor);
            Block(root, "Debris B", new Vector3(8f, 0.4f, 8f), new Vector3(2f, 0.8f, 1.2f), PropColor);
            Block(root, "Debris C", new Vector3(-8f, 0.4f, 7f), new Vector3(1.2f, 0.8f, 2f), PropColor);

            PointLight(root, new Vector3(0f, 3.6f, 0f), EmergencyColor, 11f, 6f);

            Floor(room, -7f, -3f, 7f, 3f, -3f, 8f, 3f, -8f, -9f, -9f, 9f, -9f, -2f, 3f);
            Raised(room, 0.8f, -8f, -8f, 8f, 8f, -8f, 7f);
            room.HugeSpot = new Vector3(0f, 0f, -5f);
        }

        static void BuildStorage(Transform root, BuiltRoom room)
        {
            foreach (float x in new[] { -6.5f, 6.5f })
                foreach (float z in new[] { -6.5f, 6.5f })
                    Block(root, "Shelf", new Vector3(x, 0.6f, z), new Vector3(1.2f, 1.2f, 5f), PropColor);

            PointLight(root, new Vector3(0f, 3.6f, 0f), EmergencyColor, 10f, 5f);
            PointLight(root, new Vector3(-6.5f, 3.6f, 6.5f), WarmColor, 6f, 3f);

            Floor(room, -4f, 6f, 4f, -6f, -9.5f, -6f, 9.5f, 6f, 3.5f, 3.5f, -3.5f, -3.5f, 9f, -10f, -9f, 10f);
            Raised(room, 1.2f, -6.5f, -5f, -6.5f, -8f, 6.5f, 5f, 6.5f, 8f, -6.5f, 5f, 6.5f, -5f);
            room.HugeSpot = new Vector3(-5f, 0f, 0f);
        }

        static void BuildLoft(Transform root, BuiltRoom room)
        {
            // 中 2 階（x 6〜11, z 4〜11, 高さ 2m）。西側の階段と坂で上がれる。重い物を持って上る検証用
            Block(root, "Mezzanine", new Vector3(8.5f, 1f, 7.5f), new Vector3(5f, 2f, 7f), PropColor);
            for (int k = 0; k < 8; k++)
            {
                float h = (k + 1) * 0.25f;
                Block(root, "Step " + k, new Vector3(2.25f + k * 0.5f, h * 0.5f, 5f), new Vector3(0.5f, h, 2f), PropColor);
            }
            float rampAngle = Mathf.Atan2(2f, 4f) * Mathf.Rad2Deg;
            var ramp = Block(root, "Ramp", new Vector3(4f, 0.95f, 9.5f), new Vector3(Mathf.Sqrt(20f), 0.2f, 2.5f), PropColor);
            ramp.localRotation = Quaternion.Euler(0f, 0f, rampAngle);
            Block(root, "Counter", new Vector3(-7f, 0.5f, -6f), new Vector3(4f, 1f, 1f), PropColor);

            PointLight(root, new Vector3(8.5f, 3.8f, 7.5f), EmergencyColor, 8f, 5f);
            PointLight(root, new Vector3(-6f, 3.6f, -4f), EmergencyColor, 9f, 5f);

            Floor(room, -6f, 6f, -8f, -9f, -4f, -9f, 6f, -6f, 9f, -9f, -9f, 9f, 3f, -4f);
            Raised(room, 2f, 7f, 6f, 9f, 9f, 10f, 5f, 7.5f, 10f);
            Raised(room, 1f, -8f, -6f, -6.5f, -6f);
            room.HugeSpot = new Vector3(-5f, 0f, 0f);
        }

        // ---- 部品 ----

        static void Floor(BuiltRoom room, params float[] xz) => AddSpots(room, 0f, false, xz);
        static void Raised(BuiltRoom room, float height, params float[] xz) => AddSpots(room, height, true, xz);

        static void AddSpots(BuiltRoom room, float height, bool raised, float[] xz)
        {
            for (int i = 0; i + 1 < xz.Length; i += 2)
                room.Spots.Add(new ScrapSpot { Position = new Vector3(xz[i], height, xz[i + 1]), Depth = room.Plan.Depth, Raised = raised });
        }

        public static Transform Block(Transform parent, string name, Vector3 localPosition, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(color);
            return go.transform;
        }

        static void PointLight(Transform parent, Vector3 localPosition, Color color, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
        }
    }
}
