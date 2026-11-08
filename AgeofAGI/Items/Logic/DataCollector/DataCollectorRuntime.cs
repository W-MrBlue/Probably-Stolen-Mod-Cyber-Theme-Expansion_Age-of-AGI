using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using System;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

using static AgeOfAgi.Items.Logic.DataCollector.DataCollectorState;

namespace AgeOfAgi.Items.Logic.DataCollector
{
    internal sealed class DataCollectorRuntime
    {
        private readonly GameItem machine;
        private readonly GameSlotInventory battery, network;
        private readonly GameGridInventory containers;
        private readonly PixelWindow window;
        private string spriteKey;
        private bool upgrading;

        internal DataCollectorRuntime(GameItem machine, MachineDefinition definition)
        {
            this.machine = machine;

            MachineryHelper.InitMachinery(machine, definition.PowerPerCycle);
            battery = new GameSlotInventory(1, 2);
            network = new GameSlotInventory(2, 2);
            containers = new GameGridInventory(InitialWidth, ContainerHeight);
            window = global::MachineHelper.CreateMachineryWindow(true,
                global::MachineHelper.PowerPanel(battery),
                global::MachineHelper.Panel(Text("server_network"), network, "network"),
                global::MachineHelper.Panel(Text("server_data_container"), containers, "containers"));
            machine.SetContentWindow(window);
            machine.EnableTag("CONTAINER_TAG");
            machine.EnableTag("IMPORTANT_TAG");
            global::MachineHelper.SetupBatterySlot(battery, machine);
        }

        internal void Bind()
        {
            network.mayInventoryAddItemFunc = (item, _) => item != null && item.unitCount == 1 &&
                GeneralHelper.IsItemOwned(item) && NetworkSwitchUpgrades.IsNetworkSwitch(item);
            containers.mayInventoryAddItemFunc = (item, _) => item != null && item.unitCount == 1 &&
                GeneralHelper.IsItemOwned(item) && DataContentState.IsContainer(item) && DataContentState.Valid(item);
            battery.slotPriorityFunc = (_, _, _) => 3;
            network.slotPriorityFunc = (_, _, _) => 2;
            containers.slotPriorityFunc = (_, _, _) => 2;

            containers.onSlotAddItemEndFunc += (_, _, _) => RefreshSprite(machine);
            containers.onSlotRemoveItemEndFunc += (_, _, _) => RefreshSprite(machine);
            window.AddInventoryStateChangeAction(() => RefreshSprite(machine));
            machine.ModifyTag(ExpandedTag, tag => tag.Enable().SetInt(0));
            machine.onCycleEndSlotItemFunc = (loaded, _, _) =>
                global::MachineHelper.TryDrawCyclePower(loaded, battery);
            machine.mayItemTargetThisFunc = CanUpgrade;
            machine.canItemTargetThisFunc = CanUpgrade;
            machine.onItemTargetThisFunc = ApplyUpgrade;
        }

        private void RefreshSprite(GameItem target)
        {
            string key = containers.childItems.Count > 0 ? LoadedSpriteKey : EmptySpriteKey;
            if (spriteKey == key) return;
            spriteKey = key;
            target.SetSprite(ModConstants.ModId, key);
            target.Validate();
        }

        private void ApplyUpgrade(GameItem source, GameItem target)
        {
            if (upgrading || !CanUpgrade(source, target)) return;
            upgrading = true;
            try
            {
                if (!CanUpgrade(source, target)) return;
                target.ModifyTag(ExpandedTag, tag => tag.SetInt(1));
                ApplyExpansion(target, containers);
                source.SetAmount(source.unitCount - UpgradeDiskCost);
                if (source.unitCount == 0) source.Destroy();
                EmporiumAudioPlayer.Instance.ratchet.Play();
            }
            finally
            {
                upgrading = false;
            }
        }

        private static bool CanUpgrade(GameItem source, GameItem target)
        {
            return source != null && target != null && target.identifier == CollectorId &&
                source.identifier == DiskWriterCatalog.BlankDiskId && source.unitCount >= UpgradeDiskCost &&
                target.GetTagReadonly(ExpandedTag).GetInt() == 0 &&
                GeneralHelper.IsItemOwned(source) && GeneralHelper.IsItemOwned(target);
        }

        private static void ApplyExpansion(GameItem machine, GameGridInventory containers)
        {
            bool expanded = machine.GetTagReadonly(ExpandedTag).GetInt() != 0;
            containers.SetShape(expanded ? ExpandedWidth : InitialWidth, ContainerHeight);
            containers.Validate();
            machine.contentWindow?.Validate();
            machine.Validate();
        }

        internal void Refresh()
        {
            ApplyExpansion(machine, containers);
            RefreshSprite(machine);
        }

        internal void OnLoaded(GameItem loaded)
        {
            loaded.EnableTag("CONTAINER_TAG");
            loaded.EnableTag("IMPORTANT_TAG");
            loaded.EnableTag("MACHINE_RECHARGABLE_TAG");
            loaded.SetGameItemType("MACHINE");
            loaded.EnableTag(MachineParameters.MachineTag);
            ApplyExpansion(loaded, containers);
            spriteKey = null;
            RefreshSprite(loaded);
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }
}
