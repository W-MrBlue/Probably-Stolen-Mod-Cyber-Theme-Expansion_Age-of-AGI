using System;
using AgeOfAgi.Items.Definitions;

using AgeOfAgi.Items.Logic.AiParts;
using static AgeOfAgi.Items.Logic.AiParts.AiPartState;

namespace AgeOfAgi.Items.Helpers
{
    internal static class AiPartsHelper
    {
        internal static void Init(GameItem item, ItemDefinition definition, Action<GameItem> initializeAudio,
            AiPartDefinition attributes = null)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (item.identifier != definition.ItemId)
            {
                throw new ArgumentException("AI parts definition ID does not match item '" + item.identifier + "'.", nameof(definition));
            }

            // Family membership is independent of displayed types. Price, localization,
            // sprite and category stay with the shared factory and CSV mapping.
            initializeAudio(item);

            item.EnableTag(FamilyTag);
            item.EnableTag(definition.Kind == ChipKind ? ChipTag : PartTag);
            if (attributes != null) InitAttributes(item, attributes);
        }

        internal static void InitAttributes(GameItem item, AiPartDefinition definition)
        {
            AiPartState.FillMissing(item, definition);
            item.onLoaded += loaded => AiPartState.FillMissing(loaded, definition);
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => AiPartState.CreateTooltip(builder, item);
    }
}
