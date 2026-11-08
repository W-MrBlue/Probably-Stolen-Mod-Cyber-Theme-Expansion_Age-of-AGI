using System;
using AgeOfAgi.Items.Definitions;

using AgeOfAgi.Items.Logic.BaseModel;
using static AgeOfAgi.Items.Logic.BaseModel.BaseModelState;

namespace AgeOfAgi.Items.Helpers
{
    internal static class BaseModelHelper
    {
        internal static void InitBaseModel(GameItem item, BaseModelDefinition definition, Action<GameItem> initializeAudio)
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
                throw new ArgumentException("Base model configuration ID does not match item '" + item.identifier + "'.", nameof(definition));
            }

            initializeAudio(item);
            item.EnableTag(BaseModelTag);
            item.ModifyTag(GridWidthTag, state => state.Enable().SetInt(definition.GridWidth));
            item.ModifyTag(GridHeightTag, state => state.Enable().SetInt(definition.GridHeight));
            item.ModifyTag(MemoryMultiplierTag, state => state.Enable().SetFloat(definition.MemoryMultiplier));
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => BaseModelState.CreateTooltip(builder, item);
    }
}
