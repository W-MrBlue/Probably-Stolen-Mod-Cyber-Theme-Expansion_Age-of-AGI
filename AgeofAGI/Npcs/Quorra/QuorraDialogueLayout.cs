using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AgeOfAgi.Npcs
{
    internal static class QuorraDialogueLayout
    {
        private const float OffsetPixels = 20f;
        private static readonly List<(Transform Target, float OriginalY, float ShiftedY)> shifted =
            new List<(Transform, float, float)>();

        internal static void Apply(DialogUIManager manager)
        {
            Restore();
            StoreClient client = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() : null;
            if (manager == null || client?.identifier != QuorraStoryClient.ClientId || client.sourceModId != ModConstants.ModId ||
                !ModLoader.Mods.Exists(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled)) return;
            if (client.spriteName != QuorraStoryClient.SpriteId) return;
            Transform bubble = manager.clientBubble?.transform;
            Transform name = manager.clientNameBubble?.transform;
            ShiftDown(bubble);
            // A child nameplate already follows the speech bubble's translation.
            for (Transform parent = name?.parent; parent != null; parent = parent.parent)
                if (parent == bubble) return;
            ShiftDown(name);
        }

        private static void ShiftDown(Transform target)
        {
            if (target == null) return;
            Vector3 position = target.localPosition;
            float y = position.y - OffsetPixels;
            shifted.Add((target, position.y, y));
            target.localPosition = new Vector3(position.x, y, position.z);
        }

        internal static void Restore()
        {
            foreach (var entry in shifted)
            {
                if (entry.Target == null) continue;
                Vector3 position = entry.Target.localPosition;
                // Native normal/shooting modes may already have reset the position.
                if (System.Math.Abs(position.y - entry.ShiftedY) < 0.001f)
                    entry.Target.localPosition = new Vector3(position.x, entry.OriginalY, position.z);
            }
            shifted.Clear();
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.DisplayClientText))]
    internal static class QuorraDialogueLayoutPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(DialogUIManager __instance) => QuorraDialogueLayout.Apply(__instance);
    }
}
