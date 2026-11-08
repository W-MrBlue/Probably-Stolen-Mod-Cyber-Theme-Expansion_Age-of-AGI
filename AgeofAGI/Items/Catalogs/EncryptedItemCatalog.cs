using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class EncryptedItemCatalog
    {
        internal const string CatalogPath = "Data/Definitions/encrypted_items.csv";
        private readonly Dictionary<string, EncryptedItemDefinition> entries =
            new Dictionary<string, EncryptedItemDefinition>(StringComparer.Ordinal);
        internal IEnumerable<EncryptedItemDefinition> Entries => entries.Values;
        internal bool TryGet(string id, out EncryptedItemDefinition definition) => entries.TryGetValue(id, out definition);
        internal static EncryptedItemCatalog Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static EncryptedItemCatalog Parse(string csv)
        {
            var catalog = new EncryptedItemCatalog();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in CatalogCsvReader.ReadRows(csv, CatalogPath,
                new[] { "item_id", "work", "task_type", "output_item_id" }))
            {
                var entry = row.Create("Encrypted item", () => new EncryptedItemDefinition(row.ReadItemId("item_id"),
                    row.ReadFloat("work"), row.ReadString("task_type"), row.ReadItemId("output_item_id")));
                if (!ids.Add(entry.InputItemId)) throw row.Invalid("Duplicate encrypted item ID: " + entry.InputItemId);
                catalog.entries.Add(entry.InputItemId, entry);
            }
            if (catalog.entries.Count == 0) throw CatalogCsvReader.Error(CatalogPath, "", "At least one encrypted item is required.");
            return catalog;
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var ids = new HashSet<string>(items.Where(item => item.Kind == "data_item").Select(item => item.ItemId));
            foreach (var entry in entries.Values)
                foreach (string id in new[] { entry.InputItemId, entry.OutputItemId })
                    if (!ids.Contains(id))
                        throw CatalogCsvReader.Error(CatalogPath, "items", "Require a registered data_item CSV row: " + id);
        }
    }
}
