using AgeOfAgi.Items.Catalogs;
using System;

using AgeOfAgi.Items.Logic.AiModel;
using static AgeOfAgi.Items.Logic.AiModel.AiModelState;

namespace AgeOfAgi.Items.Helpers
{
    internal static class AiModelHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio, AiValueCatalog values)
        {
            initializeAudio(item);
            item.EnableTag(ModelTag);
            item.onLoaded += loaded => { RefreshName(loaded); RefreshValue(loaded, values); };
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => AiModelTooltip.CreateTooltip(builder, item);
    }
}
