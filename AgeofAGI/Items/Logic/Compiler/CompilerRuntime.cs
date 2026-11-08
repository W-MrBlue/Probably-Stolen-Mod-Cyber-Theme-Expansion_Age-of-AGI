using AgeOfAgi.Items.Logic.BaseModel;
using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

using static AgeOfAgi.Items.Logic.Compiler.CompilerState;

namespace AgeOfAgi.Items.Logic.Compiler
{
    internal sealed class CompilerRuntime
    {
        private readonly GameItem machine;
        private readonly CompilerCatalog catalog;
        private readonly AiPartCatalog parts;
        private readonly int initialCost;
        private readonly GameSlotInventory battery, baseSlot;
        private readonly GameGridInventory grid, output;
        private readonly MachineryButton execute;
        private readonly MachineryText preview;
        private readonly MachineryPanel gridPanel;
        private readonly PixelWindow window;
        private bool busy, refreshing;
        private int gridWidth, gridHeight;
        private string spriteKey, previewText;
        private GameItem pendingOutput;

        internal CompilerRuntime(GameItem machine, CompilerCatalog catalog, AiPartCatalog parts)
        {
            this.machine = machine;
            this.catalog = catalog;
            this.parts = parts;

            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }
            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }
            initialCost = MachineParameters.GetPowerPerCycle(machine);
            battery = new GameSlotInventory(1, 2);
            baseSlot = new GameSlotInventory(2, 2);
            grid = new GameGridInventory(0, 0);
            output = new GameGridInventory(6, 4);
            execute = new MachineryButton(Text("compiler_execute"));
            preview = new MachineryText(uppercase: false);
            gridPanel = global::MachineHelper.Panel(Text("compiler_grid"), grid, "grid");
            window = global::MachineHelper.CreateMachineryWindow(true,
                global::MachineHelper.PowerPanel(battery),
                global::MachineHelper.Panel(Text("compiler_base"), baseSlot, "base"), gridPanel,
                global::MachineHelper.Panel(Text("compiler_output"), output, "output"),
                global::MachineHelper.Panel(Text("compiler_control"), MachineryStack.Vertical(preview, execute), "control"));
            machine.SetContentWindow(window);
            machine.EnableTag("CONTAINER_TAG");
            machine.EnableTag("IMPORTANT_TAG");
            global::MachineHelper.SetupBatterySlot(battery, machine);
        }

        internal void Bind()
        {
            baseSlot.mayInventoryAddItemFunc = (item, _) => item != null && item.unitCount == 1 &&
                GeneralHelper.IsItemOwned(item) && BaseModelState.IsBaseModel(item);
            grid.mayInventoryAddItemFunc = (item, _) => item != null && item.unitCount == 1 &&
                GeneralHelper.IsItemOwned(item) && AiPartState.IsPart(item) && parts.TryGet(item.identifier, out var ignored) && baseSlot.childItem != null;

            output.mayInventoryAddItemFunc = (item, _) => pendingOutput != null && ReferenceEquals(item, pendingOutput);

            execute.SetCallback(Execute);

            battery.onSlotAddItemEndFunc = (_, _, _) => RefreshState();
            battery.onSlotRemoveItemEndFunc = (_, _, _) => RefreshState();
            baseSlot.onSlotAddItemEndFunc = (_, _, _) => RefreshState();
            baseSlot.onSlotRemoveItemEndFunc = (_, _, _) => RefreshState();
            grid.onSlotAddItemEndFunc = (_, _, _) => RefreshState();
            grid.onSlotRemoveItemEndFunc = (_, _, _) => RefreshState();
            output.onSlotAddItemEndFunc = (_, _, _) => RefreshState();
            output.onSlotRemoveItemEndFunc = (_, _, _) => RefreshState();
            // Native inventory-state updates include moves/rotations within the same
            // grid, battery changes and reopening the machinery window.
            window.AddInventoryStateChangeAction(RefreshState);
        }

        private bool Evaluate(out CompilerAssemblyResult result, out string reason)
        {
            result = null;
            reason = "compiler_missing_base";
            GameItem baseItem = baseSlot.childItem;
            if (baseItem == null || baseItem.unitCount != 1 || !GeneralHelper.IsItemOwned(baseItem) || !BaseModelState.IsBaseModel(baseItem))
            {
                return false;
            }
            return CompilerAssemblyCalculator.TryCalculate(grid, BaseModelState.GetGridWidth(baseItem),
                BaseModelState.GetGridHeight(baseItem), BaseModelState.GetMemoryMultiplier(baseItem),
                parts, catalog.Recipe, out result, out reason);
        }

        private bool OutputFits()
        {
            GameItem probe = DirectoryMaster.Item(catalog.Recipe.OutputItemId);
            try
            {
                pendingOutput = probe;
                return GraphUtils.CanAccept(output, probe);
            }
            finally
            {
                pendingOutput = null;
                probe.Destroy();
            }
        }

        internal void RefreshState()
        {
            if (busy || refreshing)
            {
                return;
            }
            refreshing = true;
            try
            {
                GameItem baseItem = baseSlot.childItem;
                bool changed = false;
                if (baseItem != null)
                {
                    int width = BaseModelState.GetGridWidth(baseItem);
                    int height = BaseModelState.GetGridHeight(baseItem);
                    if (width != gridWidth || height != gridHeight)
                    {
                        grid.SetShape(width, height);
                        gridWidth = width;
                        gridHeight = height;
                        changed = true;
                    }
                }
                if (gridPanel.isHidden != (baseItem == null))
                {
                    gridPanel.SetHidden(baseItem == null);
                    changed = true;
                }
                if (changed)
                {
                    machine.contentWindow?.Validate();
                }
                if (baseItem != null && grid.childItems.Count > 0 && !GeneralHelper.IsItemLocked(baseItem))
                {
                    GeneralHelper.LockItem(baseItem);
                }
                else if (baseItem != null && grid.childItems.Count == 0 && GeneralHelper.IsItemLocked(baseItem))
                {
                    GeneralHelper.UnlockItem(baseItem);
                }
                if (baseItem != null)
                {
                    if (grid.childItems.Count > 0) baseItem.EnableTag(BaseLockedTag);
                    else baseItem.DisableTag(BaseLockedTag);
                }

                bool valid = Evaluate(out var result, out string reason);
                int cost = MachineParameters.GetPowerPerCycle(machine);
                // Native IsItemOwned dereferences its argument. Factories run
                // before save children are restored, and removing power also
                // refreshes the window with an empty battery slot.
                GameItem batteryItem = battery.childItem;
                bool powered = cost >= 0 && batteryItem != null && GeneralHelper.IsItemOwned(batteryItem) &&
                    PowerHelper.CanDrawPowerSource(batteryItem, cost);
                bool fits = valid && powered && OutputFits();
                string key = fits ? WorkingSpriteKey : IdleSpriteKey;
                if (spriteKey != key)
                {
                    spriteKey = key;
                    machine.SetSpriteAndShapeFromMod(ModConstants.ModId, key);
                    machine.Validate();
                }
                string nextText = valid
                    ? Text("compiler_preview", result.Preview.TrainingWork,
                        result.Preview.MemoryGb, result.Preview.ComputeBonusPercent, result.Preview.InitialDurability, cost, result.Preview.TrainingData)
                    : Text(reason);
                if (valid)
                {
                    nextText += "\n\n" + AiModelState.TaskName(result.TaskType);
                    string traits = AiModelTraitText.Describe(result.Preview.Traits, result.Preview.BatchSize);
                    if (traits.Length > 0) nextText += "\n" + traits;
                }
                if (valid && !powered)
                {
                    nextText += "\n" + Text("compiler_insufficient_power");
                }
                else if (valid && !fits)
                {
                    nextText += "\n" + Text("compiler_output_full");
                }
                nextText = nextText.Replace("\\n", "\n");
                if (previewText != nextText)
                {
                    previewText = nextText;
                    preview.SetText(nextText);
                }
            }
            finally
            {
                refreshing = false;
            }
        }

        private void Execute()
        {
            if (busy || refreshing)
            {
                return;
            }
            busy = true;
            GameItem product = null;
            bool committed = false;
            try
            {
                GameItem baseItem = baseSlot.childItem;
                GameItem batteryItem = battery.childItem;
                int cost = MachineParameters.GetPowerPerCycle(machine);
                if (!Evaluate(out var result, out _) || cost < 0 || batteryItem == null || !GeneralHelper.IsItemOwned(batteryItem) ||
                    !PowerHelper.CanDrawPowerSource(batteryItem, cost))
                {
                    return;
                }
                product = DirectoryMaster.Item(catalog.Recipe.OutputItemId);
                AiModelState.Apply(product, result.Model);
                var inputs = result.Parts.Select(part => part.Item).ToList();
                inputs.Add(batteryItem);
                CraftingRecipe recipe = new CraftingRecipe();
                foreach (var part in result.Parts)
                {
                    GameItem selected = part.Item;
                    recipe.AddMaterial(selected.name, 1, item => ReferenceEquals(item, selected));
                }
                recipe.AddTool(new CraftingCustomFilter(Text("compiler_power"), cost, item => ReferenceEquals(item, batteryItem)))
                    .SetCraftingFunc((_, _) => new List<GameItem> { product });
                if (!recipe.CanCraft(inputs))
                {
                    return;
                }
                pendingOutput = product;
                if (!GraphUtils.CanAccept(output, product) || GraphUtils.TryAcceptAll(output, product) != 0)
                {
                    return;
                }
                if (!ReferenceEquals(battery.childItem, batteryItem) || !ReferenceEquals(baseSlot.childItem, baseItem) ||
                    !ReferenceEquals(product.parentInventory, output) || MachineParameters.GetPowerPerCycle(machine) != cost ||
                    !Evaluate(out var current, out _) || !result.Matches(current) || !recipe.CanCraft(inputs))
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
                pendingOutput = null;
                try
                {
                    if (!committed)
                    {
                        product?.Destroy();
                    }
                }
                finally
                {
                    busy = false;
                    RefreshState();
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
            gridWidth = 0;
            gridHeight = 0;
            spriteKey = null;
            RefreshState();
        }

        private static string Text(string key, params object[] args) => ModHelper.GetLocalized(ModConstants.ModId, key, args);
    }
}
