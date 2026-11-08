using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi.Items;

internal static class OptionalItemTextChecks
{
    private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";
    private const string Row = "age_of_agi.text_test,generic,gpu0,0,item_gpu0_name,";

    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        ItemDefinition omitted = Parse(",,MODULE");
        Assert(omitted.DescriptionKey == "" && omitted.CommentKey == "" && omitted.Types.Single() == "module",
            "Blank text fields must not shift the types column.");
        ItemDefinition trimmed = Parse(" desc , comment , MODULE ; TOOL ");
        Assert(trimmed.DescriptionKey == "desc" && trimmed.CommentKey == "comment" && trimmed.Types.Count == 2,
            "Optional keys must be trimmed independently from types.");
        ItemDefinition nullKeys = new ItemDefinition("age_of_agi.text_test", "generic", "gpu0", 0, "item_gpu0_name", null, null);
        Assert(nullKeys.DescriptionKey == "" && nullKeys.CommentKey == "", "Optional keys must normalize null to empty.");
        foreach (string invalid in new[] { "bad/key,,", ",bad/key,", "bad key,,", ",\"comment\",", ",,MODULE," })
        {
            ExpectInvalid(Header + Row + invalid + ",none,none,\n");
        }
        ExpectInvalid("item_id,kind,sprite_key,name_key,description_key,types\n" + Row + ",,\n");
        ExpectInvalid(Header + Row + "desc,MODULE\n");

        LoadedMod loaded = Program.PrepareLoadedMod(new[] { omitted });
        var factory = new CatalogItemFactory(loaded.Manifest);
        int reads = ModHelper.LocalizationReads;
        GameItem noText = factory.Create(omitted);
        Assert(noText.shortDescription == "" && noText.flavorText == "" && ModHelper.LocalizationReads == reads + 1,
            "Omitted text must be empty, not null or a localization lookup.");

        foreach (string locale in new[] { "en", "zh" })
        {
            ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
            string[] lines = File.ReadAllLines(Path.Combine(ModHelper.Root, "Localization", locale + ".csv"));
            foreach (ItemDefinition definition in definitions.Where(item => item.CommentKey.Length > 0 && item.DescriptionKey.Length > 0))
            {
                int descIndex = Array.FindIndex(lines, line => line.StartsWith(definition.DescriptionKey + ",", StringComparison.Ordinal));
                Assert(descIndex >= 0 && descIndex + 1 < lines.Length &&
                    lines[descIndex + 1].StartsWith(definition.CommentKey + ",", StringComparison.Ordinal),
                    "Insert each comment directly after that item's description in " + locale);
            }
        }

        try
        {
            // Comments are editable content; verify their configured text without assuming
            // that a particular item's comment is still blank.
            ItemDefinition gpu = definitions.Single(item => item.ItemId == "age_of_agi.gpu0");
            var blankComment = new ItemDefinition(omitted.ItemId, "generic", "gpu0", 0, gpu.NameKey, gpu.DescriptionKey, gpu.CommentKey);
            ModHelper.Locale = "zh";
            GameItem blank = factory.Create(blankComment);
            Assert(blank.flavorText == ModHelper.Translations["zh"][gpu.CommentKey] &&
                blank.shortDescription == ModHelper.Translations["zh"][gpu.DescriptionKey],
                "Configured per-item text must resolve from the selected language.");
            ModHelper.Translations["zh"]["test_empty_comment"] = "";
            Assert(factory.Create(Parse(",test_empty_comment,")).flavorText == "",
                "An explicitly blank comment must not fall back to its key.");

            ModHelper.Translations["en"]["test_desc"] = "Test description";
            ModHelper.Translations["en"]["test_comment"] = "A piece of background lore.";
            ModHelper.Translations["zh"]["test_desc"] = "测试描述";
            ModHelper.Translations["zh"]["test_comment"] = "一段背景文字。";
            ItemDefinition both = Parse("test_desc,test_comment,");
            GameItem chinese = factory.Create(both);
            Assert(chinese.shortDescription == "测试描述" && chinese.flavorText == "一段背景文字。",
                "Description and comment must map to shortDescription and flavorText.");
            ModHelper.Locale = "fr";
            GameItem englishFallback = factory.Create(both);
            Assert(englishFallback.shortDescription == "Test description" && englishFallback.flavorText == "A piece of background lore.",
                "Optional text must retain English fallback when a key is provided.");
            GameItem commentOnly = factory.Create(Parse(",test_comment,"));
            GameItem descriptionOnly = factory.Create(Parse("test_desc,,"));
            Assert(commentOnly.shortDescription == "" && commentOnly.flavorText == "A piece of background lore." &&
                descriptionOnly.shortDescription == "Test description" && descriptionOnly.flavorText == "",
                "Each optional field must work independently.");

            // A deliberate empty translation is distinct from a missing translation.
            ModHelper.Translations["zh"]["test_comment"] = "";
            ModHelper.Locale = "zh";
            Assert(factory.Create(both).flavorText == "", "Empty current-language comments must stay empty.");
            ModHelper.Translations["zh"].Remove("test_comment");
            Assert(factory.Create(both).flavorText == "A piece of background lore.", "Missing comments must fall back to English.");
            Assert(factory.Create(Parse(",missing_comment,")).flavorText == "missing_comment",
                "Non-empty missing keys must retain the game's diagnostic fallback.");
        }
        finally
        {
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
    }

    private static ItemDefinition Parse(string textAndTypes)
    {
        return ItemCatalog.Parse(Header + Row + textAndTypes + ",none,none,\n", key => true)[0];
    }

    private static void ExpectInvalid(string csv)
    {
        try
        {
            ItemCatalog.Parse(csv, key => true);
        }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains("Data/items.csv line"), "Invalid text mapping must identify its CSV row.");
            return;
        }
        throw new Exception("Malformed text mapping was accepted.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
