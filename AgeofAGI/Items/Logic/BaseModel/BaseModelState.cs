using System;

namespace AgeOfAgi.Items.Logic.BaseModel
{
    internal static class BaseModelState
    {
        internal const string BaseModelTag = "AGE_OF_AGI_BASE_MODEL";
        internal const string GridWidthTag = "AGE_OF_AGI_GRID_WIDTH";
        internal const string GridHeightTag = "AGE_OF_AGI_GRID_HEIGHT";
        internal const string MemoryMultiplierTag = "AGE_OF_AGI_BASE_MEMORY_MULTIPLIER";
        internal const string GridSizeTextKey = "base_model_stat_grid";
        internal const string MemoryMultiplierTextKey = "base_model_stat_memory_multiplier";

        internal static bool IsBaseModel(GameItem item)
        {
            return item != null && item.IsTag(BaseModelTag);
        }

        internal static int GetGridWidth(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.GetTagReadonly(GridWidthTag).GetInt();
        }

        internal static int GetGridHeight(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.GetTagReadonly(GridHeightTag).GetInt();
        }

        internal static float GetMemoryMultiplier(GameItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.GetTagReadonly(MemoryMultiplierTag).GetFloat();
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !IsBaseModel(item))
            {
                return;
            }

            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, GridSizeTextKey,
                GetGridWidth(item), GetGridHeight(item)), color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, MemoryMultiplierTextKey,
                GetMemoryMultiplier(item)), color: RenderHandler.ColorPalette.LightCyan);
        }
    }
}
