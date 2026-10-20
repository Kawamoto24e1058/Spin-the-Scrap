using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class FacilityLayoutTests
    {
        static readonly Direction[] AllDirections = { Direction.North, Direction.East, Direction.South, Direction.West };

        [Test]
        public void SameSeedGivesSameLayout()
        {
            var a = FacilityLayout.Generate(42, 6);
            var b = FacilityLayout.Generate(42, 6);
            Assert.AreEqual(Describe(a), Describe(b));
        }

        [Test]
        public void DifferentSeedsGiveDifferentLayouts()
        {
            var layouts = Enumerable.Range(1, 20).Select(s => Describe(FacilityLayout.Generate((ulong)s, 6))).Distinct();
            Assert.Greater(layouts.Count(), 1);
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(9)]
        public void HasRequestedRoomCountWithEntranceFirst(int count)
        {
            var layout = FacilityLayout.Generate(7, count);
            Assert.AreEqual(count, layout.Rooms.Count);
            Assert.AreEqual(RoomKind.Entrance, layout.Entrance.Kind);
            Assert.AreEqual(0, layout.Entrance.Depth);
            Assert.AreEqual(1, layout.Rooms.Count(r => r.Kind == RoomKind.Entrance));
            Assert.IsTrue(layout.Rooms.All(r => r.Y >= 0), "入口より南には部屋を作らない");
        }

        [Test]
        public void DoorsAreSymmetricAndLeadToRooms()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var layout = FacilityLayout.Generate(seed, 7);
                foreach (var room in layout.Rooms)
                    foreach (var dir in AllDirections)
                    {
                        if (!room.HasDoor(dir)) continue;
                        var neighbor = layout.Neighbor(room, dir);
                        Assert.IsNotNull(neighbor, "ドアの先に部屋がない");
                        Assert.IsTrue(neighbor.HasDoor(FacilityLayout.Opposite(dir)), "ドアが片側だけ");
                    }
            }
        }

        [Test]
        public void EveryRoomIsReachableAndDepthIsDoorDistance()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var layout = FacilityLayout.Generate(seed, 8);
                var distance = new Dictionary<RoomPlan, int> { { layout.Entrance, 0 } };
                var queue = new Queue<RoomPlan>();
                queue.Enqueue(layout.Entrance);
                while (queue.Count > 0)
                {
                    var room = queue.Dequeue();
                    foreach (var dir in AllDirections)
                    {
                        if (!room.HasDoor(dir)) continue;
                        var next = layout.Neighbor(room, dir);
                        if (distance.ContainsKey(next)) continue;
                        distance[next] = distance[room] + 1;
                        queue.Enqueue(next);
                    }
                }
                Assert.AreEqual(layout.Rooms.Count, distance.Count, "入口から行けない部屋がある");
                foreach (var room in layout.Rooms) Assert.AreEqual(distance[room], room.Depth);
            }
        }

        [Test]
        public void TreeHasOneDoorPairPerExtraRoom()
        {
            var layout = FacilityLayout.Generate(3, 8);
            int doorSides = layout.Rooms.Sum(r => AllDirections.Count(r.HasDoor));
            Assert.AreEqual((layout.Rooms.Count - 1) * 2, doorSides);
        }

        [Test]
        public void LoftAppearsOnceAtMaxDepthWhenThreeOrMoreRooms()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var layout = FacilityLayout.Generate(seed, 5);
                var lofts = layout.Rooms.Where(r => r.Kind == RoomKind.Loft).ToList();
                Assert.AreEqual(1, lofts.Count);
                Assert.AreEqual(layout.MaxDepth, lofts[0].Depth);
            }
            Assert.IsFalse(FacilityLayout.Generate(1, 2).Rooms.Any(r => r.Kind == RoomKind.Loft));
        }

        [Test]
        public void DeeperRoomsFavorRarerScrap()
        {
            double RareShare(int depth) =>
                LootTable.Weight(Rarity.Rare, depth) /
                (LootTable.Weight(Rarity.Common, depth) + LootTable.Weight(Rarity.Uncommon, depth) + LootTable.Weight(Rarity.Rare, depth));

            Assert.Less(RareShare(0), RareShare(1));
            Assert.Less(RareShare(1), RareShare(3));
            Assert.Less(LootTable.ScrapCountFor(RoomKind.Entrance, 0), LootTable.ScrapCountFor(RoomKind.Hall, 1));
        }

        static string Describe(FacilityLayout layout) =>
            string.Join(";", layout.Rooms.Select(r =>
                r.X + "," + r.Y + ":" + r.Kind + ":" + string.Concat(AllDirections.Select(d => r.HasDoor(d) ? "1" : "0"))));
    }
}
