using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class MachineCatalog
    {
        internal const string CatalogPath = "Data/Definitions/machines.csv";
        private readonly Dictionary<string, MachineDefinition> definitions;

        internal int Count => definitions.Count;

        private MachineCatalog(Dictionary<string, MachineDefinition> definitions)
        {
            this.definitions = definitions;
        }

        // Called once per directory registration, after the mod is in ModLoader.Mods.
        internal static MachineCatalog Load()
        {
            return Parse(CatalogCsvReader.ReadModFile(CatalogPath));
        }

        internal static MachineCatalog Parse(string csv)
        {
            var definitions = CatalogCsvReader.ReadDefinitions(csv, CatalogPath, "Machine",
                new[] { "id", "powerPerCycle" },
                (entry, id) => new MachineDefinition(id, entry.ReadInt("powerPerCycle")));
            return new MachineCatalog(definitions);
        }

        internal MachineDefinition Get(string itemId)
        {
            if (itemId == null || !definitions.TryGetValue(itemId, out var definition))
            {
                throw InvalidEntry("items", "Missing Machine configuration for '" + itemId + "'.");
            }

            return definition;
        }

        // Every machine, including the implemented serverbox, requires parameters.
        internal MachineDefinition GetForItem(string itemId)
        {
            return Get(itemId);
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var mappedMachineIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item.Kind == "machine")
                {
                    GetForItem(item.ItemId);
                    mappedMachineIds.Add(item.ItemId);
                }
            }

            foreach (string itemId in definitions.Keys)
            {
                if (!mappedMachineIds.Contains(itemId))
                {
                    throw InvalidEntry("items", "Machine '" + itemId + "' must have a kind=machine row in Data/items.csv.");
                }
            }
        }

        private static InvalidOperationException InvalidEntry(string location, string message)
        {
            return CatalogCsvReader.Error(CatalogPath, location, message);
        }
    }
}
