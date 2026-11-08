using AgeOfAgi.Items.Logic.Server;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Gpu;
using Newtonsoft.Json;
using UnityEngine;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsTrader
    {
        internal const string ClientId = ModConstants.ModId + ".electronics_trader";
        internal const string UpgradeId = ModConstants.ModId + ".electronics_entry_service";
        internal const string StateKey = "electronics_entry_state";
        internal const string CatalogPath = "Data/electronics_entry.csv";
        internal const string DebugActionName = "召唤电子商人（调试供货）";
        internal const int UnlockCost = 500;
        internal const int PortraitCount = 4;
        internal static readonly WeeklyElectronicsSupplier Supplier = new WeeklyElectronicsSupplier(
            ClientId, UpgradeId, ModConstants.ModId + ".electronics_trader_portrait", StateKey, "electronics_trader",
            "maleSci_0", "maleSci_1", "femaleSci_0", "femaleSci_1");
    }

    internal sealed class WeeklyElectronicsSupplier
    {
        internal readonly string ClientId, UpgradeId, SpriteId, StateKey;
        private readonly string textPrefix;
        private readonly string[] textureKeys;
        private ModLog log;
        private NightShiftCatalog catalog;
        private readonly Dictionary<int, Sprite> portraits = new Dictionary<int, Sprite>();
        private readonly Dictionary<int, Texture2D> portraitTextures = new Dictionary<int, Texture2D>();

        internal WeeklyElectronicsSupplier(string clientId, string upgradeId, string spriteId, string stateKey,
            string textPrefix, params string[] textureKeys)
        {
            ClientId = clientId; UpgradeId = upgradeId; SpriteId = spriteId; StateKey = stateKey;
            this.textPrefix = textPrefix; this.textureKeys = textureKeys;
        }

        internal void Configure(NightShiftCatalog value, ModLog modLog) { catalog = value; log = modLog; }

        internal void Unlock()
        {
            if (catalog == null || !PlayerStore.HasInstance || StoreStation.Instance == null ||
                !ModHelper.IsNetworkUpgradeUnlocked(UpgradeId)) return;
            var state = Load();
            if (state == null)
            {
                int day = StoreStation.Instance.dayCounter;
                int firstVisit = day + 1;
                if (UpgradeId == NightShiftClient.UpgradeId)
                {
                    var entry = JsonConvert.DeserializeObject<NightShiftState>(
                        ModHelper.GetModData(ModConstants.ModId, ElectronicsTrader.StateKey) ?? "null");
                    if (entry != null && firstVisit % 7 == entry.NextVisitDay % 7) firstVisit += 3;
                }
                state = new NightShiftState { NextVisitDay = firstVisit, Gifts = catalog.GiftItems() };
                Save(state);
            }
            DeliverGifts();
        }

        internal void ClaimGifts()
        {
            try
            {
                if (!PlayerStore.HasInstance || EmporiumEntry.Instance == null)
                {
                    log?.Warning(Text(textPrefix + "_name") + "领取AI服务物品：请先进入店铺存档。");
                    return;
                }
                if (catalog == null)
                {
                    log?.Error(Text(textPrefix + "_name") + "领取AI服务物品：供货配置尚未加载，请检查模组初始化日志。");
                    return;
                }
                if (!NightShiftClient.EnsureServiceRegistered())
                {
                    log?.Warning(Text(textPrefix + "_name") + "领取AI服务物品：当前存档的王尔德服务列表尚未就绪。");
                    return;
                }
                if (!ModHelper.IsNetworkUpgradeUnlocked(UpgradeId))
                {
                    log?.Warning(Text(textPrefix + "_name") + "领取AI服务物品：尚未购买AI服务。请先在王尔德常规标签页的“电子产业”栏购买对应等级；此按钮仅补领已购礼包。");
                    return;
                }
                NightShiftState before = Load();
                int alreadyDelivered = before?.GiftIndex ?? 0;
                if (before == null) Unlock();
                else DeliverGifts();
                NightShiftState after = Load();
                int delivered = after.GiftIndex - alreadyDelivered;
                int remaining = after.Gifts.Count - after.GiftIndex;
                if (remaining > 0)
                    log?.Warning(Text(textPrefix + "_name") + "领取AI服务物品：本次发放 " + delivered + " 件，仍有 " + remaining + " 件待领取；请清理库存空间后再次领取。");
                else if (delivered > 0)
                    log?.Log(Text(textPrefix + "_name") + "领取AI服务物品：本次发放 " + delivered + " 件，礼包已全部领取。");
                else
                    log?.Log(Text(textPrefix + "_name") + "领取AI服务物品：礼包已全部领取，没有待领取物品。");
            }
            catch (Exception exception)
            {
                log?.Error("领取AI服务物品失败：" + exception);
            }
        }

        internal void DeliverGifts()
        {
            if (catalog == null || !PlayerStore.HasInstance || EmporiumEntry.Instance == null ||
                !ModHelper.IsNetworkUpgradeUnlocked(UpgradeId)) return;
            var state = Load();
            if (state == null) return;
            while (state.GiftIndex < state.Gifts.Count)
            {
                GameItem item = CreateItem(state.Gifts[state.GiftIndex], standardGpu: UpgradeId == ElectronicsTrader.UpgradeId);
                GeneralHelper.SetItemOwned(item);
                bool placed = false;
                try
                {
                    InstallGiftContents(item, state.Gifts[state.GiftIndex]);
                    foreach (GraphNodeStorage destination in new GraphNodeStorage[] {
                        EmporiumEntry.Instance.backInvinvElement, EmporiumEntry.Instance.invElement,
                        EmporiumEntry.Instance.showcaseElement, EmporiumEntry.Instance.frontInvinvElement })
                    {
                        if (destination != null && GraphUtils.CanAccept(destination, item) && GraphUtils.TryAcceptAll(destination, item) == 0)
                        { placed = true; break; }
                    }
                    if (!placed) return; // Persist the remaining gifts; retry next opening/load or through F9.
                    state.GiftIndex++;
                    Save(state);
                }
                finally { if (!placed) item.Destroy(); }
            }
        }

        internal void Generate(StoreClientManager manager)
        {
            if (catalog == null || manager == null || !PlayerStore.HasInstance || StoreStation.Instance == null ||
                !ModHelper.IsNetworkUpgradeUnlocked(UpgradeId)) return;
            var state = Load();
            if (state == null) { Unlock(); state = Load(); }
            if (state == null || StoreStation.Instance.dayCounter < state.NextVisitDay ||
                manager.clientStack.Any(IsOurClient) || IsOurClient(PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint())) return;
            if (state.PendingOffers == null)
            {
                state.PendingVisitDay = StoreStation.Instance.dayCounter;
                state.PendingOffers = catalog.Roll(Guid.NewGuid().GetHashCode(), ProductionSpeed());
                state.PendingPortraitIndex = new System.Random(Guid.NewGuid().GetHashCode()).Next(textureKeys.Length);
                Save(state);
            }
            manager.AddClient(Create(state.PendingVisitDay, state.PendingOffers, false, state.PendingPortraitIndex));
        }

        internal StoreClient Create(int visitDay, IReadOnlyList<NightShiftOffer> offers, bool debug = false, int portraitIndex = 0)
        {
            var client = new StoreClient
            {
                identifier = ClientId,
                displayName = Text(textPrefix + "_name"),
                spriteName = PortraitKey(portraitIndex),
                nonShootable = true,
                clientIntent = StoreClient.ClientIntent.SELL,
                bubbleColor = "upper" // Native purple speech bubble and nameplate.
            };
            StoreClientFactionSetup.InitLowerLevelCitizen(client);
            client.clientItemFeatureBuying.Add("premiumBuy15");
            ModHelper.SetClientSource(client, ModConstants.ModId);
            client.AddBasicDialog();
            client.CompleteClientCreation();
            bool offered = false;
            ClientDialogueCatalog.Apply(client, ClientId).SetEndAction(() =>
            {
                if (catalog == null || offered || !PlayerStore.HasInstance ||
                    PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() != client) return;
                if (!debug)
                {
                    var state = Load();
                    if (state == null || state.PendingOffers == null || state.PendingVisitDay != visitDay) return;
                    // Commit the visit before placement callbacks can reenter.
                    state.NextVisitDay = StoreStation.Instance.dayCounter + 7;
                    state.PendingOffers = null;
                    Save(state);
                }
                offered = true;
                foreach (NightShiftOffer offer in offers)
                    PlayerStore.Instance.AddDirectSellingItemToTable(CreateItem(offer));
            });
            return client;
        }

        internal bool QueueDebug()
        {
            if (catalog == null || !PlayerStore.HasInstance || StoreStation.Instance == null ||
                PlayerStore.Instance.storeClientManager == null) return false;
            PlayerStore.Instance.storeClientManager.AddNextClient(Create(StoreStation.Instance.dayCounter,
                catalog.Roll(Guid.NewGuid().GetHashCode(), ProductionSpeed()), true, new System.Random(Guid.NewGuid().GetHashCode()).Next(textureKeys.Length)));
            return true;
        }

        private float ProductionSpeed()
        {
            float best = 0;
            if (EmporiumEntry.Instance == null) return best;
            foreach (GameItem machine in EmporiumEntry.Instance.GetAllItems())
            {
                if (!machine.IsTag(ServerState.ServerTag) || !GeneralHelper.IsItemOwned(machine) ||
                    !(machine.contentWindow?.childElement is MachineryPanelRow row)) continue;
                Items.Logic.DistributedTerminal.DistributedNetwork.Refresh(machine);
                GameItem model = ServerState.GetInstalledModel(machine);
                if (!AiModelState.CanProduce(model) || AiModelState.GetTaskType(model) != "computing" ||
                    machine.GetTagReadonly(ServerState.MemoryCapacityTag).GetLong() < AiModelState.GetMemoryGb(model)) continue;
                float work = (float)machine.GetTagReadonly(ServerState.WorkPerNightTag).GetDouble();
                if (work > best) best = work;
            }
            return best;
        }

        private GameItem CreateItem(NightShiftOffer offer, bool standardGpu = false)
        {
            GameItem item = standardGpu ? GpuRuntime.CreateStandardItem(offer.ItemId) : DirectoryMaster.Item(offer.ItemId);
            if (item == null) throw new InvalidOperationException("Night Shift item is not registered: " + offer.ItemId);
            try
            {
                if (offer.DataAmount >= 0 && item.identifier != ServerCatalog.ServerId) DataContentState.RestoreFrozenOffer(item, offer.DataAmount);
                if (offer.MemoryGb > 0)
                {
                    if (item.identifier == AiModelState.TrainedId)
                        AiModelState.ApplyTrained(item, "computing", offer.MemoryGb, offer.ComputeBonus, offer.Durability);
                    else if (item.identifier == AiModelState.UntrainedId)
                        AiModelState.Apply(item, new AiModelAttributes( "computing", offer.TrainingWork,
                            offer.MemoryGb, offer.ComputeBonus, offer.Durability, offer.TrainingData));
                }
                // Both periodic suppliers disclose random hardware defects honestly.
                if (GpuRuntime.IsGpu(item)) GpuLabels.MatchActualLabels(item);
                return item;
            }
            catch { item.Destroy(); throw; }
        }

        private void InstallGiftContents(GameItem machine, NightShiftOffer offer)
        {
            int charge = machine.identifier == ServerCatalog.ServerId ? -1 :
                machine.identifier == CompilerCatalog.CompilerId || machine.identifier == DiskWriterCatalog.DiskWriterId ? 30 : 0;
            if (charge == 0) return;
            // All three machines keep the native battery storage at index 0.
            // Prepare only newly delivered gifts, including previously pending gifts.
            if (!(machine.contentWindow?.childElement is MachineryPanelRow row) ||
                !(row.GetStorage(0) is GameSlotInventory slot) || slot.childItem != null)
                throw new InvalidOperationException("Night Shift gift battery slot is unavailable: " + machine.identifier);
            GameItem battery = DirectoryMaster.Item("energy_credit");
            if (battery == null) throw new InvalidOperationException("Native energy credit is not registered.");
            try
            {
                GeneralHelper.SetItemOwned(battery);
                PowerHelper.SetPowerSourceAt(battery, charge < 0 ? battery.GetTagReadonly("power_source_item_max_energy").GetInt() : charge);
                if (GraphUtils.TryAcceptAll(slot, battery) != 0)
                    throw new InvalidOperationException("Night Shift gift battery could not be installed: " + machine.identifier);
            }
            catch { battery.Destroy(); throw; }
            if (machine.identifier == ServerCatalog.ServerId && offer.DataAmount >= 0)
            {
                var containerSlot = row.GetStorage(8) as GameSlotInventory;
                if (containerSlot == null || containerSlot.childItem != null)
                    throw new InvalidOperationException("Starter server data slot is unavailable.");
                GameItem container = DirectoryMaster.Item("age_of_agi.data_container");
                try
                {
                    DataContentState.SetAmount(container, offer.DataAmount);
                    GeneralHelper.SetItemOwned(container);
                    if (GraphUtils.TryAcceptAll(containerSlot, container) != 0)
                        throw new InvalidOperationException("Starter server data container could not be installed.");
                }
                catch { container.Destroy(); throw; }
            }
        }

        private NightShiftState Load()
        {
            string json = ModHelper.GetModData(ModConstants.ModId, StateKey);
            if (string.IsNullOrEmpty(json)) return null;
            // Invalid state must never be silently reset: that could duplicate gifts.
            var state = JsonConvert.DeserializeObject<NightShiftState>(json);
            if (state == null || (state.Version != 1 && state.Version != 2) || state.NextVisitDay < 0 || state.Gifts == null ||
                state.PendingPortraitIndex < 0 || state.PendingPortraitIndex >= textureKeys.Length ||
                state.GiftIndex < 0 || state.GiftIndex > state.Gifts.Count ||
                state.Gifts.Concat(state.PendingOffers ?? new List<NightShiftOffer>()).Any(offer =>
                    offer == null || offer.ItemId == null || !offer.ItemId.StartsWith(ModConstants.ModId + ".", StringComparison.Ordinal)))
                throw new InvalidOperationException("Night Shift save data is invalid; visit and gifts left unchanged.");
            return state;
        }

        private void Save(NightShiftState state) => ModHelper.SetModData(ModConstants.ModId, StateKey, JsonConvert.SerializeObject(state));
        private bool IsOurClient(StoreClient client) => client != null && client.identifier == ClientId && client.sourceModId == ModConstants.ModId;
        private string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);

        internal string PortraitKey(int index) => textureKeys.Length == 1 ? SpriteId : SpriteId + "." + index;

        internal bool TryGetPortrait(int index, out Sprite sprite)
        {
            sprite = null;
            if (index < 0 || index >= textureKeys.Length) return false;
            LoadedMod mod = ModLoader.Mods.Find(value => value.Manifest.ID == ModConstants.ModId && value.IsEnabled);
            if (mod == null || !mod.Textures.TryGetValue(textureKeys[index], out var texture) || texture == null) return false;
            if (!portraits.TryGetValue(index, out sprite) || sprite == null || portraitTextures[index] != texture)
            {
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                Sprite reference = SpriteDict.Instance?.spriteDictionary?.Values.FirstOrDefault(value => value != null);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                    reference != null ? reference.pixelsPerUnit : 100f);
                portraits[index] = sprite;
                portraitTextures[index] = texture;
            }
            return true;
        }

        internal void Shutdown()
        {
            if (PlayerStore.HasInstance) PlayerStore.Instance.storeClientManager?.clientStack?.RemoveAll(IsOurClient);
            foreach (Sprite sprite in portraits.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            portraits.Clear(); portraitTextures.Clear(); catalog = null;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class NightShiftState
    {
        [JsonProperty] internal int Version = 2;
        [JsonProperty] internal int PendingPortraitIndex;
        [JsonProperty] internal int NextVisitDay;
        [JsonProperty] internal int GiftIndex;
        [JsonProperty] internal List<NightShiftOffer> Gifts;
        [JsonProperty] internal int PendingVisitDay;
        [JsonProperty] internal List<NightShiftOffer> PendingOffers;
    }
}
