using System;

namespace AgeOfAgi.Items
{
    // Shared additive content pricing. Shell prices still initialize once from items.csv.
    // Rates come exclusively from CSV, independently of category definitions and helpers.
    internal static class ItemContentValue
    {
        private const string UnitValueTag = "AGE_OF_AGI_CONTENT_UNIT_VALUE";

        internal static void Init(GameItem item, int unitValue)
        {
            item.ModifyTag(UnitValueTag, tag => tag.Enable().SetInt(unitValue));
        }

        internal static bool CanValue(GameItem item, int capacity)
        {
            int unitValue = item.GetTagReadonly(UnitValueTag).GetInt();
            // Native misc-value aggregation casts lateUnitValue to Int32.
            return unitValue > 0 && (long)capacity * unitValue <= int.MaxValue;
        }

        internal static void Update(GameItem item, int amount)
        {
            long value = (long)amount * item.GetTagReadonly(UnitValueTag).GetInt();
            if (value < 0 || value > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(amount));
            item.lateUnitValue = value;
        }
    }
}
