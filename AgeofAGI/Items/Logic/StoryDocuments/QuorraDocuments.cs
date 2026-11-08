using System;
using System.Collections.Generic;
using AgeOfAgi.Npcs;
using AgeOfAgi.Npcs.Stories;

namespace AgeOfAgi.Items.Logic.StoryDocuments
{
    internal static class QuorraDocuments
    {
        internal const string WantedItemId = ModConstants.ModId + ".quorra_wanted";
        internal const string PhoneCardItemId = ModConstants.ModId + ".quorra_number";
        internal const string ClaimActionName = "补领葵拉剧情物品";
        internal const string CloseSpriteKey = "paper_buttons_close3";
        internal static readonly Document Wanted = new Document(WantedItemId, "Quorra_wanted", 2, 2, "wanted");
        internal static readonly Document PhoneCard = new Document(PhoneCardItemId, "Quorra_number", 2, 1, "phone_card");
        private static readonly Document[] documents = { Wanted, PhoneCard };
        private static bool delivering;

        internal static void Enable()
        {
            ModHook.OnGameLoadedLate -= DeliverPending;
            ModHook.OnGameLoadedLate += DeliverPending;
            ModHook.OnShutterOpenedLate -= DeliverPending;
            ModHook.OnShutterOpenedLate += DeliverPending;
        }

        internal static void Bind(GameItem item, Document document)
        {
            var cells = new byte[document.GridWidth * document.GridHeight];
            for (int i = 0; i < cells.Length; i++) cells[i] = 1;
            item.SetShape(new GridShapeBuilder().SetData(cells, document.GridWidth));
            item.EnableTag("IMPORTANT_TAG");
            item.mayActivateSlotItemFunc = (_, _, _) => true;
            item.canActivateSlotItemFunc = (_, _, _) => true;
            item.onActivateSlotItemFunc = (_, _, _) => Open(document);
        }

        internal static void TryDeliver(StorySession session, Document document)
        {
            if (!session.Current || session.State.Has(document.DeliveredFlag)) return;
            session.Flag(document.PendingFlag);
            DeliverPending();
            // A retry can commit through another callback while the story is active.
            session.State.Flags[document.DeliveredFlag] = StoryDirector.Read(session.Definition.Id).Has(document.DeliveredFlag);
        }

        internal static void DeliverPending()
        {
            if (delivering || !StoryDirector.Enabled || !PlayerStore.HasInstance || EmporiumEntry.Instance == null) return;
            delivering = true;
            try
            {
                foreach (Document document in documents) Deliver(document);
            }
            finally { delivering = false; }
        }

        private static void Deliver(Document document)
        {
            StoryState state = StoryDirector.Read(QuorraStoryClient.ClientId);
            if (!state.Has(document.PendingFlag) || state.Has(document.DeliveredFlag)) return;
            if (!DirectoryMaster.Has<GameItem>(document.ItemId))
            { StoryDirector.Report("Story document is not registered: " + document.ItemId); return; }
            GameItem item = DirectoryMaster.Item(document.ItemId);
            if (item == null) return;
            GeneralHelper.SetItemOwned(item);
            bool placed = false;
            try
            {
                if (TryPlace(item, document))
                {
                    placed = true;
                    state = StoryDirector.Read(QuorraStoryClient.ClientId);
                    state.Flags[document.DeliveredFlag] = true;
                    StoryDirector.Save(QuorraStoryClient.ClientId, state);
                    AudioManager.Instance?.TryPlayOne(item.soundDragEnd);
                    return;
                }
                string reason = ReferenceEquals(document, PhoneCard) ? "前柜台无可用的电话卡空位" : $"柜台和仓库均无 {document.GridWidth}×{document.GridHeight} 空位";
                StoryDirector.Report($"{item.name} 待交付：{reason}；腾出空间后使用 F9「{ClaimActionName}」或重新开店。");
            }
            finally { if (!placed) item.Destroy(); }
        }

