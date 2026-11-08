using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.Machines;
using AgeOfAgi.Items.Logic.Server;

namespace AgeOfAgi.Items.Helpers
{
    internal static class ServerHelper
    {
        internal static void Init(GameItem machine, MachineDefinition parameters, ServerDefinition definition,
            Action<GameItem> initializeAudio, EncryptedItemCatalog encrypted)
        {
            MachineParameters.InitMachine(machine, parameters);
            var runtime = new MachineRuntime(machine, parameters, definition, encrypted, MachineWorkMode.Server);
            initializeAudio(machine);
            runtime.Bind();
            machine.onLoaded += runtime.OnLoaded;
            runtime.Refresh();
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => ServerTooltip.CreateTooltip(builder, item);
    }
}
