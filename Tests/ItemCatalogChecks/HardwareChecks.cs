using AgeOfAgi.Items.Logic.Memory;
using AgeOfAgi.Items.Logic.Cpu;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Helpers;

internal static class HardwareChecks
{
    private const string CpuCsv = "id,powerBoostRate,powerPerCycle\nage_of_agi.cpu1,1.5,3\n";
    private const string MemoryCsv = "id,memory\nage_of_agi.mem1,16\n";

    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        CheckSchema(CpuCsv, CpuCatalog.CatalogPath, csv => CpuCatalog.Parse(csv),
            new[] { "id", "powerBoostRate", "powerPerCycle" });
        CheckSchema(MemoryCsv, MemoryCatalog.CatalogPath, csv => MemoryCatalog.Parse(csv),
            new[] { "id", "memory" });

        Assert(CpuCatalog.Parse(CpuCsv).Get("age_of_agi.cpu1").PowerBoostRate == 1.5f, "Fractional CPU rate lost.");
        Assert(CpuCatalog.Parse(CpuCsv.Replace("1.5", "2")).Get("age_of_agi.cpu1").PowerBoostRate == 2f,
            "Integer CPU rates must also be accepted.");
        foreach (string value in new[] { "0", "0.0", "1e-100" })
            Assert(CpuCatalog.Parse(CpuCsv.Replace("1.5", value)).Get("age_of_agi.cpu1").PowerBoostRate == 0,
                "A zero CPU rate, including a value rounded to zero by Single precision, means no extra compute bonus.");
        foreach (string value in new[] { "-1", "-0.1", "\"1.5\"", "NaN", "Infinity", "-Infinity", "1e100" })
        {
            ExpectFailure(() => CpuCatalog.Parse(CpuCsv.Replace("1.5", value)), CpuCatalog.CatalogPath);
        }

        foreach (string value in new[] { "0", "-1", "8.5", "\"16\"", "2147483648" })
        {
            ExpectFailure(() => MemoryCatalog.Parse(CsvFixture.Cell(MemoryCsv, "memory", value)),
                MemoryCatalog.CatalogPath);
        }

        ExpectFailure(() => CpuCatalog.Parse(CsvFixture.Cell(CpuCsv, "powerPerCycle", "-1")), CpuCatalog.CatalogPath);
        Assert(CpuCatalog.Parse(CsvFixture.Cell(CpuCsv, "powerPerCycle", "0"))
            .Get("age_of_agi.cpu1").PowerPerCycle == 0, "CPU zero power is valid.");

        CpuCatalog singleCpu = CpuCatalog.Parse(CpuCsv);
        MemoryCatalog singleMemory = MemoryCatalog.Parse(MemoryCsv);
        ExpectFailure(() => singleCpu.ValidateMappings(items), CpuCatalog.CatalogPath);
        ExpectFailure(() => singleMemory.ValidateMappings(items), MemoryCatalog.CatalogPath);
        ExpectFailure(() => singleCpu.ValidateMappings(Array.Empty<ItemDefinition>()), CpuCatalog.CatalogPath);
        ExpectFailure(() => singleMemory.ValidateMappings(Array.Empty<ItemDefinition>()), MemoryCatalog.CatalogPath);

