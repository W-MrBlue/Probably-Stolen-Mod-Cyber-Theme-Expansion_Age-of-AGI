using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class CompilerCatalog
    {
        internal const string CatalogPath = "Data/Definitions/compiler.csv";
        internal const string CompilerId = "age_of_agi.compiler";
        internal const string LinkId = "age_of_agi.part_link";
        internal const string FeedbackId = "age_of_agi.part_feedback";
        internal CompilerRecipeDefinition Recipe { get; }
        private CompilerCatalog(CompilerRecipeDefinition recipe)
        {
            Recipe = recipe;
        }

        internal static CompilerCatalog Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static CompilerCatalog Parse(string csv)
        {
            var recipes = CatalogCsvReader.ReadDefinitionEntries(csv, CatalogPath, "compiler recipe",
                new[] { "id", "output", "initialDurability", "duplicateDurabilityRetention", "linkComputeMultiplier", "feedbackTrainingRetention", "regularizationMultiplier",
                    "residualTrainingMultiplier", "residualMemoryMultiplier", "batchTrainingMultiplier", "uComputeMultiplier", "heavyEncoderComputeMultiplier", "heavyEncoderMemoryMultiplier", "highRankDurabilityMultiplier", "highRankTrainingMultiplier", "deepFeedbackDataMultiplier", "deepFeedbackTrainingMultiplier", "heavyDecoderComputeMultiplier", "heavyDecoderMemoryMultiplier" },
                (entry, id) => new CompilerRecipeDefinition(id, entry.ReadItemId("output"), entry.ReadFloat("initialDurability"),
                    entry.ReadFloat("duplicateDurabilityRetention"), entry.ReadFloat("linkComputeMultiplier"), entry.ReadFloat("feedbackTrainingRetention"), entry.ReadFloat("regularizationMultiplier"),
                    entry.ReadDouble("residualTrainingMultiplier"), entry.ReadDouble("residualMemoryMultiplier"), entry.ReadDouble("batchTrainingMultiplier"),
                    entry.ReadDouble("uComputeMultiplier"), entry.ReadDouble("heavyEncoderComputeMultiplier"), entry.ReadDouble("heavyEncoderMemoryMultiplier"),
                    entry.ReadDouble("highRankDurabilityMultiplier"), entry.ReadDouble("highRankTrainingMultiplier"), entry.ReadDouble("deepFeedbackDataMultiplier"),
                    entry.ReadDouble("deepFeedbackTrainingMultiplier"), entry.ReadDouble("heavyDecoderComputeMultiplier"), entry.ReadDouble("heavyDecoderMemoryMultiplier")));
            if (recipes.Count != 1)
                throw CatalogCsvReader.Error(CatalogPath, "recipes", "Exactly one whole-grid assembly recipe is required.");
            return new CompilerCatalog(recipes[0].Value);
        }
        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            if (!items.Any(item => item.ItemId == CompilerId && item.Kind == "machine") ||
                !items.Any(item => item.Kind == "base_model") ||
                !items.Any(item => item.ItemId == Recipe.OutputItemId && item.Kind == "ai_model"))
                throw CatalogCsvReader.Error(CatalogPath, "recipes", "Compiler requires a machine, a base model and an ai_model output CSV row.");
        }
    }
}
