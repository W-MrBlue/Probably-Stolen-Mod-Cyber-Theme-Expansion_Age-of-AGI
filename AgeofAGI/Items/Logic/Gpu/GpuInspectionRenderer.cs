using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfAgi.Items.Logic.Gpu
{
    // Borrow the native fuel view hosts so their masking, scale, reset position and
    // CounterMoveChild lens movement stay native. Restore their graphics on reset.
    internal static class GpuInspectionRenderer
    {
        internal const string SpriteKey = "gpu_detailst";
        private static InspectionUIManager owner;
        private static Image small;
        private static Image large;
        private static readonly List<Image> smallParts = new List<Image>();
        private static readonly List<Image> largeParts = new List<Image>();
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static readonly List<(Graphic Graphic, bool Enabled)> hidden = new List<(Graphic, bool)>();

        internal static void Show(InspectionUIManager manager, GameItem item)
        {
            Reset();
            LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId && candidate.IsEnabled);
            if (!GpuRuntime.IsGpu(item) || mod == null ||
                manager == null || manager.fuelUISmall == null || manager.fuelUILarge == null) return;
            GpuInspectionState state = GpuInspectionState.Read(item);
            var layers = new List<(GpuInspectionPart Part, Sprite Sprite)>();
            if (state != null)
            {
                foreach (GpuInspectionPart part in state.Parts.Where(part => part.Sprite.Length > 0))
                {
                    Sprite component = GetSprite(mod, part.Sprite);
                    if (component == null) { layers.Clear(); break; }
                    layers.Add((part, component));
                }
            }
            Sprite sprite;
            if (layers.Count > 0)
            {
                sprite = layers[0].Sprite;
                if (layers.Any(layer => layer.Part.X + layer.Sprite.rect.width > sprite.rect.width ||
                    layer.Part.Y + layer.Sprite.rect.height > sprite.rect.height)) layers.Clear();
            }
            else sprite = null;
            // Retain the old shared artwork if saved state is damaged or an asset is missing.
            if (layers.Count == 0)
            {
                if (!mod.Sprites.TryGetValue(SpriteKey, out var fallback) || fallback == null) return;
                sprite = fallback;
            }
            if (owner != manager)
            {
                DestroyViews();
                owner = manager;
            }
            if (small == null) small = CreateView(manager.fuelUISmall.transform, sprite);
            if (large == null) large = CreateView(manager.fuelUILarge.transform, sprite);
            foreach (Graphic graphic in manager.fuelUISmall.GetComponentsInChildren<Graphic>(true)
                .Concat(manager.fuelUILarge.GetComponentsInChildren<Graphic>(true)))
            {
                if (graphic == small || graphic == large || smallParts.Contains(graphic as Image) || largeParts.Contains(graphic as Image)) continue;
                hidden.Add((graphic, graphic.enabled));
                graphic.enabled = false;
            }
            small.sprite = sprite;
            large.sprite = sprite;
            foreach (Image board in new[] { small, large })
            {
                var rect = (RectTransform)board.transform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.x * sprite.rect.height / sprite.rect.width);
            }
            DrawParts(small, smallParts, layers);
            DrawParts(large, largeParts, layers);
            small.gameObject.SetActive(true);
            large.gameObject.SetActive(true);
            manager.fuelUISmall.gameObject.SetActive(true);
            manager.fuelUILarge.gameObject.SetActive(true);
            GpuInspectionCalculator.Show(manager, item);
        }

        private static Sprite GetSprite(LoadedMod mod, string key)
        {
            if (!mod.Textures.TryGetValue(key, out var texture) || texture == null) return null;
            if (!sprites.TryGetValue(key, out Sprite sprite) || sprite == null || sprite.texture != texture)
            {
                if (sprite != null) Object.Destroy(sprite);
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                sprites[key] = sprite;
            }
            return sprite;
        }

        private static void DrawParts(Image board, List<Image> views, List<(GpuInspectionPart Part, Sprite Sprite)> layers)
        {
            var boardRect = (RectTransform)board.transform;
            float scale = boardRect.sizeDelta.x / board.sprite.rect.width;
            int used = 0;
            foreach (var layer in layers.Skip(1))
            {
                if (used == views.Count)
                {
                    var node = new GameObject("AgeOfAgiGpuComponent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    node.layer = board.gameObject.layer;
                    node.transform.SetParent(board.transform, false);
                    var view = node.GetComponent<Image>();
                    view.color = Color.white;
                    view.maskable = true;
                    view.raycastTarget = false;
                    views.Add(view);
                }
                Image image = views[used++];
                image.sprite = layer.Sprite;
                var rect = (RectTransform)image.transform;
                rect.sizeDelta = new Vector2(layer.Sprite.rect.width * scale, layer.Sprite.rect.height * scale);
                rect.localPosition = new Vector3(
                    (layer.Part.X + layer.Sprite.rect.width / 2 - board.sprite.rect.width / 2) * scale,
                    (board.sprite.rect.height / 2 - layer.Part.Y - layer.Sprite.rect.height / 2) * scale, 0);
                image.gameObject.SetActive(true);
            }
            for (int index = used; index < views.Count; index++) views[index].gameObject.SetActive(false);
        }

        private static Image CreateView(Transform host, Sprite sprite)
        {
            // ResetUI hides both native hosts. Measure with the host active so
            // Unity includes its existing illustration children in the bounds.
            bool wasActive = host.gameObject.activeSelf;
            Bounds bounds;
            try
            {
                host.gameObject.SetActive(true);
                bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(host);
            }
            finally { host.gameObject.SetActive(wasActive); }
            float width = bounds.size.x > 0 ? bounds.size.x : 160;
            float height = bounds.size.y > 0 ? bounds.size.y : 160;
            // Match long edges: the GPU is wide while the native fuel art is tall.
            float scale = System.Math.Max(width, height) / System.Math.Max(sprite.rect.width, sprite.rect.height);
            var node = new GameObject("AgeOfAgiGpuInspection", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            node.layer = host.gameObject.layer;
            var rect = (RectTransform)node.transform;
            rect.SetParent(host, false);
            rect.localPosition = bounds.center;
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(sprite.rect.width * scale, sprite.rect.height * scale);
            Image image = node.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.maskable = true;
            node.SetActive(false);
            return image;
        }

        internal static void Reset()
        {
            GpuInspectionCalculator.Close();
            if (small != null) small.gameObject.SetActive(false);
            if (large != null) large.gameObject.SetActive(false);
            foreach (var entry in hidden)
                if (entry.Graphic != null) entry.Graphic.enabled = entry.Enabled;
            hidden.Clear();
        }

        private static void DestroyViews()
        {
            Reset();
            if (small != null) Object.Destroy(small.gameObject);
            if (large != null) Object.Destroy(large.gameObject);
            small = null;
            large = null;
            smallParts.Clear();
            largeParts.Clear();
            owner = null;
        }

        internal static void Shutdown()
        {
            if (owner != null && GpuRuntime.IsGpu(owner.currentInspectingItem)) owner.CloseUI();
            DestroyViews();
            foreach (Sprite sprite in sprites.Values) if (sprite != null) Object.Destroy(sprite);
            sprites.Clear();
        }
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OnInspectItem))]
    internal static class GpuInspectionOpenPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(InspectionUIManager __instance, GameItem gameItem) => GpuInspectionRenderer.Show(__instance, gameItem);
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.ResetUI))]
    internal static class GpuInspectionResetPatch
    {
        [HarmonyPostfix]
        internal static void Postfix() => GpuInspectionRenderer.Reset();
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.CloseUI))]
    internal static class GpuInspectionClosePatch
    {
        [HarmonyPostfix]
        internal static void Postfix() => GpuInspectionCalculator.Close();
    }

}
