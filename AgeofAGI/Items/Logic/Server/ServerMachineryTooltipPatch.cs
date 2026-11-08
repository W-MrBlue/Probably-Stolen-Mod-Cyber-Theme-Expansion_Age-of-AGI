using HarmonyLib;
using AgeOfAgi.Items.Logic.DistributedTerminal;

namespace AgeOfAgi.Items.Logic.Server
{
    [HarmonyPatch(typeof(MachineryHelper), "CreateMachineryTooltip")]
    internal static class ServerMachineryTooltipPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(GameItem item)
        {
            // Terminals retain native daily power handling. ServerTooltip owns
            // their power row and places the battery warning below the statistics.
            return item == null || !item.IsTag(DistributedTerminalState.TerminalTag) ||
                !item.IsTag(ServerState.ServerTag) ||
                !ModLoader.Mods.Exists(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled);
        }
    }
}
