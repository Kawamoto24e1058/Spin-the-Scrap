using System;
using System.Collections.Generic;

namespace SpinTheScrap.Core
{
    /// <summary>シンボルとレリックの定義一覧。Unity 側では ScriptableObject から組み立てる。</summary>
    public sealed class ContentDatabase
    {
        readonly Dictionary<string, SymbolDefinition> symbols = new Dictionary<string, SymbolDefinition>();
        readonly Dictionary<string, RelicDefinition> relics = new Dictionary<string, RelicDefinition>();
        readonly List<SymbolDefinition> symbolList = new List<SymbolDefinition>();
        readonly List<RelicDefinition> relicList = new List<RelicDefinition>();

        public IReadOnlyList<SymbolDefinition> Symbols => symbolList;
        public IReadOnlyList<RelicDefinition> Relics => relicList;

        public ContentDatabase(IEnumerable<SymbolDefinition> symbolDefinitions, IEnumerable<RelicDefinition> relicDefinitions)
        {
            foreach (var s in symbolDefinitions)
            {
                if (symbols.ContainsKey(s.Id)) throw new ArgumentException("Duplicate symbol id: " + s.Id);
                symbols.Add(s.Id, s);
                symbolList.Add(s);
            }
            foreach (var r in relicDefinitions)
            {
                if (relics.ContainsKey(r.Id)) throw new ArgumentException("Duplicate relic id: " + r.Id);
                relics.Add(r.Id, r);
                relicList.Add(r);
            }
        }

        public SymbolDefinition GetSymbol(string id)
        {
            if (!symbols.TryGetValue(id, out var def)) throw new KeyNotFoundException("Unknown symbol id: " + id);
            return def;
        }

        public RelicDefinition GetRelic(string id)
        {
            if (!relics.TryGetValue(id, out var def)) throw new KeyNotFoundException("Unknown relic id: " + id);
            return def;
        }

        public bool TryGetSymbol(string id, out SymbolDefinition def) => symbols.TryGetValue(id, out def);
        public bool TryGetRelic(string id, out RelicDefinition def) => relics.TryGetValue(id, out def);
    }
}
