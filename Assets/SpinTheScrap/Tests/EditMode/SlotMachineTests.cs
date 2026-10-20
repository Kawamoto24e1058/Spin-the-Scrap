using System.Linq;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    // Unity の EditMode テストと Tools/CoreTests（dotnet test）の両方から実行される。
    // NUnit と SpinTheScrap.Core 以外に依存しないこと。
    public class SlotMachineTests
    {
        static readonly ContentDatabase Db = MvpContent.Create();
        int nextId = 1;

        SymbolInstance[] Board(params (int row, int col, string id)[] placements)
        {
            var cells = new SymbolInstance[Core.Board.CellCount];
            foreach (var (row, col, id) in placements)
                cells[Core.Board.Index(row, col)] = new SymbolInstance(nextId++, id, 0);
            return cells;
        }

        static SpinResult Eval(SymbolInstance[] cells, params RelicDefinition[] relics) =>
            SlotMachine.Evaluate(cells, Db, relics, new Rng(1));

        [Test]
        public void EmptyBoardPaysNothing()
        {
            var result = Eval(new SymbolInstance[Core.Board.CellCount]);
            Assert.AreEqual(0, result.Total);
            Assert.IsEmpty(result.Lines);
        }

        [Test]
        public void SingleSymbolPaysBaseValue()
        {
            var result = Eval(Board((1, 2, "empty_can")));
            Assert.AreEqual(1, result.Total);
        }

        [Test]
        public void RustyScrewGainsPerAdjacentMetal()
        {
            // ネジの周囲に空き缶 2 個（斜めも隣接）。ラインは成立しない配置。
            var result = Eval(Board((1, 2, "rusty_screw"), (0, 1, "empty_can"), (2, 3, "empty_can")));
            Assert.AreEqual(3, result.Cells[Core.Board.Index(1, 2)].Value); // 1 + 2
            Assert.IsEmpty(result.Lines);
            Assert.AreEqual(5, result.Total);
        }

        [Test]
        public void ThreeSharedTagsInARowFromTheLeftPayLineBonus()
        {
            var result = Eval(Board((0, 0, "empty_can"), (0, 1, "empty_can"), (0, 2, "empty_can")));
            Assert.AreEqual(1, result.Lines.Count);
            Assert.AreEqual(3, result.Lines[0].Length);
            Assert.AreEqual(SymbolTag.Metal, result.Lines[0].SharedTags);
            Assert.AreEqual(3 + 3 * SlotMachine.LineFactor(3), result.Total);
        }

        [Test]
        public void LineMustStartAtLeftEdge()
        {
            var result = Eval(Board((0, 1, "empty_can"), (0, 2, "empty_can"), (0, 3, "empty_can")));
            Assert.IsEmpty(result.Lines);
        }

        [Test]
        public void LuckyCatLowersLineThresholdToTwo()
        {
            var withoutCat = Eval(Board((0, 0, "empty_can"), (0, 1, "empty_can")));
            Assert.IsEmpty(withoutCat.Lines);

            var withCat = Eval(Board((0, 0, "empty_can"), (0, 1, "empty_can"), (2, 4, "chipped_lucky_cat")));
            Assert.AreEqual(2, withCat.LineThreshold);
            Assert.AreEqual(1, withCat.Lines.Count);
            Assert.AreEqual(2, withCat.Lines[0].Length);
        }

        [Test]
        public void MannequinsDoubleTheBoardOnceAndSpawnOneEnemy()
        {
            var result = Eval(Board((0, 0, "mannequin_head"), (1, 2, "mannequin_head"), (2, 4, "mannequin_head")));
            Assert.AreEqual(2, result.BoardMultiplier);
            Assert.AreEqual(1, result.EnemyBonus);
            Assert.AreEqual(6 * 2, result.Total);
        }

        [Test]
        public void TwoMannequinsAreNotEnough()
        {
            var result = Eval(Board((0, 0, "mannequin_head"), (2, 4, "mannequin_head")));
            Assert.AreEqual(1, result.BoardMultiplier);
            Assert.AreEqual(0, result.EnemyBonus);
        }

        [Test]
        public void RadioRetriggersElectricInSameColumnOnly()
        {
            // 電卓は他の電化製品 1 つにつき +2。ラジオと同じ列なので 2 回発動する。
            var sameColumn = Eval(Board((0, 2, "broken_radio"), (2, 2, "old_calculator")));
            Assert.AreEqual(2, sameColumn.Cells[Core.Board.Index(2, 2)].Triggers);
            Assert.AreEqual(2 + 2 * 2, sameColumn.Cells[Core.Board.Index(2, 2)].Value);

            var otherColumn = Eval(Board((0, 0, "broken_radio"), (2, 2, "old_calculator")));
            Assert.AreEqual(1, otherColumn.Cells[Core.Board.Index(2, 2)].Triggers);
            Assert.AreEqual(2 + 2, otherColumn.Cells[Core.Board.Index(2, 2)].Value);
        }

        [Test]
        public void LotteryEitherPaysHundredfoldOrIsDestroyed()
        {
            int wins = 0, losses = 0;
            for (ulong seed = 1; seed <= 2000; seed++)
            {
                var cells = Board((1, 2, "lottery_bundle"));
                var result = SlotMachine.Evaluate(cells, Db, null, new Rng(seed));
                var cell = result.Cells[Core.Board.Index(1, 2)];
                if (cell.Destroyed)
                {
                    losses++;
                    Assert.AreEqual(0, cell.Value);
                    Assert.AreEqual(1, result.DestroyedInstances.Count());
                }
                else
                {
                    wins++;
                    Assert.AreEqual(100, cell.Value);
                }
            }
            Assert.Greater(wins, 50);
            Assert.Greater(losses, wins);
        }

        [Test]
        public void RelicsApplyFlatBeforeMultiplier()
        {
            var magnet = Db.GetRelic("magnet_charm");
            var result = Eval(Board((1, 2, "empty_can")), magnet);
            Assert.AreEqual(1 + 3, result.Total);

            var glove = Db.GetRelic("cursed_glove");
            var mirror = Eval(Board((1, 2, "cracked_mirror")), glove);
            Assert.AreEqual(4 * 1.5, mirror.Total);
        }

        [Test]
        public void FourCornersSharingATagPayBonus()
        {
            var result = Eval(Board((0, 0, "empty_can"), (0, 4, "empty_can"), (2, 0, "empty_can"), (2, 4, "empty_can")));
            Assert.AreEqual(4 * SlotMachine.CornerMultiplier, result.CornerBonus);
            Assert.AreEqual(4 + 8, result.Total);
        }

        [Test]
        public void DrawPlacesEveryReelSymbolWhenReelIsSmall()
        {
            var reel = Enumerable.Range(0, 6).Select(i => new SymbolInstance(i, "empty_can", 0)).ToList();
            var cells = SlotMachine.Draw(reel, new Rng(42));
            Assert.AreEqual(6, cells.Count(c => c != null));
            CollectionAssert.AreEquivalent(reel, cells.Where(c => c != null));
        }

        [Test]
        public void DrawFillsBoardWhenReelIsLarge()
        {
            var reel = Enumerable.Range(0, 40).Select(i => new SymbolInstance(i, "empty_can", 0)).ToList();
            var cells = SlotMachine.Draw(reel, new Rng(42));
            Assert.AreEqual(Core.Board.CellCount, cells.Count(c => c != null));
            Assert.AreEqual(Core.Board.CellCount, cells.Distinct().Count());
        }

        [Test]
        public void SameSeedGivesSameResult()
        {
            var reel = Db.Symbols.Select((s, i) => new SymbolInstance(i, s.Id, i % 2)).ToList();
            var a = SlotMachine.Evaluate(SlotMachine.Draw(reel, new Rng(7)), Db, null, new Rng(9));
            var b = SlotMachine.Evaluate(SlotMachine.Draw(reel, new Rng(7)), Db, null, new Rng(9));
            Assert.AreEqual(a.Total, b.Total);
        }

        [Test]
        public void ContributionIsSplitByOwner()
        {
            var cells = new SymbolInstance[Core.Board.CellCount];
            cells[Core.Board.Index(0, 1)] = new SymbolInstance(1, "safe_door", 1);
            cells[Core.Board.Index(2, 3)] = new SymbolInstance(2, "empty_can", 2);
            var map = Eval(cells).ContributionByOwner();
            Assert.AreEqual(10, map[1]);
            Assert.AreEqual(1, map[2]);
        }
    }
}
