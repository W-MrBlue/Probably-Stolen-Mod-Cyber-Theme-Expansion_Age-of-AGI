using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi.Items;

internal static class ItemTypeChecks
{
    private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";
    private const string Row = "age_of_agi.variant,generic,gpu0,0,name,desc,,";

    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        ItemDefinition multiple = Parse(" MODULE ; ELECTRONICS ; module ; electronics ");
        Assert(multiple.Kind == "generic" && multiple.Types.SequenceEqual(new[] { "module", "electronics" }),
            "Type parsing must trim, lowercase, deduplicate, and preserve order without changing kind.");
        Assert(Parse("").Types.Count == 0 && Parse("   ").Types.Count == 0, "Empty types must be allowed.");
        foreach (string field in new[] { ";MODULE", "MODULE;", "MODULE;;TOOL", "bad/type", "电子产品", "\"MODULE;TOOL\"", "MODULE,TOOL" })
        {
            ExpectInvalid(Header + Row + field + ",none,none,\n");
        }

        ExpectInvalid(Header + "age_of_agi.variant,generic,gpu0,name,desc\n");
        var inputTypes = new List<string> { "MODULE" };
        var copied = new ItemDefinition("age_of_agi.copy", "generic", "gpu0", 0, "name", "desc", types: inputTypes);
        inputTypes.Add("TOOL");
        Assert(copied.Types.Count == 1, "ItemDefinition must own its type list.");

        foreach (ItemDefinition definition in definitions)
        {
            if (definition.Kind == "electronic_sign")
                Assert(definition.Types.Count == 0, "Supplier signs follow native signs and do not attract buyers as merchandise.");
            else if (definition.Kind == "ai_parts_chip")
                Assert(definition.Types.Count == 0, "Source chips retain empty displayed categories.");
            else if (definition.ItemId == "age_of_agi.virus" || definition.ItemId == "age_of_agi.folder_revol")
                Assert(definition.Types.Count == 0, "Deferred goods do not acquire an approved market category.");
            else if (definition.Kind == "ai_model")
                Assert(definition.Types.SequenceEqual(new[] { "ai_models" }),
                    "AI models stay outside the electronics category: " + definition.ItemId);
            else if (definition.Types.Contains("storage"))
                Assert(!definition.Types.Contains("electronics"), "Native storage stays in its own category.");
            else if (definition.Types.Contains("codeparts"))
                Assert(!definition.Types.Contains("electronics"),
                    "Code components and base models stay outside the electronics category: " + definition.ItemId);
            else if (definition.Kind != "machine")
                Assert(definition.Types.Contains("electronics"),
                    "Electronic merchandise needs the main attraction category; ordinary electronics need no subtype: " + definition.ItemId);
            if (definition.Kind == "ai_parts") Assert(definition.Types.Contains("codeparts"), "Keep code component categories.");
            if (definition.Kind == "machine") Assert(definition.Types.Contains("machine"), "Keep native machine categories.");
        }

        LoadedMod loaded = Program.PrepareLoadedMod(new[] { multiple });
        var factory = new CatalogItemFactory(loaded.Manifest);
        GameItem first = factory.Create(multiple);
        GameItem second = factory.Create(multiple);
        Assert(first.GetGameItemType().SequenceEqual(new[] { "MODULE", "electronics" }) &&
            first.IsTag("MODULE") && first.IsTag("electronics"),
            "Factory must preserve uppercase native category IDs/tags and lowercase custom categories.");
        first.GetGameItemType().Clear();
        Assert(second.GetGameItemType().Count == 2 && multiple.Types.Count == 2, "Item category lists must be independent.");

        ItemDefinition material = Parse("electronics;material");
        GameItem savedMaterial = factory.Create(material);
        savedMaterial.RemoveGameItemType("MATERIAL");
        savedMaterial.SetGameItemType("electronic_materials");
        savedMaterial.SetGameItemType("RARE");
        savedMaterial.EnableTag("MATERIAL"); // A saved tag alone must not hide a missing displayed category.
        savedMaterial.SetValue(123);
        savedMaterial.onLoaded(savedMaterial);
        savedMaterial.onLoaded(savedMaterial);
        Assert(savedMaterial.GetGameItemType().SequenceEqual(new[] { "electronics", "RARE", "MATERIAL" }) &&
            savedMaterial.IsTag("MATERIAL") && !savedMaterial.IsTag("electronic_materials") && savedMaterial.unitValue == 123,
            "Loading must replace the retired material category exactly once, preserving other categories and saved prices.");

