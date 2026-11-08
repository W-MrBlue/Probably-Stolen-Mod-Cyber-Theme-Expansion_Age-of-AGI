using System;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.AiParts
{
    internal static class AiPartState
    {
        internal const string PartsKind = "ai_parts";
        internal const string ChipKind = "ai_parts_chip";
        internal const string FamilyTag = "AI_PARTS_FAMILY";
        internal const string PartTag = "AI_PARTS";
        internal const string ChipTag = "AI_PARTS_CHIP";
        internal const string ComputeBonusTag = "AGE_OF_AGI_PART_COMPUTE_BONUS_PERCENT";
        internal const string MemoryTag = "AGE_OF_AGI_PART_MEMORY_GB";
        internal const string TrainingTag = "AGE_OF_AGI_PART_TRAINING_WORK";
        internal const string TrainingDataTag = "AGE_OF_AGI_PART_TRAINING_DATA";
        internal const int FeedbackLength = 4;

        internal static bool HandlesKind(string kind)
        {
            return kind == PartsKind || kind == ChipKind;
        }

        internal static bool IsPart(GameItem item)
        {
            return item != null && item.IsTag(PartTag);
        }
        internal static bool IsChip(GameItem item)
        {
            return item != null && item.IsTag(ChipTag);
        }

        internal static void FillMissing(GameItem target, AiPartDefinition definition)
        {
            if (definition.Role == "feedback") ApplyFeedbackShape(target);
            if (!definition.IsBasis)
            {
                return;
            }
            if (!target.IsTag(ComputeBonusTag))
            {
                target.ModifyTag(ComputeBonusTag, state => state.Enable().SetFloat(definition.ComputeBonusPercent));
            }
            if (!target.IsTag(MemoryTag))
            {
                target.ModifyTag(MemoryTag, state => state.Enable().SetInt(definition.MemoryGb));
            }
            if (!target.IsTag(TrainingTag))
            {
                target.ModifyTag(TrainingTag, state => state.Enable().SetFloat(definition.TrainingWork));
            }
            if (!target.IsTag(TrainingDataTag))
            {
                target.ModifyTag(TrainingDataTag, state => state.Enable().SetInt(definition.TrainingData));
            }
        }

        private static void ApplyFeedbackShape(GameItem item)
        {
            // Restore the old three-cell override without changing placement.
            GridShape transform = item.shape;
            item.SetShape(new GridShapeBuilder().SetData(new byte[] { 1, 1, 1, 1 }, FeedbackLength)
                .SetTransform(transform.minX, transform.minY, transform.flipped, transform.orientation));
            item.Validate();
            if (item is GameItemElement element)
            {
                int size = UISettings.current.gridSize;
                var dimensions = new UnityEngine.Vector2(FeedbackLength * size, size);
                element.background.rectTransform.sizeDelta = dimensions;
                element.fakeBackground.rectTransform.sizeDelta = dimensions;
            }
        }

        internal static float GetComputeBonus(GameItem item) => item.GetTagReadonly(ComputeBonusTag).GetFloat();
        internal static int GetMemoryGb(GameItem item) => item.GetTagReadonly(MemoryTag).GetInt();
        internal static float GetTrainingWork(GameItem item) => item.GetTagReadonly(TrainingTag).GetFloat();
        internal static int GetTrainingData(GameItem item) => item.GetTagReadonly(TrainingDataTag).GetInt();

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !IsPart(item) || !item.IsTag(ComputeBonusTag))
            {
                return;
            }
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, "part_stat_compute", GetComputeBonus(item)),
                color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, "ai_stat_memory", GetMemoryGb(item)),
                color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, "ai_stat_training", GetTrainingWork(item)),
                color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, "ai_stat_training_data", GetTrainingData(item)),
                color: RenderHandler.ColorPalette.LightCyan);
        }
    }
}
