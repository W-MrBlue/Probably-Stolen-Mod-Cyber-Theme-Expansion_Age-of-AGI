using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.DataCollector
{
    internal static class DataCollectorState
    {
        internal const string CollectorId = ModConstants.ModId + ".data_collector";
        internal const string ExpandedTag = "AGE_OF_AGI_DATA_COLLECTOR_EXPANDED";
        internal const string EmptySpriteKey = "data_collector0";
        internal const string LoadedSpriteKey = "data_collector1";
        internal const int BatteryIndex = 0;
        internal const int NetworkIndex = 1;
        internal const int ContainerIndex = 2;
        internal const int InitialWidth = 4;
        internal const int ExpandedWidth = 8;
        internal const int ContainerHeight = 6;
        internal const int UpgradeDiskCost = 6;

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || item?.identifier != CollectorId || item.GetTagReadonly(ExpandedTag).GetInt() != 0) return;
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, "data_collector_expansion", 0,
                UpgradeDiskCost), color: RenderHandler.ColorPalette.LightCyan);
        }
    }
}
