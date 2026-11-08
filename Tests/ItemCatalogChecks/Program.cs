using AgeOfAgi.Items.Logic.DiskWriter;
using AgeOfAgi.Items.Logic.ComplexData;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Debugging;

internal static class Program
{
    private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";
    private const string ValidRow = "age_of_agi.gpu0,generic,gpu0,0,item_gpu0_name,item_gpu0_desc,,,none,none,\n";

    private static void Main(string[] args)
    {
        string sourceRoot = Path.GetFullPath(args.FirstOrDefault(argument => !argument.StartsWith("--")) ?? "Modding/AgeofAGI");
        GameAssemblyPath = FindGameAssembly(sourceRoot);
        // Inspect editable content only when explicitly requested as a separate diagnostic.
        if (args.Contains("--config-only"))
        {
            ModHelper.Root = sourceRoot;
            ConfigurationValidator.Run();
            return;
        }

        using var fixture = BehaviorTestFixture.Create();
        try
        {
            ModHelper.Root = fixture.Root;
            Console.WriteLine("Behavior checks use independent test inputs; their numbers are not mod balance requirements.");
            ConfigurationValidator.Run();
            RunBehaviorChecks(args.Contains("--market-only"));
        }
        finally { ModHelper.Root = sourceRoot; }
    }

    internal static string GameAssemblyPath { get; private set; }

