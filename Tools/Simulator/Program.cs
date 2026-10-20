using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SpinTheScrap.Core;

namespace SpinTheScrap.Tools
{
    // 使い方:
    //   dotnet run --project Tools/Simulator -- --reel empty_can:5,rusty_screw:3 --spins 20000 --seed 1
    //   dotnet run --project Tools/Simulator -- --all            （各シンボルを初期リールに 1〜3 個足したときの比較）
    static class Program
    {
        static int Main(string[] args)
        {
            var opts = ParseArgs(args);
            var db = MvpContent.Create();
            int spins = int.Parse(opts.GetValueOrDefault("spins", "20000"), CultureInfo.InvariantCulture);
            ulong seed = ulong.Parse(opts.GetValueOrDefault("seed", "1"), CultureInfo.InvariantCulture);
            var relics = opts.TryGetValue("relics", out var relicArg)
                ? relicArg.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(db.GetRelic).ToList()
                : new List<RelicDefinition>();

            if (opts.ContainsKey("all"))
            {
                var config = new BalanceConfig();
                Console.WriteLine($"spin cost (cycle1): {string.Join(" ", Enumerable.Range(0, 8).Select(i => config.SpinCostFor(1, i)))}");
                Console.WriteLine($"quotas: {string.Join(" ", Enumerable.Range(1, 6).Select(c => config.QuotaFor(c)))}");
                Console.WriteLine();
                PrintHeader();
                Report("(initial reel)", config.StartingReel, db, relics, spins, seed);
                foreach (var s in db.Symbols)
                foreach (int copies in new[] { 1, 3 })
                {
                    var reel = new List<string>(config.StartingReel);
                    for (int i = 0; i < copies; i++) reel.Add(s.Id);
                    Report($"+{copies} {s.Id}", reel, db, relics, spins, seed);
                }
                return 0;
            }

            if (!opts.TryGetValue("reel", out var reelArg))
            {
                Console.Error.WriteLine("usage: --reel id:count,id:count [--spins N] [--seed S] [--relics id,id] | --all");
                return 1;
            }

            PrintHeader();
            Report(reelArg, ParseReel(reelArg), db, relics, spins, seed);
            return 0;
        }

        static void PrintHeader()
        {
            Console.WriteLine($"{"reel",-28} {"mean",10} {"p50",8} {"p90",8} {"p99",10} {"max",12} {"destroyed/spin",15}");
        }

        static void Report(string label, List<string> reelIds, ContentDatabase db, List<RelicDefinition> relics, int spins, ulong seed)
        {
            var rng = new Rng(seed);
            var totals = new double[spins];
            long destroyed = 0;
            for (int i = 0; i < spins; i++)
            {
                // 消滅はリールに反映せず、毎回同じ構成での 1 スピンの分布を見る
                var reel = reelIds.Select((id, k) => new SymbolInstance(k, id, 0)).ToList();
                var result = SlotMachine.Evaluate(SlotMachine.Draw(reel, rng), db, relics, rng);
                totals[i] = result.Total;
                destroyed += result.DestroyedInstances.Count();
            }
            Array.Sort(totals);
            Console.WriteLine($"{label,-28} {totals.Average(),10:0.00} {Pct(totals, 0.5),8:0.##} {Pct(totals, 0.9),8:0.##} {Pct(totals, 0.99),10:0.##} {totals[^1],12:0.##} {(double)destroyed / spins,15:0.000}");
        }

        static double Pct(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))];

        static List<string> ParseReel(string arg)
        {
            var reel = new List<string>();
            foreach (var part in arg.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                int count = kv.Length > 1 ? int.Parse(kv[1], CultureInfo.InvariantCulture) : 1;
                for (int i = 0; i < count; i++) reel.Add(kv[0]);
            }
            return reel;
        }

        static Dictionary<string, string> ParseArgs(string[] args)
        {
            var map = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                string key = args[i].Substring(2);
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
                map[key] = hasValue ? args[++i] : "true";
            }
            return map;
        }
    }
}
