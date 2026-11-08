using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;

internal static class BaseValueChecks
{
    private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";

    internal static void Run(IReadOnlyList<ItemDefinition> source)
    {
        Assert(Parse("0").BaseValue == 0, "Zero price is valid.");
        Assert(Parse(" 50 ").BaseValue == 50, "Price cell whitespace must be trimmed.");
        Assert(Parse("5000000000").BaseValue == 5000000000L && Parse("9223372036854775807").BaseValue == long.MaxValue,
            "Common base values must support Int64.");
        foreach (string invalid in new[] { "", " ", "-1", "+1", "1.5", "1e3", "1_000", "1,000", "NaN", "9223372036854775808" })
        {
            try { Parse(invalid); throw new Exception("Invalid price accepted: " + invalid); }
            catch (InvalidOperationException exception)
            {
                Assert(exception.Message.Contains("Data/items.csv line 2"), "Invalid prices must identify their CSV row.");
            }
        }
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert(Parse("1234").BaseValue == 1234, "Price parsing must be independent of UI/system locale.");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        try
        {
            new ItemDefinition("age_of_agi.price", "generic", "gpu0", -1, "name", "");
            throw new Exception("Negative constructor price accepted.");
        }
        catch (ArgumentOutOfRangeException) { }

        // Every category receives a deliberately different common price. Type helpers
        // must not overwrite it with a stale category-specific value or a default.
        ItemDefinition[] definitions = source.Select((item, index) => new ItemDefinition(item.ItemId, item.Kind,
            item.SpriteKey, 9000L + index, item.NameKey, item.DescriptionKey, item.CommentKey, item.Types, item.AudioPreset)).ToArray();
        LoadedMod mod = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(mod.Manifest);
        var directory = new ModItemDirectory();
        registration.Register(directory, definitions);
        try
        {
            foreach (ItemDefinition definition in definitions)
            {
                GameItem item = directory.Factories[definition.ItemId]();
                Assert(item.unitValue == definition.BaseValue && item.SetValueCalls == 1,
                    "Initialize each price exactly once through the shared factory: " + definition.ItemId);
            }
            GameItem writer = directory.Factories[DiskWriterCatalog.DiskWriterId]();
            writer.SetValue(123);
            writer.onLoaded?.Invoke(writer);
            Assert(writer.unitValue == 123, "Loading an existing machine must not reset its saved price.");
        }
        finally { registration.Unregister(); }

        foreach (var check in new (string Path, Action<string> Parse)[]
        {
            (GpuCatalog.CatalogPath, csv => GpuCatalog.Parse(csv)),
            (CpuCatalog.CatalogPath, csv => CpuCatalog.Parse(csv)),
            (MemoryCatalog.CatalogPath, csv => MemoryCatalog.Parse(csv)),
            (MachineCatalog.CatalogPath, csv => MachineCatalog.Parse(csv))
        })
        {
            var root = CsvFixture.Parse(File.ReadAllText(Path.Combine(ModHelper.Root, check.Path)));
            root.AddColumn("baseValue", "10");
            try { check.Parse(root.ToString()); throw new Exception("Legacy CSV price accepted: " + check.Path); }
            catch (InvalidOperationException exception)
            {
                Assert(exception.Message.Contains(check.Path) && exception.Message.Contains("Unknown column"),
                    "Category CSV must reject duplicated price configuration.");
            }
        }
        CheckHelpersPreservePrice(source);
    }

    private static void CheckHelpersPreservePrice(IReadOnlyList<ItemDefinition> items)
    {
        GpuCatalog gpu = GpuCatalog.Load();
        CpuCatalog cpu = CpuCatalog.Load();
        MemoryCatalog memory = MemoryCatalog.Load();
        MachineCatalog machines = MachineCatalog.Load();
        foreach (ItemDefinition definition in items.Where(item => item.Kind == "gpu" || item.Kind == "cpu" ||
            item.Kind == "memory" || item.ItemId == DiskWriterCatalog.DiskWriterId))
        {
            var item = new GameItem { Id = definition.ItemId };
            item.SetValue(777);
            switch (definition.Kind)
            {
                case "gpu": GpuHelper.InitGpu(item, gpu.Get(item.identifier), ItemAudioPresets.Resolve(definition.AudioPreset)); break;
                case "cpu": CpuHelper.InitCpu(item, cpu.Get(item.identifier), ItemAudioPresets.Resolve(definition.AudioPreset)); break;
                case "memory": MemoryHelper.InitMemory(item, memory.Get(item.identifier), ItemAudioPresets.Resolve(definition.AudioPreset)); break;
                case "machine": MachineParameters.InitMachine(item, machines.Get(item.identifier)); break;
            }
            Assert(item.unitValue == 777 && item.SetValueCalls == 1, "A type helper overwrote a common price.");
        }
    }

    private static ItemDefinition Parse(string price)
    {
        return ItemCatalog.Parse(Header + "age_of_agi.price,generic,gpu0," + price + ",name,,,MODULE,none,none,\n", key => true)[0];
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
