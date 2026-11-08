using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Npcs;

internal static class DropSlotCsvFixture
{
    internal const string Header = "seller,slot,probability,min_reputation,item_ids\n";

    internal static string Market(string itemId) => OneItem(itemId, "dealer", "advanced_dealer");
    internal static string Salvage(string itemId) => OneItem(itemId, "scavenger", "petty_thief", "thief");

    internal static ElectronicsMarketCatalog LoadMarket(IReadOnlyList<ItemDefinition> items, ElectronicsVisitRules rules) =>
        ElectronicsMarketCatalog.Create(items, rules, ElectronicsDropCatalog.Load(items));

    internal static ElectronicsSalvageCatalog LoadSalvage(IReadOnlyList<ItemDefinition> items, ElectronicsVisitRules rules) =>
        ElectronicsSalvageCatalog.Create(rules, ElectronicsDropCatalog.Load(items));

    internal static ElectronicsMarketCatalog ParseMarket(IReadOnlyList<ItemDefinition> items, string csv, ElectronicsVisitRules rules) =>
        ElectronicsMarketCatalog.Create(items, rules, ElectronicsDropCatalog.Parse(items, csv,
            ElectronicsDropCatalog.CatalogPath, "dealer", "advanced_dealer"));

    internal static ElectronicsSalvageCatalog ParseSalvage(IReadOnlyList<ItemDefinition> items, string csv, ElectronicsVisitRules rules) =>
        ElectronicsSalvageCatalog.Create(rules, ElectronicsDropCatalog.Parse(items, csv,
            ElectronicsDropCatalog.CatalogPath, "scavenger", "petty_thief", "thief"));

    private static string OneItem(string itemId, params string[] sellers) => Header + string.Concat(sellers.Select(seller =>
        seller + ",low,100,0," + itemId + "\n" + seller + ",medium,0,0,\n" + seller + ",high,0,0,\n" +
        (seller == "thief" ? seller + ",reputation,0,7,\n" : string.Empty)));
}
