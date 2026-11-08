using System;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Logic.Gpu;

using static AgeOfAgi.Items.Logic.Gpu.GpuRuntime;

namespace AgeOfAgi.Items.Helpers
{
    internal static class GpuHelper
    {
        internal static void InitGpu(GameItem item, GpuDefinition definition, Action<GameItem> initializeAudio,
            GpuInspectionCatalog inspection = null, GpuCatalog cores = null)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (item.identifier != definition.ItemId)
            {
                throw new ArgumentException("GPU configuration ID does not match item '" + item.identifier + "'.", nameof(definition));
            }

            initializeAudio(item);
            item.EnableTag(GpuTag);
            item.ModifyTag(ComputePowerTag, state => state.Enable().SetInt(definition.ComputePower));
            item.ModifyTag(PowerPerCycleTag, state => state.Enable().SetInt(definition.PowerPerCycle));
            EnableInspection(item);
            // Native decoding calls the normal factory before replacing its state.
            // Do not roll transient components or allocate label identities there.
            if (!SaveManager.IsDecoding)
            {
                GpuInspectionState.Ensure(item, inspection, standardAssembly: GpuRuntime.UsesStandardAssembly(item));
                ApplyComponentStats(item, definition, inspection, cores);
                GpuLabels.Ensure(item);
            }
            item.onLoaded += loaded =>
            {
                EnableInspection(loaded);
                bool migratedLegacy = GpuInspectionState.Ensure(loaded, inspection, restoring: true);
                ApplyComponentStats(loaded, definition, inspection, cores, preserveLegacyStats: migratedLegacy);
                GpuLabels.Ensure(loaded);
            };
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => GpuRuntime.CreateTooltip(builder, item);

        internal static void ShutdownLabels() => GpuRuntime.ShutdownLabels();
    }
}