    private static string FindGameAssembly(string sourceRoot)
    {
        foreach (string start in new[] { sourceRoot, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, "Probably Stolen_Data", "Managed", "Assembly-CSharp.dll");
                if (File.Exists(path)) return path;
            }
        }
        throw new FileNotFoundException("Cannot locate the installed Assembly-CSharp.dll for native API checks.");
    }

    private static void RunBehaviorChecks(bool marketOnly)
    {
        IReadOnlyList<ItemDefinition> definitions = ItemCatalog.Load();
        if (marketOnly)
        {
            ElectronicsMarketChecks.Run(definitions);
            ElectronicsEventChecks.Run(definitions);
            ElectronicsSignChecks.Run(definitions);
            ElectronicsSalvageChecks.Run(definitions);
            ElectronicsStockChecks.Run(definitions);
            return;
        }
        var pngKeys = new HashSet<string>(Directory.GetFiles(
            Path.Combine(ModHelper.Root, "Textures", "Items"), "*.png")
            .Select(Path.GetFileNameWithoutExtension), StringComparer.Ordinal);
        var stateSpriteKeys = new[]
        {
            "brain_server_dead", "disk_writer_empty", "data_collector1", "compiler_working", "serverbox_stage1", "serverbox_stage2", "dna_data",
            "data_container1", "data_container2", "data_container3", "data_container4", "data_container5",
            "hypo_data_container1", "hypo_data_container2", "hypo_data_container3", "hypo_data_container4", "hypo_data_container5"
        };
        // gpu_detailst is the shared inspection sprite, not a separately registered item.
        Assert(pngKeys.SetEquals(definitions.Select(item => item.SpriteKey).Concat(stateSpriteKeys).Concat(new[] { "gpu_detailst", "night_shifter" })),
            "Each PNG must be an initial item sprite or a documented state sprite.");
        CheckMergedItems(definitions, stateSpriteKeys);
        Assert(definitions.Select(item => item.ItemId).Distinct().Count() == definitions.Count,
            "Item IDs must be unique.");

        foreach (string locale in new[] { "en", "zh" })
        {
            Dictionary<string, string> entries = ReadLocale(locale);
            foreach (ItemDefinition definition in definitions)
            {
                Assert(entries.ContainsKey(definition.NameKey), "Missing name: " + definition.ItemId + " / " + locale);
                Assert(!string.IsNullOrWhiteSpace(entries[definition.NameKey]), "Empty item name: " + definition.ItemId + " / " + locale);
                if (definition.DescriptionKey.Length > 0)
                {
                    Assert(entries.ContainsKey(definition.DescriptionKey), "Missing description: " + definition.ItemId + " / " + locale);
                }
                if (definition.CommentKey.Length > 0)
                {
                    Assert(entries.ContainsKey(definition.CommentKey), "Missing comment: " + definition.ItemId + " / " + locale);
                }
            }

        }

        IReadOnlyList<ItemDefinition> parsed = ItemCatalog.Parse(
            "\uFEFF# Comment\r\n\r\n" + Header + ValidRow, key => key == "gpu0");
        Assert(parsed.Count == 1 && parsed[0].SpriteKey == "gpu0", "BOM/comments/CRLF parsing failed.");
        Assert(ItemCatalog.Parse(Header + " age_of_agi.gpu0 , generic , gpu0 ,0, name , desc ,,,none,none,\n", key => true).Count == 1,
            "Identifier whitespace was not trimmed.");
        Assert(ItemCatalog.Parse(Header + ValidRow + "age_of_agi.variant,generic,gpu0,0,name,desc,,,none,none,\n", key => true).Count == 2,
            "Different item variants must be allowed to share a sprite.");
        Assert(ItemCatalog.Parse(Header + "age_of_agi.disk,,gpu0,0,name,desc,,,none,none,\n", key => true)[0].Kind == "generic",
            "Empty kinds must use the generic initializer.");
        Assert(ItemCatalog.Parse(Header + "age_of_agi.gpu0,GPU,gpu0,0,name,desc,,,none,none,\n", key => true)[0].Kind == "gpu",
            "Kinds must be normalized before dispatch.");

        ExpectInvalid("", "header");
        ExpectInvalid(Header, "at least one item");
        ExpectInvalid("sprite_key,item_id,name_key,description_key\n" + ValidRow, "Expected header");
        ExpectInvalid(Header + "age_of_agi.gpu0,generic,gpu0,name\n", "line 2");
        ExpectInvalid(Header + "other.gpu0,generic,gpu0,0,name,desc,,,none,none,\n", "must start with");
        ExpectInvalid(Header + "age_of_agi.,generic,gpu0,0,name,desc,,,none,none,\n", "needs a name");
        ExpectInvalid(Header + "age_of_agi.gpu0,generic,../gpu0,0,name,desc,,,none,none,\n", "Invalid identifier");
        ExpectInvalid(Header + "age_of_agi.gpu0,generic,gpu0,0,,desc,,,none,none,\n", "Invalid identifier");
        ExpectInvalid(Header + "age_of_agi.gpu0,generic,\"gpu0\",0,name,desc,,,none,none,\n", "Invalid identifier");
        ExpectInvalid(Header + ValidRow + ValidRow, "Duplicate");
        ExpectInvalid(Header + ValidRow + "age_of_agi.GPU0,generic,gpu1,0,name,desc,,,none,none,\n", "Duplicate");
        ExpectInvalid(Header + ValidRow, "Missing Textures/Items/gpu0.png", key => false);

        CheckLifecycle(definitions);
        CheckRegistrationFailures(definitions);
        CheckFactoryValidation(definitions);
        CheckSharedSpriteVariants();
        CatalogCsvChecks.Run();
        ClientDialogueChecks.Run();
        DialogueTimingChecks.Run();
        FactoryBindingChecks.Run(definitions);
        AudioPresetChecks.Run(definitions);
        ContrabandLevelChecks.Run(definitions);
        ManufacturerChecks.Run();
        GpuChecks.Run(definitions);
        GpuInspectionChecks.Run(definitions);
        GpuColorCheckerChecks.Run(definitions);
        GpuGuideChecks.Run(definitions);
        ElectronicScannerChecks.Run(definitions);
        StoryClientChecks.Run(definitions);
        NightShiftChecks.Run(definitions);
        ElectronicsIndustryChecks.Run(definitions);
        ElectronicsMarketChecks.Run(definitions);
        ElectronicsEventChecks.Run(definitions);
        DataBuyerChecks.Run(definitions);
        ElectronicsSignChecks.Run(definitions);
        ElectronicsSalvageChecks.Run(definitions);
        ElectronicsStockChecks.Run(definitions);
        HardwareChecks.Run(definitions);
        ItemTypeChecks.Run(definitions);
        OptionalItemTextChecks.Run(definitions);
        MachineChecks.Run(definitions);
        ElectronicStorageChecks.Run(definitions);
        DataCollectorChecks.Run(definitions);
        DiskWriterChecks.Run(definitions);
        CompilerChecks.Run(definitions);
        ServerChecks.Run(definitions);
        ServerProductionChecks.Run(definitions);
        DistributedTerminalChecks.Run(definitions);
        BrainServerChecks.Run(definitions);
        BaseValueChecks.Run(definitions);
        AiPartsChecks.Run(definitions);
        ItemTooltipChecks.Run(definitions);
        Console.WriteLine($"PASS: independent behavior fixtures ({definitions.Count} test items; {pngKeys.Count} synthetic item/state images); CSV parser boundaries, native API compatibility, registration/lifecycle, localized UI, compiler calculations, server training/production, data extraction, transaction conservation and save compatibility.");
    }

    private static void CheckMergedItems(IReadOnlyList<ItemDefinition> definitions, IEnumerable<string> stateSpriteKeys)
    {
        foreach (var pair in new[] { (Id: "compiler", Sprite: "compiler_idle"), (Id: "serverbox", Sprite: "serverbox_stage0") })
        {
            ItemDefinition item = definitions.Single(definition => definition.ItemId == "age_of_agi." + pair.Id);
            Assert(item.Kind == "machine" && item.SpriteKey == pair.Sprite && item.NameKey == "item_" + pair.Id + "_name" &&
                item.DescriptionKey == "item_" + pair.Id + "_desc", "Merged item has incorrect identity or initial appearance.");
            Assert(definitions.Count(definition => definition.ItemId.StartsWith("age_of_agi." + pair.Id, StringComparison.Ordinal)) == 1,
                "State variants must not remain separate registered items.");
        }

        Assert(!definitions.Any(definition => stateSpriteKeys.Contains(definition.SpriteKey)),
            "Alternate state sprites must remain assets instead of separate spawnable items.");
    }

    private static void CheckDebugMenuWindows()
    {
        foreach (ModDebugMenu menu in ModDebugMenu.All)
        {
            ModHelper.DebugActions[menu.Title]();
            CustomUIWindow window = CustomUIWindow.Last;
            Assert(window.Elements.Keys.Where(id => id != "search").OrderBy(id => id)
                .SequenceEqual(menu.Actions.Keys.OrderBy(id => id)), "A category window must show only its own actions.");
            window.Get("search").Input("  SPAWN  ");
            Assert(menu.Actions.Keys.All(name => window.Get(name).Visible == name.Contains("Spawn", StringComparison.OrdinalIgnoreCase)),
                "Search filters a category without changing its registered actions.");
            window.Get("search").Input("");
            Assert(menu.Actions.Keys.All(name => window.Get(name).Visible), "Clearing search restores all entries.");
            ModHelper.DebugActions[menu.Title]();
            Assert(window.Closed && !ReferenceEquals(window, CustomUIWindow.Last), "Reopening a category replaces its existing window.");
        }
        Assert(ModDebugMenu.Items.Actions.ContainsKey(AgeOfAgi.Npcs.NightShiftClient.ClaimActionName) &&
            ModDebugMenu.Characters.Actions.ContainsKey(AgeOfAgi.Npcs.NightShiftClient.DebugActionName) &&
            ModDebugMenu.Stories.Actions.ContainsKey(AgeOfAgi.Npcs.QuorraStoryClient.DebugActionName),
            "Gift claims, suppliers and story visits must be routed to their respective categories.");
    }

    private static void CheckLifecycle(IReadOnlyList<ItemDefinition> definitions)
    {
        PrepareLoadedMod(definitions);
        ModHelper.IsRegistered = false;
        ModHelper.AssetsReady = false;
        UISettings.current = null;
        ModHelper.FileReads = 0;
        ModHelper.LocalizationReads = 0;
        var mod = new AgeOfAgiMod();

        mod.Init(new ModManifest());
        mod.OnEnable();
        mod.OnEnable();
        Assert(ModHelper.FileReads == 0, "Init/OnEnable must not look up files before the mod is registered.");
        Assert(ModHelper.DebugActions.Count == 0, "Debug buttons must wait for catalog loading.");

        ModHelper.IsRegistered = true;
        ModHelper.AssetsReady = true;
        var directory = new ModItemDirectory();
        ModHook.RaiseItemDirectoryInit(directory);
        Assert(directory.Factories.Count == definitions.Count, "Not all items were registered.");
        Assert(ModHelper.DebugActions.Keys.OrderBy(name => name).SequenceEqual(ModDebugMenu.All.Select(menu => menu.Title).OrderBy(name => name)),
            "F9 must expose only the four grouped mod menu entrances.");
        Assert(definitions.All(item => ModDebugMenu.Items.Actions.ContainsKey("Spawn " + item.ItemId.Substring(ModConstants.ModId.Length + 1))) &&
            ModDebugMenu.Stories.Actions.ContainsKey(AgeOfAgi.Items.Logic.StoryDocuments.QuorraDocuments.ClaimActionName), "Register every item button and pending story-document collection in their groups.");
        Assert(ModHelper.LocalizationReads == 0, "Translation must be deferred until an item is created.");
        Assert(directory.AddCalls == definitions.Count, "Repeated enable subscribed the directory hook twice.");
        CheckDebugMenuWindows();

        UISettings.current = new UISettings();
        foreach (ItemDefinition definition in definitions)
        {
            GameItem first = directory.Factories[definition.ItemId]();
            GameItem second = directory.Factories[definition.ItemId]();
            Assert(!ReferenceEquals(first, second), "Factories must return fresh items.");
            string initialSprite = definition.ItemId == AgeOfAgi.Items.Catalogs.DiskWriterCatalog.DiskWriterId
                ? AgeOfAgi.Items.Logic.DiskWriter.DiskWriterState.EmptySpriteKey : definition.SpriteKey;
            Assert(first.Id == definition.ItemId && first.SpriteKey == initialSprite,
                "Factory bound the wrong item or sprite.");
            Assert(first.name == definition.NameKey && first.shortDescription == definition.DescriptionKey && first.flavorText == definition.CommentKey,
                "Factory did not resolve the expected localization keys.");
            Assert(first.SourceId == "age_of_agi" && first.SourceName == "Age of AGI",
                "Factory did not explicitly mark item ownership by the mod.");
            ModHelper.LocalizationPrefix = "zh:";
            Assert(directory.Factories[definition.ItemId]().name == "zh:" + definition.NameKey && first.name == definition.NameKey,
                "Factories must resolve localization at creation rather than registration.");
            ModHelper.LocalizationPrefix = "";

            ModDebugMenu.Items.Actions["Spawn " + definition.ItemId.Substring(ModConstants.ModId.Length + 1)]();
            Assert(ModHelper.Inventory.Last().Id == definition.ItemId,
                "Debug callback captured the wrong item ID.");
            if (definition.ItemId == AgeOfAgi.Items.Logic.AiModel.AiModelState.UntrainedId)
            {
                Assert(AgeOfAgi.Items.Logic.AiModel.AiModelState.CanTrain(ModHelper.Inventory.Last()),
                    "F9 must initialize a valid model that can enter server training.");
                Assert(!AgeOfAgi.Items.Logic.AiModel.AiModelState.HasAttributes(first),
                    "Debug model defaults must not affect the normal item factory or compiler output.");
            }
            else if (definition.ItemId == AgeOfAgi.Items.Logic.AiModel.AiModelState.TrainedId)
            {
                Assert(AgeOfAgi.Items.Logic.AiModel.AiModelState.CanProduce(ModHelper.Inventory.Last()) &&
                    !AgeOfAgi.Items.Logic.AiModel.AiModelState.HasAttributes(first), "Only the debug action initializes a production-ready trained model.");
            }
        }

        var fastGrower = new PlantTrait { id = "fast_grower", displayName = "Fast Grower" };
        PlantTraitRegistry.All.Add(fastGrower);
        ModDebugMenu.Items.Actions[AgeOfAgi.Debugging.ModDebugActions.FastGrowerDnaActionName]();
        GameItem dna = ModHelper.Inventory.Last();
        Assert(dna.Id == AgeOfAgi.Items.Logic.ComplexData.ComplexDataState.ItemId && dna.SpriteKey == "dna_data" &&
            ReferenceEquals(PlantGeneticsHelper.GetDiskTrait(dna), fastGrower) &&
            dna.GetTagReadonly("DNA_DISK_RANK_INT").GetInt() == 1 && PlantGeneticsHelper.GetDiskUses(dna) == 4,
            "The F9 DNA preset must create a filled native Fast Grower container with rank 1 and four uses.");
        Assert(AgeOfAgi.Items.Logic.ComplexData.ComplexDataState.IsEmpty(directory.Factories[dna.Id]()),
            "The DNA debug preset must leave ordinary container creation empty.");
        PlantTraitRegistry.All.Remove(fastGrower);

        int debugCount = ModHelper.DebugActions.Count;
        ModHook.RaiseItemDirectoryInit(directory);
        Assert(directory.Factories.Count == definitions.Count, "Repeat registration duplicated or lost items.");
        Assert(ModHelper.DebugActions.Count == debugCount, "Repeat registration duplicated debug buttons.");

        var nextDirectory = new ModItemDirectory();
        ModHook.RaiseItemDirectoryInit(nextDirectory);
        Assert(nextDirectory.Factories.Count == definitions.Count, "Items did not register in a fresh directory.");
        Assert(ModHelper.DebugActions.Count == debugCount, "Scene changes duplicated debug buttons.");

        ModHelper.DebugActions[ModDebugMenu.Stories.Title]();
        CustomUIWindow storyDebugWindow = CustomUIWindow.Last;
        mod.OnDisable();
        Assert(ModHelper.DebugActions.Count == 0 && ModDebugMenu.All.All(menu => menu.Actions.Count == 0) && storyDebugWindow.Closed,
            "Disable removes the grouped entrances, child actions and owned debug windows.");
        Assert(nextDirectory.Factories.Count == 0, "Disable did not remove the mod's registered factories.");
        ModHelper.IsRegistered = false;
        var disabledDirectory = new ModItemDirectory();
        ModHook.RaiseItemDirectoryInit(disabledDirectory);
        Assert(disabledDirectory.Factories.Count == 0, "Disable did not unsubscribe the directory hook.");

        mod.OnEnable();
        ModHelper.IsRegistered = true;
        var enabledDirectory = new ModItemDirectory();
        ModHook.RaiseItemDirectoryInit(enabledDirectory);
        Assert(enabledDirectory.Factories.Count == definitions.Count, "Re-enable did not restore item registration.");
        mod.OnDisable();
    }

    internal static LoadedMod PrepareLoadedMod(IReadOnlyList<ItemDefinition> definitions)
    {
        DirectoryMaster.Directories.Clear();
        ModLoader.Mods.Clear();
        ModHelper.IsRegistered = true;
        ModHelper.AssetsReady = true;
        ModHelper.LocalizationPrefix = "";
        UISettings.current = new UISettings();
        var mod = new LoadedMod { Manifest = new ModManifest(), IsEnabled = true, Localization = ModHelper.Translations };
        foreach (string path in Directory.GetFiles(Path.Combine(ModHelper.Root, "Textures", "Items"), "*.png"))
        {
            byte[] png = File.ReadAllBytes(path);
            mod.Sprites[Path.GetFileNameWithoutExtension(path)] = new TestSprite
            {
                Shape = PngGridReader.Read(png, 16),
                rect = new TestRect
                {
                    width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
                    height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4))
                }
            };
        }

        // Mirror Aoa.csproj's copy of the nested GPU sources into native raw textures.
        foreach (string path in Directory.GetFiles(Path.Combine(ModHelper.Root, "Textures"), "*.png")
            .Concat(Directory.GetFiles(Path.Combine(ModHelper.Root, "Textures", "gpu_details"), "*.png")))
        {
            byte[] png = File.ReadAllBytes(path);
            mod.Textures[Path.GetFileNameWithoutExtension(path)] = new UnityEngine.Texture2D(
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)), UnityEngine.TextureFormat.RGBA32, true);
        }
        ModLoader.Mods.Add(mod);
        return mod;
    }

    private static void CheckRegistrationFailures(IReadOnlyList<ItemDefinition> definitions)
    {
        LoadedMod mod = PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(mod.Manifest);
        var directory = new ModItemDirectory();
        ItemDefinition last = definitions.Last();
        Func<GameItem> foreignFactory = () => new GameItem { Id = last.ItemId };
        directory.Add(last.ItemId, foreignFactory);
        int addCalls = directory.AddCalls;
        ExpectFailure(() => registration.Register(directory, definitions), "already registered");
        Assert(directory.AddCalls == addCalls && directory.Factories.Count == 1 &&
            ReferenceEquals(directory.Factories[last.ItemId], foreignFactory),
            "Preflight must not call the destructive Add or replace a foreign factory.");

        // Also protect IDs owned by a different directory.
        var target = new ModItemDirectory();
        ExpectFailure(() => registration.Register(target, definitions), "already registered");
        Assert(target.AddCalls == 0 && ReferenceEquals(directory.Factories[last.ItemId], foreignFactory),
            "Global-directory collisions were not protected.");

        directory.Remove(last.ItemId);
        TestSprite sprite = mod.Sprites[last.SpriteKey];
        mod.Sprites.Remove(last.SpriteKey);
        ExpectFailure(() => registration.Register(directory, definitions), "is not loaded");
        Assert(directory.Factories.Count == 0 && directory.AddCalls == addCalls,
            "A missing loaded sprite must reject the batch before adding any item.");
        mod.Sprites[last.SpriteKey] = sprite;

        directory.RejectAddFor = definitions[1].ItemId;
        ExpectFailure(() => registration.Register(directory, definitions), "Could not register item");
        Assert(directory.Factories.Count == 0, "Failed batch left partially registered factories behind.");
        directory.RejectAddFor = null;
        Assert(registration.Register(directory, definitions) == definitions.Count, "Registration did not recover after a failed batch.");

        // Another mod can replace one of our entries later. Cleanup must preserve that replacement.
        directory.factoryDictionary[last.ItemId] = foreignFactory;
        directory.Add("other_mod.widget", () => new GameItem());
        registration.Unregister();
        Assert(directory.Factories.Count == 2 && ReferenceEquals(directory.Factories[last.ItemId], foreignFactory) &&
            directory.Factories.ContainsKey("other_mod.widget"), "Cleanup deleted a foreign factory.");
    }

    private static void CheckFactoryValidation(IReadOnlyList<ItemDefinition> definitions)
    {
        LoadedMod mod = PrepareLoadedMod(definitions);
        var factory = new CatalogItemFactory(mod.Manifest);
        ItemDefinition definition = definitions[0];
        int createdItems = ItemDirectory.CreatedItems;
        UISettings.current = null;
        ExpectFailure(() => factory.Create(definition), "initialized UISettings");
        UISettings.current = new UISettings();
        TestRect original = mod.Sprites[definition.SpriteKey].rect;
        mod.Sprites[definition.SpriteKey].rect = new TestRect { width = 17, height = 16 };
        ExpectFailure(() => factory.Create(definition), "gridSize 16");
        mod.Sprites[definition.SpriteKey].rect = new TestRect { width = 8, height = 16 };
        ExpectFailure(() => factory.Create(definition), "gridSize 16");
        Assert(ItemDirectory.CreatedItems == createdItems, "Invalid assets created an orphan item before validation.");
        mod.Sprites[definition.SpriteKey].rect = original;
        mod.IsEnabled = false;
        ExpectFailure(() => factory.Create(definition), "Enabled mod is not registered");
        mod.IsEnabled = true;
        mod.Manifest.Name = "";
        var unnamedFactory = new CatalogItemFactory(mod.Manifest);
        Assert(unnamedFactory.Create(definition).SourceName == "age_of_agi", "Empty mod names must fall back to the mod ID.");
    }

    private static void CheckSharedSpriteVariants()
    {
        IReadOnlyList<ItemDefinition> variants = ItemCatalog.Parse(Header +
            "age_of_agi.variant_a,generic,gpu0,0,name_a,desc_a,,,none,none,\n" +
            "age_of_agi.variant_b,generic,gpu0,0,name_b,desc_b,,,none,none,\n", key => true);
        LoadedMod mod = PrepareLoadedMod(variants);
        var registration = new ItemRegistration(mod.Manifest);
        var directory = new ModItemDirectory();
        registration.Register(directory, variants);
        AgeOfAgi.Debugging.ModDebugActions.Register(variants);
        ModDebugMenu.Items.Actions["Spawn variant_a"]();
        Assert(ModHelper.Inventory.Last().Id == variants[0].ItemId, "First variant's debug action spawned the wrong ID.");
        ModDebugMenu.Items.Actions["Spawn variant_b"]();
        Assert(ModHelper.Inventory.Last().Id == variants[1].ItemId, "Shared-sprite variants have colliding debug labels.");
        AgeOfAgi.Debugging.ModDebugActions.Unregister();
        registration.Unregister();
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains(message), "Unexpected error: " + exception.Message);
            return;
        }

        throw new Exception("Expected failure: " + message);
    }

    internal static Dictionary<string, string> ReadLocale(string locale)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        string main = Path.Combine(ModHelper.Root, "Localization", locale + ".csv");
        string npcs = Path.Combine(ModHelper.Root, "Localization", "Npcs", locale + ".csv");
        string books = Path.Combine(ModHelper.Root, "Localization", "Books", locale + ".csv");
        var paths = new[] { main, npcs, books }.Where(File.Exists);
        foreach (string rawLine in paths.SelectMany(File.ReadAllLines))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf(',');
            Assert(separator > 0, "Invalid localization entry: " + locale);
            string key = line.Substring(0, separator);
            string value = line.Substring(separator + 1);
            Assert(!entries.ContainsKey(key), "Duplicate localization key: " + key);
            entries.Add(key, value);
        }

        return entries;
    }

    private static void ExpectInvalid(string csv, string message, Func<string, bool> spriteExists = null)
    {
        try
        {
            ItemCatalog.Parse(csv, spriteExists ?? (key => true));
        }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains(message), "Unexpected validation message: " + exception.Message);
            return;
        }

        throw new Exception("Invalid catalog was accepted: " + message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}

