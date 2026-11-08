using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.Compiler;

namespace AgeOfAgi.Items.Helpers
{
    internal static class CompilerHelper
    {
        internal static void Init(GameItem machine, MachineDefinition definition, CompilerCatalog catalog, AiPartCatalog parts,
            Action<GameItem> initializeAudio)
        {
            MachineParameters.InitMachine(machine, definition);
            var runtime = new CompilerRuntime(machine, catalog, parts);
            initializeAudio(machine);
            runtime.Bind();
            machine.onLoaded += runtime.OnLoaded;
            runtime.RefreshState();
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => CompilerState.CreateTooltip(builder, item);
    }
}
