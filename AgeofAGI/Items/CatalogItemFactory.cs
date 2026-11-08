using System;
using System.Collections.Generic;

namespace AgeOfAgi.Items
{
    internal sealed class CatalogItemFactory
    {
        private readonly string modId;
        private readonly string modName;

        internal CatalogItemFactory(ModManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (manifest.ID != ModConstants.ModId)
            {
                throw new ArgumentException("Expected manifest ID: " + ModConstants.ModId, nameof(manifest));
            }

            modId = manifest.ID;
            modName = string.IsNullOrWhiteSpace(manifest.Name) ? modId : manifest.Name;
        }

        internal void ValidateSprites(IReadOnlyList<ItemDefinition> definitions)
        {
            LoadedMod mod = FindLoadedMod();
            foreach (ItemDefinition definition in definitions)
            {
                if (!mod.Sprites.TryGetValue(definition.SpriteKey, out var sprite) || sprite == null)
                {
                    throw new InvalidOperationException("Item '" + definition.ItemId + "': sprite '" +
                        definition.SpriteKey + "' is not loaded. Check Textures/Items and the asset load log.");
                }
            }
        }

        internal GameItem Create(ItemDefinition definition, Action<GameItem> initialize = null)
        {
            LoadedMod mod = FindLoadedMod();
            if (!mod.Sprites.TryGetValue(definition.SpriteKey, out var sprite) || sprite == null)
            {
                throw new InvalidOperationException("Item sprite is not loaded: " + definition.SpriteKey);
            }

            // UISettings is scene state. Inspect it when a factory is invoked, not in Init
            // or during directory registration. The game computes the shape itself below.
            int gridSize = UISettings.current != null ? UISettings.current.gridSize : 0;
            if (gridSize <= 0)
            {
                throw new InvalidOperationException("Item creation requires initialized UISettings.");
            }

            var rect = sprite.rect;
            if (rect.width < gridSize || rect.height < gridSize ||
                rect.width % gridSize != 0 || rect.height % gridSize != 0)
            {
                throw new InvalidOperationException($"Item '{definition.ItemId}': sprite size {rect.width}x{rect.height} must be a positive multiple of gridSize {gridSize}.");
            }

            GameItem item = ItemDirectory.CreateEmptyItem(definition.ItemId);
            try
            {
                item.SetValue(definition.BaseValue);
                if (definition.Manufacturer.Length > 0) item.EnableManufacturer(definition.Manufacturer);
                item.name = ModHelper.GetLocalized(modId, definition.NameKey);
                item.shortDescription = string.IsNullOrEmpty(definition.DescriptionKey)
                    ? "" : ModHelper.GetLocalized(modId, definition.DescriptionKey);
                item.flavorText = string.IsNullOrEmpty(definition.CommentKey)
                    ? "" : ModHelper.GetLocalized(modId, definition.CommentKey);
                item.SetSpriteAndShapeFromMod(modId, definition.SpriteKey);
                ModHelper.SetSource(item, modId, modName);
                foreach (string type in definition.Types)
                {
                    ItemTypeHelper.ApplyType(item, type);
                }
                if (definition.ContrabandLevel == 0) ContrabandHelper.RemoveContrabandStatus(item);
                else ContrabandHelper.InitContrabandItem(item, definition.ContrabandLevel);
                // Repair configured categories without replacing unrelated saved types or stats.
                item.onLoaded += loaded => ItemTypeHelper.RestoreTypes(loaded, definition.Types);

                initialize?.Invoke(item);

                return item;
            }
            catch
            {
                item.Destroy();
                throw;
            }
        }

        private LoadedMod FindLoadedMod()
        {
            LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == modId && candidate.IsEnabled);
            if (mod == null)
            {
                throw new InvalidOperationException("Enabled mod is not registered yet: " + modId + ". Wait for OnModItemDirectoryInit.");
            }

            return mod;
        }
    }
}
