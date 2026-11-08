using System;

namespace AgeOfAgi.Items.Logic.Memory
{
    internal static class MemoryState
    {
        internal const string MemoryTag = "AGE_OF_AGI_MEMORY";
        internal const string CapacityTag = "AGE_OF_AGI_MEMORY_GB";
        internal const string CapacityTextKey = "memory_stat_capacity";

        // Initial values only. Training machines consume these tags; this helper does not
        // implement a machine cycle or overwrite restored save data from an onLoaded hook.

        internal static bool IsMemory(GameItem item)
        {
            return item != null && item.IsTag(MemoryTag);
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !IsMemory(item))
            {
                return;
            }

            // The late tooltip hook runs before the description and source footer. Resolve
            // translations and current tag values on each build, including restored items.
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, CapacityTextKey, GetMemory(item)),
                color: RenderHandler.ColorPalette.LightCyan);
        }

        internal static int GetMemory(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.GetTagReadonly(CapacityTag).GetInt();
        }
    }
}
