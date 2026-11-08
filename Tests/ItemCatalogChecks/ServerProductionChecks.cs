using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.ComplexData;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.Memory;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Helpers;
using Newtonsoft.Json;

internal static partial class ServerProductionChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        AgeOfAgi.Items.Logic.DistributedTerminal.DistributedNetwork.Enable();
        CheckNativeDnaContract();
        PlantTraitRegistry.All.Clear();
        PlantTraitRegistry.All.Add(new PlantTrait { id = "growth", displayName = "Growth", description = "Growth trait", locked = false });
        PlantTraitRegistry.All.Add(new PlantTrait { id = "yield", displayName = "Yield", description = "Yield trait", locked = false });
        PlantTraitRegistry.All.Add(new PlantTrait { id = "locked", displayName = "Locked", description = "Locked trait", locked = true });
        LoadedMod loaded = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(loaded.Manifest);
        registration.Register(new ModItemDirectory(), definitions);
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        try
        {
            CheckBtc();
            CheckBatchProduction();
            CheckQueuedInputs();
            CheckPausesAndTransactions();
            CheckSavedProgress();
            CheckIntelligence();
            CheckGeneSelectionAndData();
            CheckSavedGeneTask();
            CheckProductionConfiguration();
            CheckEncryptedConfiguration(definitions, registration);
            CheckDataExtraction();
            CheckDataBatchTuning(definitions, registration);
        }
        finally { registration.Unregister(); ModHelper.Translations.Clear(); ModHelper.Locale = "en"; }
    }

    private static void CheckNativeDnaContract()
    {
        string path = Program.GameAssemblyPath;
        using var stream = System.IO.File.OpenRead(path);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        var type = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Single(definition => metadata.GetString(definition.Name) == "PlantGeneticsHelper");
        foreach (string name in new[] { "InitDisk", "GetExtractableTraits", "GetEffectiveComplexity", "WriteDisk", "SpendDiskUse", "UpdateDiskDisplay" })
            Assert(type.GetMethods().Count(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == name) == 1,
                "The installed game must expose the expected native genetics API: " + name);
        var display = type.GetMethods().Select(handle => metadata.GetMethodDefinition(handle))
            .Single(method => metadata.GetString(method.Name) == "UpdateDiskDisplay");
        Assert(display.GetParameters().Select(handle => metadata.GetParameter(handle)).Any(parameter =>
            parameter.SequenceNumber == 1 && metadata.GetString(parameter.Name) == "disk"), "Harmony's custom carrier prefix must bind the native disk parameter.");
    }

    private static void CheckBtc()
    {
        var normal = new Rig();
        GameItem source = DirectoryMaster.Item(ServerProduction.NormalInputId).SetAmount(2);
        Put(normal.Input, source);
        GameItem ai = normal.Model.childItem;
        normal.Night(1);
        Assert(normal.State().Active && normal.State().WorkNights == 1 && normal.Output.childItems.Count == 0,
            "Normal BTC makes partial progress on entry hardware.");
        Near(normal.State().Progress, 26.4, "Production uses hardware compute times the trained AI multiplier");
        Assert(InputLocked(normal) && normal.Model.IsRemoveLocked() && !normal.Battery.IsRemoveLocked(), "Lock production inputs while retaining battery access.");
        Near(AiModelState.GetCurrentDurability(ai), 12, "Intermediate nights spend no AI durability");
        normal.Night(2);
        Assert(normal.Output.childItems.Single().identifier == "age_of_agi.btc_normal" && source.unitCount == 1 && !source.Destroyed,
            "Complete one BTC and consume one unit from the unfinished stack.");
        Near(AiModelState.GetCurrentDurability(ai), 11, "One completed batch spends one durability");
        Assert(Energy(normal) == 992 && !InputLocked(normal), "Each working night pays hardware power; completion unlocks inputs.");
        normal.Night(2);
        Assert(normal.Output.childItems.Count == 1 && Energy(normal) == 992, "A repeated completion-day callback cannot start another batch.");
        normal.Night(3); normal.Night(4);
        Assert(source.Destroyed && normal.Output.childItems.Count == 2 && AiModelState.GetCurrentDurability(ai) == 10,
            "The next batch consumes the remaining input and one more AI use.");
        var rare = new Rig(); Put(rare.Input, DirectoryMaster.Item(ServerProduction.RareInputId));
        int nights = rare.Finish();
        Assert(nights > 2 && rare.Output.childItems.Single().identifier == "age_of_agi.btc_rare" && Energy(rare) == 1000 - 4 * nights,
            "Rare BTC takes more nights and total power.");
        Near(AiModelState.GetCurrentDurability(rare.Model.childItem), 11, "A multi-night rare task still spends just one AI use");
        var last = new Rig(durability: 1); Put(last.Input, DirectoryMaster.Item(ServerProduction.NormalInputId));
        GameItem lastAi = last.Model.childItem; last.Finish();
        Assert(lastAi.Destroyed && last.Model.childItem == null && last.Output.childItems.Count == 1,
            "The last AI use delivers the product before removing the exhausted AI.");
        var sensory = new Rig(AiTaskTypes.Sensory); Put(sensory.Input, DirectoryMaster.Item(ServerProduction.NormalInputId)); sensory.Night(1);
        Assert(sensory.Output.childItems.Count == 0 && Energy(sensory) == 1000, "Sensory AI has no invented production recipes.");
    }

    private static void CheckPausesAndTransactions()
    {
        var paused = new Rig(); Put(paused.Input, DirectoryMaster.Item(ServerProduction.RareInputId)); paused.Night(1);
        float progress = paused.State().Progress;
        GameItem power = paused.Battery.childItem; paused.Battery.Remove(power); paused.Night(2);
        Assert(paused.State().Progress == progress && AiModelState.GetCurrentDurability(paused.Model.childItem) == 12,
            "Missing power pauses without progress or durability loss.");
        Put(paused.Battery, power); paused.Night(3); Assert(paused.State().Progress > progress, "Resume the same production task after repowering.");
        var full = new Rig(); Put(full.Input, DirectoryMaster.Item(ServerProduction.NormalInputId));
        var blocker = new GameItem(); blocker.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1, 16).ToArray(), 4));
        full.Output.UncheckedAccept(blocker); full.Night(1);
        Assert(Energy(full) == 1000 && full.State().Progress == 0, "Full output blocks even a non-completion night.");
        var rejected = Fast(); rejected.Output.RejectTransfer = true; rejected.Night(1);
        Assert(rejected.Output.childItems.Count == 0 && rejected.Input.childItems.Count == 1 && Energy(rejected) == 1000,
            "Rejected output placement preserves all resources.");
        rejected.Output.RejectTransfer = false; rejected.Night(2); Assert(rejected.Output.childItems.Count == 1, "Recover from output transfer failure.");
        var reentrant = Fast(); reentrant.Output.AfterAccept = _ => reentrant.Night(1); reentrant.Night(1);
        Assert(reentrant.Output.childItems.Count == 1 && AiModelState.GetCurrentDurability(reentrant.Model.childItem) == 11,
            "Reentrant callbacks cannot duplicate output or AI wear.");
        var changed = Fast(); GameItem original = changed.Input.childItems.Single();
        changed.Output.AfterAccept = _ => changed.Input.Remove(original); changed.Night(1);
        Assert(changed.Output.childItems.Count == 0 && Energy(changed) == 1000 && !original.Destroyed &&
            AiModelState.GetCurrentDurability(changed.Model.childItem) == 12, "Changing a reserved input aborts before any fee.");
        var throwing = Fast(); throwing.Output.AfterAccept = _ => throw new InvalidOperationException("transfer callback");
        try { throwing.Night(1); throw new Exception("Expected callback failure."); } catch (InvalidOperationException) { }
        Assert(throwing.Output.childItems.Count == 0 && Energy(throwing) == 1000 && throwing.Input.childItems.Count == 1,
            "Exceptions during placement destroy uncommitted products and preserve input/power.");
    }

    private static void CheckQueuedInputs()
    {
        var rig = new Rig();
        GameItem first = DirectoryMaster.Item(ServerProduction.NormalInputId);
        GameItem second = DirectoryMaster.Item(ServerProduction.NormalInputId);
        Put(rig.Input, first); Put(rig.Input, second);
        rig.Night(1);
        Assert(GeneralHelper.IsItemLocked(first) && first.mayInventoryRemoveItemFunc(first, rig.Input) == false &&
            !GeneralHelper.IsItemLocked(second) && !rig.Input.IsRemoveLocked() && !rig.Input.IsInsertLocked() && rig.Model.IsRemoveLocked(),
            "Only the current Bitcoin locks; the queued Bitcoin stays removable and hardware stays locked.");
        float progress = rig.State().Progress;
        CheckItemProgress(rig, first, "server_decoding_progress_tooltip");
        var queuedTooltip = new RichTextBuilder(); ServerHelper.CreateTooltip(queuedTooltip, second);
        Assert(queuedTooltip.ProgressBars.Count == 0, "Queued inputs do not inherit the active input's progress.");
        rig.Input.Remove(second);
        Assert(rig.State().SourceId == first.uniqueId && rig.State().Progress == progress,
            "Removing a queued input preserves the active source and paid progress.");
        Put(rig.Input, second);
        var restored = Restore(rig);
        Assert(GeneralHelper.IsItemLocked(first) && !GeneralHelper.IsItemLocked(second) && !restored.Input.IsRemoveLocked(),
            "Loading restores the individual source lock without locking queued items.");
        CheckItemProgress(restored, first, "server_decoding_progress_tooltip");
        restored.Night(2);
        var finishedTooltip = new RichTextBuilder(); ServerHelper.CreateTooltip(finishedTooltip, first);
        Assert(finishedTooltip.ProgressBars.Count == 0, "Finished inputs clear their progress display.");
        Assert(!GeneralHelper.IsItemLocked(second) && !second.IsTag(ServerState.ProgressVisibleTag),
            "Completing one input leaves the next input unlocked without inherited progress.");
    }

    private static void CheckItemProgress(Rig rig, GameItem source, string textKey)
    {
        foreach (string locale in new[] { "zh", "en" })
        {
            ModHelper.Locale = locale;
            var sourceTooltip = new RichTextBuilder(); ServerHelper.CreateTooltip(sourceTooltip, source);
            var machineTooltip = new RichTextBuilder(); ServerHelper.CreateTooltip(machineTooltip, rig.Machine);
            var state = rig.State();
            string line = ModHelper.GetLocalized(ModConstants.ModId, textKey, state.Progress, state.TotalWork,
                100 * state.Progress / state.TotalWork);
            Assert(sourceTooltip.Lines.Count(entry => entry.Text == line) == 1 && sourceTooltip.ProgressBars.Count == 1 &&
                machineTooltip.ProgressBars.Count == 1 && sourceTooltip.ProgressBars[0].Ratio == machineTooltip.ProgressBars[0].Ratio,
                "The processing input shows one localized progress row and the server's exact progress ratio.");
            Assert(machineTooltip.Lines.Any(entry => entry.Text.StartsWith(locale == "zh" ? "任务进度：" : "Task progress:")) &&
                machineTooltip.Lines.All(entry => !entry.Text.StartsWith(locale == "zh" ? "当前批次抽取" : "Current batch")),
                "The server retains Task progress and omits the extraction batch quantity.");
        }
        ModHelper.Locale = "en";
    }

    private static void CheckDataBatchTuning(IReadOnlyList<ItemDefinition> definitions, ItemRegistration registration)
    {
        string csv = ModHelper.ReadModFileText(ModConstants.ModId, ServerCatalog.CatalogPath);
        try
        {
            foreach (var sample in new[] { (Rate: 8, Expected: 12), (Rate: 3, Expected: 4) })
            {
                var table = CsvFixture.Parse(csv); table.Rows[0]["dataUnitsPer8Gb"] = sample.Rate.ToString();
                ModHelper.FileOverrides[ServerCatalog.CatalogPath] = table.ToString();
                registration.Register(new ModItemDirectory(), definitions);
                var rig = ExtractionRig(AiTaskTypes.Computing, "dc_common_data_disk", out var source, out var container);
                var memory = (GameGridInventory)rig.Row.GetStorage(ServerState.MemoryIndex);
                memory.childItems.Single().ModifyTag(MemoryState.CapacityTag, tag => tag.SetInt(12));
                GpuTestState.SetReal(rig.Gpu.childItems.Single(), compute: 10000);
                rig.Night(1);
                Assert(DataContentState.Amount(container) == sample.Expected && DataContentState.Amount(source) == 80 - sample.Expected,
                    "Configured batch rates scale with all installed GB, round down and cap at source contents.");
            }
            var unitWork = CsvFixture.Parse(csv);
            unitWork.Rows[0]["dataUnitsPer8Gb"] = "8";
            unitWork.Rows[0]["dataWorkPerUnit"] = "1";
            ModHelper.FileOverrides[ServerCatalog.CatalogPath] = unitWork.ToString();
            registration.Register(new ModItemDirectory(), definitions);
            foreach (int amount in new[] { 80, 3 })
            {
                var rig = ExtractionRig(AiTaskTypes.Computing, "dc_common_data_disk", out var source, out var container);
                DataContentState.SetAmount(source, amount);
                GpuTestState.SetReal(rig.Gpu.childItems.Single(), compute: 1);
                rig.Night(1);
                int batch = Math.Min(8, amount);
                Assert(rig.State().BatchData == batch && rig.State().TotalWork == batch,
                    "One work per data unit makes full and tail batch work equal the actual extracted amount.");
                Near(rig.State().Progress, 2.64, "Unit-work extraction retains fractional hardware/AI throughput");
                int nights = 1;
                while (DataContentState.Amount(container) == 0 && nights < 10) rig.Night(++nights);
                Assert(nights == (int)Math.Ceiling(batch / 2.64) && DataContentState.Amount(container) == batch &&
                    DataContentState.Amount(source) == amount - batch && Energy(rig) == 1000 - 4 * nights &&
                    AiModelState.GetCurrentDurability(rig.Model.childItem) == 11,
                    "Unit-work batches deliver once, pay only effective nights and spend one AI use.");
            }
        }
        finally
        {
            ModHelper.FileOverrides.Remove(ServerCatalog.CatalogPath);
            registration.Register(new ModItemDirectory(), definitions);
        }
    }

    private static void CheckSavedProgress()
    {
        var original = new Rig(); Put(original.Input, DirectoryMaster.Item(ServerProduction.NormalInputId)); original.Night(1);
        var restored = Restore(original);
        Assert(restored.Model.IsRemoveLocked() && restored.State().Progress == original.State().Progress, "Reload restores production progress and locks.");
        int energy = Energy(restored); restored.Night(1);
        Assert(Energy(restored) == energy && restored.Output.childItems.Count == 0, "Reload cannot repeat the saved working night.");
        restored.Night(2);
        Assert(restored.Output.childItems.Count == 1 && AiModelState.GetCurrentDurability(restored.Model.childItem) == 11,
            "Restored production completes with one durability charge.");
        var machinesOnly = new Rig(); Put(machinesOnly.Input, DirectoryMaster.Item(ServerProduction.NormalInputId));
        machinesOnly.Machine.onCycleEndEarlySlotItemFunc(machinesOnly.Machine, null, null); machinesOnly.Night(5);
        machinesOnly.Machine.onCycleEndEarlySlotItemFunc(machinesOnly.Machine, null, null); machinesOnly.Night(5);
        Assert(machinesOnly.Output.childItems.Count == 1, "An authorized Machines Only cycle works twice without advancing calendar day.");
        var invalid = new Rig(); invalid.Machine.ModifyTag(ServerProduction.StateTag, tag => tag.Enable().SetString("{bad"));
        invalid.Machine.onLoaded(invalid.Machine); invalid.Night(1);
        Assert(!invalid.Input.IsRemoveLocked() && invalid.Model.IsRemoveLocked() && invalid.Output.childItems.Count == 0 && Energy(invalid) == 1000,
            "Corrupt saved production pauses and preserves resources.");
    }

    private static void CheckIntelligence()
    {
        var rig = new Rig(AiTaskTypes.Intrusion); Put(rig.Input, DirectoryMaster.Item(ServerProduction.FolderEmptyId)); rig.Night(1);
        Assert(Energy(rig) == 1000, "Intelligence requires an installed upgraded switch.");
        GameItem net = DirectoryMaster.Item(NetworkSwitchUpgrades.SwitchId); Put(rig.Network, net);
        Install(net, NetworkSwitchUpgrades.NetworkAccessUpgrade); rig.Night(2);
        Assert(Energy(rig) == 1000, "Network access alone does not satisfy packet disguise.");
        Install(net, NetworkSwitchUpgrades.PacketDisguiseUpgrade);
        rig.Finish(startDay: 3);
        Assert(rig.Output.childItems.Single().identifier == "age_of_agi.folder_security" &&
            NetworkSwitchUpgrades.GetUpgradeMask(net) == 12 && ReferenceEquals(rig.Network.childItem, net),
            "Intelligence writes a classified folder without consuming the installed upgrades or switch.");
        Near(AiModelState.GetCurrentDurability(rig.Model.childItem), 11, "Intelligence spends one completed AI use");
    }

    private static void CheckGeneSelectionAndData()
    {
        var rig = Biological(complexity: 2, out GameItem seed);
        seed.SetAmount(3);
        var content = (MachineryStack)rig.Row.GetPanel("production").content;
        MachineryButton choose = content.Buttons.Single();
        Assert(!rig.Row.GetPanel("production").isHidden && rig.Row.GetStorage(8) is GameSlotInventory && rig.Row.GetStorage(9) == null,
            "The gene selector adds no saved inventory bay; the separate data container is node 8.");
        rig.Night(1); Assert(Energy(rig) == 1000, "Biological processing requires an explicit gene selection.");
        choose.Click(); PixelWindow popup = LatestGenePopup();
        var rows = (MachineryStack)popup.childElement.GetPanel("genes").content;
        Assert(rows.Buttons.Take(rows.Buttons.Length - 1).Select(button => button.Text).SequenceEqual(new[] { "Growth", "Yield" }),
            "The UI lists present unlocked traits and excludes locked traits.");
        rows.Buttons.Last().Click(); Assert(!popup.IsVisible(), "Cancel preserves the unselected state.");
        choose.Click(); popup = LatestGenePopup(); rows = (MachineryStack)popup.childElement.GetPanel("genes").content;
        rows.Buttons[1].Click();
        Assert(ServerGeneSelector.SelectedTrait(rig.Machine, seed).id == "yield", "Choose the desired gene deterministically.");
        Near(rig.State().TotalWork, 40 + 20 * 5, "Effective complexity includes existing heat in the work requirement");
        string genes = SeedSnapshot(seed);
        rig.Finish(startDay: 2);
        GameItem disk = rig.Output.childItems.Single();
        Assert(!seed.Destroyed && ReferenceEquals(seed.parentInventory, rig.Input) && SeedSnapshot(seed) == genes,
            "Analysis preserves seed identity, quantity, trait ranks and heat.");
        Assert(disk.identifier == ComplexDataState.ItemId && disk.SpriteKey == "dna_data" && PlantGeneticsHelper.IsWrittenDisk(disk) &&
            PlantGeneticsHelper.GetDiskTrait(disk).id == "yield" && PlantGeneticsHelper.GetDiskRank(disk) == 1 && PlantGeneticsHelper.GetDiskUses(disk) == 4,
            "Produce one native-compatible one-gene carrier with rank one and four uses.");
        Assert(!ComplexDataState.IsEmpty(disk) && disk.name.Contains("Yield"), "Filled carriers display the chosen trait and reject blank-carrier recipes.");
        for (int i = 0; i < 3; i++) Assert(!PlantGeneticsHelper.SpendDiskUse(disk) && disk.SpriteKey == "dna_data", "Native use updates retain the custom sprite.");
        if (PlantGeneticsHelper.SpendDiskUse(disk)) disk.Destroy();
        Assert(disk.Destroyed && rig.Output.childItems.Count == 0, "The native fourth-use contract removes the whole exhausted carrier.");
        var native = new GameItem { Id = "dna_disk" }; Assert(ComplexDataDisplayPatch.Prefix(native), "Leave native DNA disk displays unchanged.");
        var simple = Biological(complexity: 0, out var simpleSeed); SelectGene(simple, 0);
        Assert(simple.State().TotalWork < 140, "Lower-complexity seeds need less work.");
        GameItem replacement = Seed(0); simple.Input.Remove(simpleSeed); Put(simple.Input, replacement);
        Assert(ServerGeneSelector.SelectedTrait(simple.Machine, replacement) == null, "A replacement seed cannot inherit another seed's selection.");
        replacement.EnableTag("GROWING_TAG");
        Assert(!ServerProduction.IsSeed(replacement), "Growing plants cannot be used as analysis seed inputs.");
    }

    private static void CheckSavedGeneTask()
    {
        var original = Biological(0, out GameItem seed); SelectGene(original, 0); original.Night(1);
        Assert(original.State().Active && original.State().Progress > 0, "Start a gene task before testing save restore.");
        string before = SeedSnapshot(seed);
        var restored = Restore(original); restored.Finish(startDay: 2);
        Assert(restored.Output.childItems.Count == 1 && PlantGeneticsHelper.GetDiskTrait(restored.Output.childItems.Single()).id == "growth" &&
            SeedSnapshot(seed) == before, "Restore the chosen gene, seed identity and frozen work without harming the seed.");
    }

    private static void CheckProductionConfiguration()
    {
        string csv = System.IO.File.ReadAllText(System.IO.Path.Combine(ModHelper.Root, ServerCatalog.CatalogPath));
        foreach (string field in new[] { "intelligenceWork", "bioBaseWork", "bioComplexityWork", "dataUnitsPer8Gb", "dataWorkPerUnit" })
        {
            var root = CsvFixture.Parse(csv); root.Rows[0][field] = "0";
            try { ServerCatalog.Parse(root.ToString()); throw new Exception("Accepted zero production work."); }
            catch (InvalidOperationException) { }
        }
    }

    private static void CheckEncryptedConfiguration(IReadOnlyList<ItemDefinition> definitions, ItemRegistration registration)
    {
        string csv = ModHelper.ReadModFileText(ModConstants.ModId, EncryptedItemCatalog.CatalogPath);
        foreach (var sample in new[] { ("work", "0"), ("work", "-1"), ("work", "NaN"), ("work", "Infinity"),
            ("task_type", "unknown"), ("output_item_id", "age_of_agi.missing"), ("item_id", "age_of_agi.missing"),
            ("output_item_id", ServerCatalog.ServerId), ("output_item_id", ServerProduction.NormalInputId) })
        {
            var invalid = CsvFixture.Parse(csv); invalid.Rows[0][sample.Item1] = sample.Item2;
            bool rejected = false;
            try { EncryptedItemCatalog.Parse(invalid.ToString()).ValidateMappings(definitions); }
            catch (InvalidOperationException exception) { rejected = exception.Message.Contains(EncryptedItemCatalog.CatalogPath); }
            Assert(rejected, "Encrypted recipe validation must reject invalid work, task and registered input/output mappings.");
        }
        var duplicate = CsvFixture.Parse(csv); duplicate.DuplicateFirstRow();
        bool duplicateRejected = false;
        try { EncryptedItemCatalog.Parse(duplicate.ToString()); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        Assert(duplicateRejected, "Each encrypted input must have an unambiguous recipe.");

        var legacy = new Rig(); Put(legacy.Input, DirectoryMaster.Item(ServerProduction.NormalInputId)); legacy.Night(1);
        var oldState = legacy.State(); oldState.Version = 1; oldState.RecipeId = "btc_normal"; oldState.OutputItemId = null;
        legacy.Machine.ModifyTag(ServerProduction.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(oldState)));
        try
        {
            var table = CsvFixture.Parse(csv);
            table.Rows[0]["item_id"] = ServerProduction.FolderEmptyId;
            table.Rows[0]["work"] = "80.5";
            table.Rows[0]["task_type"] = AiTaskTypes.Intrusion;
            table.Rows[0]["output_item_id"] = "age_of_agi.btc_rare";
            ModHelper.FileOverrides[EncryptedItemCatalog.CatalogPath] = table.ToString();
            registration.Register(new ModItemDirectory(), definitions);
            var wrongTask = new Rig(); Put(wrongTask.Input, DirectoryMaster.Item(ServerProduction.FolderEmptyId)); wrongTask.Night(1);
            Assert(wrongTask.Output.childItems.Count == 0 && Energy(wrongTask) == 1000,
                "Encrypted input must require the configured task head without charging a mismatched model.");
            var configured = new Rig(AiTaskTypes.Intrusion);
            Put(configured.Input, DirectoryMaster.Item(ServerProduction.FolderEmptyId)); configured.Night(1);
            Assert(configured.State().Version == 2 && configured.State().IsValid() && configured.State().TotalWork == 80.5f &&
                configured.State().OutputItemId == "age_of_agi.btc_rare" && configured.State().RecipeId == "encrypted_item",
                "Input identity, task condition, fractional work and output identity must all come from encrypted_items.csv.");
            table.Rows[0]["work"] = "1";
            table.Rows[0]["task_type"] = AiTaskTypes.Computing;
            table.Rows[0]["output_item_id"] = "age_of_agi.btc_normal";
            ModHelper.FileOverrides[EncryptedItemCatalog.CatalogPath] = table.ToString();
            registration.Register(new ModItemDirectory(), definitions);
            var restored = Restore(configured);
            Assert(restored.State().TotalWork == 80.5f && restored.State().TaskType == AiTaskTypes.Intrusion &&
                restored.State().Progress == configured.State().Progress && restored.State().OutputItemId == "age_of_agi.btc_rare",
                "Reloading an active task retains its frozen recipe despite changes to every recipe parameter.");
            restored.Finish(startDay: 2);
            Assert(restored.Output.childItems.Single().identifier == "age_of_agi.btc_rare" &&
                AiModelState.GetCurrentDurability(restored.Model.childItem) == 11,
                "Frozen output must be delivered once with one AI durability charge.");
            var next = new Rig(); Put(next.Input, DirectoryMaster.Item(ServerProduction.FolderEmptyId)); next.Night(1);
            Assert(next.Output.childItems.Single().identifier == "age_of_agi.btc_normal",
                "New tasks must use the updated recipe's work, head and output.");
            var legacyRestored = Restore(legacy);
            Assert(legacyRestored.State().IsValid() && legacyRestored.State().TotalWork == oldState.TotalWork,
                "Version-one BTC progress remains valid after moving and changing the recipe table.");
            legacyRestored.Finish(startDay: 2);
            Assert(legacyRestored.Output.childItems.Single().identifier == "age_of_agi.btc_normal",
                "Legacy BTC finishes its original product even when the new table no longer lists its input.");
        }
        finally
        {
            ModHelper.FileOverrides.Remove(EncryptedItemCatalog.CatalogPath);
            registration.Register(new ModItemDirectory(), definitions);
        }
    }

    private static void CheckDataExtraction()
    {
        CheckDataValuesAndConfiguration();
        var rig = new Rig();
        GameItem source = DirectoryMaster.Item("age_of_agi.dc_common_data_disk");
        GameItem container = DirectoryMaster.Item("age_of_agi.data_container");
        Put(rig.Input, source); Put(rig.DataContainer, container);
        // Extraction writes into its own bay and must ignore a blocked ordinary output.
        var blocker = new GameItem(); blocker.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1, 16).ToArray(), 4));
        rig.Output.UncheckedAccept(blocker);
        rig.Night(1);
        Assert(rig.State().RecipeId == "data_extract" && rig.State().BatchData == 10 && rig.State().TotalWork == 40 && rig.State().IsValid(),
            "8 GB freezes a 10-unit batch with 40 work.");
        Near(rig.State().Progress, 26.4, "Current-batch progress uses trained hardware throughput");
        CheckItemProgress(rig, source, "server_extraction_progress_tooltip");
        Assert(DataContentState.Amount(source) == 80 && DataContentState.Amount(container) == 0 && Energy(rig) == 996 &&
            InputLocked(rig) && rig.Model.IsRemoveLocked() && !rig.DataContainer.IsRemoveLocked(),
            "Intermediate nights debit power only; inputs lock and destination remains accessible.");
        float progress = rig.State().Progress;
        rig.DataContainer.Remove(container); rig.Night(2);
        CheckItemProgress(rig, source, "server_extraction_progress_tooltip");
        Assert(rig.State().Progress == progress && Energy(rig) == 996 && InputLocked(rig), "Missing container pauses without unlocking or fees.");
        GameItem full = DirectoryMaster.Item("age_of_agi.hypo_data_container"); DataContentState.SetAmount(full, 225);
        Put(rig.DataContainer, full); rig.Night(3);
        Assert(rig.State().Progress == progress && Energy(rig) == 996 && InputLocked(rig), "A replacement full container also pauses a running batch.");
        rig.DataContainer.Remove(full);
        GameItem replacement = DirectoryMaster.Item("age_of_agi.hypo_data_container"); Put(rig.DataContainer, replacement);
        rig.Night(4);
        Assert(DataContentState.Amount(source) == 70 && DataContentState.Amount(replacement) == 10 && DataContentState.Amount(container) == 0 &&
            !source.Destroyed && !InputLocked(rig) && !rig.Model.IsRemoveLocked() && Energy(rig) == 992,
            "Finish into the current destination and unlock all inputs between batches.");
        Near(AiModelState.GetCurrentDurability(rig.Model.childItem), 11, "One completed extraction batch spends one AI use");
        Assert(rig.State().Progress == 0 && rig.State().WorkNights == 0 && !rig.State().Active, "Next-batch preview must not relock inputs or inherit progress.");
        Assert(source.GetBaseValue() == 90 && replacement.GetBaseValue() == 340, "Raw content depreciates the carrier while parsed content appreciates the container.");
        rig.Night(4);
        Assert(DataContentState.Amount(source) == 70 && Energy(rig) == 992, "Duplicate night callbacks cannot start another batch.");
        rig.Night(5);
        Assert(InputLocked(rig) && rig.State().Progress > 0, "The following effective night locks the new batch.");
        var restored = Restore(rig);
        Assert(InputLocked(restored) && restored.State().Progress == rig.State().Progress &&
            DataContentState.Amount(restored.DataContainer.childItem) == 10, "Restore both batch progress and appended destination bay.");
        restored.Night(5); Assert(Energy(restored) == 988, "Load preserves the nightly duplicate guard.");
        restored.Night(6);
        Assert(DataContentState.Amount(source) == 60 && DataContentState.Amount(replacement) == 20 && Energy(restored) == 984,
            "Restored extraction completes exactly once.");

        var overflow = ExtractionRig(AiTaskTypes.Computing, "dc_common_data_disk", out var overflowSource, out var overflowContainer);
        DataContentState.SetAmount(overflowContainer, 57);
        overflow.Machine.contentWindow.stateChangeActionFunc();
        Assert(overflow.State().BatchData == 3 && overflow.State().TotalWork == 12 &&
            overflow.Machine.GetTagReadonly(ServerState.StatusTag).GetString() == "server_insufficient_extraction_data",
            "Destination free space shrinks the planned batch and its work without blocking extraction.");
        var shortTooltip = new RichTextBuilder(); ServerHelper.CreateTooltip(shortTooltip, overflow.Machine);
        Assert(shortTooltip.Lines.Any(line => line.Text == "Insufficient extractable data for a full batch" &&
            line.Color == RenderHandler.ColorPalette.DarkYellow), "A short batch uses the same-level dark-yellow status warning.");
        overflow.Night(1); overflow.Night(2);
        Assert(DataContentState.Amount(overflowSource) == 77 && DataContentState.Amount(overflowContainer) == 60,
            "A partly free container accepts exactly three and preserves the remaining seven source units.");
        overflow.Night(3);
        Assert(Energy(overflow) == 996 && !InputLocked(overflow) && DataContentState.Amount(overflowSource) == 77,
            "An already full container does not start another batch or charge resources.");

        var constrained = ExtractionRig(AiTaskTypes.Computing, "dc_common_data_disk", out var constrainedSource, out var constrainedContainer);
        constrained.Night(1);
        float constrainedProgress = constrained.State().Progress;
        constrained.DataContainer.Remove(constrainedContainer);
        GameItem smallDestination = DirectoryMaster.Item("age_of_agi.data_container");
        DataContentState.SetAmount(smallDestination, 59); Put(constrained.DataContainer, smallDestination);
        constrained.Night(2);
        Assert(constrained.State().Progress == constrainedProgress && DataContentState.Amount(constrainedSource) == 80 &&
            DataContentState.Amount(smallDestination) == 59 && Energy(constrained) == 996 &&
            constrained.Machine.GetTagReadonly(ServerState.StatusTag).GetString() == "server_data_container_full",
            "Replacing the destination mid-batch with insufficient free space pauses without overflow, progress or fees.");

        var tail = ExtractionRig(AiTaskTypes.Computing, "dc_common_data_disk_shabby", out var tailSource, out var tailContainer, durability: 1);
        DataContentState.SetAmount(tailSource, 5);
        tail.Input.onSlotAddItemEndFunc?.Invoke(tailSource, tail.Input, null);
        Assert(tail.State().BatchData == 5 && tail.State().TotalWork == 20, "Tail batch work shrinks with source data.");
        GameItem lastAi = tail.Model.childItem; tail.Night(1);
        Assert(lastAi.Destroyed && DataContentState.Amount(tailContainer) == 5 && DataContentState.Amount(tailSource) == 0 &&
            !tailSource.Destroyed && tailSource.GetBaseValue() == 8 && !InputLocked(tail),
            "Last AI use delivers the short batch, preserves the empty carrier's residual price and unlocks.");

        foreach (var (memoryGb, batch) in new[] { (8, 10), (16, 20), (24, 30), (32, 40), (64, 80), (128, 160) })
        {
            var memoryRig = ExtractionRig(AiTaskTypes.Computing, "dc_enterprise_databox", out var memorySource, out var memoryContainer);
            var memoryBay = (GameGridInventory)memoryRig.Row.GetStorage(ServerState.MemoryIndex);
            foreach (GameItem old in memoryBay.childItems) memoryBay.Remove(old);
            if (memoryGb == 128)
            {
                GameItem contacts = DirectoryMaster.Item(ServerState.GoldFingerId).SetAmount(ServerCatalog.Load().Definition.MemoryUpgradeUnits);
                while (contacts.unitCount > 0) memoryRig.Machine.onItemTargetThisFunc(contacts, memoryRig.Machine);
                for (int i = 0; i < 4; i++) Put(memoryBay, DirectoryMaster.Item("age_of_agi.mem2"));
            }
            else
            {
                GameItem mem = DirectoryMaster.Item("age_of_agi.mem0"); mem.ModifyTag(MemoryState.CapacityTag, tag => tag.SetInt(memoryGb)); Put(memoryBay, mem);
            }
            GpuTestState.SetReal(memoryRig.Gpu.childItems.Single(), compute: 10000);
            memoryRig.Night(1);
            Assert(DataContentState.Amount(memorySource) == 400 - Math.Min(batch, DataContentState.Capacity(memoryContainer)) &&
                DataContentState.Amount(memoryContainer) == Math.Min(batch, DataContentState.Capacity(memoryContainer)) &&
                AiModelState.GetCurrentDurability(memoryRig.Model.childItem) == 11 && !InputLocked(memoryRig),
                "Memory batch size and one-batch-per-night limit failed at " + memoryGb + " GB.");
        }
        foreach (string carrier in new[] { "dc_common_data_disk", "dc_enterprise_databox", "dc_military_terminal",
            "dc_braininterface", "dc_eyeball", "dc_idcard", "dc_suspeciousdisk" })
        {
            foreach (string task in new[] { AiTaskTypes.Computing, AiTaskTypes.Intrusion, AiTaskTypes.Biological, AiTaskTypes.Sensory })
            {
                var access = ExtractionRig(task, carrier, out var accessSource, out var accessContainer);
                access.Night(1); access.Night(2);
                bool allowed = task == AiTaskTypes.Intrusion || task == AiTaskTypes.Computing && carrier != "dc_military_terminal";
                Assert(DataContentState.Amount(accessContainer) == (allowed ? 10 : 0) && Energy(access) == (allowed ? 992 : 1000),
                    "AI permissions failed without network upgrades: " + task + "/" + carrier);
            }
        }
        var noPower = ExtractionRig(AiTaskTypes.Computing, "dc_common_data_disk", out _, out _);
        noPower.Battery.childItem.ModifyTag("power_source_item_energy", tag => tag.SetInt(0)); noPower.Night(1);
        Assert(!InputLocked(noPower) && noPower.State().Progress == 0, "A batch locks only when actual work starts.");
        var invalid = ExtractionRig(AiTaskTypes.Computing, "dc_common_data_disk", out _, out _); invalid.Night(1);
        var broken = invalid.State(); broken.BatchData = broken.SourceData + 1;
        Assert(!broken.IsValid(), "Saved extraction cannot exceed its frozen source amount.");
        invalid.Machine.ModifyTag(ServerProduction.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(broken)));
        invalid.Machine.onLoaded(invalid.Machine); invalid.Night(2);
        Assert(Energy(invalid) == 996 && InputLocked(invalid), "Invalid saved extraction preserves resources and locks.");
    }

    private static Rig ExtractionRig(string task, string carrier, out GameItem source, out GameItem container, float durability = 12)
    {
        var rig = new Rig(task, durability);
        source = DirectoryMaster.Item(ModConstants.ModId + "." + carrier);
        container = DirectoryMaster.Item(ModConstants.ModId + ".data_container");
        Put(rig.Input, source); Put(rig.DataContainer, container);
        return rig;
    }

    private static void CheckDataValuesAndConfiguration()
    {
        var items = ItemCatalog.Load();
        var catalog = DataContentCatalog.Load();
        foreach (var definition in items.Where(item => item.Kind == "data_carrier"))
        {
            Assert(definition.ItemId.StartsWith(ModConstants.ModId + ".dc_", StringComparison.Ordinal) &&
                definition.SpriteKey == definition.ItemId.Substring(ModConstants.ModId.Length + 1),
                "Raw data carrier IDs and sprite keys must share the dc_ prefix.");
            GameItem item = DirectoryMaster.Item(definition.ItemId);
            int capacity = catalog.Get(definition.ItemId).Capacity;
            Assert(DataContentState.IsCarrier(item) && !DataContentState.IsContainer(item) &&
                DataContentState.Capacity(item) == capacity && DataContentState.Amount(item) == capacity &&
                item.GetBaseValue() == definition.BaseValue + (long)capacity * catalog.GetUnitValue(definition.ItemId),
                "Raw carriers start full with a CSV shell price and additive data value.");
            DataContentState.SetAmount(item, 1);
            item.SetValue(777);
            item.lateUnitValue = 0;
            item.onLoaded(item);
            Assert(DataContentState.Amount(item) == 1 && DataContentState.Capacity(item) == capacity &&
                item.GetBaseValue() == 777 + catalog.GetUnitValue(definition.ItemId),
                "Loading a partly extracted carrier preserves saved amount, capacity and shell price.");
            DataContentState.SetAmount(item, 0);
            Assert(!item.Destroyed && item.GetBaseValue() == 777 && item.SpriteKey == definition.SpriteKey,
                "Depleted carriers retain their shell, identity and appearance.");
        }
        string csv = System.IO.File.ReadAllText(System.IO.Path.Combine(ModHelper.Root, DataContentCatalog.CatalogPath));
        foreach (string bad in new[] { "", csv.Replace("80,80,1,", "80,81,1,"), csv.Replace("80,80,1,", "0,0,1,"),
            csv.Replace("80,80,1,", "80,80,-1,"), csv.Replace("60,0,4", "60,0,1"), csv + csv.Split('\n')[1] + "\n" })
        {
            try { DataContentCatalog.Parse(bad); throw new Exception("Accepted malformed content CSV."); }
            catch (InvalidOperationException) { }
        }
        foreach (string id in new[] { "dc_common_data_disk", "dc_enterprise_databox", "dc_military_terminal" })
        {
            GameItem good = DirectoryMaster.Item(ModConstants.ModId + "." + id);
            GameItem shabby = DirectoryMaster.Item(ModConstants.ModId + "." + id + "_shabby");
            Assert(DataContentState.Amount(good) > DataContentState.Amount(shabby) && good.GetBaseValue() > shabby.GetBaseValue(),
                "Shabby carriers start with less data and lower value.");
        }
        foreach (string id in new[] { "data_container", "hypo_data_container" })
        {
            GameItem item = DirectoryMaster.Item(ModConstants.ModId + "." + id);
            int capacity = DataContentState.Capacity(item);
            Assert(capacity == (id == "data_container" ? 60 : 225), "Container capacities cap new full values at 300 and 1200 without changing the data rate.");
            long shellValue = item.GetBaseValue();
            for (int stage = 0; stage <= 5; stage++)
            {
                int amount = capacity / 5 * stage;
                DataContentState.SetAmount(item, amount);
                Assert(item.SpriteKey == id + stage && item.GetBaseValue() == shellValue + amount * 4 && item.SetValueCalls == 1,
                    "Content drives six state sprites and additive value while keeping a single shell-price initialization.");
            }
            DataContentState.SetAmount(item, 1); Assert(item.SpriteKey == id + "1", "Nonempty containers must show a filled state.");
            item.lateUnitValue = 0; item.onLoaded(item);
            Assert(DataContentState.Amount(item) == 1 && item.GetBaseValue() == shellValue + 4 && item.SpriteKey == id + "1",
                "Loading rebuilds derived appearance and value without replacing saved contents.");
            foreach (string locale in new[] { "en", "zh" })
            {
                ModHelper.Locale = locale; var tooltip = new RichTextBuilder(); DataContentState.CreateTooltip(tooltip, item);
                Assert(tooltip.Lines.Single().Text == ModHelper.GetLocalized(ModConstants.ModId, "data_content_amount", 1, capacity),
                    "Content tooltip resolves current amounts and locale.");
            }
            ModHelper.Locale = "en";
        }
        Console.WriteLine("PASS: data extraction batches, destination-limited planning without overflow, short-batch status, container replacement, source value, AI permissions and saved progress.");
    }

    private static Rig Fast()
    {
        var rig = new Rig(); GpuTestState.SetReal(rig.Gpu.childItems.Single(), compute: 1000);
        Put(rig.Input, DirectoryMaster.Item(ServerProduction.NormalInputId)); return rig;
    }
    private static Rig Biological(int complexity, out GameItem seed)
    {
        var rig = new Rig(AiTaskTypes.Biological); seed = Seed(complexity);
        Put(rig.Input, seed); Put(rig.Input, DirectoryMaster.Item(ComplexDataState.ItemId)); return rig;
    }
    private static GameItem Seed(int complexity)
    {
        var seed = new GameItem { Id = "test_seed", name = "Seed" }; seed.EnableTag("SEED_ITEM_TAG"); seed.EnableTag("TEST_VALID_SEED");
        seed.ModifyTag("TEST_COMPLEXITY", tag => tag.Enable().SetInt(complexity));
        seed.ModifyTag("GENETIC_HEAT_INT", tag => tag.Enable().SetInt(3));
        seed.ModifyTag("PLANT_TRAIT_growth", tag => tag.Enable().SetInt(2));
        seed.ModifyTag("PLANT_TRAIT_yield", tag => tag.Enable().SetInt(3));
        seed.ModifyTag("PLANT_TRAIT_locked", tag => tag.Enable().SetInt(1)); return seed;
    }
    private static string SeedSnapshot(GameItem seed) => string.Join(";", PlantGeneticsHelper.GetTraits(seed).Select(entry => entry.trait.id + ":" + entry.rank)) +
        "|" + PlantGeneticsHelper.GetHeat(seed) + "|" + seed.unitCount;
    private static PixelWindow LatestGenePopup() => PixelWindow.CreatedWindows.Last(window => window.childElement?.GetPanel("genes") != null);
    private static void SelectGene(Rig rig, int index)
    {
        ((MachineryStack)rig.Row.GetPanel("production").content).Buttons.Single().Click();
        ((MachineryStack)LatestGenePopup().childElement.GetPanel("genes").content).Buttons[index].Click();
    }
    private static void Install(GameItem net, int index)
    {
        GameItem module = DirectoryMaster.Item(NetworkSwitchUpgrades.ModuleIdPrefix + index); module.onThisTargetItemFunc(module, net);
        Assert(module.Destroyed, "Installing an upgrade consumes its module once.");
    }
    private static Rig Restore(Rig previous)
    {
        var result = new Rig(install: false);
        foreach (var pair in previous.Machine.modifiedState.dict)
            result.Machine.ModifyTag(pair.Key, tag => { tag.Enabled = pair.Value.Enabled; tag.Value = pair.Value.Value;
                tag.FloatValue = pair.Value.FloatValue; tag.LongValue = pair.Value.LongValue;
                tag.DoubleValue = pair.Value.DoubleValue; tag.StringValue = pair.Value.StringValue; });
        for (int i = 0; i < 9; i++)
            foreach (GameItem item in ((GameInventory)previous.Row.GetStorage(i)).childItems)
                ((GameInventory)result.Row.GetStorage(i)).UncheckedAccept(item);
        result.Machine.onLoaded(result.Machine); return result;
    }
    private sealed class Rig
    {
        internal readonly GameItem Machine = DirectoryMaster.Item(ServerCatalog.ServerId);
        internal MachineryPanelRow Row => Machine.contentWindow.childElement;
        internal GameSlotInventory Battery => (GameSlotInventory)Row.GetStorage(ServerState.BatteryIndex);
        internal GameSlotInventory Model => (GameSlotInventory)Row.GetStorage(ServerState.ModelIndex);
        internal GameSlotInventory Network => (GameSlotInventory)Row.GetStorage(ServerState.NetworkIndex);
        internal GameGridInventory Gpu => (GameGridInventory)Row.GetStorage(ServerState.GpuIndex);
        internal GameGridInventory Input => (GameGridInventory)Row.GetStorage(ServerState.InputIndex);
        internal GameGridInventory Output => (GameGridInventory)Row.GetStorage(ServerState.OutputIndex);
        internal GameSlotInventory DataContainer => (GameSlotInventory)Row.GetStorage(ServerState.DataContainerIndex);
        internal Rig(string task = AiTaskTypes.Computing, float durability = 12, bool install = true)
        {
            if (!install) return;
            var power = new GameItem { Id = "energy_credit" }; power.EnableTag("power_source_item");
            power.ModifyTag("power_source_item_energy", tag => tag.Enable().SetInt(1000)); Put(Battery, power);
            Put((GameInventory)Row.GetStorage(ServerState.CpuIndex), DirectoryMaster.Item("age_of_agi.cpu0"));
            Put(Gpu, DirectoryMaster.Item("age_of_agi.gpu0"));
            Put((GameInventory)Row.GetStorage(ServerState.MemoryIndex), DirectoryMaster.Item("age_of_agi.mem0"));
            GameItem ai = DirectoryMaster.Item(AiModelState.TrainedId); AiModelState.ApplyTrained(ai, task, 2, 20, durability); Put(Model, ai);
        }
        internal ServerProductionState State() => JsonConvert.DeserializeObject<ServerProductionState>(Machine.GetTagReadonly(ServerProduction.StateTag).GetString());
        internal void Night(int day) { StoreStation.Instance.dayCounter = day; Machine.onCycleEndSlotItemFunc(Machine, null, null); }
        internal int Finish(int startDay = 1)
        {
            for (int i = 0; i < 100; i++) { Night(startDay + i); if (Output.childItems.Count > 0) return i + 1; }
            throw new Exception("Production did not finish: " + Machine.GetTagReadonly(ServerState.StatusTag).GetString());
        }
    }
    private static bool InputLocked(Rig rig) => rig.Input.childItems.Any(item => item.IsTag(ServerState.ProcessingLockTag) && GeneralHelper.IsItemLocked(item));
    private static int Energy(Rig rig) => rig.Battery.childItem.GetTagReadonly("power_source_item_energy").GetInt();
    private static void Put(GameInventory inventory, GameItem item)
    {
        if (inventory is GameGridInventory && !GraphUtils.CanAccept(inventory, item))
        {
            GridShape original = item.modifiedShape.Clone();
            for (int rotation = 1; rotation < 4; rotation++)
            {
                item.SetShape(new GridShapeBuilder(original).SetOrientation(rotation));
                if (GraphUtils.CanAccept(inventory, item)) break;
            }
        }
        Assert(GraphUtils.TryAcceptAll(inventory, item) == 0, "Fixture rejected: " + item.identifier);
    }
    private static void Near(double actual, double expected, string message) => Assert(Math.Abs(actual - expected) < 0.0001, message + ": " + actual + " != " + expected);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
