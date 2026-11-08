using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Npcs;
using Newtonsoft.Json;
using UnityEngine;

internal static class NightShiftChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        NightShiftCatalog catalog = NightShiftCatalog.Load(items);
        CheckCsvConfiguration(catalog, items);
        int giftCount = catalog.GiftItems().Count;
        CheckStock(catalog, items);
        LoadedMod loaded = Program.PrepareLoadedMod(items);
        CheckBootLifecycle(loaded);
        var mod = new AgeOfAgiMod(); mod.Init(loaded.Manifest); mod.OnEnable();
        ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
        var nativeDirectory = new ModItemDirectory();
        nativeDirectory.Add("energy_credit", () =>
        {
            var battery = new GameItem { Id = "energy_credit" };
            battery.EnableTag("power_source_item");
            return battery;
        });
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry();
        // No load/new-game event: opening Wild must repair a missed registration.
        NightShiftWildOpenPatch.Prefix();
        SpriteDict.Instance = new SpriteDict(); SpriteDict.Instance.spriteDictionary.Add("native", new Sprite { pixelsPerUnit = 80 });
        try
        {
            var service = ModHelper.Services[NightShiftClient.UpgradeId];
            Assert(service.Cost == NightShiftClient.UnlockCost && service.Prerequisites.SequenceEqual(new[] { ElectronicsTrader.UpgradeId }) &&
                service.Icon == ModConstants.ModId + ":E_service_advanced", "Second-tier service requires entry industry and its own icon.");
            Assert(ModHelper.Services[ElectronicsTrader.UpgradeId].Icon == ModConstants.ModId + ":E_service",
                "Entry industry uses its own service icon.");
            ModHelper.Locale = "zh";
            Assert(service.Title() == "电子产业 II", "Resolve the second-tier title lazily.");
            var visible = ModHelper.Services.Values.Select(value => value.Definition)
                .Where(value => PlayerStore.Instance.networkUpgrade.ContainsKey(value.id)).ToList();
            var tabs = NetworkUpgradeLayout.Build(visible);
            var regular = tabs.Single(tab => tab.tab == "");
            var chain = regular.cards.Single(card => card.subtitle == "AgeOfAgi.Electronics").cells;
            Assert(tabs.Count == 1 && regular.cards[0].recommended && chain[0].id == ElectronicsTrader.UpgradeId &&
                chain[0].connectorToNext && chain[1].id == NightShiftClient.UpgradeId,
                "Native layout puts the connected electronics category on the regular page without creating another tab.");
            PlayerStore.Instance.networkUpgrade.Remove(NightShiftClient.UpgradeId);
            NightShiftWildOpenPatch.Prefix();
            Assert(PlayerStore.Instance.networkUpgrade.ContainsKey(NightShiftClient.UpgradeId), "Repair missing runtime entries as well as definitions.");
            NetworkUpgrade runtime = PlayerStore.Instance.networkUpgrade[NightShiftClient.UpgradeId];
            runtime.state = 2;
            ModHelper.Services.Remove(NightShiftClient.UpgradeId);
            NightShiftWildOpenPatch.Prefix();
            Assert(ReferenceEquals(runtime, PlayerStore.Instance.networkUpgrade[NightShiftClient.UpgradeId]) && runtime.state == 2,
                "Restoring a missing definition must preserve the purchased runtime entry.");
            runtime.state = 1;
            PlayerStore.Instance.networkUpgrade[ElectronicsTrader.UpgradeId].state = 1;
            PlayerStore.Instance.ModData.Remove(ModConstants.ModId + ":" + ElectronicsTrader.StateKey);
            ModHook.RaiseNewGame();
            StoreStation.Instance.dayCounter = 5;
            ModHook.RaiseGenerate(PlayerStore.Instance.storeClientManager);
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Count == 0, "Locked supplier must not arrive.");
            AgeOfAgi.Debugging.ModDebugMenu.Items.Actions[NightShiftClient.ClaimActionName]();
            Assert(ModLog.Entries.Last().Contains("尚未购买AI服务") &&
                ModHelper.GetModData(ModConstants.ModId, NightShiftClient.StateKey) == null && EmporiumEntry.Instance.GetAllItems().Count == 0,
                "Locked claim button must explain the purchase requirement without granting or unlocking anything.");
            foreach (var inv in Inventories()) inv.RejectTransfer = true;
            PlayerStore.Instance.UnlockedServices.Add(NightShiftClient.UpgradeId);
            PlayerStore.Instance.UnlockedServices.Add(ElectronicsTrader.UpgradeId);
            ModHelper.SetModData(ModConstants.ModId, ElectronicsTrader.StateKey, JsonConvert.SerializeObject(
                new NightShiftState { Gifts = new List<NightShiftOffer>(), NextVisitDay = 1001 }));
            NightShiftClient.ClaimGifts();
            Assert(State().GiftIndex == 0 && EmporiumEntry.Instance.GetAllItems().Count == 0, "Full inventories retain undelivered gifts.");
            Assert(ModLog.Entries.Last().Contains("清理库存空间"), "Explain why a full inventory prevented delivery.");
            EmporiumEntry.Instance.backInvinvElement.RejectTransfer = false;
            AgeOfAgi.Debugging.ModDebugMenu.Items.Actions[NightShiftClient.ClaimActionName]();
            var gifts = EmporiumEntry.Instance.GetAllItems();
            Assert(gifts.Count == giftCount && State().GiftIndex == giftCount && gifts.All(GeneralHelper.IsItemOwned), "Deliver the complete configured starter package.");
            Assert(ModLog.Entries.Last().Contains("本次发放 " + giftCount + " 件"), "A successful claim must report the delivered quantity.");
            Assert(gifts.Count(item => item.identifier == "age_of_agi.disk_empty") == 4, "Include the four configured blank disks.");
            Assert(gifts.Count(item => item.identifier == "age_of_agi.base_grid") == 1 &&
                DataContentState.Amount(gifts.Single(DataContentState.IsContainer)) == 60, "Include a basic base and full 60-unit data container.");
            foreach (var gift in new[] { ("age_of_agi.compiler", 30), ("age_of_agi.disk_writer", 30) })
            {
                var machine = gifts.Single(item => item.identifier == gift.Item1);
                var battery = ((GameSlotInventory)((MachineryPanelRow)machine.contentWindow.childElement).GetStorage(0)).childItem;
                Assert(battery != null && battery.identifier == "energy_credit" && GeneralHelper.IsItemOwned(battery) &&
                    PowerHelper.GetAvailableEnergyFromItem(battery) == gift.Item2, "Preinstall the owned starter battery in each machine's power slot.");
                PowerHelper.TryRemoveEnergy(battery, 7);
            }
            service.Unlock(); ModHook.RaiseLoaded(); ModHook.RaiseOpened();
            Assert(EmporiumEntry.Instance.GetAllItems().Count == giftCount, "Unlock/load/open cannot duplicate delivered gifts.");
            Assert(PowerHelper.GetAvailableEnergyFromItem(((GameSlotInventory)((MachineryPanelRow)gifts
                .Single(item => item.identifier == "age_of_agi.compiler").contentWindow.childElement).GetStorage(0)).childItem) == 23,
                "Load and repeated unlock callbacks must not recharge delivered batteries.");
            NightShiftClient.ClaimGifts();
            Assert(ModLog.Entries.Last().Contains("没有待领取物品"), "Explain an already-claimed starter package.");
            string savedState = ModHelper.GetModData(ModConstants.ModId, NightShiftClient.StateKey);
            ModHelper.SetModData(ModConstants.ModId, NightShiftClient.StateKey, "{broken");
            NightShiftClient.ClaimGifts();
            Assert(ModLog.Entries.Last().StartsWith("ERROR:") && ModLog.Entries.Last().Contains("领取AI服务物品失败"),
                "A corrupt record must produce a useful error rather than silently resetting the gift.");
            ModHelper.SetModData(ModConstants.ModId, NightShiftClient.StateKey, savedState);
            StoreStation.Instance.dayCounter = 6;
            ModHook.RaiseGenerate(PlayerStore.Instance.storeClientManager);
            ModHook.RaiseGenerate(PlayerStore.Instance.storeClientManager);
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Count == 1, "Queue next-day visit once.");
            string frozen = JsonConvert.SerializeObject(State().PendingOffers);
            // Native clientStack is not serialized. Rebuild the visit from mod data.
            PlayerStore.Instance.storeClientManager.clientStack.Clear();
            mod.OnDisable(); mod.OnEnable(); ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
            ModHook.RaiseLoaded();
            Assert(JsonConvert.SerializeObject(State().PendingOffers) == frozen &&
                PlayerStore.Instance.storeClientManager.clientStack.Count == 1, "Reload restores pending stock without rerolling or losing a visit.");
            var client = PlayerStore.Instance.storeClientManager.clientStack.Single();
            Assert(client.displayName == "夜班" && client.clientIntent == StoreClient.ClientIntent.SELL && client.nonShootable &&
                client.clientItemFeatureBuying.Contains("premiumBuy15"), "Use native protected supplier trade and difficulty-dependent markup.");
            client.mainDialogue.endAction();
            Assert(EmporiumEntry.Instance.frontInvinvElement.childItems.Count == 0, "Stale dialogue cannot place goods for another customer.");
            PlayerStore.Instance.storeClientManager.clientStack.Clear();
            PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = client };
            EmporiumEntry.Instance.frontInvinvElement.RejectTransfer = false;
            client.mainDialogue.endAction();
            var stock = EmporiumEntry.Instance.frontInvinvElement.childItems;
            Assert(stock.Count >= 12 && stock.All(item => !GeneralHelper.IsItemOwned(item)), "Offer goods for purchase, without gifting supplier stock.");
            Assert(((GameSlotInventory)((MachineryPanelRow)stock.Single(item => item.identifier == "age_of_agi.serverbox")
                .contentWindow.childElement).GetStorage(0)).childItem == null, "Weekly stock remains an empty server chassis.");
            Assert(stock.Count(item => item.identifier == "age_of_agi.disk_empty") == 6 &&
                stock.Count(item => DataContentState.IsContainer(item) && DataContentState.Amount(item) == 60 && item.GetBaseValue() == 300) == 2 &&
                stock.Count(item => item.identifier == "age_of_agi.dc_enterprise_databox" && DataContentState.Amount(item) == 400) == 1,
                "Guaranteed quantities, full-container value and normal enterprise raw data.");
            int count = stock.Count; client.mainDialogue.endAction();
            Assert(EmporiumEntry.Instance.frontInvinvElement.childItems.Count == count && State().NextVisitDay == 13, "Repeated dialogue cannot duplicate stock.");
            PlayerStore.Instance.currentClientInstance = null;
            StoreStation.Instance.dayCounter = 12; ModHook.RaiseGenerate(PlayerStore.Instance.storeClientManager);
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Count == 0, "No early weekly visit.");
            StoreStation.Instance.dayCounter = 13; ModHook.RaiseGenerate(PlayerStore.Instance.storeClientManager);
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Count == 1, "Next visit arrives after seven days.");
            Sprite portrait = null;
            Assert(!NightShiftPortraitPatch.Prefix(NightShiftClient.SpriteId, ref portrait) && portrait.pixelsPerUnit == 80 &&
                portrait.texture == loaded.Textures["night_shifter"], "Use the original portrait at native scale.");
            Assert(portrait.rect.width == portrait.texture.width && portrait.rect.height == portrait.texture.height,
                "Use the complete editable runtime portrait, independently of the original item/art reference image.");
            var foreign = new StoreClient { identifier = "other" }; PlayerStore.Instance.storeClientManager.clientStack.Add(foreign);
            mod.OnDisable();
            NightShiftWildOpenPatch.Prefix();
            Assert(PlayerStore.Instance.storeClientManager.clientStack.SequenceEqual(new[] { foreign }) && portrait.Destroyed &&
                !ModHelper.Services.ContainsKey(NightShiftClient.UpgradeId), "Disable cleans owned resources and a remaining UI patch cannot register the service again.");
        }
        finally
        {
            mod.OnDisable(); PlayerStore.Instance = null; EmporiumEntry.Instance = null; SpriteDict.Instance = null;
            nativeDirectory.Remove("energy_credit");
            ModHelper.Translations.Clear(); ModHelper.Locale = "en";
        }
        Console.WriteLine("PASS: Night Shift Wild unlock, retained one-time gifts, weekly native supply, deterministic saved stock, quantities/value, portrait and cleanup.");
    }

    private static void CheckBootLifecycle(LoadedMod loaded)
    {
        PlayerStore.Instance = null;
        PlayerStore.InstanceReads = 0;
        PlayerStore.ForbidLazyCreation = true;
        var mod = new AgeOfAgiMod(); mod.Init(loaded.Manifest);
        try
        {
            mod.OnEnable(); mod.OnEnable();
            ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
            ModHook.RaiseLoaded(); ModHook.RaiseNewGame(); ModHook.RaiseOpened();
            NightShiftWildOpenPatch.Prefix();
            NightShiftClient.ClaimGifts();
            Assert(ModLog.Entries.Last().Contains("请先进入店铺存档"), "Claiming without a loaded store must explain the requirement.");
            NightShiftClient.Generate(new StoreClientManager());
            Assert(!NightShiftClient.QueueDebug(), "A main-menu debug call must not construct PlayerStore.");
            NightShiftClient.Unlock();
            Assert(!ModHelper.Services.ContainsKey(NightShiftClient.UpgradeId), "Defer service registration until a real run exists.");
            mod.OnDisable();
            Assert(PlayerStore.InstanceReads == 0, "Enable, catalog setup and shutdown must not read the lazy PlayerStore getter before scene setup.");
        }
        finally { mod.OnDisable(); PlayerStore.ForbidLazyCreation = false; }
    }

    private static void CheckStock(NightShiftCatalog catalog, IReadOnlyList<ItemDefinition> items)
    {
        int machines = 0, chips = 0, rare = 0;
        var upgrades = new HashSet<string>();
        for (int seed = 0; seed < 10000; seed++)
        {
            var stock = catalog.Roll(seed, 300);
            Assert(JsonConvert.SerializeObject(stock) == JsonConvert.SerializeObject(catalog.Roll(seed, 300)), "A seed determines stock.");
            Assert(stock.Count(offer => offer.ItemId == "age_of_agi.serverbox") == 1 &&
                stock.Count(offer => offer.ItemId == "age_of_agi.data_container" && offer.DataAmount == 60) == 2 &&
                stock.Count(offer => offer.ItemId == "age_of_agi.data_container" && offer.DataAmount == 0) == 1 &&
                stock.Count(offer => offer.ItemId == "age_of_agi.disk_empty") == 6 &&
                new[] { "base_grid", "base_neural", "base_tsfm", "base_jepa" }.All(id =>
                    stock.Count(offer => offer.ItemId == "age_of_agi." + id) == 1), "Guaranteed stock on every visit, including every base model.");
            int machineCount = stock.Count(offer => offer.ItemId == "age_of_agi.disk_writer" || offer.ItemId == "age_of_agi.compiler");
            int chipCount = stock.Count(offer => offer.ItemId.EndsWith("_chip", StringComparison.Ordinal));
            Assert(machineCount <= 1 && chipCount <= 1, "Alternative stock rows are mutually exclusive.");
            machines += machineCount; chips += chipCount;
            rare += stock.Count(offer => offer.ItemId == "age_of_agi.btc_rare_unfinish");
            upgrades.Add(stock.Single(offer => offer.ItemId.StartsWith(NetworkSwitchUpgrades.ModuleIdPrefix, StringComparison.Ordinal)).ItemId);
            Assert(!catalog.Roll(seed, 299).Any(offer => offer.ItemId == "age_of_agi.btc_rare_unfinish"), "Rare BTC waits for a computing server with production speed 300.");
        }
        Assert(machines > 6500 && machines < 7500 && chips > 7000 && chips < 8000 && rare > 700 && rare < 1300 && upgrades.Count == 4,
            "Configured probabilities and all four upgrade choices are exercised.");
        var root = CsvFixture.Parse(File.ReadAllText(Path.Combine(ModHelper.Root, NightShiftCatalog.Path)));
        root.Rows.First(row => row["section"] == "stock")["chance"] = "101";
        try { NightShiftCatalog.Parse(root.ToString(), items); throw new Exception("Invalid stock chance accepted."); }
        catch (InvalidOperationException) { }
    }

    private static void CheckCsvConfiguration(NightShiftCatalog catalog, IReadOnlyList<ItemDefinition> items)
    {
        string csv = File.ReadAllText(Path.Combine(ModHelper.Root, NightShiftCatalog.Path));
        var source = CsvFixture.Parse(csv);
        foreach (string column in source.Columns)
        {
            var missing = CsvFixture.Parse(csv);
            missing.RemoveColumn(column);
            Reject(missing.ToString());
        }
        foreach (var change in new[]
        {
            ("section", "unknown"), ("itemIds", ""), ("itemIds", "age_of_agi.missing"),
            ("count", "0"), ("count", "101"), ("count", "1.5"), ("count", "2147483648"),
            ("chance", "-1"), ("chance", "101"), ("dataAmount", "-2"),
            ("minimumProductionSpeed", "-1"), ("minimumProductionSpeed", "NaN"),
            ("minimumProductionSpeed", "Infinity"), ("minimumProductionSpeed", "1e100")
        })
            Reject(CsvFixture.Cell(csv, change.Item1, change.Item2));
        foreach (var change in new[]
        {
            ("chance", "99"), ("minimumProductionSpeed", "1"),
            ("itemIds", "age_of_agi.disk_writer;age_of_agi.compiler"), ("dataAmount", "0")
        })
            Reject(CsvFixture.Cell(csv, change.Item1, change.Item2));
        foreach (string section in new[] { "gifts", "stock" })
        {
            var missing = CsvFixture.Parse(csv);
            missing.Rows.RemoveAll(row => row["section"] == section);
            Reject(missing.ToString());
        }
        var pool = CsvFixture.Parse(csv);
        pool.Rows.First(row => row["itemIds"].Contains(';'))["itemIds"] += ";";
        Reject(pool.ToString());
        var unknown = CsvFixture.Parse(csv);
        unknown.AddColumn("typo", "0");
        Reject(unknown.ToString());
        Reject("");
        source.Columns.Reverse();
        var reordered = NightShiftCatalog.Parse("\uFEFF# comment\r\n" + source.ToString().Replace("\n", "\r\n"), items);
        Assert(catalog.GiftItems().Select(offer => (offer.ItemId, offer.DataAmount))
            .SequenceEqual(reordered.GiftItems().Select(offer => (offer.ItemId, offer.DataAmount))), "CSV preserves gift order and data.");
        Assert(catalog.Roll(1234, 300).Select(offer => (offer.ItemId, offer.DataAmount))
            .SequenceEqual(reordered.Roll(1234, 300).Select(offer => (offer.ItemId, offer.DataAmount))), "Header order does not alter seeded stock.");

        void Reject(string invalid)
        {
            try { NightShiftCatalog.Parse(invalid, items); }
            catch (InvalidOperationException exception)
            {
                Assert(exception.Message.Contains(NightShiftCatalog.Path), "Night Shift CSV errors identify the file.");
                return;
            }
            throw new Exception("Invalid Night Shift CSV accepted.");
        }
    }

    private static NightShiftState State() => JsonConvert.DeserializeObject<NightShiftState>(ModHelper.GetModData(ModConstants.ModId, NightShiftClient.StateKey));
    private static IEnumerable<GameGridInventory> Inventories() => new[] { EmporiumEntry.Instance.backInvinvElement,
        EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.showcaseElement, EmporiumEntry.Instance.frontInvinvElement };
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
