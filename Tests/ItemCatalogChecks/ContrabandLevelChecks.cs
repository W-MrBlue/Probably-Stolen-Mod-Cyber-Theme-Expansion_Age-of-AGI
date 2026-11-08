using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;

internal static class ContrabandLevelChecks
{
    private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";
    private const string Row = "age_of_agi.level,generic,gpu0,0,name,,,electronics,none,";

    internal static void Run(IReadOnlyList<ItemDefinition> source)
    {
        foreach (var sample in new (string Key, int Level)[] { ("none", 0), ("low", 1), ("mid", 2), ("high", 3), ("critical", 4) })
        {
            ItemDefinition parsed = ItemCatalog.Parse(Header + Row + " " + sample.Key + " ,\n", _ => true).Single();
            Assert(parsed.ContrabandLevel == sample.Level, "Trim and parse each native contraband level.");
            ItemDefinition[] definitions = source.Select(item => new ItemDefinition(item.ItemId, item.Kind, item.SpriteKey,
                item.BaseValue, item.NameKey, item.DescriptionKey, item.CommentKey, item.Types, item.AudioPreset, sample.Key)).ToArray();
            var loaded = Program.PrepareLoadedMod(definitions);
            var registration = new ItemRegistration(loaded.Manifest);
            var directory = new ModItemDirectory();
            registration.Register(directory, definitions);
            try
            {
                foreach (ItemDefinition definition in definitions)
                {
                    GameItem item = directory.Factories[definition.ItemId]();
                    Assert(item.IsTag("CONTRABAND_ITEM_TAG") == (sample.Level > 0) &&
                        item.GetGameItemType().Contains("CONTRABAND") == (sample.Level > 0),
                        "All item families receive the configured native contraband status: " + definition.ItemId);
                    if (sample.Level > 0)
                        Assert(item.GetTagReadonly("CONTRABAND_LEVEL").GetInt() == sample.Level &&
                            item.GetTagReadonly("CONTRABAND_LEVEL").GetString() == "CONTRABAND_LEVEL_" + sample.Key.ToUpperInvariant(),
                            "Initialize both native numeric and string severity fields.");
                    else Assert(!item.IsTag("CONTRABAND_LEVEL"), "None leaves no enabled native severity tag.");
                    // Native decoding restores saved tags before onLoaded. Configuration
                    // initializes new instances without resetting saved gameplay changes.
                    ContrabandHelper.InitContrabandItem(item, 3);
                    item.onLoaded?.Invoke(item);
                    Assert(item.GetTagReadonly("CONTRABAND_LEVEL").GetString() == "CONTRABAND_LEVEL_HIGH",
                        "Loading retains saved contraband status instead of reapplying initial configuration.");
                    item.Destroy();
                }
            }
            finally { registration.Unregister(); }
        }

        foreach (string invalid in new[] { "", " ", "dangerous", "critical_level", "LOW", "1", "\"low\"" })
        {
            try
            {
                ItemCatalog.Parse(Header + Row + invalid + ",\n", _ => true);
                throw new Exception("Accepted invalid contraband level: " + invalid);
            }
            catch (InvalidOperationException exception)
            {
                Assert(exception.Message.Contains("Data/items.csv line 2") && exception.Message.Contains("contraband_level"),
                    "Invalid level diagnostics identify the file, row and column.");
            }
        }
        try
        {
            new ItemDefinition("age_of_agi.level", "generic", "gpu0", 0, "name", "", contrabandLevel: "dangerous");
            throw new Exception("Programmatic definitions bypassed contraband validation.");
        }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: CSV contraband levels, native none/low/mid/high/critical status across all item families, saved state and row diagnostics.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
