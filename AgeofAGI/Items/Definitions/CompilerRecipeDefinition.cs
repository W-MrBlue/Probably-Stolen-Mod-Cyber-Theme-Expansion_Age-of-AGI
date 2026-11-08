using System;

namespace AgeOfAgi.Items.Definitions
{
    // A single assembly rule. Electricity belongs to the machine.
    internal sealed class CompilerRecipeDefinition
    {
        internal string RecipeId { get; }
        internal string OutputItemId { get; }
        internal float InitialDurability { get; }
        internal float DuplicateDurabilityRetention { get; }
        internal float LinkComputeMultiplier { get; }
        internal float FeedbackTrainingRetention { get; }
        internal float RegularizationMultiplier { get; }
        internal double ResidualTrainingMultiplier { get; }
        internal double ResidualMemoryMultiplier { get; }
        internal double BatchTrainingMultiplier { get; }
        internal double UComputeMultiplier { get; }
        internal double HeavyEncoderComputeMultiplier { get; }
        internal double HeavyEncoderMemoryMultiplier { get; }
        internal double HighRankDurabilityMultiplier { get; }
        internal double HighRankTrainingMultiplier { get; }
        internal double DeepFeedbackDataMultiplier { get; }
        internal double DeepFeedbackTrainingMultiplier { get; }
        internal double HeavyDecoderComputeMultiplier { get; }
        internal double HeavyDecoderMemoryMultiplier { get; }

        internal CompilerRecipeDefinition(string recipeId, string outputItemId, float initialDurability,
            float duplicateDurabilityRetention, float linkComputeMultiplier, float feedbackTrainingRetention, float regularizationMultiplier,
            double residualTrainingMultiplier,
            double residualMemoryMultiplier,
            double batchTrainingMultiplier,
            double uComputeMultiplier,
            double heavyEncoderComputeMultiplier,
            double heavyEncoderMemoryMultiplier,
            double highRankDurabilityMultiplier,
            double highRankTrainingMultiplier,
            double deepFeedbackDataMultiplier,
            double deepFeedbackTrainingMultiplier,
            double heavyDecoderComputeMultiplier,
            double heavyDecoderMemoryMultiplier)
        {
            if (string.IsNullOrEmpty(recipeId))
            {
                throw new ArgumentException("Compiler recipe ID must not be empty.", nameof(recipeId));
            }
            foreach (char character in recipeId)
            {
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') || character == '_' || character == '-'))
                    throw new ArgumentException("Invalid compiler recipe ID: " + recipeId, nameof(recipeId));
            }
            DefinitionValidation.RequireItemId(outputItemId, nameof(outputItemId), "Compiler recipe output");
            DefinitionValidation.RequireFinitePositive(initialDurability, nameof(initialDurability), "initialDurability");
            DefinitionValidation.RequireFinitePositive(duplicateDurabilityRetention, nameof(duplicateDurabilityRetention), "duplicateDurabilityRetention");
            DefinitionValidation.RequireFinitePositive(linkComputeMultiplier, nameof(linkComputeMultiplier), "linkComputeMultiplier");
            DefinitionValidation.RequireFinitePositive(feedbackTrainingRetention, nameof(feedbackTrainingRetention), "feedbackTrainingRetention");
            DefinitionValidation.RequireFinitePositive(regularizationMultiplier, nameof(regularizationMultiplier), "regularizationMultiplier");
            if (regularizationMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(regularizationMultiplier));
            if (duplicateDurabilityRetention > 1 || feedbackTrainingRetention > 1 || linkComputeMultiplier < 1)
                throw new ArgumentOutOfRangeException("Compiler retention must be at most 1; link multiplier at least 1.");
            RecipeId = recipeId;
            OutputItemId = outputItemId;
            InitialDurability = initialDurability;
            DuplicateDurabilityRetention = duplicateDurabilityRetention;
            LinkComputeMultiplier = linkComputeMultiplier;
            FeedbackTrainingRetention = feedbackTrainingRetention;
            RegularizationMultiplier = regularizationMultiplier;
            DefinitionValidation.RequireFinitePositive(residualTrainingMultiplier, nameof(residualTrainingMultiplier), "residualTrainingMultiplier");
            ResidualTrainingMultiplier = residualTrainingMultiplier;
            DefinitionValidation.RequireFinitePositive(residualMemoryMultiplier, nameof(residualMemoryMultiplier), "residualMemoryMultiplier");
            ResidualMemoryMultiplier = residualMemoryMultiplier;
            DefinitionValidation.RequireFinitePositive(batchTrainingMultiplier, nameof(batchTrainingMultiplier), "batchTrainingMultiplier");
            BatchTrainingMultiplier = batchTrainingMultiplier;
            DefinitionValidation.RequireFinitePositive(uComputeMultiplier, nameof(uComputeMultiplier), "uComputeMultiplier");
            UComputeMultiplier = uComputeMultiplier;
            DefinitionValidation.RequireFinitePositive(heavyEncoderComputeMultiplier, nameof(heavyEncoderComputeMultiplier), "heavyEncoderComputeMultiplier");
            HeavyEncoderComputeMultiplier = heavyEncoderComputeMultiplier;
            DefinitionValidation.RequireFinitePositive(heavyEncoderMemoryMultiplier, nameof(heavyEncoderMemoryMultiplier), "heavyEncoderMemoryMultiplier");
            HeavyEncoderMemoryMultiplier = heavyEncoderMemoryMultiplier;
            DefinitionValidation.RequireFinitePositive(highRankDurabilityMultiplier, nameof(highRankDurabilityMultiplier), "highRankDurabilityMultiplier");
            HighRankDurabilityMultiplier = highRankDurabilityMultiplier;
            DefinitionValidation.RequireFinitePositive(highRankTrainingMultiplier, nameof(highRankTrainingMultiplier), "highRankTrainingMultiplier");
            HighRankTrainingMultiplier = highRankTrainingMultiplier;
            DefinitionValidation.RequireFinitePositive(deepFeedbackDataMultiplier, nameof(deepFeedbackDataMultiplier), "deepFeedbackDataMultiplier");
            DeepFeedbackDataMultiplier = deepFeedbackDataMultiplier;
            DefinitionValidation.RequireFinitePositive(deepFeedbackTrainingMultiplier, nameof(deepFeedbackTrainingMultiplier), "deepFeedbackTrainingMultiplier");
            DeepFeedbackTrainingMultiplier = deepFeedbackTrainingMultiplier;
            DefinitionValidation.RequireFinitePositive(heavyDecoderComputeMultiplier, nameof(heavyDecoderComputeMultiplier), "heavyDecoderComputeMultiplier");
            HeavyDecoderComputeMultiplier = heavyDecoderComputeMultiplier;
            DefinitionValidation.RequireFinitePositive(heavyDecoderMemoryMultiplier, nameof(heavyDecoderMemoryMultiplier), "heavyDecoderMemoryMultiplier");
            HeavyDecoderMemoryMultiplier = heavyDecoderMemoryMultiplier;
        }
    }
}
