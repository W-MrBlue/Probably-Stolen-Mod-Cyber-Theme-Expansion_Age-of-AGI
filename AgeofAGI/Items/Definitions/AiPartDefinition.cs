using System;
using System.Globalization;

namespace AgeOfAgi.Items.Definitions
{
    internal sealed class AiPartDefinition
    {
        internal string ItemId { get; }
        internal string Role { get; }
        internal int ShapeId { get; }
        internal float ComputeBonusPercent { get; }
        internal int MemoryGb { get; }
        internal float TrainingWork { get; }
        internal int TrainingData { get; }
        internal string TaskType { get; }
        internal bool IsBasis => Role == "encoder" || Role == "decoder";

        internal AiPartDefinition(string itemId, string role, float computeBonusPercent, int memoryGb,
            float trainingWork, int trainingData, string taskType)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "AI part");
            if (role != "encoder" && role != "decoder" && role != "link" && role != "feedback" && role != "task_head" && role != "regularization" &&
                role != "residual" && role != "batch")
            {
                throw new ArgumentException("Unknown AI part role: " + role, nameof(role));
            }
            ItemId = itemId;
            Role = role;
            if (IsBasis)
            {
                string prefix = ModConstants.ModId + (role == "encoder" ? ".part_enc_" : ".part_dec_");
                if (!itemId.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(itemId.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int shapeId))
                {
                    throw new ArgumentException("Encoder/decoder IDs require a numeric shape suffix.", nameof(itemId));
                }
                ShapeId = shapeId;
                DefinitionValidation.RequireFinitePositive(computeBonusPercent, nameof(computeBonusPercent), "computeBonusPercent");
                DefinitionValidation.RequirePositive(memoryGb, nameof(memoryGb), "memoryGb");
                DefinitionValidation.RequireFinitePositive(trainingWork, nameof(trainingWork), "trainingWork");
                DefinitionValidation.RequirePositive(trainingData, nameof(trainingData), "trainingData");
            }
            else if (computeBonusPercent != 0 || memoryGb != 0 || trainingWork != 0 || trainingData != 0)
            {
                throw new ArgumentException("Functional parts must have zero base compute, memory, trainingWork and trainingData.");
            }
            if (role == "task_head" ? !AiTaskTypes.IsValid(taskType) : taskType != "")
            {
                throw new ArgumentException("taskType must be supported for a head, or empty for other parts.", nameof(taskType));
            }
            ComputeBonusPercent = computeBonusPercent;
            MemoryGb = memoryGb;
            TrainingWork = trainingWork;
            TrainingData = trainingData;
            TaskType = taskType;
        }
    }
}
