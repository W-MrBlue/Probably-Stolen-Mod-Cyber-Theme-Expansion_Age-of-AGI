using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class ServerCatalog
    {
        internal const string CatalogPath = "Data/Definitions/server.csv";
        internal const string ServerId = "age_of_agi.serverbox";
        internal ServerDefinition Definition { get; }

        private ServerCatalog(ServerDefinition definition)
        {
            Definition = definition;
        }

        internal static ServerCatalog Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static ServerCatalog Parse(string csv)
        {
            var entries = CatalogCsvReader.ReadDefinitionEntries(csv, CatalogPath, "Server",
                new[] { "id", "trainedOutputId", "coolingMultiplier", "cpuUpgradeUnitsStep1", "cpuUpgradeUnitsStep2", "gpuUpgradeUnitsPerStep",
                    "intelligenceWork", "bioBaseWork", "bioComplexityWork", "dataUnitsPer8Gb", "dataWorkPerUnit", "memoryUpgradeUnits" },
                (entry, id) => new ServerDefinition(id, entry.ReadItemId("trainedOutputId"), entry.ReadFloat("coolingMultiplier"),
                    entry.ReadInt("cpuUpgradeUnitsStep1"), entry.ReadInt("cpuUpgradeUnitsStep2"), entry.ReadInt("gpuUpgradeUnitsPerStep"),
                    entry.ReadFloat("intelligenceWork"),
                    entry.ReadFloat("bioBaseWork"), entry.ReadFloat("bioComplexityWork"),
                    entry.ReadInt("dataUnitsPer8Gb"), entry.ReadFloat("dataWorkPerUnit"),
                    entry.ReadInt("memoryUpgradeUnits")));
            if (entries.Count != 1 || entries[0].Key != ServerId || entries[0].Value.TrainedOutputId != "age_of_agi.ai_trained")
            {
                throw CatalogCsvReader.Error(CatalogPath, "items", "Exactly one serverbox definition producing age_of_agi.ai_trained is required.");
            }
            return new ServerCatalog(entries[0].Value);
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            if (!items.Any(item => item.ItemId == ServerId && item.Kind == "machine") ||
                !items.Any(item => item.ItemId == Definition.TrainedOutputId && item.Kind == "ai_model") ||
                !items.Any(item => item.ItemId == UntrainedModelId && item.Kind == "ai_model"))
            {
                throw CatalogCsvReader.Error(CatalogPath, "items", "Server requires a machine and trained/untrained AI CSV rows.");
            }
            foreach (string id in new[] { "folder_empty", "folder_security" })
            {
                if (!items.Any(item => item.ItemId == ModConstants.ModId + "." + id && item.Kind == "data_item"))
                    throw CatalogCsvReader.Error(CatalogPath, "items", "Server production requires data_item CSV row: " + id);
            }
            if (!items.Any(item => item.ItemId == ModConstants.ModId + ".complex_data" && item.Kind == "complex_data"))
                throw CatalogCsvReader.Error(CatalogPath, "items", "Server production requires the complex_data CSV row.");
        }

        private const string UntrainedModelId = "age_of_agi.ai_untrained";
    }
}
