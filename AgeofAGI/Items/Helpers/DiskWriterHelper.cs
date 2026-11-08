using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.DiskWriter;

namespace AgeOfAgi.Items.Helpers
{
    internal static class DiskWriterHelper
    {
        internal static void Init(GameItem machine, MachineDefinition definition, DiskWriterCatalog catalog,
            Action<GameItem> initializeAudio)
        {
            MachineParameters.InitMachine(machine, definition);
            var runtime = new DiskWriterRuntime(machine, catalog);
            initializeAudio(machine);
            runtime.Bind();
            machine.onLoaded += runtime.OnLoaded;
            runtime.Refresh();
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => DiskWriterState.CreateTooltip(builder, item);
    }
}
