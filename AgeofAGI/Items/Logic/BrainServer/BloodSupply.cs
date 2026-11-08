using HarmonyLib;

namespace AgeOfAgi.Items.Logic.BrainServer
{
    internal static class BloodSupply
    {
        internal const string AmountTag = "AGE_OF_AGI_BLOOD_AMOUNT";
        internal static int Capacity(GameItem item) => DailyUse(item) > 0 ? 1000 : 0;
        internal static int DailyUse(GameItem item) => item?.identifier == "blood_bag" ? 500 :
            item?.identifier == "blue_blood_bag" ? 50 : 0;

        internal static void Initialize(GameItem item)
        {
            int capacity = Capacity(item);
            if (capacity == 0 || item.IsTag(AmountTag)) return;
            item.ModifyTag(AmountTag, tag => tag.Enable().SetInt(capacity));
        }

        internal static int Amount(GameItem item)
        {
            Initialize(item);
            return Capacity(item) == 0 ? 0 : item.GetTagReadonly(AmountTag).GetInt();
        }

        internal static bool CanConsume(GameItem item) => item != null && item.unitCount == 1 &&
            GeneralHelper.IsItemOwned(item) && Capacity(item) > 0 &&
            Amount(item) >= DailyUse(item) && Amount(item) <= Capacity(item);

        internal static void Consume(GameItem item)
        {
            if (!CanConsume(item)) throw new System.InvalidOperationException("Insufficient blood for a daily draw.");
            int amount = Amount(item) - DailyUse(item);
            item.ModifyTag(AmountTag, tag => tag.Enable().SetInt(amount));
            if (amount == 0) item.Destroy();
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || Capacity(item) == 0) return;
            string key = item.identifier == "blue_blood_bag" ? "brain_blue_blood_amount" : "brain_blood_amount";
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, key, Amount(item)),
                color: RenderHandler.ColorPalette.LightRed);
        }
    }

    [HarmonyPatch(typeof(DirectoryMaster), nameof(DirectoryMaster.Item), new[] { typeof(string), typeof(bool) })]
    internal static class BloodSupplyCreationPatch
    {
        internal static void Postfix(GameItem __result) => BloodSupply.Initialize(__result);
    }

    // Native stacking compares identifiers. Never merge partly used bags into fresh ones.
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.MayStack), new[] { typeof(GameItem) })]
    internal static class BloodSupplyStackPatch
    {
        internal static void Postfix(GameItem __instance, GameItem otherItem, ref bool __result)
        {
            if (__result && BloodSupply.Capacity(__instance) > 0 &&
                (BloodSupply.Amount(__instance) != BloodSupply.Capacity(__instance) ||
                 BloodSupply.Amount(otherItem) != BloodSupply.Capacity(otherItem))) __result = false;
        }
    }
}
