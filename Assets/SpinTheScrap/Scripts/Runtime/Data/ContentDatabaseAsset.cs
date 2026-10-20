using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Data
{
    /// <summary>ゲームで使うシンボル・レリックの一覧。RunSession に割り当てる。</summary>
    [CreateAssetMenu(menuName = "Spin the Scrap/Content Database", fileName = "ContentDatabase")]
    public sealed class ContentDatabaseAsset : ScriptableObject
    {
        public List<SymbolDefinitionAsset> symbols = new List<SymbolDefinitionAsset>();
        public List<RelicDefinitionAsset> relics = new List<RelicDefinitionAsset>();

        public ContentDatabase Build() => new ContentDatabase(
            symbols.Where(s => s != null).Select(s => s.ToDefinition()),
            relics.Where(r => r != null).Select(r => r.ToDefinition()));

        public SymbolDefinitionAsset FindSymbol(string id) => symbols.FirstOrDefault(s => s != null && s.id == id);
    }
}
