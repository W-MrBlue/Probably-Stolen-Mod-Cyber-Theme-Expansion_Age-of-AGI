using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.DiskWriter
{
    internal static class DiskWriterState
    {
        internal const int BatteryIndex = 0;
        internal const int InputIndex = 1;
        internal const int ChipIndex = 2;
        internal const int OutputIndex = 3;
        internal const string EmptySpriteKey = "disk_writer_empty";
        internal const string LoadedSpriteKey = "disk_writer";
        internal const string SelectedRecipeTag = "AGE_OF_AGI_DISK_WRITER_RECIPE";

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (item?.identifier == DiskWriterCatalog.DiskWriterId) MachineParameters.AppendPowerTooltip(builder, item);
        }
    }
}
