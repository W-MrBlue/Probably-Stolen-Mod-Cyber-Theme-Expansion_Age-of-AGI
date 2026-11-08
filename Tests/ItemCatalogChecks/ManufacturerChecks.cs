using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Logic.Manufacturers;

internal static class ManufacturerChecks
{
    private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";
    private const string Row = "age_of_agi.manufacturer_fixture,generic,gpu0,17,fixture_name,,,,none,none,";

    internal static void Run()
    {
        Assert(Parse("  solana  ").Manufacturer == "solana" && Parse("").Manufacturer == "",
            "Manufacturer cells are optional and trim surrounding whitespace.");
        foreach (string invalid in new[] { "bad/id", "bad id", "\"solana\"", "one,two" })
        {
            try { Parse(invalid); throw new Exception("Accepted invalid manufacturer: " + invalid); }
            catch (InvalidOperationException error)
            { Assert(error.Message.Contains("Data/items.csv line 2"), "Manufacturer diagnostics identify their CSV row."); }
        }

        ItemDefinition definition = Parse("age_of_agi.prism");
        LoadedMod mod = Program.PrepareLoadedMod(new[] { definition });
        var factory = new CatalogItemFactory(mod.Manifest);
        try
        {
            foreach (string company in new[] { "", "solana", "age_of_agi.astar", "age_of_agi.prism", "age_of_agi.silicon" })
            {
                GameItem item = factory.Create(Parse(company));
                Assert(item.IsTag("manufacturer") == (company.Length > 0) &&
                    (company.Length == 0 || item.GetTagReadonly("manufacturer").GetString() == company),
                    "Creation binds the configured company through the native manufacturer tag.");
                item.EnableManufacturer("saved_instance_company");
                item.onLoaded?.Invoke(item);
                Assert(item.GetTagReadonly("manufacturer").GetString() == "saved_instance_company",
                    "Load callbacks must not replace the native saved manufacturer with catalog defaults.");
            }

            foreach (string locale in new[] { "en", "zh" })
            {
                ModHelper.Locale = locale;
                ModHelper.Translations[locale] = new Dictionary<string, string>();
                foreach (string company in new[] { "astar", "prism", "silicon" })
                {
                    string text = locale + " fixture " + company;
                    ModHelper.Translations[locale]["manufacturer_" + company] = text;
                    var builder = new RichTextBuilder();
                    Assert(!ManufacturerTooltipPatch.Prefix(builder, "age_of_agi." + company) &&
                        builder.Lines.Single().Text == text && builder.Lines.Single().Color == RenderHandler.ColorPalette.Turquoise,
                        "New companies render one localized manufacturer row using native styling.");
                }
            }
            foreach (string company in new[] { "solana", "pastel", "foreign.custom_company", "", null })
            {
                var builder = new RichTextBuilder();
                Assert(ManufacturerTooltipPatch.Prefix(builder, company) && builder.Text == "",
                    "Native and other manufacturers pass through without extra rows or whitespace.");
            }
            mod.IsEnabled = false;
            Assert(ManufacturerTooltipPatch.Prefix(new RichTextBuilder(), "age_of_agi.astar"),
                "Manufacturer rendering passes through when the mod is disabled.");
        }
        finally { ModHelper.Translations.Clear(); ModHelper.Locale = "en"; }
        Console.WriteLine("PASS: optional manufacturer CSV fields, native company tags, saved instance ownership, localized company rows and scoped native fallback.");
    }

    private static ItemDefinition Parse(string company) => ItemCatalog.Parse(Header + Row + company + "\n", _ => true).Single();
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
