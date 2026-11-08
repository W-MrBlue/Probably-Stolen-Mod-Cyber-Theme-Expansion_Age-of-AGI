using System;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.DataContent
{
    internal static class DataContentState
    {
        internal const string CarrierTag = "AGE_OF_AGI_DATA_CARRIER";
        internal const string ContainerTag = "AGE_OF_AGI_DATA_CONTAINER";
        internal const string AmountTag = "AGE_OF_AGI_DATA_AMOUNT";
        internal const string CapacityTag = "AGE_OF_AGI_DATA_CAPACITY";
        internal const string IntrusionTag = "AGE_OF_AGI_DATA_INTRUSION";
        internal const string SpriteTag = "AGE_OF_AGI_DATA_SPRITE_PREFIX";

        internal static bool IsContainer(GameItem item) => item != null && item.IsTag(ContainerTag);
        internal static bool IsCarrier(GameItem item) => item != null && item.IsTag(CarrierTag);
        internal static int Amount(GameItem item) => item.GetTagReadonly(AmountTag).GetInt();
        internal static int Capacity(GameItem item) => item.GetTagReadonly(CapacityTag).GetInt();
        internal static bool Valid(GameItem item) => (IsCarrier(item) || IsContainer(item)) && item.unitCount == 1 &&
            Capacity(item) > 0 && Amount(item) >= 0 && Amount(item) <= Capacity(item) &&
            ItemContentValue.CanValue(item, Capacity(item));
        internal static bool CanExtract(GameItem item, string task) => IsCarrier(item) && Valid(item) && Amount(item) > 0 &&
            (task == AiTaskTypes.Intrusion || task == AiTaskTypes.Computing && item.GetTagReadonly(IntrusionTag).GetInt() == 0);

        internal static void SetAmount(GameItem item, int amount)
        {
            if (!Valid(item) || amount < 0 || amount > Capacity(item)) throw new ArgumentOutOfRangeException(nameof(amount));
            item.ModifyTag(AmountTag, tag => tag.SetInt(amount));
            Update(item);
        }

        // Only frozen, already-saved supplier offers use this path. New offers must fit current capacities.
        internal static void RestoreFrozenOffer(GameItem item, int amount)
        {
            if (IsContainer(item) && amount > Capacity(item) &&
                (item.identifier == "age_of_agi.data_container" && amount <= 200 ||
                 item.identifier == "age_of_agi.hypo_data_container" && amount <= 2000))
                item.ModifyTag(CapacityTag, tag => tag.SetInt(item.identifier == "age_of_agi.data_container" ? 200 : 2000));
            SetAmount(item, amount);
        }

        internal static void Update(GameItem item)
        {
            if (!Valid(item)) return;
            // Native estimates, negotiation and refreshed inventory values include lateUnitValue.
            // Keep the CSV shell price untouched; rebuild the derived content value after loading.
            ItemContentValue.Update(item, Amount(item));
            if (IsContainer(item))
            {
                int stage = (int)(((long)Amount(item) * 5 + Capacity(item) - 1) / Capacity(item));
                item.SetSprite(ModConstants.ModId, item.GetTagReadonly(SpriteTag).GetString() + stage);
            }
            item.Validate();
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !Valid(item)) return;
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, IsContainer(item) ? "data_content_amount" : "data_carrier_amount",
                Amount(item), Capacity(item)), color: RenderHandler.ColorPalette.LightCyan);
        }
    }
}
