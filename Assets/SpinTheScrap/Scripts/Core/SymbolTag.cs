using System;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// シンボルのタグ系統（要件書 5.2）。発売時は 8〜10 系統を想定。
    /// 1 シンボルが複数タグを持てるようにフラグで表現する。
    /// </summary>
    [Flags]
    public enum SymbolTag
    {
        None = 0,
        Metal = 1 << 0,
        Electric = 1 << 1,
        Curse = 1 << 2,
        Doll = 1 << 3,
        Luck = 1 << 4,
        Gamble = 1 << 5,
        Fragile = 1 << 6,
        Toy = 1 << 7,
        Paper = 1 << 8,
        Food = 1 << 9,
    }

    /// <summary>ガラクタの運搬区分（要件書 4.1）。割れ物は区分ではなく別フラグで扱う。</summary>
    public enum ScrapSize
    {
        /// <summary>1 人で持てる。インベントリ枠に入る。</summary>
        Small,
        /// <summary>両手で抱える。移動速度が低下し、走れない。</summary>
        Large,
        /// <summary>2 人以上で持ち上げる。1 人では引きずるのが限界。</summary>
        Huge,
    }

    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
    }

    public static class SymbolTagExtensions
    {
        public static bool HasAny(this SymbolTag tags, SymbolTag mask) => (tags & mask) != 0;
    }
}
