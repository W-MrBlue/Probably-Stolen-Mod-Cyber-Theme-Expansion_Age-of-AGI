using AgeOfAgi.Items.Logic.Gpu;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi;

internal static class GpuChecks
{
    private const string ValidCsv = "id,computePower,powerPerCycle,coreSprite\nage_of_agi.gpu0,10,2,gpu_core_nut\n";

    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        GpuDefinition sample = GpuCatalog.Parse(ValidCsv).Get("age_of_agi.gpu0");
        Assert(sample.ComputePower == 10 && sample.PowerPerCycle == 2,
            "GPU fields did not map to the definition.");
        Assert(GpuCatalog.Parse(ValidCsv).GetByCore("gpu_core_nut").ItemId == sample.ItemId,
            "Core identity must resolve to the corresponding CSV base values.");
        ExpectFailure(() => GpuCatalog.Parse(CsvFixture.Cell(ValidCsv, "coreSprite", "")), "coreSprite");
        ExpectFailure(() => GpuCatalog.Parse(CsvFixture.Cell(ValidCsv, "coreSprite", "../core")), "coreSprite");
        ExpectFailure(() => GpuCatalog.Parse(ValidCsv + "age_of_agi.gpu1,20,3,gpu_core_nut\n"), "Duplicate GPU coreSprite");
        ExpectFailure(() => GpuCatalog.Parse(ValidCsv).GetByCore("missing_core"), "Missing GPU configuration for core");
        Assert(GpuCatalog.Parse(CsvFixture.Cell(ValidCsv, "powerPerCycle", "0"))
            .Get(sample.ItemId).PowerPerCycle == 0, "Zero-power GPUs must be allowed.");

        foreach (string invalid in new[] { "", "id,computePower,powerPerCycle", "id,computePower,powerPerCycle\nage_of_agi.gpu0,10", "id,computePower,computePower" })
            ExpectFailure(() => GpuCatalog.Parse(invalid), GpuCatalog.CatalogPath);
        ExpectFailure(() => GpuCatalog.Parse(CsvFixture.Cell(ValidCsv, "computePower", "10.5")), "integer");
        ExpectFailure(() => GpuCatalog.Parse(CsvFixture.Cell(ValidCsv, "computePower", "0")), "positive");
        ExpectFailure(() => GpuCatalog.Parse(CsvFixture.Cell(ValidCsv, "powerPerCycle", "-1")), "non-negative");
        ExpectFailure(() => GpuCatalog.Parse(CsvFixture.Cell(ValidCsv, "computePower", "2147483648")), "computePower");
        ExpectFailure(() => GpuCatalog.Parse(ValidCsv.Replace("age_of_agi.gpu0", "other.gpu0")), "GPU ID");
        ExpectFailure(() => GpuCatalog.Parse(ValidCsv.Replace("powerPerCycle", "powerPercycle")), "Unknown column");
        foreach (string field in new[] { "id", "computePower", "powerPerCycle", "coreSprite" })
        {
            var missing = CsvFixture.Parse(ValidCsv);
            missing.RemoveColumn(field);
            ExpectFailure(() => GpuCatalog.Parse(missing.ToString()), field);
        }
        var duplicates = CsvFixture.Parse(ValidCsv);
        duplicates.DuplicateFirstRow();
        ExpectFailure(() => GpuCatalog.Parse(duplicates.ToString()), "Duplicate GPU ID");
        duplicates.Rows[1]["id"] = "age_of_agi.GPU0";
        ExpectFailure(() => GpuCatalog.Parse(duplicates.ToString()), "Duplicate GPU ID");

        GpuCatalog singleGpu = GpuCatalog.Parse(ValidCsv);
        ExpectFailure(() => singleGpu.Get("age_of_agi.missing"), "Missing GPU configuration");
        ExpectFailure(() => singleGpu.ValidateMappings(Array.Empty<ItemDefinition>()), "kind=gpu row");
        ExpectFailure(() => singleGpu.ValidateMappings(new[]
        {
            new ItemDefinition(sample.ItemId, "generic", "gpu0", 0, "name", "desc")
        }), "kind=gpu row");
        ExpectFailure(() => singleGpu.ValidateMappings(items), "Missing GPU configuration");

