using AgeOfAgi.Items.Logic.BaseModel;
using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.Compiler
{
    internal static class CompilerState
    {
        internal const string BaseLockedTag = "AGE_OF_AGI_COMPILER_BASE_LOCKED";
        internal const int BatteryIndex = 0;
        internal const int BaseIndex = 1;
        internal const int GridIndex = 2;
        internal const int OutputIndex = 3;
        internal const string IdleSpriteKey = "compiler_idle";
        internal const string WorkingSpriteKey = "compiler_working";

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (item?.identifier == CompilerCatalog.CompilerId) MachineParameters.AppendPowerTooltip(builder, item);
        }
    }
}
