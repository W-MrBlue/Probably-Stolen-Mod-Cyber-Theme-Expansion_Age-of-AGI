using System;

namespace AgeOfAgi.Items.Logic.Cpu
{
    internal static class CpuState
    {
        internal const string CpuTag = "CPU";
        internal const string PowerBoostRateTag = "POWER_BOOST_RATE";
        internal const string PowerPerCycleTag = "POWER_PER_CYCLE";
        internal const string PowerBoostRateTextKey = "cpu_stat_power_boost_rate";
        internal const string PowerPerCycleTextKey = "cpu_stat_power_per_cycle";

        // Initial values only. Training machines consume these tags; this helper does not
        // implement a machine cycle or overwrite restored save data from an onLoaded hook.

        internal static bool IsCpu(GameItem item)
        {
            return item != null && item.IsTag(CpuTag);
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !IsCpu(item))
            {
                return;
            }

            // The late tooltip hook runs before the description and source footer. Resolve
            // translations and current tag values on each build, including restored items.
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, PowerBoostRateTextKey, GetPowerBoostRate(item)),
                color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, PowerPerCycleTextKey, GetPowerPerCycle(item)),
                color: RenderHandler.ColorPalette.LightCyan);
        }

        internal static float GetPowerBoostRate(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.GetTagReadonly(PowerBoostRateTag).GetFloat();
        }

        internal static int GetPowerPerCycle(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.GetTagReadonly(PowerPerCycleTag).GetInt();
        }
    }
}
