using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Data
{
    /// <summary>レリック（ラン中の永続強化）の定義アセット。</summary>
    [CreateAssetMenu(menuName = "Spin the Scrap/Relic", fileName = "Relic")]
    public sealed class RelicDefinitionAsset : ScriptableObject
    {
        public string id;
        public double price = 10;
        public RelicKind kind;
        public SymbolTag tag;
        public double amount;
        public Sprite icon;

        public RelicDefinition ToDefinition() => new RelicDefinition(id, price, kind, tag, amount);

        public void CopyFrom(RelicDefinition d)
        {
            id = d.Id;
            price = d.Price;
            kind = d.Kind;
            tag = d.Tag;
            amount = d.Amount;
        }
    }
}
