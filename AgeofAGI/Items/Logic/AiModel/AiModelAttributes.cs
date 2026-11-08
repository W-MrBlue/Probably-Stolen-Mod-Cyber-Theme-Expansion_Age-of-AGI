namespace AgeOfAgi.Items.Logic.AiModel
{
    // Frozen model properties shared by compiled outputs, suppliers and debug creation.
    // Component identities and compiler grid geometry stay with the compiler transaction.
    internal sealed class AiModelAttributes
    {
        internal string TaskType { get; }
        internal float TrainingWork { get; }
        internal int TrainingData { get; }
        internal int MemoryGb { get; }
        internal float ComputeBonusPercent { get; }
        internal float InitialDurability { get; }
        internal AiModelTraits Traits { get; }
        internal int BatchSize { get; }

        internal AiModelAttributes(string taskType, float trainingWork, int memoryGb,
            float computeBonusPercent, float initialDurability, int trainingData,
            AiModelTraits traits = AiModelTraits.None, int batchSize = 1)
        {
            TaskType = taskType;
            TrainingWork = trainingWork;
            TrainingData = trainingData;
            MemoryGb = memoryGb;
            ComputeBonusPercent = computeBonusPercent;
            InitialDurability = initialDurability;
            Traits = traits;
            BatchSize = batchSize;
        }
    }
}
