using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class GpuCatalog
    {
        internal const string CatalogPath = "Data/Definitions/gpu.csv";
        private readonly Dictionary<string, GpuDefinition> definitions;
        private readonly Dictionary<string, GpuDefinition> cores = new Dictionary<string, GpuDefinition>(StringComparer.Ordinal);

        internal int Count => definitions.Count;

        private GpuCatalog(Dictionary<string, GpuDefinition> definitions)
        {
            this.definitions = definitions;
            foreach (GpuDefinition definition in definitions.Values)
                if (!cores.TryAdd(definition.CoreSprite, definition))
                    throw InvalidEntry("cores", "Duplicate GPU coreSprite: " + definition.CoreSprite);
        }

        // Called once per directory registration, after the mod is in ModLoader.Mods.
        internal static GpuCatalog Load()
        {
            return Parse(CatalogCsvReader.ReadModFile(CatalogPath));
        }

        internal static GpuCatalog Parse(string csv)
        {
            var definitions = CatalogCsvReader.ReadDefinitions(csv, CatalogPath, "GPU",
                new[] { "id", "computePower", "powerPerCycle", "coreSprite" },
                (entry, id) => new GpuDefinition(id, entry.ReadInt("computePower"), entry.ReadInt("powerPerCycle"), entry.ReadString("coreSprite")));
            return new GpuCatalog(definitions);
        }

        internal GpuDefinition Get(string itemId)
        {
            if (itemId == null || !definitions.TryGetValue(itemId, out var definition))
            {
                throw InvalidEntry("items", "Missing GPU configuration for '" + itemId + "'.");
            }

            return definition;
        }

        internal GpuDefinition GetByCore(string coreSprite)
        {
            if (coreSprite == null || !cores.TryGetValue(coreSprite, out var definition))
                throw InvalidEntry("cores", "Missing GPU configuration for core '" + coreSprite + "'.");
            return definition;
        }

        internal bool TryGetByCore(string coreSprite, out GpuDefinition definition) => cores.TryGetValue(coreSprite, out definition);

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var mappedGpuIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item.Kind == "gpu")
                {
                    Get(item.ItemId);
                    mappedGpuIds.Add(item.ItemId);
                }
            }

            foreach (string itemId in definitions.Keys)
            {
                if (!mappedGpuIds.Contains(itemId))
                {
                    throw InvalidEntry("items", "GPU '" + itemId + "' must have a kind=gpu row in Data/items.csv.");
                }
            }
        }

        private static InvalidOperationException InvalidEntry(string location, string message)
        {
            return CatalogCsvReader.Error(CatalogPath, location, message);
        }
    }
}