        CheckRegistration(items);
        CheckTooltips();
    }

    private static void CheckSchema(string validCsv, string path, Action<string> parse, string[] fields)
    {
        foreach (string invalid in new[] { "", "id", "id,id", "id\nage_of_agi.invalid", validCsv + " {}" })
        {
            ExpectFailure(() => parse(invalid), path);
        }

        foreach (string field in fields)
        {
            var root = CsvFixture.Parse(validCsv);
            root.RemoveColumn(field);
            ExpectFailure(() => parse(root.ToString()), path);
        }

        var unknown = CsvFixture.Parse(validCsv);
        unknown.AddColumn("typo", "1");
        ExpectFailure(() => parse(unknown.ToString()), path);
        ExpectFailure(() => parse(validCsv.Replace("age_of_agi.", "other_mod.")), path);
        var duplicates = CsvFixture.Parse(validCsv);
        duplicates.DuplicateFirstRow();
        var array = duplicates.Rows;
        array[1]["id"] = array[1]["id"].ToUpperInvariant();
        // Keep the required prefix; IDs are otherwise compared without case for duplicates.
        array[1]["id"] = array[1]["id"].Replace("AGE_OF_AGI.", "age_of_agi.");
        ExpectFailure(() => parse(duplicates.ToString()), path);
    }

    private static void CheckRegistration(IReadOnlyList<ItemDefinition> items)
    {
        LoadedMod loadedMod = Program.PrepareLoadedMod(items);
        CpuCatalog cpus = CpuCatalog.Load();
        MemoryCatalog memory = MemoryCatalog.Load();
        cpus.ValidateMappings(items);
        memory.ValidateMappings(items);
        Assert(cpus.Count == 3 && memory.Count == 3, "CPU/memory catalog coverage differs from mappings.");
        Assert(items.Single(item => item.ItemId == "age_of_agi.mem2").BaseValue == 400, "mem2 must use the progression-design price.");
        var registration = new ItemRegistration(loadedMod.Manifest);
        var directory = new ModItemDirectory();
        ModHelper.FileReads = 0;
        registration.Register(directory, items);
        Assert(ModHelper.FileReads == 14, "Read each property catalog once per batch.");
        try
        {
            foreach (ItemDefinition definition in items)
            {
                GameItem item = directory.Factories[definition.ItemId]();
                Assert(CpuState.IsCpu(item) == (definition.Kind == "cpu"), "CPU tags leaked between kinds.");
                Assert(MemoryState.IsMemory(item) == (definition.Kind == "memory"), "Memory tags leaked between kinds.");
                if (definition.Kind == "cpu")
                {
                    CpuDefinition config = cpus.Get(definition.ItemId);
                    Assert(item.unitValue == definition.BaseValue && CpuState.GetPowerBoostRate(item) == config.PowerBoostRate &&
                        CpuState.GetPowerPerCycle(item) == config.PowerPerCycle, "CPU factory did not apply CSV.");
                    item.ModifyTag(CpuState.PowerBoostRateTag, state => state.SetFloat(7.25f));
                    Assert(CpuState.GetPowerBoostRate(directory.Factories[definition.ItemId]()) == config.PowerBoostRate,
                        "CPU instances share mutable state.");
                }
                else if (definition.Kind == "memory")
                {
                    MemoryDefinition config = memory.Get(definition.ItemId);
                    Assert(item.unitValue == definition.BaseValue && MemoryState.GetMemory(item) == config.Memory,
                        "Memory factory did not apply CSV.");
                    item.ModifyTag(MemoryState.CapacityTag, state => state.SetInt(64));
                    Assert(MemoryState.GetMemory(directory.Factories[definition.ItemId]()) == config.Memory,
                        "Memory instances share mutable state.");
                }

                var expectedAudio = new GameItem();
                ItemAudioPresets.Resolve(definition.AudioPreset)(expectedAudio);
                Assert(item.soundDragStart == expectedAudio.soundDragStart && item.soundDragEnd == expectedAudio.soundDragEnd &&
                    item.soundContainerOpen == expectedAudio.soundContainerOpen && item.soundContainerClose == expectedAudio.soundContainerClose &&
                    item.soundActivate == expectedAudio.soundActivate, "Factory audio differs from its configured preset: " + definition.ItemId);
            }

            Assert(ModHelper.FileReads == 14, "Factories reread CSV.");
            Func<GameItem> previous = directory.Factories["age_of_agi.cpu1"];
            foreach (string path in new[] { CpuCatalog.CatalogPath, MemoryCatalog.CatalogPath })
            {
                try
                {
                    ModHelper.FileOverrides[path] = null;
                    ExpectFailure(() => registration.Register(directory, items), path);
                    ModHelper.FileOverrides[path] = "{";
                    ExpectFailure(() => registration.Register(directory, items), path);
                    Assert(ReferenceEquals(previous, directory.Factories["age_of_agi.cpu1"]),
                        "Invalid replacement config changed an existing factory.");
                }
                finally
                {
                    ModHelper.FileOverrides.Remove(path);
                }
            }
        }
        finally
        {
            registration.Unregister();
        }
    }

    private static void CheckTooltips()
    {
        foreach (string locale in new[] { "en", "zh" })
        {
            var translations = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(Path.Combine(ModHelper.Root, "Localization", locale + ".csv")))
            {
                int comma = line.IndexOf(',');
                if (comma > 0)
                {
                    translations.Add(line.Substring(0, comma), line.Substring(comma + 1));
                }
            }

            ModHelper.Translations.Add(locale, translations);
        }

        var cpu = new GameItem { Id = "age_of_agi.cpu1" };
        var memory = new GameItem { Id = "age_of_agi.mem1" };
        CpuHelper.InitCpu(cpu, CpuCatalog.Parse(CpuCsv).Get(cpu.Id), AudioHelper.InitSmallModule);
        MemoryHelper.InitMemory(memory, MemoryCatalog.Parse(MemoryCsv).Get(memory.Id), AudioHelper.InitSmallModule);
        var mod = new AgeOfAgiMod();
        mod.Init(new ModManifest());
        mod.OnEnable();
        mod.OnEnable();
        try
        {
            ModHelper.Locale = "zh";
            ExpectTooltip(cpu, ExpectedText("zh", CpuState.PowerBoostRateTextKey, 1.5f),
                ExpectedText("zh", CpuState.PowerPerCycleTextKey, 3));
            ExpectTooltip(memory, ExpectedText("zh", MemoryState.CapacityTextKey, 16));
            cpu.ModifyTag(CpuState.PowerBoostRateTag, state => state.SetFloat(2.25f));
            cpu.ModifyTag(CpuState.PowerPerCycleTag, state => state.SetInt(0));
            memory.ModifyTag(MemoryState.CapacityTag, state => state.SetInt(32));
            ModHelper.Locale = "en";
            ExpectTooltip(cpu, ExpectedText("en", CpuState.PowerBoostRateTextKey, 2.25f),
                ExpectedText("en", CpuState.PowerPerCycleTextKey, 0));
            ExpectTooltip(memory, ExpectedText("en", MemoryState.CapacityTextKey, 32));
            ModHelper.Locale = "fr";
            ExpectTooltip(memory, ExpectedText("en", MemoryState.CapacityTextKey, 32));
            ExpectTooltip(new GameItem { Id = "other_mod.item" });
            mod.OnDisable();
            ExpectTooltip(cpu);
            ExpectTooltip(memory);
            mod.OnEnable();
            ExpectTooltip(cpu, ExpectedText("en", CpuState.PowerBoostRateTextKey, 2.25f),
                ExpectedText("en", CpuState.PowerPerCycleTextKey, 0));
        }
        finally
        {
            mod.OnDisable();
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
    }

    private static string ExpectedText(string locale, string key, object value)
    {
        // Translation wording and spacing are editable; verify the current format and value.
        return string.Format(ModHelper.Translations[locale][key], value);
    }

    private static void ExpectTooltip(GameItem item, params string[] expected)
    {
        var builder = new RichTextBuilder();
        ModHook.RaiseCreateTooltip(builder, item);
        Assert(builder.Lines.Select(line => line.Text).SequenceEqual(expected), "Wrong localized hardware tooltip.");
        Assert(builder.Lines.All(line => line.Color == RenderHandler.ColorPalette.LightCyan), "Wrong stat color.");
    }

    private static void ExpectFailure(Action action, string path)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains(path), "Error must identify catalog: " + exception.Message);
            return;
        }

        throw new Exception("Expected validation failure: " + path);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
