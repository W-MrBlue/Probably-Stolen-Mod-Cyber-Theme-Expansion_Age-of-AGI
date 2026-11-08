using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

using static AgeOfAgi.Items.Logic.DiskWriter.DiskWriterState;

namespace AgeOfAgi.Items.Logic.DiskWriter
{
    internal sealed class DiskWriterRuntime
    {
        private readonly GameItem machine;
        private readonly DiskWriterCatalog catalog;
        private readonly int initialCost;
        private readonly GameSlotInventory battery, input, chip;
        private readonly GameGridInventory output;
        private readonly DiskWriterSelector selector;
        private readonly MachineryButton execute;
        private readonly PixelWindow window;
        private GameItem internalOutput;
        private bool executing, refreshing;
        private string spriteKey;

        internal DiskWriterRuntime(GameItem machine, DiskWriterCatalog catalog)
        {
            this.machine = machine;
            this.catalog = catalog;

            initialCost = MachineParameters.GetPowerPerCycle(machine);
            battery = new GameSlotInventory(1, 2);
            input = new GameSlotInventory(2, 2);
            chip = new GameSlotInventory(1, 1);
            output = new GameGridInventory(6, 4);
            selector = new DiskWriterSelector(machine, catalog, chip);
            execute = new MachineryButton(Text("disk_writer_execute"));

            // Use the same native UI and battery-slot setup as MachineBottlePrinter.
            window = global::MachineHelper.CreateMachineryWindow(true,
                global::MachineHelper.PowerPanel(battery),
                global::MachineHelper.Panel(Text("disk_writer_input"), input, "input"),
                global::MachineHelper.Panel(Text("disk_writer_chip"), chip, "chip"),
                global::MachineHelper.Panel(Text("disk_writer_recipe"), selector.Content, "recipe"),
                global::MachineHelper.Panel(Text("disk_writer_output"), output, "output"),
                global::MachineHelper.Panel(Text("disk_writer_control"), MachineryStack.Vertical(execute), "control"));
            machine.SetContentWindow(window);
            window.closeCallback += selector.Close;
            machine.EnableTag("CONTAINER_TAG");
            machine.EnableTag("IMPORTANT_TAG");
            global::MachineHelper.SetupBatterySlot(battery, machine);
        }

        internal void Bind()
        {
            input.mayInventoryAddItemFunc = (item, _) => item != null && GeneralHelper.IsItemOwned(item) && item.identifier == DiskWriterCatalog.BlankDiskId;
            chip.mayInventoryAddItemFunc = (item, _) => item != null && GeneralHelper.IsItemOwned(item) && DiskWriterCatalog.IsChip(item.identifier);

            selector.CanChoose = () => !executing;
            selector.SelectionChanged = Refresh;
            // Like CraftingHelper's native output slot, accept only during the machine's
            // internal transfer. The item identity check also blocks reentrant injections.
            output.mayInventoryAddItemFunc = (item, _) => internalOutput != null && ReferenceEquals(item, internalOutput);
            execute.SetCallback(Execute);

            foreach (GameInventory inventory in new GameInventory[] { input, chip })
            {
                inventory.onSlotAddItemEndFunc += (_, _, _) => Refresh();
                inventory.onSlotRemoveItemEndFunc += (_, _, _) => Refresh();
            }
            window.AddInventoryStateChangeAction(Refresh);

            // Old parameter-only disk writers may lack container tags in saved state.
            // Factories rebuild the native UI; restore structural markers and only missing
            // cost metadata, preserving any power value already present in the save.
        }

        private bool TryGetSelection(out string resultId)
        {
            resultId = null;
            GameItem source = chip.childItem;
            return source != null && GeneralHelper.IsItemOwned(source) &&
                catalog.TryGetResult(source.identifier, machine.GetTagReadonly(SelectedRecipeTag).GetString(), out resultId);
        }

