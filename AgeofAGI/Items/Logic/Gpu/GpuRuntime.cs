using System;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Catalogs;
using Newtonsoft.Json;
using System.Globalization;

namespace AgeOfAgi.Items.Logic.Gpu
{
    internal static class GpuRuntime
    {
        internal const string GpuTag = "AGE_OF_AGI_GPU";
        internal const string ComputePowerTag = "AGE_OF_AGI_COMPUTE_POWER";
        internal const string PowerPerCycleTag = "AGE_OF_AGI_POWER_PER_CYCLE";
        internal const string ComputePowerTextKey = "gpu_stat_compute_power";
        internal const string PowerPerCycleTextKey = "gpu_stat_power_per_cycle";
        internal const string ComponentStatsTag = "AGE_OF_AGI_GPU_COMPONENT_STATS";
        private static string standardAssemblyItemId;

        internal static bool UsesStandardAssembly(GameItem item) => standardAssemblyItemId == item.identifier;

        internal static GameItem CreateStandardItem(string itemId)
        {
            // Native factories run synchronously. Limit the request to this item ID
            // and restore it even if creation fails; normal spawns keep their rolls.
            string previous = standardAssemblyItemId;
            standardAssemblyItemId = itemId;
            try { return DirectoryMaster.Item(itemId); }
            finally { standardAssemblyItemId = previous; }
        }

        // Initialize from the frozen assembly. Load converts old assemblies once, then
        // preserves the authoritative snapshot; machines never consume public labels.

        internal static void EnableInspection(GameItem item) => item.EnableTag("INSPECTABLE_TAG");

        internal static void ApplyComponentStats(GameItem item, GpuDefinition definition, GpuInspectionCatalog inspection, GpuCatalog cores,
            bool preserveLegacyStats = false)
        {
            // Completed conversions retain their saved numeric attributes and frozen core budgets.
            if (inspection == null) return;
            GpuInspectionState state = GpuInspectionState.Read(item);
            if (state == null) return;
            if (state.TryGetPerformance(out var saved))
            {
                // Compute follows the current formula using this card's frozen core
                // and components. Loading never rerolls parts or reads new core budgets.
                if ((GpuPerformanceCalculator.HasMissingGoldfinger(state) || !GpuPerformanceCalculator.HasPowerInterface(state)) &&
                    state.CoreComputePower.HasValue && state.Parts.TrueForAll(part =>
                    Array.IndexOf(GpuInspectionCatalog.Conditions, part.Condition) >= 0))
                {
                    var calculated = GpuPerformanceCalculator.Calculate(state, state.CoreComputePower.Value, state.CorePowerPerCycle ?? 0);
                    if (Math.Abs(calculated.ComputePower - saved.ComputePower) > 0.000001 || calculated.PowerPerCycle != saved.PowerPerCycle ||
                        calculated.PowerSupplyWorking != saved.PowerSupplyWorking)
                    {
                        state.RealComputePower = calculated.ComputePower;
                        state.RealPowerPerCycle = calculated.PowerPerCycle;
                        state.PowerSupplyWorking = calculated.PowerSupplyWorking;
                        item.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
                        item.ModifyTag(ComputePowerTag, tag => tag.SetDouble(calculated.ComputePower));
                        item.ModifyTag(PowerPerCycleTag, tag => tag.SetInt(calculated.PowerPerCycle));
                    }
                }
                return;
            }
            foreach (GpuInspectionPart part in state.Parts)
            {
                if (part.Condition == null) part.Condition = inspection.ResolveCondition(item.identifier, part);
                if (Array.IndexOf(GpuInspectionCatalog.Conditions, part.Condition) < 0) return;
            }
            if (!state.CoreComputePower.HasValue || !state.CorePowerPerCycle.HasValue)
            {
                string coreSprite = state.Parts[1].Sprite;
                GpuDefinition core = null;
                cores?.TryGetByCore(coreSprite, out core);
                if (core == null && coreSprite == definition.CoreSprite) core = definition;
                if (core == null) return;
                state.CoreComputePower ??= core.ComputePower;
                state.CorePowerPerCycle ??= core.PowerPerCycle;
            }
            if (state.CoreComputePower <= 0 || state.CorePowerPerCycle < 0) return;
            GpuPerformance performance = GpuPerformanceCalculator.Calculate(state, state.CoreComputePower.Value, state.CorePowerPerCycle.Value);
            if (preserveLegacyStats)
            {
                // A missing assembly in an old save is not permission to replace
                // its saved hardware values with a new random card's performance.
                double compute = item.IsTag(ComponentStatsTag) ? item.GetTagReadonly(ComputePowerTag).GetDouble() :
                    item.GetTagReadonly(ComputePowerTag).GetInt();
                if (!item.IsTag(ComputePowerTag) || compute < 0 || double.IsNaN(compute) || double.IsInfinity(compute)) compute = performance.ComputePower;
                int power = item.GetTagReadonly(PowerPerCycleTag).GetInt();
                bool working = performance.PowerSupplyWorking;
                if (!item.IsTag(PowerPerCycleTag) || power < 0) power = performance.PowerPerCycle;
                if (power == 9999)
                {
                    // Keep the old known fault while retiring its numeric sentinel.
                    power = 0;
                    working = false;
                    var connector = state.Parts.Find(part => part.Slot == "power_interface");
                    if (connector != null) { connector.Sprite = ""; connector.Condition = "empty"; }
                }
                performance = new GpuPerformance(compute, power, working);
            }
            state.RealComputePower = performance.ComputePower;
            state.RealPowerPerCycle = performance.PowerPerCycle;
            state.PowerSupplyWorking = performance.PowerSupplyWorking;
            item.ModifyTag(GpuInspectionState.StateTag, tag => tag.Enable().SetString(JsonConvert.SerializeObject(state)));
            item.ModifyTag(ComputePowerTag, tag => tag.Enable().SetDouble(performance.ComputePower));
            item.ModifyTag(PowerPerCycleTag, tag => tag.Enable().SetInt(performance.PowerPerCycle));
            item.ModifyTag(ComponentStatsTag, tag => tag.Enable().SetInt(2));
        }

