using AgeOfAgi.Items.Logic.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AgeOfAgi.Items.Logic.NetworkSwitch;

namespace AgeOfAgi.Items.Logic.DistributedTerminal
{
    internal static class DistributedNetwork
    {
        private static readonly ConditionalWeakTable<GameItem, DistributedEndpoint> endpoints = new ConditionalWeakTable<GameItem, DistributedEndpoint>();
        private static readonly ConditionalWeakTable<GameItem, DistributedTerminalRuntime> terminals = new ConditionalWeakTable<GameItem, DistributedTerminalRuntime>();
        private static bool running;
        private static bool enabled;
        internal static void Enable() => enabled = true;
        internal static void Shutdown()
        {
            // Weak entries allow the same live items to resume after re-enabling.
            enabled = false;
        }

        internal static void Refresh(GameItem machine)
        {
            if (enabled && !running && endpoints.TryGetValue(machine, out var endpoint)) endpoint.Refresh();
        }

        internal static void Register(DistributedEndpoint endpoint, DistributedTerminalRuntime runtime)
        {
            endpoints.Remove(endpoint.Machine);
            endpoints.Add(endpoint.Machine, endpoint);
            if (runtime != null) { terminals.Remove(endpoint.Machine); terminals.Add(endpoint.Machine, runtime); }
        }

        private static List<DistributedEndpoint> Owned() => NetworkSwitchLinks.OwnedItems().Select(item =>
            endpoints.TryGetValue(item, out var endpoint) ? endpoint : null).Where(endpoint => endpoint != null).Distinct().ToList();

        internal static List<DistributedEndpoint> Available(DistributedTerminalRuntime terminal)
        {
            var connected = terminal.Connected();
            return Owned().Where(endpoint => !endpoint.Terminal && connected.Contains(endpoint.Network.childItem) &&
                !endpoint.CanWork() && endpoint.CanSupply(false)).OrderBy(endpoint => endpoint.Machine.GetUniqueID()).ToList();
        }

        internal static void RunCycle(DistributedEndpoint trigger)
        {
            if (!enabled || running || !GeneralHelper.IsItemOwned(trigger.Machine)) return;
            int day = StoreStation.Instance.dayCounter;
            bool Due(DistributedEndpoint endpoint) => endpoint.CyclePending || !endpoint.Machine.IsTag(ServerState.LastNightTag) ||
                endpoint.Machine.GetTagReadonly(ServerState.LastNightTag).GetInt() != day;
            if (!Due(trigger)) return;
            running = true;
            var all = Owned();
            // Direct native callbacks may precede discovery through the store's roots.
            if (!all.Contains(trigger) && GeneralHelper.IsItemOwned(trigger.Machine)) all.Add(trigger);
            var due = all.Where(Due).ToList();
            try
            {
                foreach (var endpoint in due)
                {
                    endpoint.CyclePending = false;
                    endpoint.Machine.ModifyTag(ServerState.LastNightTag, tag => tag.Enable().SetInt(day));
                }
                foreach (var endpoint in due) endpoint.PrepareCycle?.Invoke();
                // Freeze local priority before any work changes the inventories.
                var local = new HashSet<DistributedEndpoint>(due.Where(endpoint => !endpoint.Terminal && endpoint.CanWork()));
                foreach (var endpoint in local.OrderBy(endpoint => endpoint.Machine.GetUniqueID())) endpoint.Run();
                var reserved = new HashSet<DistributedEndpoint>(local);
                foreach (var endpoint in due.Where(endpoint => endpoint.Terminal).OrderBy(endpoint => endpoint.Machine.GetUniqueID()))
                {
                    var runtime = terminals.GetValue(endpoint.Machine, _ => throw new InvalidOperationException("Missing terminal runtime."));
                    if (endpoint.Battery.childItem == null || !GeneralHelper.IsItemOwned(endpoint.Battery.childItem) ||
                        !global::MachineHelper.TryDrawCyclePower(endpoint.Machine, endpoint.Battery)) continue;
                    var connected = runtime.Connected();
                    var donors = due.Where(donor => !donor.Terminal && !reserved.Contains(donor) &&
                        connected.Contains(donor.Network.childItem) && donor.CanSupply(false)).OrderBy(donor => donor.Machine.GetUniqueID()).ToList();
                    runtime.Assign(donors);
                    // A lower-numbered terminal reserves its eligible servers for the whole night.
                    foreach (var donor in donors) reserved.Add(donor);
                    try { if (donors.Count > 0) endpoint.Run(); }
                    finally { runtime.Release(); }
                }
            }
            finally
            {
                running = false;
                foreach (var endpoint in all) endpoint.Refresh();
            }
        }
    }
}
