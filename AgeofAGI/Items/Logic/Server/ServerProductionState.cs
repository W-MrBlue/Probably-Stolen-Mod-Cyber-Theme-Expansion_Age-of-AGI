using AgeOfAgi.Items.Logic.ComplexData;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Definitions;
using Newtonsoft.Json;

namespace AgeOfAgi.Items.Logic.Server
{
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class ServerProductionState
    {
        [JsonProperty] internal int Version = 1;
        [JsonProperty] internal bool Intrinsic;
        [JsonProperty] internal string RecipeId;
        [JsonProperty] internal int ModelId;
        [JsonProperty] internal string TaskType;
        [JsonProperty] internal int MemoryGb;
        [JsonProperty] internal float ComputeBonus;
        [JsonProperty] internal float Durability;
        [JsonProperty] internal int BatchSize = 1;
        [JsonProperty] internal AiModelTraits Traits;
        [JsonProperty] internal int SourceId;
        [JsonProperty] internal string SourceItemId;
        [JsonProperty] internal string OutputItemId;
        [JsonProperty] internal int SeedId;
        [JsonProperty] internal string TraitId;
        [JsonProperty] internal string SeedSignature;
        [JsonProperty] internal float TotalWork;
        [JsonProperty] internal float Progress;
        [JsonProperty] internal int WorkNights;
        [JsonProperty] internal bool Active;
        [JsonProperty] internal int BatchData;
        [JsonProperty] internal int SourceData;
        [JsonProperty] internal long BatchMemoryGb;

        internal bool IsValid()
        {
            string expectedTask = RecipeId == "bio" ? Definitions.AiTaskTypes.Biological :
                RecipeId == "intelligence" ? Definitions.AiTaskTypes.Intrusion : Definitions.AiTaskTypes.Computing;
            bool extracting = RecipeId == "data_extract";
            bool encrypted = RecipeId == "encrypted_item";
            bool taskMatches = encrypted ? Definitions.AiTaskTypes.IsValid(TaskType) : extracting ? TaskType == Definitions.AiTaskTypes.Computing || TaskType == Definitions.AiTaskTypes.Intrusion :
                TaskType == expectedTask;
            string source = extracting || encrypted ? SourceItemId : RecipeId == "bio" ? ComplexDataState.ItemId : RecipeId == "intelligence" ? ServerProduction.FolderEmptyId :
                RecipeId == "btc_normal" ? ServerProduction.NormalInputId : RecipeId == "btc_rare" ? ServerProduction.RareInputId : null;
            return Version == (encrypted ? 2 : 1) && source != null && SourceItemId == source && SourceId > 0 && ModelId > 0 && taskMatches &&
                (!encrypted || (ValidItemId(SourceItemId) && ValidItemId(OutputItemId) && SourceItemId != OutputItemId)) &&
                AiModelState.ValidTraits(Traits, BatchSize) && MemoryGb > 0 &&
                (Intrinsic ? ComputeBonus == 0 && Durability == 0 && Traits == AiModelTraits.None && BatchSize == 1 : Positive(ComputeBonus) && Positive(Durability) && Durability >= 1) && Positive(TotalWork) &&
                !float.IsNaN(Progress) && Progress >= 0 && Progress < TotalWork && WorkNights >= 0 &&
                ((Progress > 0) == (WorkNights > 0)) && (Active || (Progress == 0 && WorkNights == 0)) &&
                (!extracting || (SourceItemId.StartsWith(ModConstants.ModId + ".", System.StringComparison.Ordinal) &&
                    BatchMemoryGb >= MemoryGb && BatchMemoryGb >= 8 && BatchData > 0 && SourceData >= BatchData)) &&
                (RecipeId != "bio" || (SeedId > 0 && !string.IsNullOrEmpty(TraitId) && !string.IsNullOrEmpty(SeedSignature)));
        }

        private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool ValidItemId(string id)
        {
            try { DefinitionValidation.RequireItemId(id, nameof(id), "Encrypted item"); return true; }
            catch (System.ArgumentException) { return false; }
        }
    }
}
