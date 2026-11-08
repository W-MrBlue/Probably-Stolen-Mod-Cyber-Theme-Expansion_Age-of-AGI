using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AgeOfAgi.Items.Logic.Gpu
{
    internal static class GpuColorChecker
    {
        internal const string ItemId = "age_of_agi.gpu_color_checker";
        internal const string TextureKey = "gpu_colorChecker";
        private const string PaperKey = "gpu_color_checker_reference";
        private const string CloseSpriteKey = "paper_buttons_close3";
        private static LoadedMod owner;
        private static Sprite originalPaper, reference;
        private static Texture2D sourceTexture;
        private static PaperUI panel;

        internal static void PrepareAssets()
        {
            LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId && candidate.IsEnabled);
            if (mod == null || !mod.Textures.TryGetValue(TextureKey, out var source) || source == null)
                throw new InvalidOperationException("GPU color checker texture is not loaded: " + TextureKey);
            if (ReferenceEquals(owner, mod) && sourceTexture == source && reference != null) return;
            Shutdown();
            owner = mod;
            sourceTexture = source;
            mod.PaperSprites.TryGetValue(PaperKey, out originalPaper);
            try
            {
                source.filterMode = FilterMode.Point;
                reference = Sprite.Create(source, new Rect(0, 0, source.width, source.height), new Vector2(.5f, .5f), 100f);
                mod.PaperSprites[PaperKey] = reference;
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        internal static void Open()
        {
            if (owner == null || !owner.IsEnabled || reference == null || !PlayerStore.HasInstance) return;
            // Native paper controls provide dragging, closing, zoom and paper audio,
            // with a separate panel from the original cigarette color reference.
            if (panel == null)
            {
                panel = PaperUI.Create(ModConstants.ModId + ":gpu_color_checker", ModConstants.ModId + ":" + PaperKey,
                    sourceTexture.width * 4, sourceTexture.height * 4, ModConstants.ModId);
                foreach (var element in panel.Data.chrome)
                    if (element.action == "close")
                    {
                        element.sprite = ModConstants.ModId + ":" + CloseSpriteKey;
                        element.hoverSprite = element.sprite;
                    }
            }
            panel.Show();
        }

        internal static void Shutdown()
        {
            panel?.Close();
            panel = null;
            if (owner != null)
            {
                if (owner.PaperSprites.TryGetValue(PaperKey, out var paper) && ReferenceEquals(paper, reference))
                {
                    if (originalPaper != null) owner.PaperSprites[PaperKey] = originalPaper;
                    else owner.PaperSprites.Remove(PaperKey);
                }
            }
            if (reference != null) Object.Destroy(reference);
            owner = null;
            sourceTexture = null;
            originalPaper = reference = null;
        }
    }
}
