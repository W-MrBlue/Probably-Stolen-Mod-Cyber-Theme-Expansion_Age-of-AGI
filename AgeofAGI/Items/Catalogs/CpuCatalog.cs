using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class CpuCatalog
    {
        internal const string CatalogPath = "Data/Definitions/cpu.csv";
        private readonly Dictionary<string, CpuDefinition> definitions;

        internal int Count => definitions.Count;

        private CpuCatalog(Dictionary<string, CpuDefinition> definitions)
        {
            this.definitions = definitions;
        }

        // Called once per directory registration, after the mod is in ModLoader.Mods.
        internal static CpuCatalog Load()
        {
            return Parse(CatalogCsvReader.ReadModFile(CatalogPath));
        }

        internal static CpuCatalog Parse(string csv)
        {
            var definitions = CatalogCsvReader.ReadDefinitions(csv, CatalogPath, "CPU",
                new[] { "id", "powerBoostRate", "powerPerCycle" },
                (entry, id) => new CpuDefinition(id, entry.ReadFloat("powerBoostRate"), entry.ReadInt("powerPerCycle")));
            return new CpuCatalog(definitions);
        }

        internal CpuDefinition Get(string itemId)
        {
            if (itemId == null || !definitions.TryGetValue(itemId, out var definition))
            {
                throw InvalidEntry("items", "Missing CPU configuration for '" + itemId + "'.");
            }

            return definition;
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var mappedCpuIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item.Kind == "cpu")
                {
                    Get(item.ItemId);
                    mappedCpuIds.Add(item.ItemId);
                }
            }

            foreach (string itemId in definitions.Keys)
            {
                if (!mappedCpuIds.Contains(itemId))
                {
                    throw InvalidEntry("items", "CPU '" + itemId + "' must have a kind=cpu row in Data/items.csv.");
                }
            }
        }

        private static InvalidOperationException InvalidEntry(string location, string message)
        {
            return CatalogCsvReader.Error(CatalogPath, location, message);
        }
    }
}