        internal static bool IsGpu(GameItem item)
        {
            return item != null && item.IsTag(GpuTag);
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !IsGpu(item))
            {
                return;
            }

            if (GpuLabels.CreateTooltip(builder, item)) return;
            // Legacy items without a readable assembly retain their old display only.
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, ComputePowerTextKey, GetComputePower(item).ToString("0.##", CultureInfo.CurrentCulture)),
                color: RenderHandler.ColorPalette.LightCyan);
            if (GetPowerPerCycle(item) == 9999)
            {
                // An unreadable pre-migration assembly may still carry the retired sentinel.
                builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, GpuLabels.MissingSupplyTextKey), color: RenderHandler.ColorPalette.LightRed);
                return;
            }
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, PowerPerCycleTextKey, GetPowerPerCycle(item)),
                color: RenderHandler.ColorPalette.LightCyan);
        }

        internal static double GetComputePower(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return TryGetRealPerformance(item, out var performance) ? performance.ComputePower :
                item.IsTag(ComponentStatsTag) ? item.GetTagReadonly(ComputePowerTag).GetDouble() : item.GetTagReadonly(ComputePowerTag).GetInt();
        }

        internal static int GetPowerPerCycle(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return TryGetRealPerformance(item, out var performance) ? performance.PowerPerCycle : item.GetTagReadonly(PowerPerCycleTag).GetInt();
        }

        internal static bool TryGetRealPerformance(GameItem item, out GpuPerformance performance)
        {
            performance = default;
            if (!IsGpu(item)) return false;
            var state = GpuInspectionState.Read(item);
            if (state?.TryGetPerformance(out performance) != true) return false;
            if (GpuPerformanceCalculator.HasMissingGoldfinger(state) || !GpuPerformanceCalculator.HasPowerInterface(state))
                performance = new GpuPerformance(performance.ComputePower, performance.PowerPerCycle, false);
            return true;
        }

        internal static void ShutdownLabels() => GpuLabels.Shutdown();
    }
}