        internal void Refresh()
        {
            if (executing || refreshing) return;
            refreshing = true;
            try
            {
                string key = input.childItem == null ? EmptySpriteKey : LoadedSpriteKey;
                if (spriteKey != key)
                {
                    spriteKey = key;
                    // Both states share a footprint. Keep the machine's saved transform.
                    machine.SetSprite(ModConstants.ModId, key);
                    machine.Validate();
                }
                GameItem source = chip.childItem;
                bool hasChip = source != null && GeneralHelper.IsItemOwned(source) && DiskWriterCatalog.IsChip(source.identifier);
                bool selected = TryGetSelection(out string resultId);
                if (selected) execute.Enable();
                else execute.Disable();
                selector.Refresh(resultId, hasChip);
            }
            finally { refreshing = false; }
        }

        private void Execute()
        {
            if (executing)
            {
                return;
            }
            executing = true;
            GameItem product = null;
            bool committed = false;
            try
            {
                GameItem diskItem = input.childItem;
                GameItem chipItem = chip.childItem;
                GameItem batteryItem = battery.childItem;
                string recipeId = machine.GetTagReadonly(SelectedRecipeTag).GetString();
                int cost = MachineParameters.GetPowerPerCycle(machine);
                if (cost < 0 || diskItem == null || diskItem.identifier != DiskWriterCatalog.BlankDiskId || diskItem.unitCount < 1 ||
                    chipItem == null || batteryItem == null || chipItem.unitCount < 1 ||
                    !GeneralHelper.IsItemOwned(diskItem) || !GeneralHelper.IsItemOwned(chipItem) ||
                    !catalog.TryGetResult(chipItem.identifier, recipeId, out string resultId) ||
                    !PowerHelper.CanDrawPowerSource(batteryItem, cost))
                {
                    return;
                }

                product = DirectoryMaster.Item(resultId);
                var inputs = new List<GameItem> { diskItem, chipItem, batteryItem };
                CraftingRecipe recipe = new CraftingRecipe()
                    .AddMaterialIdentifier(diskItem.name, 1, DiskWriterCatalog.BlankDiskId)
                    .AddToolIdentifier(chipItem.name, chipItem.identifier)
                    .AddTool(new CraftingCustomFilter(Text("disk_writer_power"), cost, item => ReferenceEquals(item, batteryItem)))
                    .SetCraftingFunc((_, _) => new List<GameItem> { product });
                if (!recipe.CanCraft(inputs))
                {
                    return;
                }

                internalOutput = product;
                if (!GraphUtils.CanAccept(output, product) || GraphUtils.TryAcceptAll(output, product) != 0)
                {
                    return;
                }
                // Output is reserved first. Failed placement never consumes materials
                // or power. Native Craft handles one disk and the powered-tool cost;
                // its tool filter keeps the source chip unchanged.
                if (!ReferenceEquals(input.childItem, diskItem) || !ReferenceEquals(chip.childItem, chipItem) ||
                    !ReferenceEquals(battery.childItem, batteryItem) || MachineParameters.GetPowerPerCycle(machine) != cost ||
                    machine.GetTagReadonly(SelectedRecipeTag).GetString() != recipeId ||
                    !TryGetSelection(out string currentResult) || currentResult != resultId ||
                    !recipe.CanCraft(inputs))
                {
                    return;
                }
                if (!recipe.Craft(inputs).Contains(product))
                {
                    return;
                }
                committed = true;
                PowerHelper.UpdateEnergyCreditSprite(batteryItem);
            }
            finally
            {
                internalOutput = null;
                try
                {
                    if (!committed)
                    {
                        product?.Destroy();
                    }
                }
                finally
                {
                    executing = false;
                    Refresh();
                }
            }
        }

        internal void OnLoaded(GameItem loaded)
        {
            loaded.EnableTag("CONTAINER_TAG");
            loaded.EnableTag("IMPORTANT_TAG");
            loaded.EnableTag("MACHINE_RECHARGABLE_TAG");
            loaded.SetGameItemType("MACHINE");
            loaded.EnableTag(MachineParameters.MachineTag);
            if (!loaded.IsTag(MachineParameters.PowerPerCycleTag))
            {
                loaded.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.Enable().SetInt(initialCost));
            }
            spriteKey = null;
            selector.Close();
            Refresh();
        }

        private static string Text(string key, params object[] args)
        {
            return ModHelper.GetLocalized(ModConstants.ModId, key, args);
        }
    }
}
