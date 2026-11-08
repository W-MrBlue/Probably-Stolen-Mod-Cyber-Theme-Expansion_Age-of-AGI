using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class MemoryCatalog
    {
        internal const string CatalogPath = "Data/Definitions/memory.csv";
        private readonly Dictionary<string, MemoryDefinition> definitions;

        internal int Count => definitions.Count;

        private MemoryCatalog(Dictionary<string, MemoryDefinition> definitions)
        {
            this.definitions = definitions;
        }

        // Called once per directory registration, after the mod is in ModLoader.Mods.
        internal static MemoryCatalog Load()
        {
            return Parse(CatalogCsvReader.ReadModFile(CatalogPath));
        }

        internal static MemoryCatalog Parse(string csv)
        {
            var definitions = CatalogCsvReader.ReadDefinitions(csv, CatalogPath, "Memory",
                new[] { "id", "memory" },
                (entry, id) => new MemoryDefinition(id, entry.ReadInt("memory")));
            return new MemoryCatalog(definitions);
        }

        internal MemoryDefinition Get(string itemId)
        {
            if (itemId == null || !definitions.TryGetValue(itemId, out var definition))
            {
                throw InvalidEntry("items", "Missing Memory configuration for '" + itemId + "'.");
            }

            return definition;
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var mappedMemoryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item.Kind == "memory")
                {
                    Get(item.ItemId);
                    mappedMemoryIds.Add(item.ItemId);
                }
            }

            foreach (string itemId in definitions.Keys)
            {
                if (!mappedMemoryIds.Contains(itemId))
                {
                    throw InvalidEntry("items", "Memory '" + itemId + "' must have a kind=memory row in Data/items.csv.");
                }
            }
        }

        private static InvalidOperationException InvalidEntry(string location, string message)
        {
            return CatalogCsvReader.Error(CatalogPath, location, message);
        }
    }
}
