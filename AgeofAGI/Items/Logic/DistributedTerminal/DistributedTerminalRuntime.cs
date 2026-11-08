using AgeOfAgi.Items.Logic.DataCollector;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.Server;

namespace AgeOfAgi.Items.Logic.DistributedTerminal
{
    internal sealed class DistributedTerminalRuntime
    {
        internal readonly GameItem Machine;
        private readonly GameSlotInventory network, localData;
        private List<DistributedEndpoint> assigned;
        private readonly HashSet<DistributedEndpoint> paid = new HashSet<DistributedEndpoint>();
        internal int Count => Donors().Count;
        internal bool Dispatching => assigned != null;

        internal DistributedTerminalRuntime(GameItem machine, GameSlotInventory network, GameSlotInventory localData)
        { Machine = machine; this.network = network; this.localData = localData; }

        internal HashSet<GameItem> Connected() => new HashSet<GameItem>(NetworkSwitchLinks.Connected(network.childItem, NetworkSwitchLinks.OwnedItems()));
        private List<DistributedEndpoint> Donors() => assigned ?? DistributedNetwork.Available(this);

        internal ServerHardware ReadHardware()
        {
            var donors = Donors();
            var connected = Connected();
            if (donors.Count == 0 || donors.Any(donor => !connected.Contains(donor.Network.childItem) || !donor.CanSupply(paid.Contains(donor)))) return null;
            return ServerHardware.Aggregate(donors.Select(donor => donor.ReadHardware()));
        }

        internal ServerDataPool ReadData() => new ServerDataPool(() =>
        {
            var connected = Connected();
            return NetworkSwitchLinks.OwnedItems().Where(item => item.identifier == DataCollectorState.CollectorId)
                .OrderBy(item => item.GetUniqueID()).Select(item => item.contentWindow?.childElement as MachineryPanelRow)
                .Where(row => row != null && connected.Contains((row.GetStorage(DataCollectorState.NetworkIndex) as GameSlotInventory)?.childItem))
                .SelectMany(row => ((GameGridInventory)row.GetStorage(DataCollectorState.ContainerIndex)).childItems)
                .Concat(localData.childItems);
        });

        internal void Assign(List<DistributedEndpoint> donors) { assigned = donors; paid.Clear(); }
        internal void Release() { assigned = null; paid.Clear(); }

        internal void AddPower(CraftingRecipe recipe, List<GameItem> inputs)
        {
            foreach (var donor in Donors().Where(donor => !paid.Contains(donor)))
            {
                GameItem power = donor.Battery.childItem;
                inputs.Add(power);
                recipe.AddTool(new CraftingCustomFilter(power.name, donor.ReadHardware().Power, item => ReferenceEquals(item, power)));
            }
        }

        internal void Committed()
        {
            foreach (var donor in Donors())
            {
                paid.Add(donor);
                PowerHelper.UpdateEnergyCreditSprite(donor.Battery.childItem);
            }
        }
    }
}
