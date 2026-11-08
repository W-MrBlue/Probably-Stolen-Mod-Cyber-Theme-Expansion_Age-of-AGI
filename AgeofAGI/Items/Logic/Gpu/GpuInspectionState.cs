using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using Newtonsoft.Json;

namespace AgeOfAgi.Items.Logic.Gpu
{
    // Freeze both selected variants and their pixel positions with the individual item.
    internal sealed class GpuInspectionState
    {
        internal const string StateTag = "AGE_OF_AGI_GPU_INSPECTION_STATE";
        public int Version { get; set; } = 1;
        public List<GpuInspectionPart> Parts { get; set; } = new List<GpuInspectionPart>();
        public int? CoreComputePower { get; set; }
        public int? CorePowerPerCycle { get; set; }
        public double? RealComputePower { get; set; }
        public int? RealPowerPerCycle { get; set; }
        public bool? PowerSupplyWorking { get; set; }

        internal bool TryGetPerformance(out GpuPerformance performance)
        {
            performance = default;
            if (!RealComputePower.HasValue || !RealPowerPerCycle.HasValue || !PowerSupplyWorking.HasValue ||
                RealComputePower.Value < 0 || double.IsNaN(RealComputePower.Value) || double.IsInfinity(RealComputePower.Value) ||
                RealPowerPerCycle.Value < 0) return false;
            performance = new GpuPerformance(RealComputePower.Value, RealPowerPerCycle.Value, PowerSupplyWorking.Value);
            return true;
        }

        internal static bool Ensure(GameItem item, GpuInspectionCatalog catalog, bool restoring = false, bool standardAssembly = false)
        {
            // Preserve saved states, including unknown future versions; never reroll on inspection.
            if (catalog == null || !string.IsNullOrEmpty(item.GetTagReadonly(StateTag).GetString())) return false;
            string saved = JsonConvert.SerializeObject(restoring || standardAssembly ?
                catalog.CreateStandardAssembly(item.identifier) : catalog.Roll(item.identifier));
            item.ModifyTag(StateTag, tag => tag.Enable().SetString(saved));
            return true;
        }

        internal static GpuInspectionState Read(GameItem item)
        {
            string saved = item.GetTagReadonly(StateTag).GetString();
            if (string.IsNullOrEmpty(saved)) return null;
            try
            {
                var state = JsonConvert.DeserializeObject<GpuInspectionState>(saved);
                if (state == null || state.Version != 1 || state.Parts == null || state.Parts.Count < 2 ||
                    state.Parts.Any(part => part == null || !GpuInspectionCatalog.IsKey(part.Slot) || part.X < 0 || part.Y < 0 ||
                        part.Sprite == null || part.Sprite.Length > 0 && !GpuInspectionCatalog.IsKey(part.Sprite)) ||
                    state.Parts.Select(part => part.Slot).Distinct().Count() != state.Parts.Count ||
                    state.Parts[0].Slot != "board" || state.Parts[0].Sprite.Length == 0 || state.Parts[0].X != 0 || state.Parts[0].Y != 0 ||
                    state.Parts[1].Slot != "core" || state.Parts[1].Sprite.Length == 0) return null;
                return state;
            }
            catch (JsonException) { return null; }
        }
    }

    internal sealed class GpuInspectionPart
    {
        public string Slot { get; set; }
        // An empty key records an empty socket, rather than an omitted/uninitialized roll.
        public string Sprite { get; set; }
        public string Condition { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}
