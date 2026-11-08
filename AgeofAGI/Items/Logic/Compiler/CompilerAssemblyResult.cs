using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.Compiler
{
    internal sealed class CompilerAssemblyResult
    {
        internal IReadOnlyList<CompilerPartSnapshot> Parts { get; }
        internal AiModelAttributes Model { get; }
        internal CompilerAssemblyPreview Preview { get; }
        internal string TaskType => Model.TaskType;
        internal float TrainingWork => Model.TrainingWork;
        internal int TrainingData => Model.TrainingData;
        internal int MemoryGb => Model.MemoryGb;
        internal float ComputeBonusPercent => Model.ComputeBonusPercent;
        internal float InitialDurability => Model.InitialDurability;
        internal int LinkedPairs { get; }
        internal int FeedbackPairs { get; }

        internal CompilerAssemblyResult(IReadOnlyList<CompilerPartSnapshot> parts, string taskType, float trainingWork,
            int memoryGb, float computeBonusPercent, float initialDurability, int linkedPairs, int feedbackPairs, int trainingData,
            CompilerAssemblyPreview preview,
            AiModelTraits traits = AiModelTraits.None, int batchSize = 1)
        {
            Parts = parts;
            Model = new AiModelAttributes(taskType, trainingWork, memoryGb, computeBonusPercent, initialDurability, trainingData, traits, batchSize);
            Preview = preview;
            LinkedPairs = linkedPairs;
            FeedbackPairs = feedbackPairs;
        }

        // Output placement can call arbitrary inventory callbacks. Compare both values
        // and actual input instances/transforms again before paying the native recipe.
        internal bool Matches(CompilerAssemblyResult other)
        {
            return other != null && TaskType == other.TaskType && TrainingWork == other.TrainingWork && TrainingData == other.TrainingData &&
                MemoryGb == other.MemoryGb && ComputeBonusPercent == other.ComputeBonusPercent &&
                InitialDurability == other.InitialDurability && Model.Traits == other.Model.Traits && Model.BatchSize == other.Model.BatchSize && Parts.Count == other.Parts.Count &&
                Parts.Zip(other.Parts, (first, second) => first.Matches(second)).All(equal => equal);
        }
    }
}
