using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    /// <summary>道具屋で買える道具の ID。</summary>
    public static class ToolIds
    {
        public const string Cart = "cart";
        public const string InsulatedBoots = "insulated_boots";
        public const string Battery = "battery";
        public const string Bandage = "bandage";
        public const string Firecracker = "firecracker";
    }

    /// <summary>道具の定義。永続（一度買えばずっと使える）か、消耗品（使うと減る、持てる数に上限）か。</summary>
    public sealed class ToolDefinition
    {
        public string Id { get; }
        public double Price { get; }
        public bool Permanent { get; }
        /// <summary>持てる数の上限（永続なら 1）。</summary>
        public int MaxCount { get; }
        public string NameKey => "tool." + Id + ".name";
        public string DescriptionKey => "tool." + Id + ".desc";

        public ToolDefinition(string id, double price, bool permanent, int maxCount = 1)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
            Id = id;
            Price = price;
            Permanent = permanent;
            MaxCount = permanent ? 1 : Math.Max(1, maxCount);
        }
    }

    /// <summary>道具屋の品ぞろえ（要件書 4.2 道具）。数値は調整前の初期値。</summary>
    public sealed class ToolCatalog
    {
        readonly List<ToolDefinition> tools;

        public IReadOnlyList<ToolDefinition> Tools => tools;

        public ToolCatalog(IEnumerable<ToolDefinition> definitions)
        {
            tools = definitions.ToList();
            if (tools.Select(t => t.Id).Distinct().Count() != tools.Count) throw new ArgumentException("Duplicate tool id");
        }

        public ToolDefinition Get(string id)
        {
            var tool = tools.FirstOrDefault(t => t.Id == id);
            if (tool == null) throw new KeyNotFoundException("Unknown tool: " + id);
            return tool;
        }

        public static ToolCatalog CreateMvp()
        {
            return new ToolCatalog(new[]
            {
                new ToolDefinition(ToolIds.Cart, 20, permanent: true),
                new ToolDefinition(ToolIds.InsulatedBoots, 25, permanent: true),
                new ToolDefinition(ToolIds.Battery, 3, permanent: false, maxCount: 3),
                new ToolDefinition(ToolIds.Bandage, 6, permanent: false, maxCount: 3),
                new ToolDefinition(ToolIds.Firecracker, 4, permanent: false, maxCount: 3),
            });
        }
    }
}
