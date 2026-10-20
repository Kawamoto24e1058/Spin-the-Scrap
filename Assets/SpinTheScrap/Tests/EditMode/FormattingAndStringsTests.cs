using System.IO;
using NUnit.Framework;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tests
{
    public class NumberFormatterTests
    {
        [TestCase(0, "0")]
        [TestCase(999, "999")]
        [TestCase(9999, "9,999")]
        [TestCase(12345, "1.23万")]
        [TestCase(99999999, "9,999万")]
        [TestCase(123456789, "1.23億")]
        [TestCase(5e12, "5兆")]
        [TestCase(9.999e15, "9,999兆")]
        [TestCase(1e16, "1.00e+16")]
        public void Japanese(double value, string expected)
        {
            Assert.AreEqual(expected, NumberFormatter.Format(value, Language.Japanese));
        }

        [TestCase(999, "999")]
        [TestCase(1500, "1.5K")]
        [TestCase(2.5e6, "2.5M")]
        [TestCase(123.4e9, "123B")]
        [TestCase(1e12, "1.00e+12")]
        public void English(double value, string expected)
        {
            Assert.AreEqual(expected, NumberFormatter.Format(value, Language.English));
        }

        [Test]
        public void ExtremeValuesDoNotThrow()
        {
            Assert.AreEqual("∞", NumberFormatter.Format(double.PositiveInfinity, Language.Japanese));
            Assert.AreEqual("???", NumberFormatter.Format(double.NaN, Language.English));
            Assert.AreEqual("1.80e+308", NumberFormatter.Format(double.MaxValue, Language.English));
            Assert.AreEqual("-1.5K", NumberFormatter.Format(-1500, Language.English));
        }
    }

    public class StringTableTests
    {
        [Test]
        public void ParsesQuotedFieldsAndFallsBackToEnglish()
        {
            var table = StringTable.FromCsv("key,ja,en\n# comment\na,あ,A\nb,,\"B, with comma\"\n");
            Assert.AreEqual("あ", table.Get("a"));
            Assert.AreEqual("B, with comma", table.Get("b"));
            Assert.AreEqual("missing.key", table.Get("missing.key"));
            table.Language = Language.English;
            Assert.AreEqual("A", table.Get("a"));
        }

        [Test]
        public void ShippedCsvCoversAllMvpContentInBothLanguages()
        {
            var table = StringTable.FromCsv(File.ReadAllText(FindStringsCsv()));
            var db = MvpContent.Create();
            foreach (var language in new[] { Language.Japanese, Language.English })
            {
                foreach (var s in db.Symbols)
                {
                    Assert.IsTrue(table.Contains(s.NameKey, language), s.NameKey + " " + language);
                    Assert.IsTrue(table.Contains(s.DescriptionKey, language), s.DescriptionKey + " " + language);
                }
                foreach (var r in db.Relics)
                {
                    Assert.IsTrue(table.Contains(r.NameKey, language), r.NameKey + " " + language);
                    Assert.IsTrue(table.Contains(r.DescriptionKey, language), r.DescriptionKey + " " + language);
                }
                foreach (var t in ToolCatalog.CreateMvp().Tools)
                {
                    Assert.IsTrue(table.Contains(t.NameKey, language), t.NameKey + " " + language);
                    Assert.IsTrue(table.Contains(t.DescriptionKey, language), t.DescriptionKey + " " + language);
                }
            }
        }

        // Unity（カレントはプロジェクトルート）と dotnet test（カレントは bin 配下）の両方で見つける。
        static string FindStringsCsv()
        {
            const string relative = "Assets/SpinTheScrap/Resources/Localization/strings.csv";
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            Assert.Fail("strings.csv not found from " + Directory.GetCurrentDirectory());
            return null;
        }
    }
}
