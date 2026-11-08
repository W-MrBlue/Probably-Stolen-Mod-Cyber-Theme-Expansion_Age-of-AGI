using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AgeOfAgi.Npcs
{
    internal static class QuorraStoryClient
    {
        internal const string ClientId = ModConstants.ModId + ".quorra_intro";
        internal const string SpriteId = ModConstants.ModId + ".quorra";
        internal const string TextureKey = "Quorra_V1";
        internal const string DebugActionName = "召唤未知女孩（队首）";
        private static Texture2D portraitTexture;
        private static Sprite portrait;

        internal static StoreClient Create() => Stories.StoryDirector.Create(QuorraStory.CreateDefinition());

        internal static StoreClient CreateBlueprint()
        {
            var client = new StoreClient
            {
                identifier = ClientId,
                displayName = Text("npc_quorra_unknown_name"),
                spriteName = SpriteId,
                clientIntent = StoreClient.ClientIntent.DIALOGUE,
                dismissable = false,
                nonShootable = true,
                bubbleColor = "DEFAULT"
            };
            StoreClientFactionSetup.InitLowerLevelCitizen(client);
            ModHelper.SetClientSource(client, ModConstants.ModId);
            return client;
        }

        internal static bool QueueFirst()
        {
            LoadedMod mod = EnabledMod();
            if (mod == null) return false;
            if (!PlayerStore.HasInstance || PlayerStore.Instance.storeClientManager == null)
            {
                new ModLog(mod.Manifest).Warning("Load a game before queuing the unknown girl.");
                return false;
            }
            if (!TryGetPortrait(out _))
            {
                new ModLog(mod.Manifest).Warning("Missing Quorra portrait: Textures/Quorra_V1.png.");
                return false;
            }
            var manager = PlayerStore.Instance.storeClientManager;
            if (Stories.StoryDirector.Read(ClientId).CompletedDay >= 0 ||
                PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier == ClientId ||
                manager.clientStack.Any(client => client?.identifier == ClientId && client.sourceModId == ModConstants.ModId))
            {
                new ModLog(mod.Manifest).Warning("葵拉剧情已完成或已在当前/等待队列中。");
                return false;
            }
            manager.AddNextClient(Create());
            return true;
        }

        internal static bool TryGetPortrait(out Sprite sprite)
        {
            sprite = null;
            LoadedMod mod = EnabledMod();
            if (mod == null || !mod.Textures.TryGetValue(TextureKey, out var texture) || texture == null) return false;
            if (portrait == null || portraitTexture != texture)
            {
                if (portrait != null) Object.Destroy(portrait);
                portraitTexture = texture;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                // Match the native character atlas' world scale when available.
                Sprite reference = SpriteDict.Instance?.spriteDictionary?.Values.FirstOrDefault(value => value != null);
                float pixelsPerUnit = reference != null ? reference.pixelsPerUnit : 100f;
                portrait = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            }
            sprite = portrait;
            return true;
        }

        internal static void Shutdown()
        {
            QuorraDialogueLayout.Restore();
            // These queued clients hold executable mod dialogue callbacks.
            if (PlayerStore.HasInstance)
                PlayerStore.Instance.storeClientManager?.clientStack?.RemoveAll(client =>
                    client != null && client.identifier == ClientId && client.sourceModId == ModConstants.ModId);
            if (portrait != null) Object.Destroy(portrait);
            portrait = null;
            portraitTexture = null;
        }

        private static LoadedMod EnabledMod() => ModLoader.Mods.Find(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled);
        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }

    // Native story characters use SpriteDict too. Resolve only our namespaced
    // character key; leave all native pools and other mods' keys untouched.
    [HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
    internal static class QuorraPortraitPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(string key, ref Sprite __result)
        {
            if (key != QuorraStoryClient.SpriteId || !QuorraStoryClient.TryGetPortrait(out var sprite)) return true;
            __result = sprite;
            return false;
        }
    }
}
