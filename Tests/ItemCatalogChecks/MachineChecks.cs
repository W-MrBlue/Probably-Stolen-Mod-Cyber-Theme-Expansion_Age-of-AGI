using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;

internal static class MachineChecks
{
    private const string DiskWriterId = "age_of_agi.disk_writer";
    private const string ValidCsv = "id,powerPerCycle\nage_of_agi.disk_writer,3\n";

    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        MachineCatalog catalog = MachineCatalog.Load();
        catalog.ValidateMappings(items);
        Assert(catalog.Count == items.Count(item => item.Kind == "machine") && catalog.Get(DiskWriterId).PowerPerCycle == 3 &&
            catalog.Get("age_of_agi.compiler").PowerPerCycle == 10,
            "Disk writer must cost 3 and the compiler 10 per operation.");
        Assert(catalog.GetForItem("age_of_agi.compiler") != null && catalog.GetForItem("age_of_agi.serverbox").PowerPerCycle == 0,
            "The server starts at zero and derives running power from installed hardware.");
        ExpectFailure(() => catalog.GetForItem("age_of_agi.missing"));
        foreach (string csv in new[] { "", "id", "id,id", "id\nage_of_agi.invalid", ValidCsv + " {}",
            CsvFixture.Cell(ValidCsv, "powerPerCycle", "-1"),
            CsvFixture.Cell(ValidCsv, "powerPerCycle", "1.5"),
            CsvFixture.Cell(ValidCsv, "powerPerCycle", "\"3\""),
            CsvFixture.Cell(ValidCsv, "powerPerCycle", "2147483648"),
            ValidCsv.Replace("powerPerCycle", "powerPercycle"), ValidCsv.Replace("age_of_agi.", "other_mod.") })
        {
            ExpectFailure(() => MachineCatalog.Parse(csv));
        }
        foreach (string field in new[] { "id", "powerPerCycle" })
        {
            var root = CsvFixture.Parse(ValidCsv);
            root.RemoveColumn(field);
            ExpectFailure(() => MachineCatalog.Parse(root.ToString()));
        }
        var duplicates = CsvFixture.Parse(ValidCsv);
        duplicates.DuplicateFirstRow();
        duplicates.Rows[1]["id"] = "age_of_agi.DISK_WRITER";
        ExpectFailure(() => MachineCatalog.Parse(duplicates.ToString()));
        Assert(MachineCatalog.Parse(CsvFixture.Cell(ValidCsv, "powerPerCycle", "0"))
            .Get(DiskWriterId).PowerPerCycle == 0, "Zero operation cost is valid.");
        ExpectFailure(() => catalog.ValidateMappings(items.Where(item => item.ItemId != DiskWriterId).ToArray()));
        ExpectFailure(() => catalog.ValidateMappings(items.Concat(new[]
        {
            new ItemDefinition("age_of_agi.new_machine", "machine", "disk_writer", 0, "name", "")
        }).ToArray()));

        LoadedMod loaded = Program.PrepareLoadedMod(items);
        var directory = new ModItemDirectory();
        var registration = new ItemRegistration(loaded.Manifest);
        ModHelper.FileReads = 0;
        registration.Register(directory, items);
        try
        {
            Assert(ModHelper.FileReads == 14, "Read every property catalog once per batch.");
            GameItem writer = directory.Factories[DiskWriterId]();
            Assert(writer.unitValue == 150 && MachineParameters.IsMachine(writer) &&
                MachineParameters.GetPowerPerCycle(writer) == 3 && writer.GetGameItemType().Contains("MACHINE"),
                "Disk writer factory must apply value, category, and operation cost.");
            Assert(!writer.IsTag("machinery") && !writer.IsTag("machinery_drawn_power"),
                "Per-operation cost must not register automatic daily machinery power.");
            Assert(MachineParameters.IsMachine(directory.Factories["age_of_agi.serverbox"]()), "Serverbox is a configured machine.");
            GameItem compiler = directory.Factories["age_of_agi.compiler"]();
            Assert(MachineParameters.IsMachine(compiler) && MachineParameters.GetPowerPerCycle(compiler) == 10 &&
                compiler.GetGameItemType().Contains("MACHINE"),
                "The compiler must receive the common machine parameters and native category.");
            writer.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(8));
            Assert(MachineParameters.GetPowerPerCycle(directory.Factories[DiskWriterId]()) == 3,
                "Machine instances must not share mutable tags.");
            Assert(ModHelper.FileReads == 14, "Creating machines must not reread CSV.");

            Func<GameItem> previous = directory.Factories[DiskWriterId];
            foreach (string invalid in new[] { null, "{", ValidCsv.Replace(DiskWriterId, "age_of_agi.compiler") })
            {
                ModHelper.FileOverrides[MachineCatalog.CatalogPath] = invalid;
                ExpectFailure(() => registration.Register(directory, items));
                Assert(ReferenceEquals(previous, directory.Factories[DiskWriterId]), "Invalid config replaced an existing factory.");
            }
            ModHelper.FileOverrides.Remove(MachineCatalog.CatalogPath);
            CheckTooltips(writer);
        }
        finally
        {
            ModHelper.FileOverrides.Remove(MachineCatalog.CatalogPath);
            registration.Unregister();
        }
    }

    private static void CheckTooltips(GameItem writer)
    {
        foreach (string locale in new[] { "en", "zh" })
        {
            ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        }
        var mod = new AgeOfAgiMod();
        mod.Init(new ModManifest());
        mod.OnEnable();
        mod.OnEnable();
        try
        {
            ModHelper.Locale = "zh";
            ExpectTooltip(writer, "单次运行耗电：8");
            writer.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(0));
            ModHelper.Locale = "en";
            ExpectTooltip(writer, "Power per operation: 0");
            ModHelper.Locale = "fr";
            ExpectTooltip(writer, "Power per operation: 0");
            ExpectTooltip(new GameItem());
            mod.OnDisable();
            ExpectTooltip(writer);
            mod.OnEnable();
            ExpectTooltip(writer, "Power per operation: 0");
        }
        finally
        {
            mod.OnDisable();
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
    }

    private static void ExpectTooltip(GameItem item, params string[] expected)
    {
        var builder = new RichTextBuilder();
        ModHook.RaiseCreateTooltip(builder, item);
        Assert(builder.Lines.Select(line => line.Text).SequenceEqual(expected), "Wrong machine tooltip or duplicate subscription.");
        Assert(builder.Lines.All(line => line.Color == RenderHandler.ColorPalette.LightCyan), "Wrong machine stat color.");
    }

    private static void ExpectFailure(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains(MachineCatalog.CatalogPath), "Machine error must identify its config file.");
            return;
        }
        throw new Exception("Expected invalid machine config to fail.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
