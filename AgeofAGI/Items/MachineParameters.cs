using System;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items
{
    // Shared metadata only. Each machine's helper owns its initialization,
    // interactions, loading and tooltip entry point.
    internal static class MachineParameters
    {
        internal const string MachineTag = "MACHINE";
        internal const string PowerPerCycleTag = "MACHINE_POWER_PER_CYCLE";
        internal const string PowerPerCycleTextKey = "machine_stat_power_per_cycle";

        internal static void InitMachine(GameItem item, MachineDefinition definition)
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
                throw new ArgumentException("Machine configuration ID does not match item '" + item.identifier + "'.", nameof(definition));
            }

            item.EnableTag(MachineTag);
            // Cost of one operation. Do not register this as native daily machinery power.
            item.ModifyTag(PowerPerCycleTag, state => state.Enable().SetInt(definition.PowerPerCycle));
        }

        internal static bool IsMachine(GameItem item)
        {
            return item != null && item.IsTag(MachineTag);
        }

        internal static int GetPowerPerCycle(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.GetTagReadonly(PowerPerCycleTag).GetInt();
        }

        internal static void AppendPowerTooltip(RichTextBuilder builder, GameItem item, string textKey = PowerPerCycleTextKey)
        {
            if (builder == null || !IsMachine(item))
            {
                return;
            }

            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, textKey, GetPowerPerCycle(item)),
                color: RenderHandler.ColorPalette.LightCyan);
        }
    }
}
