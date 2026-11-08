using AgeOfAgi.Items.Logic.AiModel;

namespace AgeOfAgi.Items.Logic.Compiler
{
    // Display values can fall outside model limits before hidden architecture
    // effects are applied, so they must not impose extra compilation constraints.
    internal sealed class CompilerAssemblyPreview
    {
        internal double TrainingWork { get; }
        internal long TrainingData { get; }
        internal decimal MemoryGb { get; }
        internal double ComputeBonusPercent { get; }
        internal float InitialDurability { get; }
        internal AiModelTraits Traits { get; }
        internal int BatchSize { get; }

        internal CompilerAssemblyPreview(double trainingWork, decimal memoryGb, double computeBonusPercent,
            float initialDurability, long trainingData, AiModelTraits traits, int batchSize)
        {
            TrainingWork = trainingWork;
            MemoryGb = memoryGb;
            ComputeBonusPercent = computeBonusPercent;
            InitialDurability = initialDurability;
            TrainingData = trainingData;
            Traits = traits;
            BatchSize = batchSize;
        }
    }
}
