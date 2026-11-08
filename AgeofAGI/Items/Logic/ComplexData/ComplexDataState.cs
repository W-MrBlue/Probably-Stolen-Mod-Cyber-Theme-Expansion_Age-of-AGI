using System;
using HarmonyLib;

namespace AgeOfAgi.Items.Logic.ComplexData
{
    internal static class ComplexDataState
    {
        internal const string ItemId = ModConstants.ModId + ".complex_data";
        internal const string ContainerTag = "AGE_OF_AGI_COMPLEX_DATA";
        internal static bool IsContainer(GameItem item) => item != null && item.IsTag(ContainerTag);
        internal static bool IsEmpty(GameItem item) => IsContainer(item) && PlantGeneticsHelper.IsBlankDisk(item);

        internal static void Write(GameItem item, PlantTrait trait)
        {
            // Native disks encode one trait/rank and a remaining-use counter.
            PlantGeneticsHelper.WriteDisk(item, trait, 1, 4);
        }

        internal static void RefreshDisplay(GameItem item)
        {
            PlantTrait trait = PlantGeneticsHelper.GetDiskTrait(item);
            item.SetSprite(ModConstants.ModId, trait == null ? "virus_empty" : "dna_data");
            item.name = trait == null ? ModHelper.GetLocalized(ModConstants.ModId, "item_complex_data_name") :
                ModHelper.GetLocalized(ModConstants.ModId, "complex_data_named", trait.displayName);
            item.shortDescription = ModHelper.GetLocalized(ModConstants.ModId, "item_complex_data_desc");
            item.Validate();
        }
    }

    // Native apply/refill/use paths all call this display method. Intercept only
    // our carrier, retaining native DNA tags/uses while preserving its mod sprite.
    [HarmonyPatch(typeof(PlantGeneticsHelper), nameof(PlantGeneticsHelper.UpdateDiskDisplay))]
    internal static class ComplexDataDisplayPatch
    {
        internal static bool Prefix(GameItem disk)
        {
            if (!ComplexDataState.IsContainer(disk)) return true;
            ComplexDataState.RefreshDisplay(disk);
            return false;
        }
    }
}
