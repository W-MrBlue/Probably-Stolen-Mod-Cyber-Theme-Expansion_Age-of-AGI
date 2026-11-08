using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class BaseModelCatalog
    {
        internal const string CatalogPath = "Data/Definitions/base_model.csv";
        private readonly Dictionary<string, BaseModelDefinition> definitions;

        internal int Count => definitions.Count;

        private BaseModelCatalog(Dictionary<string, BaseModelDefinition> definitions)
        {
            this.definitions = definitions;
        }

        // Called once per directory registration, after the mod is in ModLoader.Mods.
        internal static BaseModelCatalog Load()
        {
            return Parse(CatalogCsvReader.ReadModFile(CatalogPath));
        }

        internal static BaseModelCatalog Parse(string csv)
        {
            var definitions = CatalogCsvReader.ReadDefinitions(csv, CatalogPath, "Base model",
                new[] { "id", "gridWidth", "gridHeight", "memoryMultiplier" },
                (entry, id) => new BaseModelDefinition(id, entry.ReadInt("gridWidth"), entry.ReadInt("gridHeight"),
                    entry.ReadFloat("memoryMultiplier")));
            return new BaseModelCatalog(definitions);
        }

        internal BaseModelDefinition Get(string itemId)
        {
            if (itemId == null || !definitions.TryGetValue(itemId, out var definition))
            {
                throw InvalidEntry("items", "Missing base model configuration for '" + itemId + "'.");
            }

            return definition;
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var mappedBaseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item.Kind == "base_model")
                {
                    Get(item.ItemId);
                    mappedBaseIds.Add(item.ItemId);
                }
            }

            foreach (string itemId in definitions.Keys)
            {
                if (!mappedBaseIds.Contains(itemId))
                {
                    throw InvalidEntry("items", "Base model '" + itemId + "' must have a kind=base_model row in Data/items.csv.");
                }
            }
        }

        private static InvalidOperationException InvalidEntry(string location, string message)
        {
            return CatalogCsvReader.Error(CatalogPath, location, message);
        }
    }
}
