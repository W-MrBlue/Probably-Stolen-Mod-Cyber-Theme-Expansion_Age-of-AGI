using AgeOfAgi.Items.Logic.AiModel;
using System;
using AgeOfAgi.Items.Definitions;
using Newtonsoft.Json;

namespace AgeOfAgi.Items.Logic.Server
{
    // Freeze the model's independent work/data budgets and cumulative payment.
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class ServerTrainingState
    {
        [JsonProperty] internal int Version;
        [JsonProperty] internal string RecipeId = "S01";
        [JsonProperty] internal int ModelId;
        [JsonProperty] internal string TaskType;
        [JsonProperty] internal int MemoryGb;
        [JsonProperty] internal float TotalWork;
        [JsonProperty] internal float ComputeBonus;
        [JsonProperty] internal float Durability;
        [JsonProperty] internal AiModelTraits Traits;
        [JsonProperty] internal int BatchSize = 1;
        [JsonProperty] internal float Progress;
        [JsonProperty] internal bool Active;
        [JsonProperty] internal int WorkNights;
        [JsonProperty] internal int DataConsumed;
        [JsonProperty] internal int TotalData;

        internal static ServerTrainingState Create(GameItem model)
        {
            return new ServerTrainingState
            {
                Version = 2,
                ModelId = model.GetUniqueID(),
                TaskType = AiModelState.GetTaskType(model),
                MemoryGb = AiModelState.GetMemoryGb(model),
                TotalWork = AiModelState.GetTrainingWork(model),
                ComputeBonus = AiModelState.GetPredictedComputeBonus(model),
                Durability = AiModelState.GetPredictedDurability(model),
                Traits = AiModelState.GetTraits(model),
                BatchSize = AiModelState.GetBatchSize(model),
                TotalData = AiModelState.GetTrainingData(model)
            };
        }

        // Cumulative rounding preserves the total data cost at every hardware speed.
        internal int DataNeededAt(float progress)
        {
            if (progress >= TotalWork) return TotalData;
            return checked((int)Math.Ceiling(TotalData * Math.Max(0, (double)progress) / TotalWork));
        }

        internal int DataForNight(float next) => DataNeededAt(next) - DataConsumed;

        internal bool Matches(GameItem model)
        {
            return AiModelState.CanTrain(model) && model.uniqueId == ModelId && TaskType == AiModelState.GetTaskType(model) &&
                MemoryGb == AiModelState.GetMemoryGb(model) && TotalWork == AiModelState.GetTrainingWork(model) &&
                ComputeBonus == AiModelState.GetPredictedComputeBonus(model) && Durability == AiModelState.GetPredictedDurability(model) &&
                Traits == AiModelState.GetTraits(model) && BatchSize == AiModelState.GetBatchSize(model) &&
                Version == 2 && TotalData == AiModelState.GetTrainingData(model);
        }

        internal void Record(float progress)
        {
            Progress = progress;
            WorkNights++;
        }

        internal bool IsValid()
        {
            if (Version != 2 || RecipeId != "S01" || ModelId <= 0 || !AiTaskTypes.IsValid(TaskType) || MemoryGb <= 0 || !Positive(TotalWork) ||
                !Positive(ComputeBonus) || !Positive(Durability) || !Finite(Progress) || Progress < 0 || Progress > TotalWork ||
                !AiModelState.ValidTraits(Traits, BatchSize) || WorkNights < 0 || ((Progress > 0) != (WorkNights > 0)))
            {
                return false;
            }
            return TotalData > 0 && DataConsumed >= 0 && DataConsumed <= TotalData && DataConsumed == DataNeededAt(Progress);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && value > 0;
    }
}
