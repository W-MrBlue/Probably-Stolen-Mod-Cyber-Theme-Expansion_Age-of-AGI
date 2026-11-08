using AgeOfAgi.Items.Definitions;
using static AgeOfAgi.Items.Logic.AiModel.AiModelState;

namespace AgeOfAgi.Items.Logic.AiModel
{
    internal static class AiModelTooltip
    {
        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !HasAttributes(item)) return;
            void Attribute(string key, params object[] values) => builder.AddLine(
                ModHelper.GetLocalized(ModConstants.ModId, key, values), color: RenderHandler.ColorPalette.LightCyan);
            bool trained = item.identifier == TrainedId;
            Attribute("ai_stat_memory", GetMemoryGb(item));
            Attribute(trained ? "ai_stat_compute" : "ai_stat_predicted_compute", trained ? GetComputeBonus(item) : GetPredictedComputeBonus(item));
            if (trained) Attribute("ai_stat_durability", GetCurrentDurability(item), GetInitialDurability(item));
            else
            {
                Attribute("ai_stat_predicted_durability", GetPredictedDurability(item));
                Attribute("ai_stat_training", GetTrainingWork(item));
                if (UsesIndependentTrainingData(item) && item.IsTag(TrainingDataTag)) Attribute("ai_stat_training_data", GetTrainingData(item));
            }

            builder.AddLine("", ignoreEmpty: false);
            string task = GetTaskType(item);
            var color = task == AiTaskTypes.Intrusion ? RenderHandler.ColorPalette.LightRed :
                task == AiTaskTypes.Sensory ? RenderHandler.ColorPalette.LightBlue :
                task == AiTaskTypes.Biological ? RenderHandler.ColorPalette.LightGreen : RenderHandler.ColorPalette.LightCyan;
            builder.AddLine(TaskName(task), color: color);
            foreach (string line in AiModelTraitText.Lines(GetTraits(item), GetBatchSize(item)))
                builder.AddLine(line, color: RenderHandler.ColorPalette.LightCyan);
            // Native GetTooltipBasic appends shortDescription/flavorText immediately after the late hook.
            if (!string.IsNullOrEmpty(item.shortDescription) || !string.IsNullOrEmpty(item.flavorText))
                builder.AddLine("", ignoreEmpty: false);
        }
    }
}
