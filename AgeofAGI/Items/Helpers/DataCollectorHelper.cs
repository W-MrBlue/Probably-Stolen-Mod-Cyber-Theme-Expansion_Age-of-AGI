using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.DataCollector;

namespace AgeOfAgi.Items.Helpers
{
    internal static class DataCollectorHelper
    {
        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => DataCollectorState.CreateTooltip(builder, item);

        internal static void Init(GameItem machine, MachineDefinition definition, Action<GameItem> initializeAudio)
        {
            MachineParameters.InitMachine(machine, definition);
            var runtime = new DataCollectorRuntime(machine, definition);
            initializeAudio(machine);
            runtime.Bind();
            machine.onLoaded += runtime.OnLoaded;
            runtime.Refresh();
        }
    }
}
