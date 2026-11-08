using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.BrainServer;

namespace AgeOfAgi.Items.Catalogs
{
    internal static class BrainServerCatalog
    {
        internal const string CatalogPath = "Data/Definitions/brain_server.csv";
        internal static BrainServerDefinition Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static BrainServerDefinition Parse(string csv)
        {
            var entries = CatalogCsvReader.ReadDefinitionEntries(csv, CatalogPath, "Brain server",
                new[] { "id", "compute", "memoryGb", "waterMlPerDay" },
                (entry, _) => new BrainServerDefinition(entry.ReadFloat("compute"), entry.ReadInt("memoryGb"), entry.ReadInt("waterMlPerDay")));
            if (entries.Count != 1 || entries[0].Key != BrainServerLifeSupport.ItemId)
                throw CatalogCsvReader.Error(CatalogPath, "id", "Exactly one brain_server definition is required.");
            return entries[0].Value;
        }
    }
}
