using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinTheScrap.Core
{
    /// <summary>道具（消耗品と、台車などの永続の道具）の ID。道具スロットで出る。武器は WeaponCatalog。</summary>
    public static class ToolIds
    {
        public const string Cart = "cart";
        public const string InsulatedBoots = "insulated_boots";
        public const string Battery = "battery";
        public const string Bandage = "bandage";
        public const string Firecracker = "firecracker";
    }

    /// <summary>道具の定義。永続（一度手に入れればずっと使える）か、消耗品（使うと減る、持てる数に上限）か。</summary>
    public sealed class ToolDefinition
    {
        public string Id { get; }
        public bool Permanent { get; }
        /// <summary>持てる数の上限（永続なら 1）。</summary>
        public int MaxCount { get; }
        public string NameKey => "tool." + Id + ".name";
        public string DescriptionKey => "tool." + Id + ".desc";

        public ToolDefinition(string id, bool permanent, int maxCount = 1)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            Permanent = permanent;
            MaxCount = permanent ? 1 : Math.Max(1, maxCount);
        }
    }

    /// <summary>道具の一覧（docs/floor-design.md）。数値は調整前の初期値。</summary>
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
                new ToolDefinition(ToolIds.Cart, permanent: true),
                new ToolDefinition(ToolIds.InsulatedBoots, permanent: true),
                new ToolDefinition(ToolIds.Battery, permanent: false, maxCount: 3),
                new ToolDefinition(ToolIds.Bandage, permanent: false, maxCount: 3),
                new ToolDefinition(ToolIds.Firecracker, permanent: false, maxCount: 3),
            });
        }
    }
}
