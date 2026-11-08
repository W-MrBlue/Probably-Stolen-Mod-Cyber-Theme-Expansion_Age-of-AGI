using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Npcs;
using System;
using System.Collections.Generic;
using System.Linq;

internal static class ElectronicsEventChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        CheckStockValidation(items);
        var loaded = Program.PrepareLoadedMod(items);
        foreach (string locale in new[] { "zh", "en" }) ModHelper.Translations[locale] = Program.ReadLocale(locale);
        AddSyntheticTranslations();
        var mod = new AgeOfAgiMod();
        mod.Init(loaded.Manifest);
        mod.OnEnable();
        PlayerStore.Instance = new PlayerStore { Attractiveness = 400 };
        EmporiumEntry.Instance = new EmporiumEntry();
        StoreStation.Instance.dayCounter = 20;
        try
        {
            ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
            Assert(StoreEventManager.normalEventBlueprints.Any(blueprint => blueprint.identifier == ElectronicsEvents.CryptoBoomId) &&
                StoreEventActionDict.actions.ContainsKey(ElectronicsEvents.CryptoBoomId + "addClientAction"),
                "Market events register their weighted blueprints and daily client actions.");
            ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
            Assert(StoreEventManager.normalEventBlueprints.Count(blueprint => blueprint.identifier == ElectronicsEvents.CryptoBoomId) == 1,
                "Repeated scene registration cannot duplicate an event blueprint.");

            string debugEventName = "Spawn " + ElectronicsEvents.CryptoBoomId.Substring(ModConstants.ModId.Length + 1);
            ModHelper.DebugActions[AgeOfAgi.Debugging.ModDebugMenu.Events.Title]();
            CustomUIWindow.Last.Get(debugEventName).Click();
            Assert(StoreStation.Instance.storeEventManager.Activated.Last().identifier == ElectronicsEvents.CryptoBoomId &&
                PlayerStore.Instance.storeClientManager.clientStack.Count == 0,
                "The event group dispatches to native event activation rather than spawning a visitor directly.");

            PlayerStore.Instance = null;
            int activatedCount = StoreStation.Instance.storeEventManager.Activated.Count;
            CustomUIWindow.Last.Get(debugEventName).Click();
            Assert(!PlayerStore.HasInstance && StoreStation.Instance.storeEventManager.Activated.Count == activatedCount,
                "Debug actions before scene setup must not construct PlayerStore or activate events.");
            PlayerStore.Instance = new PlayerStore { Attractiveness = 400 };

            var spec = new ElectronicsEvents.EventSpec("age_of_agi.fixture_market_event", "fixture_market_event",
                new[] { ElectronicsEventClients.CryptoFanId, ElectronicsEventClients.CryptoSecondhandId },
                new ElectronicsEvents.Modifier("fixture_market_type", 17));
            StoreEvent storeEvent = ElectronicsEvents.CreateEvent(spec);
            Assert(storeEvent.identifier == spec.Id && storeEvent.addClientFromEventActionId == spec.ActionId &&
                storeEvent.negociationDatas.Single().itemType == "fixture_market_type" &&
                storeEvent.negociationDatas.Single().value == 17 && storeEvent.duration > 0,
                "Event construction maps an independent type modifier and daily action without depending on balance tables.");
            spec.QueueClient();
            StoreClient queued = PlayerStore.Instance.storeClientManager.clientStack.Single();
            Assert(queued.eventSourceId == spec.Id && spec.ClientIds.Contains(queued.identifier),
                "A daily event action queues one configured visitor and preserves its source event.");
            PlayerStore.Instance.storeClientManager.clientStack.Clear();

            CheckPredicates();
            CheckConditionalSeller();
            CheckStockDelivery();

            StoreEventActionDict.actions[ElectronicsEvents.CryptoBoomId + "addClientAction"]();
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Single().eventSourceId == ElectronicsEvents.CryptoBoomId,
                "The registered native action queues a visitor belonging to its event.");
        }
        finally
        {
            mod.OnDisable();
            Assert(!StoreEventManager.normalEventBlueprints.Any(blueprint => blueprint.identifier == ElectronicsEvents.CryptoBoomId) &&
                !StoreEventActionDict.actions.ContainsKey(ElectronicsEvents.CryptoBoomId + "addClientAction") &&
                PlayerStore.Instance.storeClientManager.clientStack.All(client => client.eventSourceId != ElectronicsEvents.CryptoBoomId),
                "Disable removes owned blueprints, actions and queued event visitors.");
            PlayerStore.Instance.currentClientInstance = null;
            PlayerStore.Instance = null;
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
        Console.WriteLine("PASS: electronics event registration, isolated price modifiers, purchase rules, conditional food trading, frozen stock and owned cleanup.");
    }

    private static void CheckPredicates()
    {
        GameItem unfinished = DirectoryMaster.Item("age_of_agi.btc_normal_unfinish");
        GameItem finished = DirectoryMaster.Item("age_of_agi.btc_normal");
        unfinished.SetGameItemType("crypto");
        finished.SetGameItemType("crypto");
        GameItem gpu = DirectoryMaster.Item("age_of_agi.gpu0");
        GameItem raw = DirectoryMaster.Item("age_of_agi.dc_common_data_disk");
        GameItem container = DirectoryMaster.Item("age_of_agi.data_container");
        GameItem part = DirectoryMaster.Item("age_of_agi.capacitors");
        GameItem model = DirectoryMaster.Item("age_of_agi.ai_trained");
        GameItem code = DirectoryMaster.Item("age_of_agi.part_enc_0");
        Assert(ElectronicsEventClients.AcceptsCrypto(unfinished) && ElectronicsEventClients.AcceptsCrypto(finished),
            "Crypto purchases treat finished and unfinished BTC equally.");
        Assert(ElectronicsEventClients.AcceptsCryptoAndGpu(gpu) && ElectronicsEventClients.AcceptsCryptoAndGpu(unfinished) &&
            !ElectronicsEventClients.AcceptsCryptoAndGpu(raw), "Crypto/GPU purchases reject unrelated electronics.");
        Assert(ElectronicsEventClients.AcceptsAstarGoods(raw) && ElectronicsEventClients.AcceptsAstarGoods(gpu) &&
            ElectronicsEventClients.AcceptsAstarGoods(container) && ElectronicsEventClients.AcceptsAstarGoods(unfinished) &&
            !ElectronicsEventClients.AcceptsAstarGoods(model) && !ElectronicsEventClients.AcceptsAstarGoods(code),
            "Astar buyers accept all affected electronics and containers while excluding unaffected models and code parts.");
        Assert(ElectronicsEventClients.AcceptsHardwareAndParts(gpu) && ElectronicsEventClients.AcceptsHardwareAndParts(part) &&
            !ElectronicsEventClients.AcceptsHardwareAndParts(raw) && !ElectronicsEventClients.AcceptsHardwareAndParts(container),
            "Repair purchases combine functional hardware and electronic parts only.");
        unfinished.EnableTag("NOT_FOR_RESALE");
        Assert(!ElectronicsEventClients.AcceptsCrypto(unfinished), "Native resale restrictions remain authoritative.");
        unfinished.DisableTag("NOT_FOR_RESALE");
    }

    private static void CheckConditionalSeller()
    {
        StoreClient client = ElectronicsEventClients.CreateClient(ElectronicsEventClients.CryptoDesperateSellerId);
        GameItem food = DirectoryMaster.Item("age_of_agi.btc_normal");
        food.SetGameItemType("FOOD");
        Assert(client.isOfferWholesale && client.clientIntent == StoreClient.ClientIntent.SELLNBUY &&
            !client.willAcceptItem(food), "The desperate investor offers wholesale BTC but cannot buy food before a real BTC sale.");
        client.onItemSold(DirectoryMaster.Item("age_of_agi.btc_normal_unfinish"), 1);
        Assert(client.willAcceptItem(food), "Buying at least one unfinished normal BTC unlocks food purchases for the current visit.");
        StoreClient nextVisit = ElectronicsEventClients.CreateClient(ElectronicsEventClients.CryptoDesperateSellerId);
        Assert(!nextVisit.willAcceptItem(food), "Food unlock state is not shared with a later visit.");
        foreach (string id in ElectronicsEventClients.SellerIds)
            Assert(ElectronicsEventClients.CreateClient(id).isOfferWholesale,
                "Every event seller supports the native whole-lot discount, including sellers who do not mention it.");
    }

    private static void CheckStockDelivery()
    {
        var inventory = EmporiumEntry.Instance.frontInvinvElement;
        inventory.childItems.ToList().ForEach(item => item.Destroy());
        StoreClient client = ElectronicsEventClients.CreateClient(ElectronicsEventClients.FearDataMerchantId);
        Dialogue delivery = client.mainDialogue;
        while (delivery.nextDialogue != null) delivery = delivery.nextDialogue;
        Assert(client != null && PlayerStore.Instance != null && delivery.endAction != null,
            "The event seller must bind stock delivery to the final dialogue line.");
        PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = client };
        delivery.endAction();
        GameItem delivered = inventory.childItems.Single();
        int expected = (int)Math.Round(DataContentState.Capacity(delivered) * 0.25, MidpointRounding.AwayFromZero);
        Assert(DataContentState.Amount(delivered) == expected,
            "The independent seller fixture freezes the configured fraction of container data.");
        delivery.endAction();
        Assert(inventory.childItems.Count == 1, "Event seller dialogue cannot deliver the same stock twice.");
        inventory.childItems.ToList().ForEach(item => item.Destroy());
        PlayerStore.Instance.currentClientInstance = null;
    }

    private static void CheckStockValidation(IReadOnlyList<ItemDefinition> items)
    {
        const string header = "seller,item_id,count,data_fraction\n";
        string valid = header +
            "age_of_agi.crypto_crash_desperate,age_of_agi.btc_normal_unfinish,2,-1\n" +
            "age_of_agi.crypto_crash_angry_upper,age_of_agi.btc_rare_unfinish,1,-1\n" +
            "age_of_agi.ai_fear_angry_lower,age_of_agi.gpu0,1,-1\n" +
            "age_of_agi.ai_fear_data_merchant,age_of_agi.data_container,1,0.25\n" +
            "age_of_agi.ai_fear_astar_researcher,age_of_agi.hypo_data_container,1,0.75\n";
        var catalog = ElectronicsEventStockCatalog.Parse(items, valid, "fixture.csv");
        Assert(catalog.GetOffers(ElectronicsEventClients.CryptoDesperateSellerId).Count == 2 &&
            catalog.GetOffers(ElectronicsEventClients.FearDataMerchantId).Single().State == "fraction",
            "The stock parser expands explicit quantities and preserves independent data fractions.");
        ExpectInvalid(items, header + "other.seller,age_of_agi.gpu0,1,-1\n", "Unknown event seller");
        ExpectInvalid(items, header + "age_of_agi.crypto_crash_desperate,age_of_agi.gpu0,1,0.5\n", "Only data carriers");
        ExpectInvalid(items, header + "age_of_agi.crypto_crash_desperate,age_of_agi.gpu0,0,-1\n", "count must be positive");
    }

    private static void ExpectInvalid(IReadOnlyList<ItemDefinition> items, string csv, string message)
    {
        try
        {
            ElectronicsEventStockCatalog.Parse(items, csv, "invalid.csv");
            throw new Exception("Expected stock validation failure: " + message);
        }
        catch (Exception exception) when (exception.Message.Contains(message)) { }
    }

    private static void AddSyntheticTranslations()
    {
        string[] clientIds =
        {
            ElectronicsEventClients.CryptoFanId, ElectronicsEventClients.CryptoSecondhandId, ElectronicsEventClients.CryptoUpperId,
            ElectronicsEventClients.AstarFanId, ElectronicsEventClients.AstarInvestorId, ElectronicsEventClients.AstarDesperateId,
            ElectronicsEventClients.ElectronicsEnthusiastId, ElectronicsEventClients.ElectronicsRepairOwnerId,
            ElectronicsEventClients.ElectronicsHoarderId
        };
        clientIds = clientIds.Concat(ElectronicsEventClients.SellerIds).ToArray();
        foreach (string locale in new[] { "zh", "en" })
        {
            var entries = ModHelper.Translations[locale];
            foreach (string id in clientIds) entries[id + "_name"] = id;
            entries["fixture_market_event_news"] = "Fixture event";
            entries["fixture_market_event_display"] = "Fixture event";
            entries["fixture_market_event_desc"] = "Fixture event description";
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
