using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Data
{
    /// <summary>シンボル（＝ガラクタ）の定義アセット。コードを触らずに追加・調整できる。</summary>
    [CreateAssetMenu(menuName = "Spin the Scrap/Symbol", fileName = "Symbol")]
    public sealed class SymbolDefinitionAsset : ScriptableObject
    {
        public string id;
        public double baseValue = 1;
        public double sellPrice = 1;
        public SymbolTag tags;
        public Rarity rarity;
        public ScrapSize size;
        public bool fragile;
        public List<EffectData> effects = new List<EffectData>();
        [Tooltip("廃墟に置く 3D モデル。未設定なら仮素材（箱など）")]
        public GameObject model;
        public Sprite icon;

        public SymbolDefinition ToDefinition() =>
            new SymbolDefinition(id, baseValue, sellPrice, tags, size, fragile, rarity, effects.Select(e => e.ToDefinition()).ToList());

        public void CopyFrom(SymbolDefinition d)
        {
            id = d.Id;
            baseValue = d.BaseValue;
            sellPrice = d.SellPrice;
            tags = d.Tags;
            rarity = d.Rarity;
            size = d.Size;
            fragile = d.Fragile;
            effects = d.Effects.Select(EffectData.From).ToList();
        }
    }
}
