using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.Machines;

namespace AgeOfAgi.Items.Helpers
{
    internal static class BrainServerHelper
    {
        internal static void Init(GameItem machine, MachineDefinition parameters, ServerDefinition definition,
            Action<GameItem> initializeAudio, EncryptedItemCatalog encrypted, BrainServerDefinition brain)
        {
            MachineParameters.InitMachine(machine, parameters);
            var runtime = new MachineRuntime(machine, parameters, definition, encrypted, MachineWorkMode.BrainServer, brain);
            initializeAudio(machine);
            runtime.Bind();
            machine.onLoaded += runtime.OnLoaded;
            runtime.Refresh();
        }

        internal static void CreateBloodTooltip(RichTextBuilder builder, GameItem item) =>
            Logic.BrainServer.BloodSupply.CreateTooltip(builder, item);
    }
}
