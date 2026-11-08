using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.DiskWriter;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;

internal static class DiskWriterChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        DiskWriterCatalog catalog = DiskWriterCatalog.Load();
        Assert(catalog.Count == 5 && !items.Any(item => item.Kind == "template"), "Register five selectable recipes without template items.");
        catalog.ValidateMappings(items);
        CheckCatalog(items, catalog);
        LoadedMod loaded = Program.PrepareLoadedMod(items);
        var registration = new ItemRegistration(loaded.Manifest);
        var directory = new ModItemDirectory();
        ModHelper.FileReads = 0;
        registration.Register(directory, items);
        Assert(ModHelper.FileReads == 14, "Load disk-writer mappings once with the other catalogs.");
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        try
        {
            CheckWhitelists();
            CheckStateSprites();
            CheckSelection();
            for (int index = 0; index < 5; index++)
                foreach (string family in new[] { "enc", "dec" }) CheckRecipe(index, family);
            CheckFailures();
            CheckReentrancy();
            CheckRebuiltWindow();
            CheckPreviewFailureCleanup(directory);

            Func<GameItem> previous = directory.Factories[DiskWriterCatalog.DiskWriterId];
            foreach (string invalid in new[] { null, "{}", "{" })
            {
                ModHelper.FileOverrides[DiskWriterCatalog.CatalogPath] = invalid;
                ExpectFailure(() => registration.Register(directory, items));
                Assert(ReferenceEquals(previous, directory.Factories[DiskWriterCatalog.DiskWriterId]),
                    "Invalid recipe mappings must preserve the registered batch.");
            }
        }
        finally
        {
            ModHelper.FileOverrides.Remove(DiskWriterCatalog.CatalogPath);
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
            registration.Unregister();
        }
    }

    private static void CheckCatalog(IReadOnlyList<ItemDefinition> items, DiskWriterCatalog catalog)
    {
        const string valid = "id,decoderItemId,encoderItemId\nshape_0,age_of_agi.part_dec_0,age_of_agi.part_enc_0\n";
        foreach (string invalid in new[] { "", "id", "id,id", "id\nage_of_agi.invalid", valid + " {}",
            valid.Replace("decoderItemId", "typo"), valid.Replace("shape_0", "other.recipe"), valid.Replace("shape_0", "") })
            ExpectFailure(() => DiskWriterCatalog.Parse(invalid));
        foreach (string field in new[] { "id", "decoderItemId", "encoderItemId" })
        {
            var root = CsvFixture.Parse(valid);
            root.RemoveColumn(field);
            ExpectFailure(() => DiskWriterCatalog.Parse(root.ToString()));
        }
        var duplicate = CsvFixture.Parse(valid);
        duplicate.DuplicateFirstRow();
        duplicate.Rows[1]["id"] = "SHAPE_0";
        ExpectFailure(() => DiskWriterCatalog.Parse(duplicate.ToString()));
        ExpectFailure(() => catalog.ValidateMappings(items.Where(item => item.ItemId != "age_of_agi.part_dec_0").ToArray()));
        ExpectFailure(() => DiskWriterCatalog.Parse(valid.Replace("age_of_agi.part_dec_0", "age_of_agi.part_dec_chip")).ValidateMappings(items));
        ExpectFailure(() => DiskWriterCatalog.Parse(valid).ValidateMappings(items));
        var sameOutput = CsvFixture.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(ModHelper.Root, DiskWriterCatalog.CatalogPath)));
        sameOutput.Rows[1]["encoderItemId"] = sameOutput.Rows[0]["encoderItemId"];
        ExpectFailure(() => DiskWriterCatalog.Parse(sameOutput.ToString()).ValidateMappings(items));
        foreach (string chip in new[] { null, "other.chip", DiskWriterCatalog.BlankDiskId })
            Assert(!catalog.TryGetResult(chip, "shape_0", out _), "Unknown chips must not select recipes.");
        Assert(!catalog.TryGetResult(DiskWriterCatalog.EncoderChipId, "missing", out _) &&
            !catalog.TryGetResult(DiskWriterCatalog.EncoderChipId, null, out _), "Missing recipe choices cannot select an output.");
    }

    private static void CheckWhitelists()
    {
        ModHelper.Locale = "zh";
        var ports = new Ports();
        Assert(ports.Button.Text == "执行", "Use the native MachineryButton with localized text.");
        Assert(ports.Row.Panels.Select(panel => panel.role).SequenceEqual(new[] { "power", "input", "chip", "recipe", "output", "control" }),
            "Native panels must replace the template slot with a selector.");
        Assert(ports.Battery.Width == 1 && ports.Battery.Height == 2 && ports.Input.Width == 2 && ports.Input.Height == 2 &&
            ports.Chip.Width == 1 && ports.Chip.Height == 1 &&
            ports.Output.Width == 6 && ports.Output.Height == 4, "Incorrect slot/grid dimensions.");
        Assert(ports.Row.GetStorage(4) == null && ports.Row.GetPanel("template") == null,
            "The selector is not a storage node; only power/input/chip/output remain.");
        Assert(ports.Machine.IsTag("CONTAINER_TAG") && ports.Machine.IsTag("MACHINE_RECHARGABLE_TAG"), "Use native container/battery structural tags.");
        GameItem disk = DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId);
        GameItem encoder = DirectoryMaster.Item(DiskWriterCatalog.EncoderChipId);
        GameItem decoder = DirectoryMaster.Item(DiskWriterCatalog.DecoderChipId);
        GameItem foreign = new GameItem { Id = "other.disk_empty" };
        Assert(GraphUtils.CanAccept(ports.Input, disk) && !GraphUtils.CanAccept(ports.Input, encoder) && !GraphUtils.CanAccept(ports.Input, foreign),
            "Blank-disk input must use its exact ID whitelist.");
        Assert(GraphUtils.CanAccept(ports.Chip, encoder) && GraphUtils.CanAccept(ports.Chip, decoder) && !GraphUtils.CanAccept(ports.Chip, disk),
            "Chip input must accept encoder/decoder chips only.");
        encoder.Owned = false;
        Assert(!GraphUtils.CanAccept(ports.Chip, encoder), "Match native ownership restrictions.");
        Assert(GraphUtils.CanAccept(ports.Battery, MakeBattery(10)) && !GraphUtils.CanAccept(ports.Battery, disk), "Use native battery acceptance.");
        Assert(!GraphUtils.CanAccept(ports.Output, DirectoryMaster.Item("age_of_agi.part_enc_0")), "Output must reject all external input.");
        ModHelper.Locale = "en";
        Assert(new Ports().Button.Text == "Execute", "English native button text is missing.");
    }

    private static void CheckStateSprites()
    {
        var ports = new Ports();
        Assert(ports.Machine.SpriteKey == DiskWriterState.EmptySpriteKey, "A new disk writer has no disk.");
        ports.Machine.SetShape(new GridShapeBuilder(ports.Machine.shape).SetTransform(7, 9, true, 1));
        GridShape placement = ports.Machine.shape.Clone();
        GameItem disk = DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId);
        Put(ports.Input, disk);
        Assert(ports.Machine.SpriteKey == DiskWriterState.LoadedSpriteKey, "Inserting a disk changes appearance without requiring power or a recipe.");
        Assert(ports.Machine.shape.minX == 7 && ports.Machine.shape.minY == 9 && ports.Machine.shape.flipped &&
            ports.Machine.shape.orientation == 1 && ItemShapeHelper.SameCells(placement, ports.Machine.shape),
            "Changing disk state must preserve machine placement, rotation, reflection and footprint.");
        ports.Input.Remove(disk);
        Assert(ports.Machine.SpriteKey == DiskWriterState.EmptySpriteKey, "Removing the disk restores the empty appearance.");
        ports.Input.UncheckedAccept(disk);
        ports.Machine.onLoaded(ports.Machine);
        Assert(ports.Machine.SpriteKey == DiskWriterState.LoadedSpriteKey, "Load refreshes restored contents without inventory events.");
        ports.Input.RawRemove(disk);
        ports.Machine.onLoaded(ports.Machine);
        Assert(ports.Machine.SpriteKey == DiskWriterState.EmptySpriteKey, "Loading an empty writer restores the empty appearance.");
    }

    private static void CheckSelection()
    {
        var ports = new Ports();
        Assert(!ports.Choose.Enabled && !ports.Button.Enabled, "No chip means no choices or execution.");
        Assert(ports.Choose.Text == "Choose component" && ports.Preview.ItemId == null, "Start with no selected image.");
        var content = (MachineryStack)ports.Row.GetPanel("recipe").content;
        Assert(content.vertical && ReferenceEquals(content.Elements[0], ports.Preview) && ReferenceEquals(content.Elements[1], ports.Choose),
            "The selected component must appear above the choose/change button.");
        Put(ports.Chip, DirectoryMaster.Item(DiskWriterCatalog.EncoderChipId));
        Assert(ports.Choose.Enabled && !ports.Button.Enabled, "A chip enables selection without silently choosing a recipe.");
        ModHelper.Locale = "zh";
        ports.Choose.rectTransform.position = new UnityEngine.Vector3(100, 200, 0);
        int created = ItemDirectory.CreatedInstances.Count;
        ports.Choose.Click();
        PixelWindow popup = ports.Popup;
        var rows = ports.ChoiceRows;
        Assert(rows.vertical && rows.Buttons.Length == 6 && rows.Buttons.Last().Text == "取消",
            "Show five vertically arranged image buttons followed by localized Cancel.");
        Assert(popup.ShowPosition.x == 100 + ports.Choose.widthPixels + 6 && popup.ShowPosition.y == 200,
            "The chooser initially opens beside the choose button, at its current world position.");
        Assert(rows.Buttons.Take(5).Select(button => button.Icon.AssetKey).SequenceEqual(Enumerable.Range(0, 5).Select(i => "part_enc_" + i)),
            "The installed chip determines the image family and catalog order.");
        Assert(rows.Buttons.Take(5).All(button => button.Text == null && button.Description.Length > 0) && popup.childElement.GetStorage(0) == null,
            "Choices are image buttons with names available on hover, not inventory items or labels.");
        Assert(ItemDirectory.CreatedInstances.Skip(created).All(item => item.Destroyed), "Keep no temporary preview items alive in the chooser.");
        rows.Buttons.Last().Click();
        Assert(!popup.IsVisible() && !ports.Button.Enabled, "Cancel hides the chooser without changing selection.");
        int windowCount = PixelWindow.CreatedWindows.Count;
        ports.Choose.Click();
        Assert(PixelWindow.CreatedWindows.Count == windowCount, "Reopen the same chooser instead of leaking additional windows.");
        rows.Buttons[3].Click();
        Assert(!popup.IsVisible() && ports.Machine.GetTagReadonly(DiskWriterState.SelectedRecipeTag).GetString() == "shape_3" &&
            ports.Preview.ItemId == "age_of_agi.part_enc_3" && ports.Choose.Text == "更改组件",
            "Selecting a component closes the chooser, persists its ID, displays its image and changes the button caption.");
        var images = ports.Preview.handler.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        Assert(images.Length == 2 && images.Count(image => image.enabled) == 1 && images.All(image => !image.raycastTarget),
            "The selected component has only a sprite: no interactive surface, grid, background or item labels.");
        Assert(ports.Preview.handler.Children.Single().rectTransform.sizeDelta.x == 48 &&
            ports.Preview.handler.Children.Single().rectTransform.sizeDelta.y == 32,
            "Keep the sprite's native aspect ratio inside the fixed preview area.");
        ports.Choose.Click();
        rows.Buttons.Last().Click();
        Assert(ports.Machine.GetTagReadonly(DiskWriterState.SelectedRecipeTag).GetString() == "shape_3", "Cancel preserves an existing selection.");
        ports.Choose.Click();
        ports.Machine.contentWindow.closeCallback();
        Assert(!popup.IsVisible(), "Closing the machine closes its adjacent chooser.");
        ports.Choose.Click();
        ports.Chip.Remove(ports.Chip.childItem);
        Assert(!popup.IsVisible() && !ports.Button.Enabled && !ports.Choose.Enabled && ports.Preview.ItemId == null,
            "Removing the chip closes its chooser, clears the image and disables selection and execution.");
        Put(ports.Chip, DirectoryMaster.Item(DiskWriterCatalog.DecoderChipId));
        ports.Choose.Click();
        Assert(rows.Buttons.Take(5).All(button => button.Icon.AssetKey.StartsWith("part_dec_")),
            "Replacing the chip rebuilds choices for the decoder family.");
        // A stale hidden row must not overwrite the choice after its source chip changes.
        ports.Chip.Remove(ports.Chip.childItem);
        Put(ports.Chip, DirectoryMaster.Item(DiskWriterCatalog.EncoderChipId));
        rows.Buttons[4].Click();
        Assert(ports.Machine.GetTagReadonly(DiskWriterState.SelectedRecipeTag).GetString() == "shape_3",
            "A stale choice from another chip instance is discarded.");
        ModHelper.Locale = "en";
        Put(ports.Battery, MakeBattery(10));
        Put(ports.Input, DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId));
        ports.Chip.Remove(ports.Chip.childItem);
        Put(ports.Chip, DirectoryMaster.Item(DiskWriterCatalog.DecoderChipId));
        ports.Button.Click();
        Assert(ports.Output.childItems.Single().identifier == "age_of_agi.part_dec_3",
            "The retained shape choice follows the current chip family when executing.");
        ports.Machine.ModifyTag(DiskWriterState.SelectedRecipeTag, state => state.Enable().SetString("removed_recipe"));
        ports.Machine.onLoaded(ports.Machine);
        Assert(!ports.Button.Enabled && ports.Preview.ItemId == null && ports.Choose.Text == "Choose component",
            "An unknown saved recipe clears the image and does not fall back to an unintended output.");
        ports.Choose.Click();
        var lifetime = ports.Preview.handler.gameObject.Components.OfType<DiskWriterSelectorLifetime>().Single();
        typeof(DiskWriterSelectorLifetime).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(lifetime, null);
        Assert(!popup.IsVisible(), "Hiding the machine UI closes the popup even without a close-button callback.");
        lifetime.Selector.Dispose();
        Assert(!GraphHandler.windowGraphNodeRoots.Contains(popup) && popup.handler.gameObject.Destroyed,
            "Destroying the selector releases its floating window and graph root.");
    }

    private static void CheckRecipe(int index, string family)
    {
        var ports = Filled(index, family);
        GameItem disk = ports.Input.childItem, chip = ports.Chip.childItem;
        GameItem battery = ports.Battery.childItem;
        ports.Button.Click();
        GameItem product = ports.Output.childItems.Single();
        Assert(product.identifier == "age_of_agi.part_" + family + "_" + index, "Chip/selected recipe mapping selected the wrong product.");
        Assert(AiPartState.IsPart(product) && product.GetGameItemType().SequenceEqual(new[] { "codeparts" }),
            "Printed code components must use the AI parts family initializer and displayed category.");
        Assert(ItemShapeHelper.SameCells(product.modifiedShape, ModLoader.Mods.Single().Sprites[product.SpriteKey].Shape),
            "The output keeps its own sprite-derived shape without template normalization.");
        Assert(Energy(battery) == 7 && disk.Destroyed && ports.Input.childItem == null, "Consume exactly 3 power and one blank disk.");
        Assert(ports.Machine.SpriteKey == DiskWriterState.EmptySpriteKey, "Consuming the last disk restores the empty appearance.");
        Assert(ReferenceEquals(ports.Chip.childItem, chip) && chip.unitCount == 1 && !chip.Destroyed, "The source chip must remain unchanged.");
        Assert(chip.IsTag(AiPartState.ChipTag) && chip.GetGameItemType().Count == 0, "Source chips must belong to the family without a displayed type.");
        Assert(!GraphUtils.CanAccept(ports.Output, DirectoryMaster.Item(product.identifier)), "Close output insertion after execution.");
        ports.Button.Click();
        Assert(Energy(battery) == 7 && ports.Output.childItems.Count == 1, "A second click without a disk must do nothing.");
        Put(ports.Input, DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId));
        ports.Button.Click();
        Assert(Energy(battery) == 4 && ports.Output.childItems.Count == 2, "The source chip and saved choice must support repeated writes.");
        // Products can be removed normally; only insertion is restricted.
        ports.Output.Remove(product);
        Assert(product.parentInventory == null, "Output must allow extraction.");
    }

    private static void CheckFailures()
    {
        foreach (string missing in new[] { "power", "input", "chip" })
        {
            var ports = Filled();
            var inventory = (GameSlotInventory)ports.Row.GetPanel(missing).content;
            inventory.Remove(inventory.childItem);
            ExpectNoExecution(ports);
        }
        var weak = Filled();
        weak.Battery.childItem.ModifyTag("power_source_item_energy", state => state.SetInt(2));
        ExpectNoExecution(weak);
        var full = Filled();
        var blocker = new GameItem { Id = "blocker" };
        blocker.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1, 24).ToArray(), 6));
        full.Output.Accept(blocker); // Simulate existing persisted output without opening public insertion.
        ExpectNoExecution(full);
        var transferFailure = Filled();
        transferFailure.Output.RejectTransfer = true;
        ExpectNoExecution(transferFailure);
        var missingChoice = Filled();
        missingChoice.Machine.ModifyTag(DiskWriterState.SelectedRecipeTag, state => state.SetString(""));
        ExpectNoExecution(missingChoice);
        var unknownChoice = Filled();
        unknownChoice.Machine.ModifyTag(DiskWriterState.SelectedRecipeTag, state => state.SetString("missing"));
        ExpectNoExecution(unknownChoice);
        var wrongChip = Filled();
        wrongChip.Chip.childItem.Id = "other.chip";
        ExpectNoExecution(wrongChip);
        var wrongDisk = Filled();
        wrongDisk.Input.childItem.Id = "other.disk_empty";
        ExpectNoExecution(wrongDisk);
        var unowned = Filled();
        unowned.Chip.childItem.Owned = false;
        ExpectNoExecution(unowned);
        var negative = Filled();
        negative.Machine.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(-1));
        ExpectNoExecution(negative);

        var stacked = Filled();
        GameItem disks = stacked.Input.childItem;
        disks.SetAmount(2);
        stacked.Button.Click();
        Assert(disks.unitCount == 1 && !disks.Destroyed, "Consume one unit from a disk stack using the native recipe.");
        stacked.Button.Click();
        Assert(disks.Destroyed && Energy(stacked.Battery.childItem) == 4, "Consume the last disk correctly.");
        var adjusted = Filled();
        adjusted.Machine.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(5));
        adjusted.Button.Click();
        Assert(Energy(adjusted.Battery.childItem) == 5, "Read current machine power cost at execution time.");
    }

    private static void CheckReentrancy()
    {
        var ports = Filled();
        ports.Output.AfterAccept = _ => ports.Button.Click();
        ports.Button.Click();
        Assert(ports.Output.childItems.Count == 1 && Energy(ports.Battery.childItem) == 7, "Reentrant execution must not duplicate products or consumption.");
        var changedInput = Filled();
        changedInput.Output.AfterAccept = _ => changedInput.Input.Remove(changedInput.Input.childItem);
        changedInput.Button.Click();
        Assert(changedInput.Output.childItems.Count == 0 && Energy(changedInput.Battery.childItem) == 10,
            "Changing input during placement must abort without spending power.");
        var changedSelection = Filled();
        changedSelection.Output.AfterAccept = _ => changedSelection.Machine.ModifyTag(DiskWriterState.SelectedRecipeTag, state => state.SetString("shape_4"));
        changedSelection.Button.Click();
        Assert(changedSelection.Output.childItems.Count == 0 && Energy(changedSelection.Battery.childItem) == 10 &&
            changedSelection.Input.childItem.unitCount == 1, "Changing the recipe during placement must preserve resources.");
        var changedCost = Filled();
        changedCost.Output.AfterAccept = _ => changedCost.Machine.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(5));
        changedCost.Button.Click();
        Assert(changedCost.Output.childItems.Count == 0 && Energy(changedCost.Battery.childItem) == 10,
            "Changing the power cost during placement must abort before paying the old price.");
        var failedCallback = Filled();
        failedCallback.Output.AfterAccept = _ => throw new InvalidOperationException("simulated native transfer callback failure");
        try { failedCallback.Button.Click(); throw new Exception("Expected callback failure."); }
        catch (InvalidOperationException) { }
        Assert(failedCallback.Output.childItems.Count == 0 && Energy(failedCallback.Battery.childItem) == 10,
            "Transfer exceptions must clean up the uncommitted output.");
        Assert(!GraphUtils.CanAccept(failedCallback.Output, DirectoryMaster.Item("age_of_agi.part_dec_0")), "Restore the output whitelist even after exceptions.");
    }

    private static void CheckRebuiltWindow()
    {
        var previous = Filled(4, "dec");
        var rebuilt = new Ports();
        for (int index = 0; index < 4; index++)
        {
            var from = (GameInventory)previous.Row.GetStorage(index);
            var to = (GameInventory)rebuilt.Row.GetStorage(index);
            foreach (GameItem item in from.childItems) to.UncheckedAccept(item);
        }
        rebuilt.Machine.ModifyTag(DiskWriterState.SelectedRecipeTag, state => state.Enable()
            .SetString(previous.Machine.GetTagReadonly(DiskWriterState.SelectedRecipeTag).GetString()));
        rebuilt.Machine.onLoaded?.Invoke(rebuilt.Machine);
        Assert(rebuilt.Preview.ItemId == "age_of_agi.part_dec_4" && rebuilt.Choose.Text == "Change component",
            "Restore the selected component image and change caption after load.");
        rebuilt.Button.Click();
        Assert(rebuilt.Output.childItems.Single().identifier == "age_of_agi.part_dec_4" && Energy(rebuilt.Battery.childItem) == 7,
            "A reconstructed window must bind execution to its restored slot nodes.");
        Assert(previous.Output.childItems.Count == 0, "Factory instances must not share output containers.");
        rebuilt.Machine.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.Enabled = false);
        rebuilt.Machine.onLoaded?.Invoke(rebuilt.Machine);
        Assert(MachineParameters.GetPowerPerCycle(rebuilt.Machine) == 3, "Initialize missing power metadata on old parameter-only saves.");
        rebuilt.Machine.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(8));
        rebuilt.Machine.onLoaded?.Invoke(rebuilt.Machine);
        Assert(MachineParameters.GetPowerPerCycle(rebuilt.Machine) == 8, "Do not overwrite a saved machine power value.");
    }

    private static void CheckPreviewFailureCleanup(ModItemDirectory directory)
    {
        var ports = new Ports();
        Put(ports.Chip, DirectoryMaster.Item(DiskWriterCatalog.EncoderChipId));
        const string failingId = "age_of_agi.part_enc_2";
        Func<GameItem> original = directory.Factories[failingId];
        int created = ItemDirectory.CreatedInstances.Count;
        try
        {
            directory.Factories[failingId] = () => throw new InvalidOperationException("simulated preview factory failure");
            try { ports.Choose.Click(); throw new Exception("Expected preview factory failure."); }
            catch (InvalidOperationException) { }
            Assert(ItemDirectory.CreatedInstances.Skip(created).All(item => item.Destroyed),
                "Failure partway through building choices must destroy all partial previews.");
        }
        finally { directory.Factories[failingId] = original; }
    }

    private static void ExpectNoExecution(Ports ports)
    {
        GameItem battery = ports.Battery.childItem, disk = ports.Input.childItem;
        int energy = Energy(battery), disks = disk?.unitCount ?? 0, outputs = ports.Output.childItems.Count;
        int created = ItemDirectory.CreatedInstances.Count;
        ports.Button.Click();
        Assert(Energy(battery) == energy && (disk?.unitCount ?? 0) == disks && ports.Output.childItems.Count == outputs,
            "A blocked operation must preserve materials, power, and output.");
        Assert(ItemDirectory.CreatedInstances.Skip(created).All(item => item.Destroyed || item.parentInventory != null),
            "A failed execution leaked an unplaced product.");
    }

    private static Ports Filled(int index = 0, string family = "enc")
    {
        var ports = new Ports();
        Put(ports.Battery, MakeBattery(10));
        Put(ports.Input, DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId));
        Put(ports.Chip, DirectoryMaster.Item(family == "enc" ? DiskWriterCatalog.EncoderChipId : DiskWriterCatalog.DecoderChipId));
        ports.Choose.Click();
        ports.ChoiceRows.Buttons[index].Click();
        return ports;
    }
    private static GameItem MakeBattery(int energy)
    {
        var battery = new GameItem { Id = "energy_credit" };
        battery.EnableTag("power_source_item");
        battery.ModifyTag("power_source_item_energy", state => state.Enable().SetInt(energy));
        return battery;
    }
    private static int Energy(GameItem battery) => battery?.GetTagReadonly("power_source_item_energy").GetInt() ?? 0;
    private static void Put(GameInventory slot, GameItem item) => Assert(GraphUtils.TryAcceptAll(slot, item) == 0, "Fixture item rejected by its native slot whitelist.");
    private sealed class Ports
    {
        internal readonly GameItem Machine = DirectoryMaster.Item(DiskWriterCatalog.DiskWriterId);
        internal MachineryPanelRow Row => Machine.contentWindow.childElement;
        internal GameSlotInventory Battery => (GameSlotInventory)Row.GetStorage(DiskWriterState.BatteryIndex);
        internal GameSlotInventory Input => (GameSlotInventory)Row.GetStorage(DiskWriterState.InputIndex);
        internal GameSlotInventory Chip => (GameSlotInventory)Row.GetStorage(DiskWriterState.ChipIndex);
        internal GameGridInventory Output => (GameGridInventory)Row.GetStorage(DiskWriterState.OutputIndex);
        internal MachineryButton Button => ((MachineryStack)Row.GetPanel("control").content).Buttons.Single();
        internal MachineryButton Choose => ((MachineryStack)Row.GetPanel("recipe").content).Buttons.Single();
        internal DiskWriterComponentPreview Preview => ((MachineryStack)Row.GetPanel("recipe").content).Elements.OfType<DiskWriterComponentPreview>().Single();
        internal PixelWindow Popup => PixelWindow.CreatedWindows.Last(window => window.childElement?.GetPanel("choices") != null);
        internal MachineryStack ChoiceRows => (MachineryStack)Popup.childElement.GetPanel("choices").content;
    }
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains(DiskWriterCatalog.CatalogPath), "Recipe errors must identify the config path.");
            return;
        }
        throw new Exception("Invalid recipe configuration was accepted.");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
