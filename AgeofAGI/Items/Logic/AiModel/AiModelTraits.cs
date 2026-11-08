using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.AiModel
{
    [Flags]
    internal enum AiModelTraits
    {
        None = 0,
        Residual = 1,
        Batch = 2,
        UShape = 4,
        HeavyEncoder = 8,
        HighRank = 16,
        DeepFeedback = 32,
        HeavyDecoder = 64
    }

    internal static class AiModelTraitText
    {
        private static CompilerRecipeDefinition rules;
        internal static void Configure(CompilerRecipeDefinition definition) => rules = definition;

        internal static IEnumerable<string> Lines(AiModelTraits traits, int batchSize)
        {
            if ((traits & AiModelTraits.Batch) != 0) yield return Text("ai_stat_batch", batchSize);
            foreach (AiModelTraits trait in Enum.GetValues(typeof(AiModelTraits)))
            {
                if (trait == AiModelTraits.None || trait == AiModelTraits.Batch || (traits & trait) == 0) continue;
                string key = trait.ToString().ToLowerInvariant();
                string name = Text("ai_trait_" + key);
                if (rules == null) { yield return name; continue; }
                object[] values;
                switch (trait)
                {
                    case AiModelTraits.Residual: values = new object[] { rules.ResidualTrainingMultiplier, rules.ResidualMemoryMultiplier }; break;
                    case AiModelTraits.UShape: values = new object[] { rules.UComputeMultiplier }; break;
                    case AiModelTraits.HeavyEncoder: values = new object[] { rules.HeavyEncoderComputeMultiplier, rules.HeavyEncoderMemoryMultiplier }; break;
                    case AiModelTraits.HighRank: values = new object[] { rules.HighRankDurabilityMultiplier, rules.HighRankTrainingMultiplier }; break;
                    case AiModelTraits.DeepFeedback: values = new object[] { rules.DeepFeedbackDataMultiplier, rules.DeepFeedbackTrainingMultiplier }; break;
                    case AiModelTraits.HeavyDecoder: values = new object[] { rules.HeavyDecoderComputeMultiplier, rules.HeavyDecoderMemoryMultiplier }; break;
                    default: continue;
                }
                yield return Text("ai_stat_trait", name, Text("ai_effect_" + key, values));
            }
        }

        internal static string Describe(AiModelTraits traits, int batchSize) => string.Join("\n", Lines(traits, batchSize));
        private static string Text(string key, params object[] values) => ModHelper.GetLocalized(ModConstants.ModId, key, values);
    }
}
