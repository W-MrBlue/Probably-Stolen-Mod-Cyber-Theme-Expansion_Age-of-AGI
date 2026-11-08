using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.AiModel;

namespace AgeOfAgi.Items.Logic.Compiler
{
    internal sealed class CompilerPair
    {
        internal CompilerPartSnapshot Encoder { get; }
        internal CompilerPartSnapshot Decoder { get; }
        internal CompilerPair(CompilerPartSnapshot encoder, CompilerPartSnapshot decoder)
        {
            Encoder = encoder;
            Decoder = decoder;
        }
    }

    internal sealed class CompilerArchitecture
    {
        internal AiModelTraits Traits;
        internal int BatchSize = 1;
        internal double ComputeMultiplier = 1, TrainingMultiplier = 1, DurabilityMultiplier = 1;
        internal decimal MemoryMultiplier = 1, DataMultiplier = 1;

        internal static CompilerArchitecture Calculate(List<CompilerPartSnapshot> parts, CompilerPartSnapshot[] encoders,
            CompilerPartSnapshot[] decoders, List<CompilerPair> linked, List<CompilerPair> feedback,
            bool regularized, CompilerRecipeDefinition rules)
        {
            var result = new CompilerArchitecture();
            bool Adjacent(CompilerPartSnapshot first, CompilerPartSnapshot second) =>
                CompilerAssemblyCalculator.Adjacent(first.Shape, second.Shape);
            if (parts.Where(part => part.Definition.Role == "residual").Any(bridge =>
                linked.Any(pair => CompilerResidualBridge.Connects(bridge.Shape, pair.Encoder.Shape, pair.Decoder.Shape))))
            {
                result.Traits |= AiModelTraits.Residual;
                result.TrainingMultiplier *= rules.ResidualTrainingMultiplier;
                result.MemoryMultiplier *= (decimal)rules.ResidualMemoryMultiplier;
            }
            var unvisited = new HashSet<CompilerPartSnapshot>(parts.Where(part => part.Definition.Role == "batch"));
            while (unvisited.Count > 0)
            {
                var pending = new Queue<CompilerPartSnapshot>();
                var groupEncoders = new HashSet<CompilerPartSnapshot>();
                var first = unvisited.First(); unvisited.Remove(first); pending.Enqueue(first);
                while (pending.Count > 0)
                {
                    var processor = pending.Dequeue();
                    foreach (var encoder in encoders.Where(encoder => Adjacent(processor, encoder))) groupEncoders.Add(encoder);
                    foreach (var neighbor in unvisited.Where(other => Adjacent(processor, other)).ToArray())
                    {
                        unvisited.Remove(neighbor);
                        pending.Enqueue(neighbor);
                    }
                }
                result.BatchSize = Math.Max(result.BatchSize, groupEncoders.Count);
            }
            if (result.BatchSize >= 2)
            {
                result.Traits |= AiModelTraits.Batch;
                result.TrainingMultiplier *= rules.BatchTrainingMultiplier;
            }
            if (encoders.Length >= 2 && encoders.Length == decoders.Length && linked.Count == encoders.Length)
            {
                result.Traits |= AiModelTraits.UShape;
                result.ComputeMultiplier *= rules.UComputeMultiplier;
            }
            if (encoders.Length > decoders.Length + 2 && regularized)
            {
                result.Traits |= AiModelTraits.HeavyEncoder;
                result.ComputeMultiplier *= rules.HeavyEncoderComputeMultiplier;
                result.MemoryMultiplier *= (decimal)rules.HeavyEncoderMemoryMultiplier;
            }
            if (encoders.Length > 2 && decoders.Length > 2 &&
                encoders.Select(part => part.Item.identifier).Distinct().Count() == encoders.Length &&
                decoders.Select(part => part.Item.identifier).Distinct().Count() == decoders.Length)
            {
                result.Traits |= AiModelTraits.HighRank;
                result.DurabilityMultiplier *= rules.HighRankDurabilityMultiplier;
                result.TrainingMultiplier *= rules.HighRankTrainingMultiplier;
            }
            // Deep feedback covers every encoder; unlike U-shape, spare decoders are allowed.
            if (encoders.Length >= 2 && decoders.Length >= 2 && feedback.Count == encoders.Length)
            {
                result.Traits |= AiModelTraits.DeepFeedback;
                result.DataMultiplier *= (decimal)rules.DeepFeedbackDataMultiplier;
                result.TrainingMultiplier *= rules.DeepFeedbackTrainingMultiplier;
            }
            if (decoders.Length > encoders.Length + 2 && regularized)
            {
                result.Traits |= AiModelTraits.HeavyDecoder;
                result.ComputeMultiplier *= rules.HeavyDecoderComputeMultiplier;
                result.MemoryMultiplier *= (decimal)rules.HeavyDecoderMemoryMultiplier;
            }
            return result;
        }
    }
}
