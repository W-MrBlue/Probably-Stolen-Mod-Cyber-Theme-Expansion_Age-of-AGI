using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.DistributedTerminal;
using AgeOfAgi.Items.Logic.Machines;

namespace AgeOfAgi.Items.Helpers
{
    internal static class DistributedTerminalHelper
    {
        internal static void Init(GameItem machine, MachineDefinition parameters, ServerDefinition definition,
            Action<GameItem> initializeAudio, EncryptedItemCatalog encrypted)
        {
            machine.EnableTag(DistributedTerminalState.TerminalTag);
            MachineParameters.InitMachine(machine, parameters);
            var runtime = new MachineRuntime(machine, parameters, definition, encrypted, MachineWorkMode.DistributedTerminal);
            initializeAudio(machine);
            runtime.Bind();
            machine.onLoaded += runtime.OnLoaded;
            runtime.Refresh();
        }
    }
}
