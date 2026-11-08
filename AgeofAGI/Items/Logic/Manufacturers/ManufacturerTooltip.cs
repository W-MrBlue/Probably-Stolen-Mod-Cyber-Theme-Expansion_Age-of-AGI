using HarmonyLib;

namespace AgeOfAgi.Items.Logic.Manufacturers
{
    internal static class ManufacturerTooltip
    {
        internal static bool TryAppend(RichTextBuilder builder, string manufacturer)
        {
            string key;
            switch (manufacturer)
            {
                case "age_of_agi.astar": key = "manufacturer_astar"; break;
                case "age_of_agi.prism": key = "manufacturer_prism"; break;
                case "age_of_agi.silicon": key = "manufacturer_silicon"; break;
                default: return false;
            }
            if (builder == null || !ModLoader.Mods.Exists(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled)) return false;
            // GameItemElement calls DisplayManufacturer directly beneath the item name.
            // Match the native manufacturer row, including its leading space and underline.
            builder.Add(" ");
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, key), color: RenderHandler.ColorPalette.Turquoise,
                underline: true);
            return true;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.DisplayManufacturer))]
    internal static class ManufacturerTooltipPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(RichTextBuilder builder, string manufacturer) => !ManufacturerTooltip.TryAppend(builder, manufacturer);
    }
}
