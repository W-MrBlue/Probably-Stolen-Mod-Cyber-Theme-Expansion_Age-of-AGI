using System.Collections.Generic;
using HarmonyLib;

namespace AgeOfAgi.Items
{
    internal static class ItemTypeHelper
    {
        internal static void ApplyType(GameItem item, string type)
        {
            string nativeType = type.ToUpperInvariant();
            // CSV uses lowercase; native category lists and tags are case-sensitive.
            item.SetGameItemType(TypeHelper.ALL_TYPES_LIST.Contains(nativeType) ? nativeType : type);
        }

        internal static void RestoreTypes(GameItem item, IReadOnlyList<string> types)
        {
            foreach (string type in types)
            {
                // Retire the former custom material category on saved instances.
                if (type == "material") item.RemoveGameItemType("electronic_materials");
                string nativeType = type.ToUpperInvariant();
                string category = TypeHelper.ALL_TYPES_LIST.Contains(nativeType) ? nativeType : type;
                if (!item.GetGameItemType().Contains(category) || !item.IsTag(category)) ApplyType(item, type);
            }
        }

        internal static bool TryGetDisplayName(string type, out string displayName)
        {
            displayName = null;
            if (string.IsNullOrEmpty(type) || TypeHelper.ALL_TYPES_LIST.Contains(type.ToUpperInvariant()))
            {
                return false;
            }

            // Resolve custom categories by their own ID, without a per-category map.
            // Native categories retain their native localization and behavior.
            LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId && candidate.IsEnabled);
            if (mod == null)
            {
                return false;
            }

            string key = type.ToLowerInvariant();
            string locale = LocHelper.GetCurrentLocaleCode();
            bool hasTranslation = mod.Localization.TryGetValue(locale, out var current) && current.ContainsKey(key) ||
                mod.Localization.TryGetValue("en", out var english) && english.ContainsKey(key);
            if (!hasTranslation)
            {
                return false;
            }

            displayName = ModHelper.GetLocalized(ModConstants.ModId, key);
            return true;
        }
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeDisplayName))]
    internal static class ItemTypeDisplayNamePatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(string typeConst, ref string __result)
        {
            if (!ItemTypeHelper.TryGetDisplayName(typeConst, out string displayName))
            {
                return true;
            }

            __result = displayName;
            return false;
        }
    }
}
