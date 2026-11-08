using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class AiPartCatalog
    {
        internal const string CatalogPath = "Data/Definitions/ai_parts.csv";
        private readonly Dictionary<string, AiPartDefinition> definitions;
        internal int Count => definitions.Count;
        private AiPartCatalog(Dictionary<string, AiPartDefinition> definitions)
        {
            this.definitions = definitions;
        }

        internal static AiPartCatalog Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static AiPartCatalog Parse(string csv)
        {
            return new AiPartCatalog(CatalogCsvReader.ReadDefinitions(csv, CatalogPath, "AI part",
                new[] { "id", "role", "computeBonusPercent", "memoryGb", "trainingWork", "trainingData", "taskType" },
                (entry, id) => new AiPartDefinition(id, entry.ReadString("role"), entry.ReadFloat("computeBonusPercent"),
                    entry.ReadInt("memoryGb"), entry.ReadFloat("trainingWork"), entry.ReadInt("trainingData"), entry.ReadString("taskType"))));
        }
        internal bool TryGet(string id, out AiPartDefinition definition) => definitions.TryGetValue(id, out definition);

        internal AiPartDefinition Get(string id)
        {
            if (!TryGet(id, out var definition))
                throw CatalogCsvReader.Error(CatalogPath, "items", "Missing AI part configuration for '" + id + "'.");
            return definition;
        }
        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var ids = new HashSet<string>(items.Where(item => item.Kind == "ai_parts").Select(item => item.ItemId), StringComparer.Ordinal);
            foreach (string id in ids) Get(id);
            foreach (string id in definitions.Keys)
            {
                if (!ids.Contains(id))
                    throw CatalogCsvReader.Error(CatalogPath, "items", "Expected kind=ai_parts CSV row for " + id);
            }
        }
    }
}
