using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;

internal static class AudioPresetChecks
{
    private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";
    private const string Row = "age_of_agi.audio,generic,gpu0,0,name,,,,";

    internal static void Run(IReadOnlyList<ItemDefinition> source)
    {
        Assert(ItemCatalog.Parse(Header + Row + " plastic_card ,none,\n", key => true)[0].AudioPreset == "plastic_card",
            "Trim the preset field without changing its case.");
        foreach (string preset in new[] { "", "unknown", "Small_Module", "small-module", "\"small_module\"" })
        {
            try
            {
                ItemCatalog.Parse(Header + Row + preset + ",none,\n", key => true);
                throw new Exception("Accepted invalid audio preset: " + preset);
            }
            catch (InvalidOperationException exception)
            {
                Assert(exception.Message.Contains("Data/items.csv line 2") && exception.Message.Contains("audio_preset"),
                    "Invalid preset diagnostics must identify the CSV row and field.");
            }
        }

        var unchanged = new GameItem { soundDragStart = "custom", soundDragEnd = "custom" };
        ItemAudioPresets.Resolve("none")(unchanged);
        Assert(unchanged.soundDragStart == "custom" && unchanged.soundDragEnd == "custom",
            "The none preset leaves existing native defaults untouched.");

        foreach (string preset in new[] { "scanner", "plastic_card", "none" })
        {
            ItemDefinition[] items = source.Select(item => Copy(item, preset)).ToArray();
            LoadedMod loaded = Program.PrepareLoadedMod(items);
            var registration = new ItemRegistration(loaded.Manifest);
            var directory = new ModItemDirectory();
            registration.Register(directory, items);
            try
            {
                foreach (ItemDefinition definition in items)
                {
                    GameItem item = directory.Factories[definition.ItemId]();
                    Assert(item.soundDragStart == (preset == "none" ? null : preset + "_drag") &&
                        item.soundDragEnd == (preset == "none" ? null : preset + "_drop") &&
                        item.soundActivate == (preset == "scanner" ? "scanner_activate" : null) &&
                        item.soundContainerOpen == null && item.soundContainerClose == null,
                        "An owning helper ignored or overwrote configured audio: " + definition.ItemId + " / " + preset);
                    item.Destroy();
                }

                Func<GameItem> existing = directory.Factories[items[0].ItemId];
                var invalid = items.Select(item => Copy(item, item.AudioPreset)).ToArray();
                invalid[0] = Copy(items[0], "misspelled_preset");
                try
                {
                    registration.Register(directory, invalid);
                    throw new Exception("Programmatic definitions bypassed audio preset validation.");
                }
                catch (InvalidOperationException exception)
                {
                    Assert(exception.Message.Contains(items[0].ItemId) && exception.Message.Contains("audio_preset"),
                        "Registration errors must identify the item and invalid audio preset.");
                }
                Assert(ReferenceEquals(existing, directory.Factories[items[0].ItemId]),
                    "Invalid replacement audio must preserve the previously registered factories.");
            }
            finally { registration.Unregister(); }
        }

        Console.WriteLine("PASS: per-item CSV audio, all owning helpers and generic items, native activation fields, explicit none, invalid-row diagnostics and registration conservation.");
    }

    private static ItemDefinition Copy(ItemDefinition item, string preset)
    {
        return new ItemDefinition(item.ItemId, item.Kind, item.SpriteKey, item.BaseValue,
            item.NameKey, item.DescriptionKey, item.CommentKey, item.Types, preset);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
