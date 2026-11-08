using System;
using AgeOfAgi.Items.Definitions;

using AgeOfAgi.Items.Logic.Cpu;
using static AgeOfAgi.Items.Logic.Cpu.CpuState;

namespace AgeOfAgi.Items.Helpers
{
    internal static class CpuHelper
    {
        internal static void InitCpu(GameItem item, CpuDefinition definition, Action<GameItem> initializeAudio)
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
                throw new ArgumentException("CPU configuration ID does not match item '" + item.identifier + "'.", nameof(definition));
            }

            initializeAudio(item);
            item.EnableTag(CpuTag);
            item.ModifyTag(PowerBoostRateTag, state => state.Enable().SetFloat(definition.PowerBoostRate));
            item.ModifyTag(PowerPerCycleTag, state => state.Enable().SetInt(definition.PowerPerCycle));
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => CpuState.CreateTooltip(builder, item);
    }
}
