using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsPortraits
    {
        internal const string GpuSupplierSpriteId = ModConstants.ModId + ".gpu_supplier_portrait";
        internal const string SecondhandBuyerSpriteId = ModConstants.ModId + ".secondhand_buyer_portrait";
        private static readonly Dictionary<string, Portrait> portraits = new Dictionary<string, Portrait>
        {
            { GpuSupplierSpriteId, new Portrait("gpu_supplier") },
            { SecondhandBuyerSpriteId, new Portrait("secondhand_buyer") }
        };

        internal static bool TryGet(string key, out Sprite sprite)
        {
            sprite = null;
            if (key == null || !portraits.TryGetValue(key, out var portrait)) return false;
            LoadedMod mod = ModLoader.Mods.Find(value => value.Manifest.ID == ModConstants.ModId && value.IsEnabled);
            if (mod == null || !mod.Textures.TryGetValue(portrait.TextureKey, out var texture) || texture == null) return false;
            if (portrait.Sprite == null || portrait.Texture != texture)
            {
                if (portrait.Sprite != null) Object.Destroy(portrait.Sprite);
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                Sprite reference = SpriteDict.Instance?.spriteDictionary?.Values.FirstOrDefault(value => value != null);
                portrait.Sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                    reference != null ? reference.pixelsPerUnit : 100f);
                portrait.Texture = texture;
            }
            sprite = portrait.Sprite;
            return true;
        }

        internal static void Shutdown()
        {
            foreach (Portrait portrait in portraits.Values)
            {
                if (portrait.Sprite != null) Object.Destroy(portrait.Sprite);
                portrait.Sprite = null;
                portrait.Texture = null;
            }
        }

        private sealed class Portrait
        {
            internal readonly string TextureKey;
            internal Texture2D Texture;
            internal Sprite Sprite;

            internal Portrait(string textureKey) { TextureKey = textureKey; }
        }
    }

    [HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
    internal static class ElectronicsPortraitPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(string key, ref Sprite __result)
        {
            if (!ElectronicsPortraits.TryGet(key, out var sprite)) return true;
            __result = sprite;
            return false;
        }
    }
}
