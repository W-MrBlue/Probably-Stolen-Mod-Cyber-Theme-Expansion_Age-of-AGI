using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class DiskWriterCatalog
    {
        internal const string CatalogPath = "Data/Definitions/disk_writer.csv";
        internal const string DiskWriterId = "age_of_agi.disk_writer";
        internal const string BlankDiskId = "age_of_agi.disk_empty";
        internal const string DecoderChipId = "age_of_agi.part_dec_chip";
        internal const string EncoderChipId = "age_of_agi.part_enc_chip";
        private readonly Dictionary<string, DiskWriterRecipeDefinition> recipes;

        internal IReadOnlyList<DiskWriterRecipeDefinition> Recipes { get; }
        internal int Count => Recipes.Count;

        private DiskWriterCatalog(List<KeyValuePair<string, DiskWriterRecipeDefinition>> definitions)
        {
            recipes = definitions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Recipes = definitions.Select(pair => pair.Value).ToList().AsReadOnly();
        }

        internal static DiskWriterCatalog Load()
        {
            return Parse(CatalogCsvReader.ReadModFile(CatalogPath));
        }

        internal static DiskWriterCatalog Parse(string csv)
        {
            if (csv == null)
            {
                throw Invalid("CSV text must not be null.");
            }

            var definitions = CatalogCsvReader.ReadDefinitionEntries(csv, CatalogPath, "disk writer recipe",
                new[] { "id", "decoderItemId", "encoderItemId" },
                (entry, id) => new DiskWriterRecipeDefinition(id,
                    entry.ReadItemId("decoderItemId"), entry.ReadItemId("encoderItemId")));
            return new DiskWriterCatalog(definitions);
        }

        internal static bool IsChip(string itemId)
        {
            return itemId == DecoderChipId || itemId == EncoderChipId;
        }

        internal bool TryGetResult(string chipId, string recipeId, out string resultId)
        {
            resultId = null;
            if (!IsChip(chipId) || recipeId == null || !recipes.TryGetValue(recipeId, out var definition))
            {
                return false;
            }
            resultId = chipId == DecoderChipId ? definition.DecoderItemId : definition.EncoderItemId;
            return true;
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var byId = items.ToDictionary(item => item.ItemId, StringComparer.Ordinal);
            RequireKind(byId, DiskWriterId, "machine");
            if (!byId.ContainsKey(BlankDiskId))
            {
                throw Invalid("Missing blank disk mapping: " + BlankDiskId);
            }
            RequireKind(byId, DecoderChipId, "ai_parts_chip");
            RequireKind(byId, EncoderChipId, "ai_parts_chip");
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in Recipes)
            {
                RequireKind(byId, definition.DecoderItemId, "ai_parts");
                RequireKind(byId, definition.EncoderItemId, "ai_parts");
                if (!definition.DecoderItemId.StartsWith(ModConstants.ModId + ".part_dec_", StringComparison.Ordinal) ||
                    !definition.EncoderItemId.StartsWith(ModConstants.ModId + ".part_enc_", StringComparison.Ordinal))
                {
                    throw Invalid("Recipe outputs must be decoder/encoder code-component IDs: " + definition.RecipeId);
                }
                if (!outputs.Add(definition.DecoderItemId) || !outputs.Add(definition.EncoderItemId))
                    throw Invalid("Recipe outputs must be unique: " + definition.RecipeId);
            }
            foreach (ItemDefinition item in items.Where(item => item.Kind == "ai_parts" &&
                (item.ItemId.StartsWith(ModConstants.ModId + ".part_dec_", StringComparison.Ordinal) ||
                 item.ItemId.StartsWith(ModConstants.ModId + ".part_enc_", StringComparison.Ordinal))))
            {
                if (!outputs.Contains(item.ItemId)) throw Invalid("Missing code-component recipe: " + item.ItemId);
            }
        }

        private static void RequireKind(Dictionary<string, ItemDefinition> items, string id, string kind)
        {
            if (!items.TryGetValue(id, out var item) || item.Kind != kind)
            {
                throw Invalid("Expected kind=" + kind + " CSV row for " + id);
            }
        }

        private static InvalidOperationException Invalid(string message)
        {
            return CatalogCsvReader.Error(CatalogPath, "", message);
        }
    }
}
