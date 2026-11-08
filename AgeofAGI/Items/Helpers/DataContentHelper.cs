using System;
using AgeOfAgi.Items.Definitions;

using AgeOfAgi.Items.Logic.DataContent;
using static AgeOfAgi.Items.Logic.DataContent.DataContentState;

namespace AgeOfAgi.Items.Helpers
{
    internal static class DataContentHelper
    {
        internal static void Init(GameItem item, DataContentDefinition definition, int unitValue, Action<GameItem> initializeAudio)
        {
            ItemContentValue.Init(item, unitValue);
            initializeAudio(item);

            item.EnableTag(definition.Container ? ContainerTag : CarrierTag);
            item.ModifyTag(CapacityTag, tag => tag.Enable().SetInt(definition.Capacity));
            item.ModifyTag(AmountTag, tag => tag.Enable().SetInt(definition.InitialData));
            item.ModifyTag(IntrusionTag, tag => tag.Enable().SetInt(definition.RequiresIntrusion ? 1 : 0));
            item.ModifyTag(SpriteTag, tag => tag.Enable().SetString(definition.SpritePrefix));
            Update(item);
            item.onLoaded += Update;
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => DataContentState.CreateTooltip(builder, item);
    }
}