        string[] customTypes = definitions.SelectMany(definition => definition.Types)
            .Where(type => !TypeHelper.ALL_TYPES_LIST.Contains(type.ToUpperInvariant()))
            .Distinct().ToArray();
        foreach (string locale in new[] { "en", "zh" })
        {
            var translations = new Dictionary<string, string>();
            string[] lines = File.ReadAllLines(Path.Combine(ModHelper.Root, "Localization", locale + ".csv"));
            foreach (string key in customTypes)
            {
                string[] entries = lines.Where(entry => entry.StartsWith(key + ",", StringComparison.Ordinal)).ToArray();
                Assert(entries.Length == 1, "Custom category needs exactly one translation: " + key + " / " + locale);
                string text = entries[0].Substring(entries[0].IndexOf(',') + 1);
                Assert(!string.IsNullOrWhiteSpace(text), "Custom category translation must not be empty: " + key + " / " + locale);
                translations.Add(key, text);
            }
            // A new category needs only same-name translations, with no production code change.
            translations.Add("future_part", locale == "zh" ? "未来组件" : "Future Part");
            translations.Add("machine", "must not replace native categories");
            translations.Add("empty_category", locale == "zh" ? "" : "Empty Category");
            ModHelper.Translations.Add(locale, translations);
        }

        try
        {
            foreach (string locale in new[] { "en", "zh" })
            {
                ModHelper.Locale = locale;
                foreach (string type in customTypes)
                {
                    ExpectLocalized(ModHelper.Translations[locale][type], type);
                }
            }

            ModHelper.Locale = "zh";
            ExpectLocalized("电子产品");
            ExpectLocalized("代码组件", "codeparts");
            ExpectLocalized("数据载体", "data_carrier");
            ExpectLocalized("未来组件", "future_part");
            ExpectLocalized("", "empty_category");
            ModHelper.Locale = "en";
            ExpectLocalized("Electronics");
            ExpectLocalized("Code Components", "codeparts");
            ExpectLocalized("Data Carrier", "data_carrier");
            ExpectLocalized("Future Part", "future_part");
            ExpectLocalized("Electronics", "ELECTRONICS");
            ModHelper.Locale = "fr";
            ExpectLocalized("Electronics");
            ExpectLocalized("Code Components", "codeparts");
            ExpectLocalized("Data Carrier", "data_carrier");
            ExpectLocalized("Future Part", "future_part");

            int reads = ModHelper.LocalizationReads;
            foreach (string type in new[] { "MODULE", "MACHINE", "machine", "OTHER_MOD_TYPE", "missing_custom", "", null })
            {
                string result = "unchanged";
                Assert(ItemTypeDisplayNamePatch.Prefix(type, ref result) && result == "unchanged",
                    "Native/foreign types must continue through the original method.");
            }

            loaded.IsEnabled = false;
            string disabledResult = "unchanged";
            Assert(ItemTypeDisplayNamePatch.Prefix("electronics", ref disabledResult) && disabledResult == "unchanged",
                "Disabled mods must not override type names.");
            Assert(ItemTypeDisplayNamePatch.Prefix("codeparts", ref disabledResult) && disabledResult == "unchanged",
                "Disabled mods must not override code-parts names.");
            ModLoader.Mods.Clear();
            Assert(ItemTypeDisplayNamePatch.Prefix("electronics", ref disabledResult),
                "The prefix must be safe before the loader registers the mod.");
            Assert(ModHelper.LocalizationReads == reads, "Passthrough categories must not resolve localization.");
        }
        finally
        {
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
    }

    private static ItemDefinition Parse(string types)
    {
        return ItemCatalog.Parse(Header + Row + types + ",none,none,\n", key => true)[0];
    }

    private static void ExpectLocalized(string expected, string type = "electronics")
    {
        string result = null;
        Assert(!ItemTypeDisplayNamePatch.Prefix(type, ref result) && result == expected,
            "Custom category localization or Harmony prefix result is incorrect.");
    }

    private static void ExpectInvalid(string csv)
    {
        try
        {
            ItemCatalog.Parse(csv, key => true);
        }
        catch (InvalidOperationException exception)
        {
            Assert(exception.Message.Contains("Data/items.csv line 2"), "Invalid category must identify its CSV row.");
            return;
        }

        throw new Exception("Malformed types were accepted.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