        private static bool TryPlace(GameItem item, Document document)
        {
            if (ReferenceEquals(document, PhoneCard))
            {
                GameGridInventory front = EmporiumEntry.Instance.frontInvinvElement;
                if (front == null) return false;
                int width = item.modifiedShape.globalWidth, height = item.modifiedShape.globalHeight;
                if (front.inventoryShape.width < width || front.inventoryShape.height < height) return false;
                int maxX = front.inventoryShape.width - width, maxY = front.inventoryShape.height - height;
                int preferredX = maxX / 2;
                var positions = new List<(int X, int Y, int Distance)>();
                for (int y = maxY; y >= 0; y--)
                    for (int x = 0; x <= maxX; x++)
                    {
                        int dx = x - preferredX, dy = y - maxY;
                        positions.Add((x, y, dx * dx + dy * dy));
                    }
                // Start at bottom centre, then search by distance. Equal distances
                // favour the lower row, then the leftmost position for a stable result.
                positions.Sort((a, b) =>
                {
                    int order = a.Distance.CompareTo(b.Distance);
                    if (order == 0) order = b.Y.CompareTo(a.Y);
                    return order == 0 ? a.X.CompareTo(b.X) : order;
                });
                foreach (var position in positions)
                {
                    var shape = new GridShapeBuilder(item.modifiedShape).SetPosition(position.X, position.Y);
                    SlotMarker slot = front.TryInventorySlot(item, shape);
                    if (slot != null && slot.IsValid() && slot.TryAcceptOnce(item.unitCount) == item.unitCount) return true;
                }
                return false;
            }
            foreach (var target in new GraphNodeStorage[] { EmporiumEntry.Instance.frontInvinvElement,
                EmporiumEntry.Instance.backInvinvElement, EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.showcaseElement })
                if (target != null && GraphUtils.CanAccept(target, item) && GraphUtils.TryAcceptAll(target, item) == 0) return true;
            return false;
        }

        private static void Open(Document document)
        {
            if (!PlayerStore.HasInstance) return;
            LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId && candidate.IsEnabled);
            if (mod == null) return;
            if (!mod.PaperSprites.TryGetValue(document.DetailKey, out var detail) || detail == null)
            { StoryDirector.Report("Missing story document detail: " + document.DetailKey); return; }
            if (ReferenceEquals(document, PhoneCard))
            {
                if (!mod.PaperSprites.TryGetValue(CloseSpriteKey, out var close) || close == null)
                { StoryDirector.Report("Missing story document close icon: " + CloseSpriteKey); return; }
                QuorraPhoneCard.Toggle(detail, close);
                return;
            }
            if (document.Panel == null)
            {
                document.Panel = PaperUI.Create(ModConstants.ModId + ":" + document.DetailKey,
                    ModConstants.ModId + ":" + document.DetailKey, detail.rect.width, detail.rect.height, ModConstants.ModId);
                foreach (var element in document.Panel.Data.chrome)
                    if (element.action == "close")
                    {
                        element.sprite = ModConstants.ModId + ":" + CloseSpriteKey;
                        element.hoverSprite = element.sprite;
                    }
            }
            // Show rebuilds a closed native panel while preserving its drag/zoom controls.
            document.Panel.Show();
        }

        internal static void Shutdown()
        {
            ModHook.OnGameLoadedLate -= DeliverPending;
            ModHook.OnShutterOpenedLate -= DeliverPending;
            QuorraPhoneCard.Shutdown();
            foreach (Document document in documents)
            {
                document.Panel?.Close();
                document.Panel = null;
            }
        }

        internal sealed class Document
        {
            internal readonly string ItemId, DetailKey, PendingFlag, DeliveredFlag;
            internal readonly int GridWidth, GridHeight;
            internal PaperUI Panel;

            internal Document(string itemId, string detailKey, int gridWidth, int gridHeight, string flagPrefix)
            {
                ItemId = itemId;
                DetailKey = detailKey;
                GridWidth = gridWidth;
                GridHeight = gridHeight;
                PendingFlag = flagPrefix + "_pending";
                DeliveredFlag = flagPrefix + "_delivered";
            }
        }
    }
}
