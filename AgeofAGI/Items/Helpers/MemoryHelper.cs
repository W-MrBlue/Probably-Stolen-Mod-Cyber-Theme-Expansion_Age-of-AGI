using System;
using AgeOfAgi.Items.Definitions;

using AgeOfAgi.Items.Logic.Memory;
using static AgeOfAgi.Items.Logic.Memory.MemoryState;

namespace AgeOfAgi.Items.Helpers
{
    internal static class MemoryHelper
    {
        internal static void InitMemory(GameItem item, MemoryDefinition definition, Action<GameItem> initializeAudio)
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
                throw new ArgumentException("MEMORY configuration ID does not match item '" + item.identifier + "'.", nameof(definition));
            }

            initializeAudio(item);
            item.EnableTag(MemoryTag);
            item.ModifyTag(CapacityTag, state => state.Enable().SetInt(definition.Memory));
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => MemoryState.CreateTooltip(builder, item);
    }
}
