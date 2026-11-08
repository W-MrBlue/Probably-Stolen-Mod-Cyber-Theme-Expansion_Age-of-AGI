using AgeOfAgi.Items.Logic.ComplexData;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Helpers;

// Native DNA tag contracts, trait registry and seed lookups without a Unity plant scene.
internal sealed class PlantTrait
{
    internal string id;
    internal string displayName;
    internal string description;
    internal bool locked;
}

internal static class PlantTraitRegistry
{
    internal static readonly List<PlantTrait> All = new List<PlantTrait>();
    internal static PlantTrait Get(string id) => All.FirstOrDefault(trait => trait.id == id);
}

internal static class PlantTraitList
{
    internal const string FAST_GROWER = "fast_grower";
}

internal sealed class PlantDefinition { }
internal static class HydroponicHelper
{
    internal static PlantDefinition GetPlant(GameItem seed) => seed != null && seed.IsTag("TEST_VALID_SEED") ? new PlantDefinition() : null;
}

internal static class PlantGeneticsHelper
{
    internal static List<(PlantTrait trait, int rank)> GetTraits(GameItem seed) => PlantTraitRegistry.All
        .Select(trait => (trait, rank: seed.GetTagReadonly("PLANT_TRAIT_" + trait.id).GetInt())).Where(entry => entry.rank > 0).ToList();
    internal static List<PlantTrait> GetExtractableTraits(GameItem seed) => GetTraits(seed).Where(entry => !entry.trait.locked).Select(entry => entry.trait).ToList();
    internal static int GetHeat(GameItem seed) => seed.GetTagReadonly("GENETIC_HEAT_INT").GetInt();
    internal static int GetEffectiveComplexity(GameItem seed) => Math.Max(0, seed.GetTagReadonly("TEST_COMPLEXITY").GetInt() + GetHeat(seed));
    internal static void InitDisk(GameItem disk)
    {
        disk.EnableTag("DNA_DISK_TAG");
        disk.ModifyTag("DNA_DISK_TRAIT_STRING", tag => tag.Enable().SetString(""));
        UpdateDiskDisplay(disk);
    }
    internal static bool IsDisk(GameItem disk) => disk != null && disk.IsTag("DNA_DISK_TAG");
    internal static PlantTrait GetDiskTrait(GameItem disk) => IsDisk(disk) ? PlantTraitRegistry.Get(disk.GetTagReadonly("DNA_DISK_TRAIT_STRING").GetString()) : null;
    internal static bool IsBlankDisk(GameItem disk) => IsDisk(disk) && GetDiskTrait(disk) == null;
    internal static bool IsWrittenDisk(GameItem disk) => IsDisk(disk) && GetDiskTrait(disk) != null;
    internal static int GetDiskRank(GameItem disk) => Math.Max(1, disk.GetTagReadonly("DNA_DISK_RANK_INT").GetInt());
    internal static int GetDiskUses(GameItem disk) => Math.Max(1, disk.GetTagReadonly("DNA_DISK_USES_INT").GetInt());
    internal static void WriteDisk(GameItem disk, PlantTrait trait, int rank = 1, int uses = 1)
    {
        disk.ModifyTag("DNA_DISK_TRAIT_STRING", tag => tag.Enable().SetString(trait?.id ?? ""));
        disk.ModifyTag("DNA_DISK_RANK_INT", tag => tag.Enable().SetInt(trait == null ? 0 : Math.Max(1, rank)));
        disk.ModifyTag("DNA_DISK_USES_INT", tag => tag.Enable().SetInt(trait == null ? 0 : Math.Max(1, uses)));
        UpdateDiskDisplay(disk);
    }
    internal static bool SpendDiskUse(GameItem disk)
    {
        int left = GetDiskUses(disk) - 1;
        if (left <= 0) return true;
        disk.ModifyTag("DNA_DISK_USES_INT", tag => tag.Enable().SetInt(left));
        UpdateDiskDisplay(disk);
        return false;
    }
    internal static void UpdateDiskDisplay(GameItem disk)
    {
        if (ComplexDataDisplayPatch.Prefix(disk)) disk.name = "Native DNA disk";
    }
}
