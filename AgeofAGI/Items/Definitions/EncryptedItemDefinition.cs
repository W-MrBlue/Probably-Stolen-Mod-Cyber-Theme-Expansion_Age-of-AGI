using System;

namespace AgeOfAgi.Items.Definitions
{
    internal sealed class EncryptedItemDefinition
    {
        internal string InputItemId { get; }
        internal float Work { get; }
        internal string TaskType { get; }
        internal string OutputItemId { get; }

        internal EncryptedItemDefinition(string inputItemId, float work, string taskType, string outputItemId)
        {
            DefinitionValidation.RequireItemId(inputItemId, nameof(inputItemId), "Encrypted item");
            DefinitionValidation.RequireFinitePositive(work, nameof(work), "work");
            DefinitionValidation.RequireItemId(outputItemId, nameof(outputItemId), "Processed item");
            if (!AiTaskTypes.IsValid(taskType)) throw new ArgumentException("Unknown task_type: " + taskType, nameof(taskType));
            if (inputItemId == outputItemId) throw new ArgumentException("Processed item must differ from its source.", nameof(outputItemId));
            InputItemId = inputItemId;
            Work = work;
            TaskType = taskType;
            OutputItemId = outputItemId;
        }
    }
}