        CheckRegisteredGpus(items);
        CheckTooltips();
    }

    private static void CheckTooltips()
    {
        // Load the actual UI entries. Other keys keep the existing key-returning test behavior.
        foreach (string locale in new[] { "en", "zh" })
        {
            var entries = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(Path.Combine(ModHelper.Root, "Localization", locale + ".csv")))
            {
                int comma = line.IndexOf(',');
                if (comma < 0)
                {
                    continue;
                }

                string key = line.Substring(0, comma).Trim();
                if (key == GpuRuntime.ComputePowerTextKey || key == GpuRuntime.PowerPerCycleTextKey)
                {
                    entries.Add(key, line.Substring(comma + 1).Trim());
                }
            }

            Assert(entries.Count == 2, "Both GPU tooltip translations must exist in " + locale);
            ModHelper.Translations.Add(locale, entries);
        }

        var mod = new AgeOfAgiMod();
        mod.Init(new ModManifest());
        ModHelper.AssetsReady = false;
        int localizationReads = ModHelper.LocalizationReads;
        mod.OnEnable();
        mod.OnEnable();
        Assert(ModHelper.LocalizationReads == localizationReads, "Tooltip subscription resolved text before assets loaded.");

        var gpu = new GameItem { Id = "age_of_agi.gpu0" };
        GpuHelper.InitGpu(gpu, GpuCatalog.Parse(ValidCsv).Get(gpu.Id), AudioHelper.InitModule);
        try
        {
            var ordinaryTooltip = new RichTextBuilder();
            ModHook.RaiseCreateTooltip(ordinaryTooltip, new GameItem { Id = "other_mod.item" });
            ModHook.RaiseCreateTooltip(ordinaryTooltip, null);
            Assert(ordinaryTooltip.Lines.Count == 0 && ModHelper.LocalizationReads == localizationReads,
                "Non-GPU items must not receive GPU tooltip text.");

            ModHelper.AssetsReady = true;
            ModHelper.Locale = "zh";
            var chineseTooltip = new RichTextBuilder();
            ModHook.RaiseCreateTooltip(chineseTooltip, gpu);
            Assert(chineseTooltip.Lines.Select(line => line.Text).SequenceEqual(new[] { "算力：10", "所需能量：2 /天" }),
                "Chinese GPU tooltip is missing, duplicated, or not formatted with the configured values.");
            Assert(chineseTooltip.Lines.All(line => line.Color == RenderHandler.ColorPalette.LightCyan),
                "GPU stats must use the native stat text color.");

            gpu.ModifyTag(GpuRuntime.ComputePowerTag, state => state.SetInt(75));
            gpu.ModifyTag(GpuRuntime.PowerPerCycleTag, state => state.SetInt(0));
            ModHelper.Locale = "en";
            var englishTooltip = new RichTextBuilder();
            ModHook.RaiseCreateTooltip(englishTooltip, gpu);
            Assert(englishTooltip.Lines.Select(line => line.Text).SequenceEqual(new[] { "Compute power: 75", "Required energy: 0 /day" }),
                "Tooltip must resolve the current locale and current tag values, including zero power.");
            Assert(GpuRuntime.GetComputePower(gpu) == 75 && GpuRuntime.GetPowerPerCycle(gpu) == 0,
                "Rendering the tooltip changed saved item properties.");

            ModHelper.Locale = "fr";
            var fallbackTooltip = new RichTextBuilder();
            ModHook.RaiseCreateTooltip(fallbackTooltip, gpu);
            Assert(fallbackTooltip.Lines.Select(line => line.Text).SequenceEqual(englishTooltip.Lines.Select(line => line.Text)),
                "GPU tooltip must fall back to the English entries.");

            mod.OnDisable();
            var disabledTooltip = new RichTextBuilder();
            ModHook.RaiseCreateTooltip(disabledTooltip, gpu);
            Assert(disabledTooltip.Lines.Count == 0, "Disabling the mod did not unsubscribe tooltip rendering.");
            mod.OnEnable();
            var enabledTooltip = new RichTextBuilder();
            ModHook.RaiseCreateTooltip(enabledTooltip, gpu);
            Assert(enabledTooltip.Lines.Count == 2, "Re-enabling the mod did not restore exactly one tooltip renderer.");
        }
        finally
        {
            mod.OnDisable();
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
            ModHelper.AssetsReady = true;
        }
    }

    private static void CheckRegisteredGpus(IReadOnlyList<ItemDefinition> items)
    {
        LoadedMod mod = Program.PrepareLoadedMod(items);
        GpuCatalog catalog = GpuCatalog.Load();
        catalog.ValidateMappings(items);
        Assert(catalog.Count == items.Count(item => item.Kind == "gpu"), "GPU CSV and kind=gpu rows must match.");

        var registration = new ItemRegistration(mod.Manifest);
        var directory = new ModItemDirectory();
        ModHelper.FileReads = 0;
        registration.Register(directory, items);
        Assert(ModHelper.FileReads == 14, "Each property catalog must be read once per registration batch.");
        foreach (ItemDefinition itemDefinition in items)
        {
            GameItem item = directory.Factories[itemDefinition.ItemId]();
            if (itemDefinition.Kind != "gpu")
            {
                Assert(!GpuRuntime.IsGpu(item), "GPU initialization leaked into a different kind.");
                continue;
            }

            GpuDefinition gpu = catalog.Get(itemDefinition.ItemId);
            Assert(GpuRuntime.IsGpu(item) && item.unitValue == itemDefinition.BaseValue &&
                GpuRuntime.GetComputePower(item) == gpu.ComputePower &&
                GpuRuntime.GetPowerPerCycle(item) == gpu.PowerPerCycle,
                "Registered GPU did not receive its configured value and tags: " + gpu.ItemId);
            GameItem other = directory.Factories[itemDefinition.ItemId]();
            item.ModifyTag(GpuRuntime.ComputePowerTag, state => state.SetDouble(1));
            Assert(GpuRuntime.GetComputePower(other) == gpu.ComputePower, "GPU instances share mutable tag state.");
        }

        Assert(ModHelper.FileReads == 14, "Item creation must not reread hardware CSV.");
        Func<GameItem> originalFactory = directory.Factories["age_of_agi.gpu0"];
        try
        {
            ModHelper.FileOverrides[GpuCatalog.CatalogPath] = null;
            ExpectFailure(() => registration.Register(directory, items), "Missing " + GpuCatalog.CatalogPath);
            ModHelper.FileOverrides[GpuCatalog.CatalogPath] = "{";
            ExpectFailure(() => registration.Register(directory, items), "Unknown column");
            Assert(ReferenceEquals(directory.Factories["age_of_agi.gpu0"], originalFactory),
                "Invalid replacement GPU config changed the existing registration.");
            Assert(GpuRuntime.GetComputePower(originalFactory()) == catalog.Get("age_of_agi.gpu0").ComputePower,
                "Registered factories must retain the validated GPU configuration snapshot.");
        }
        finally
        {
            ModHelper.FileOverrides.Remove(GpuCatalog.CatalogPath);
            registration.Unregister();
        }

        GameItem mismatch = new GameItem { Id = "age_of_agi.other" };
        try
        {
            GpuHelper.InitGpu(mismatch, catalog.Get("age_of_agi.gpu0"), AudioHelper.InitModule);
            throw new Exception("GPU helper accepted a different item's configuration.");
        }
        catch (ArgumentException)
        {
            Assert(mismatch.unitValue == 0 && !GpuRuntime.IsGpu(mismatch), "Mismatched GPU config mutated the item.");
        }
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains(GpuCatalog.CatalogPath) && exception.Message.Contains(message),
                "GPU error must identify the file and problem: " + exception.Message);
            return;
        }

        throw new Exception("Expected GPU validation failure: " + message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
