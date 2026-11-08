using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class DataContentCatalog
    {
        internal const string CatalogPath = "Data/Definitions/data_carriers.csv";
        private readonly Dictionary<string, DataContentDefinition> entries = new Dictionary<string, DataContentDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> unitValues = new Dictionary<string, int>(StringComparer.Ordinal);
        internal DataContentDefinition Get(string id) => entries[id];
        internal int GetUnitValue(string id) => unitValues[id];
        internal static DataContentCatalog Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static DataContentCatalog Parse(string text)
        {
            var catalog = new DataContentCatalog();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in CatalogCsvReader.ReadRows(text, CatalogPath,
                new[] { "item_id", "mode", "capacity", "initial_data", "data_unit_value", "sprite_prefix" }))
            {
                var entry = row.Create("Data content", () => new DataContentDefinition(row.ReadItemId("item_id"),
                    row.ReadString("mode"), row.ReadInt("capacity"), row.ReadInt("initial_data"), row.ReadString("sprite_prefix")));
                int unitValue = row.ReadInt("data_unit_value");
                if (unitValue <= 0 || (long)entry.Capacity * unitValue > int.MaxValue)
                    throw row.Invalid("data_unit_value must be positive and content value must fit native Int32 misc-value aggregation.");
                if (!ids.Add(entry.ItemId)) throw row.Invalid("Duplicate data content ID: " + entry.ItemId);
                catalog.entries.Add(entry.ItemId, entry);
                catalog.unitValues.Add(entry.ItemId, unitValue);
            }
            if (catalog.entries.Values.Where(entry => entry.Container).Select(entry => catalog.GetUnitValue(entry.ItemId)).Distinct().Count() != 1 ||
                catalog.entries.Values.Where(entry => !entry.Container).Select(entry => catalog.GetUnitValue(entry.ItemId)).Distinct().Count() != 1 ||
                catalog.GetUnitValue(catalog.entries.Values.First(entry => entry.Container).ItemId) <=
                    catalog.GetUnitValue(catalog.entries.Values.First(entry => !entry.Container).ItemId))
                throw new InvalidOperationException(CatalogPath + ": use one raw data price and one higher parsed data price.");
            return catalog;
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var mapped = items.Where(item => item.Kind == "data_carrier" || item.Kind == "data_container").ToArray();
            if (!new HashSet<string>(mapped.Select(item => item.ItemId)).SetEquals(entries.Keys) ||
                mapped.Any(item => (item.Kind == "data_container") != Get(item.ItemId).Container ||
                    Get(item.ItemId).Container && item.SpriteKey != Get(item.ItemId).SpritePrefix + "0" ||
                    !Get(item.ItemId).Container && (!item.ItemId.StartsWith(ModConstants.ModId + ".dc_", StringComparison.Ordinal) ||
                        item.SpriteKey != item.ItemId.Substring(ModConstants.ModId.Length + 1))))
                throw new InvalidOperationException(CatalogPath + ": data family must match items.csv in both directions.");
        }
    }
}
