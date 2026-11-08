using AgeOfAgi.Items.Catalogs;
using System;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.AiModel
{
    internal static class AiModelState
    {
        private static AiValueCatalog prices;
        internal static void ConfigureValues(AiValueCatalog values) => prices = values;
        internal const string ModelTag = "AGE_OF_AGI_AI_MODEL";
        internal const string TraitsTag = "AGE_OF_AGI_AI_TRAITS";
        internal const string BatchSizeTag = "AGE_OF_AGI_AI_BATCH_SIZE";

        internal static AiModelTraits GetTraits(GameItem item) => (AiModelTraits)item.GetTagReadonly(TraitsTag).GetInt();
        internal static int GetBatchSize(GameItem item) => (GetTraits(item) & AiModelTraits.Batch) != 0
            ? Math.Max(1, item.GetTagReadonly(BatchSizeTag).GetInt()) : 1;

        private static void SetTraits(GameItem item, AiModelTraits traits, int batchSize)
        {
            if (!ValidTraits(traits, batchSize)) throw new ArgumentException("Invalid model traits or batch size.");
            item.ModifyTag(TraitsTag, tag => tag.Enable().SetInt((int)traits));
            item.ModifyTag(BatchSizeTag, tag => tag.Enable().SetInt(batchSize));
        }

        internal static bool ValidTraits(AiModelTraits traits, int batchSize) =>
            ((int)traits & ~127) == 0 && ((traits & AiModelTraits.Batch) != 0 ? batchSize >= 2 : batchSize == 1);

        private static bool ValidTraits(GameItem item) => ValidTraits(GetTraits(item), GetBatchSize(item));
        internal const string VersionTag = "AGE_OF_AGI_AI_ATTRIBUTES_VERSION";
        internal const string TaskTag = "AGE_OF_AGI_AI_TASK_TYPE";
        internal const string TrainingTag = "AGE_OF_AGI_AI_TRAINING_WORK";
        internal const string TrainingDataTag = "AGE_OF_AGI_AI_TRAINING_DATA";
        internal const string MemoryTag = "AGE_OF_AGI_AI_MEMORY_GB";
        internal const string PredictedComputeTag = "AGE_OF_AGI_AI_PREDICTED_COMPUTE_BONUS_PERCENT";
        internal const string PredictedDurabilityTag = "AGE_OF_AGI_AI_PREDICTED_INITIAL_DURABILITY";
        internal const string ComputeTag = "AGE_OF_AGI_AI_COMPUTE_BONUS_PERCENT";
        internal const string InitialDurabilityTag = "AGE_OF_AGI_AI_INITIAL_DURABILITY";
        internal const string CurrentDurabilityTag = "AGE_OF_AGI_AI_CURRENT_DURABILITY";
        internal const string UntrainedId = "age_of_agi.ai_untrained";
        internal const string TrainedId = "age_of_agi.ai_trained";

        internal static void Apply(GameItem item, AiModelAttributes result)
        {
            item.EnableTag(ModelTag);
            SetTraits(item, result.Traits, result.BatchSize);
            item.ModifyTag(TaskTag, state => state.Enable().SetString(result.TaskType));
            item.ModifyTag(TrainingTag, state => state.Enable().SetFloat(result.TrainingWork));
            item.ModifyTag(TrainingDataTag, state => state.Enable().SetInt(result.TrainingData));
            item.ModifyTag(MemoryTag, state => state.Enable().SetInt(result.MemoryGb));
            item.ModifyTag(PredictedComputeTag, state => state.Enable().SetFloat(result.ComputeBonusPercent));
            item.ModifyTag(PredictedDurabilityTag, state => state.Enable().SetFloat(RoundDurability(result.InitialDurability)));
            item.ModifyTag(VersionTag, state => state.Enable().SetInt(2));
            RefreshName(item);
            RefreshValue(item, prices);
            item.Validate();
        }

        internal static bool HasAttributes(GameItem item) => item != null && item.IsTag(ModelTag) && item.IsTag(VersionTag);
        internal static bool IsUntrained(GameItem item) => item != null && item.identifier == UntrainedId && item.IsTag(ModelTag);
        internal static bool IsModel(GameItem item) => item != null && item.IsTag(ModelTag) &&
            (item.identifier == UntrainedId || item.identifier == TrainedId);
        internal static bool CanTrain(GameItem item)
        {
            return IsUntrained(item) && HasAttributes(item) && ValidTraits(item) && item.unitCount == 1 &&
                item.IsTag(TaskTag) && AiTaskTypes.IsValid(GetTaskType(item)) && item.IsTag(TrainingTag) &&
                Positive(GetTrainingWork(item)) && item.IsTag(MemoryTag) && GetMemoryGb(item) > 0 &&
                item.IsTag(PredictedComputeTag) && Positive(GetPredictedComputeBonus(item)) &&
                item.IsTag(PredictedDurabilityTag) && Positive(GetPredictedDurability(item)) &&
                UsesIndependentTrainingData(item) && item.IsTag(TrainingDataTag) && GetTrainingData(item) > 0;
        }

        private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);

        internal static void ApplyTrained(GameItem item, string taskType, int memoryGb, float computeBonus, float durability,
            AiModelTraits traits = AiModelTraits.None, int batchSize = 1)
        {
            durability = RoundDurability(durability);
            SetTraits(item, traits, batchSize);
            item.EnableTag(ModelTag);
            item.ModifyTag(TaskTag, state => state.Enable().SetString(taskType));
            item.ModifyTag(MemoryTag, state => state.Enable().SetInt(memoryGb));
            item.ModifyTag(ComputeTag, state => state.Enable().SetFloat(computeBonus));
            item.ModifyTag(InitialDurabilityTag, state => state.Enable().SetFloat(durability));
            item.ModifyTag(CurrentDurabilityTag, state => state.Enable().SetFloat(durability));
            item.ModifyTag(VersionTag, state => state.Enable().SetInt(1));
            RefreshName(item);
            RefreshValue(item, prices);
            item.Validate();
        }

        internal static float GetComputeBonus(GameItem item) => item.GetTagReadonly(ComputeTag).GetFloat();
        internal static float RoundDurability(double value) => (float)Math.Round(value, MidpointRounding.AwayFromZero);
        internal static bool IsTrained(GameItem item) => IsModel(item) && item.identifier == TrainedId;
        internal static bool CanProduce(GameItem item) => IsTrained(item) && HasAttributes(item) && ValidTraits(item) && item.unitCount == 1 &&
            AiTaskTypes.IsValid(GetTaskType(item)) && GetMemoryGb(item) > 0 && Positive(GetComputeBonus(item)) &&
            Positive(GetInitialDurability(item)) && GetCurrentDurability(item) >= 1 &&
            !float.IsInfinity(GetCurrentDurability(item)) && GetCurrentDurability(item) <= GetInitialDurability(item);
        internal static void SpendProductionUse(GameItem item)
        {
            float remaining = Math.Max(0, RoundDurability(GetCurrentDurability(item)) - 1);
            item.ModifyTag(CurrentDurabilityTag, state => state.Enable().SetFloat(remaining));
            if (remaining <= 0) item.Destroy();
            else { RefreshValue(item, prices); item.Validate(); }
        }

        internal static void RefreshValue(GameItem item, AiValueCatalog values)
        {
            if (!HasAttributes(item) || values == null) return;
            bool trained = IsTrained(item);
            long value = trained ? (CanProduce(item) ? values.Value(GetTaskType(item), GetComputeBonus(item), GetCurrentDurability(item), true) : 0) :
                (CanTrain(item) ? values.Value(GetTaskType(item), GetPredictedComputeBonus(item), GetPredictedDurability(item), false) : 0);
            // Preserve saved unitValue; the owned derived adjustment also permits depreciation below the shell price.
            item.lateUnitValue = Math.Max(int.MinValue, Math.Min(int.MaxValue, value - item.unitValue));
        }
        internal static float GetComputeMultiplier(GameItem item) => 1 + GetComputeBonus(item) / 100;
        internal static float GetInitialDurability(GameItem item) => item.GetTagReadonly(InitialDurabilityTag).GetFloat();
        internal static float GetCurrentDurability(GameItem item) => item.GetTagReadonly(CurrentDurabilityTag).GetFloat();
        internal static string GetTaskType(GameItem item) => item.GetTagReadonly(TaskTag).GetString();
        internal static float GetTrainingWork(GameItem item) => item.GetTagReadonly(TrainingTag).GetFloat();
        internal static int GetTrainingData(GameItem item) => item.GetTagReadonly(TrainingDataTag).GetInt();
        internal static bool UsesIndependentTrainingData(GameItem item) => HasAttributes(item) && item.GetTagReadonly(VersionTag).GetInt() == 2;
        internal static int GetMemoryGb(GameItem item) => item.GetTagReadonly(MemoryTag).GetInt();
        internal static float GetPredictedComputeBonus(GameItem item) => item.GetTagReadonly(PredictedComputeTag).GetFloat();
        internal static float GetPredictedComputeMultiplier(GameItem item) => 1 + GetPredictedComputeBonus(item) / 100;
        internal static float GetPredictedDurability(GameItem item) => item.GetTagReadonly(PredictedDurabilityTag).GetFloat();
        internal static string TaskName(string taskType) => ModHelper.GetLocalized(ModConstants.ModId, "ai_task_" + taskType);

        internal static void RefreshName(GameItem item)
        {
            if (HasAttributes(item) && item.identifier == "age_of_agi.ai_untrained")
            {
                item.name = ModHelper.GetLocalized(ModConstants.ModId, "item_ai_untrained_name");
            }
            else if (HasAttributes(item) && item.identifier == TrainedId && item.IsTag(ComputeTag))
            {
                item.name = ModHelper.GetLocalized(ModConstants.ModId, "item_ai_trained_name");
            }
        }

    }
}
