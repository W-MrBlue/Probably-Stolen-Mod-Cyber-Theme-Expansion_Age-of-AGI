using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Logic.ElectronicStorage;
using AgeOfAgi.Npcs;

internal static class ElectronicStorageChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        var mod = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(mod.Manifest);
        var directory = new ModItemDirectory();
        registration.Register(directory, definitions);
        try
        {
            CheckNativeTargets();
            CheckDiskBox();
            CheckCodeContainer();
            CheckRestore();
            CheckPackingFailure(directory);
            CheckConfiguredStock(definitions);
            CheckJackson();
        }
        finally
        {
            SaveManager.IsDecoding = false;
            JacksonCodeStorageStock.Shutdown();
            registration.Unregister();
            PlayerStore.ForbidLazyCreation = false;
            PlayerStore.Instance = null;
        }
        Console.WriteLine("PASS: electronic storage native filters, child-value conservation, decode without refill, packing rollback, configured disk packages and every Jackson supply path.");
    }

    private static GameGridInventory Inventory(GameItem item) => (GameGridInventory)item.contentWindow.childElement.GetStorage(0);

    private static void CheckDiskBox()
    {
        GameItem box = DirectoryMaster.Item(ElectronicStorageRuntime.DiskBoxId);
        GameGridInventory inventory = Inventory(box);
        var contents = inventory.childItems;
        Assert(contents.Count > 0 && contents.All(item => item.identifier == ElectronicStorageRuntime.EmptyDiskId && item.unitCount == 1),
            "New disk packages contain real blank disk instances.");
        Assert(contents.Distinct().Count() == contents.Count && box.shortDescription == "" && box.lateUnitValue == 0,
            "No placeholder contents, descriptions or duplicate derived content values.");
        Assert(box.GetBaseValue() == box.unitValue + contents.Sum(item => item.GetCurrentValue()),
            "Native price aggregation adds exactly the shell and its actual contents.");
        GameItem disk = contents[0];
        long total = box.GetBaseValue();
        long oldDiskValue = disk.GetCurrentValue();
        inventory.Remove(disk);
        Assert(box.GetBaseValue() == total - disk.GetCurrentValue(), "Taking a disk removes only that disk's value.");
        disk.SetValue(37);
        Put(inventory, disk);
        Assert(box.GetBaseValue() == total - oldDiskValue + 37,
            "Returned disks use their saved instance prices.");
        Assert(!inventory.mayInventoryAddItemFunc(new GameItem { Id = "unrelated" }, inventory), "Only blank disk IDs are accepted.");
        box.onLoaded(box); box.onLoaded(box);
        Assert(inventory.childItems.SequenceEqual(contents.Skip(1).Concat(new[] { disk })), "Load callbacks do not refill a partially used package.");

        var nativeStorage = new GameItem(); nativeStorage.SetGameItemType("STORAGE");
        var nativeInventory = new GameGridInventory(5, 5) { ParentItem = nativeStorage };
        ContainerHelper.InitContainerItem(nativeInventory, nativeStorage);
        Assert(nativeInventory.mayInventoryAddItemFunc(box, nativeInventory), "A disk package can be kept in native storage.");
    }

    private static void CheckCodeContainer()
    {
        GameItem container = DirectoryMaster.Item(ElectronicStorageRuntime.CodeContainerId);
        var inventory = Inventory(container);
        Assert(container.GetGameItemType().SequenceEqual(new[] { "STORAGE" }) && inventory.childItems.Count == 0,
            "Code storage starts empty and uses the native storage category.");
        foreach (string id in new[] { "part_enc_0", "base_grid", "ai_trained", "ai_untrained" })
        {
            GameItem item = DirectoryMaster.Item("age_of_agi." + id);
            Put(inventory, item);
            inventory.Remove(item);
        }
        foreach (string id in new[] { "disk_empty", "disk_box", "cpu0", "serverbox", "code_container", "part_enc_chip" })
            Assert(!GraphUtils.CanAccept(inventory, DirectoryMaster.Item("age_of_agi." + id)), "Reject unrelated or nested items: " + id);
        var code = new GameItem { Id = "fixture.code" }; code.SetGameItemType("codeparts");
        code.Owned = false;
        Assert(!GraphUtils.CanAccept(inventory, code), "Do not place unpurchased contents into owned storage.");
        code.Owned = true; container.Owned = false;
        Assert(!GraphUtils.CanAccept(inventory, code), "Do not place owned contents into unpurchased storage.");
    }

    private static void CheckRestore()
    {
        int created = ItemDirectory.CreatedItems;
        SaveManager.IsDecoding = true;
        GameItem box;
        try { box = DirectoryMaster.Item(ElectronicStorageRuntime.DiskBoxId); }
        finally { SaveManager.IsDecoding = false; }
        var inventory = Inventory(box);
        Assert(ItemDirectory.CreatedItems == created + 1 && inventory.childItems.Count == 0, "Decoding creates no initial contents.");
        foreach (int price in new[] { 7, 31, 53 })
        {
            GameItem saved = DirectoryMaster.Item(ElectronicStorageRuntime.EmptyDiskId);
            saved.SetValue(price);
            Put(inventory, saved);
        }
        box.SetValue(61);
        var savedChildren = inventory.childItems;
        box.onLoaded(box); box.onLoaded(box);
        Assert(box.unitValue == 61 && box.GetBaseValue() == 61 + 7 + 31 + 53 && inventory.childItems.SequenceEqual(savedChildren),
            "Current-format restore retains saved shell price, child identities and values without repacking.");
        foreach (var disk in savedChildren) inventory.Remove(disk);
        box.onLoaded(box);
        Assert(inventory.childItems.Count == 0 && box.GetBaseValue() == 61, "Empty saved boxes stay empty.");
    }

    private static void CheckPackingFailure(ModItemDirectory directory)
    {
        var factory = directory.Factories[ElectronicStorageRuntime.EmptyDiskId];
        int calls = 0, created = ItemDirectory.CreatedInstances.Count;
        directory.Factories[ElectronicStorageRuntime.EmptyDiskId] = () => ++calls == 3
            ? throw new InvalidOperationException("packing fixture failure") : factory();
        try
        {
            try { DirectoryMaster.Item(ElectronicStorageRuntime.DiskBoxId); throw new Exception("Expected packing failure."); }
            catch (InvalidOperationException exception) when (exception.Message == "packing fixture failure") { }
            Assert(ItemDirectory.CreatedInstances.Skip(created).All(item => item.Destroyed), "Packing failures destroy the shell and all partial contents.");
        }
        finally { directory.Factories[ElectronicStorageRuntime.EmptyDiskId] = factory; }
    }

    private static void CheckConfiguredStock(IReadOnlyList<ItemDefinition> definitions)
    {
        const string csv = "section,itemIds,count,chance,dataAmount,minimumProductionSpeed,memoryGb,computeBonus,durability,trainingWork,trainingData\n" +
            "gifts,age_of_agi.disk_empty,3,100,-1,0,0,0,0,0,0\nstock,age_of_agi.disk_box,2,100,-1,0,0,0,0,0,0\n";
        var catalog = NightShiftCatalog.Parse(csv, definitions);
        var offers = catalog.Roll(17, 0);
        Assert(offers.Count == 2 && offers.All(offer => offer.ItemId == ElectronicStorageRuntime.DiskBoxId) &&
            catalog.GiftItems().Count == 3 && catalog.GiftItems().All(offer => offer.ItemId == ElectronicStorageRuntime.EmptyDiskId),
            "Independent stock quantities can offer packages while retaining loose disks in gifts.");
    }

    private static void CheckJackson()
    {
        PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry();
        JacksonCodeStorageStock.Enable(); JacksonCodeStorageStock.Enable();
        var stock = EmporiumEntry.Instance.frontInvinvElement;
        ModHook.RaiseInventorStock(new List<GameItem>());
        Assert(stock.childItems.Count == 0, "Locked electronics industry prevents code storage stock.");
        PlayerStore.Instance.UnlockedServices.Add(ElectronicsTrader.UpgradeId);
        ModHook.RaiseInventorStock(new List<GameItem>());
        Assert(stock.childItems.Count == 0, "Tier one alone does not unlock code storage stock.");
        PlayerStore.Instance.UnlockedServices.Add(NightShiftClient.UpgradeId);
        ModHook.RaiseInventorStock(new List<GameItem>());
        ModHook.RaiseInventorStock(new List<GameItem>());
        Assert(stock.childItems.Count == 1 && !GeneralHelper.IsItemOwned(stock.childItems.Single()), "Regular/special stock adds one paid code container without duplicate subscriptions.");
        GameItem purchased = stock.childItems.Single();
        purchased.Owned = true;
        ModHook.RaiseInventorStock(new List<GameItem>());
        Assert(stock.childItems.Count == 1, "An owned code container on the counter blocks replenishment.");
        stock.Remove(purchased);
        foreach (var location in new[] { EmporiumEntry.Instance.backInvinvElement, EmporiumEntry.Instance.invElement,
            EmporiumEntry.Instance.showcaseElement })
        {
            location.UncheckedAccept(purchased);
            ModHook.RaiseInventorStock(new List<GameItem>());
            Assert(stock.childItems.Count == 0, "Live ownership checks cover every native inventory regardless of the hook snapshot.");
            location.Remove(purchased);
        }
        var holder = new GameItem();
        _ = new ElectronicStorageRuntime(holder, 4, 4, new List<string>());
        Inventory(holder).UncheckedAccept(purchased);
        EmporiumEntry.Instance.invElement.UncheckedAccept(holder);
        ModHook.RaiseInventorStock(new List<GameItem>());
        Assert(stock.childItems.Count == 0, "Nested owned code containers also block supply.");
        EmporiumEntry.Instance.invElement.Remove(holder);
        ModHook.RaiseInventorStock(new List<GameItem>());
        Assert(stock.childItems.Count == 1, "After ownership ends, eligible inventory refresh always supplies a replacement.");
        foreach (var create in new Func<StoreClient>[] { StoreClientList.CreateInventorStorage,
            StoreClientListStory.CreateInventorIntro, StoreClientListStory.CreateInventorModuleIntro })
        {
            foreach (var eligibility in new[] { (Unlocked: false, Owned: false), (Unlocked: true, Owned: true), (Unlocked: true, Owned: false) })
            {
                if (eligibility.Unlocked) PlayerStore.Instance.UnlockedServices.Add(NightShiftClient.UpgradeId);
                else PlayerStore.Instance.UnlockedServices.Remove(NightShiftClient.UpgradeId);
                if (eligibility.Owned) EmporiumEntry.Instance.backInvinvElement.UncheckedAccept(purchased);
                foreach (var item in stock.childItems) stock.Remove(item);
                var client = create();
                var head = client.mainDialogue;
                var tail = head.NextDialogue();
                int nativeCalls = 0;
                Action native = () => nativeCalls++;
                tail.endAction = native;
                JacksonCodeStorageVisitPatch.Postfix(client); JacksonCodeStorageVisitPatch.Postfix(client);
                Assert(ReferenceEquals(head, client.mainDialogue), "Retain the native dialogue head.");
                PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = client };
                tail.endAction(); tail.endAction();
                bool shouldOffer = eligibility.Unlocked && !eligibility.Owned;
                Assert(nativeCalls == 2 && stock.childItems.Count == (shouldOffer ? 1 : 0) && stock.childItems.All(item => !item.Owned),
                    "Every visit supplies a paid item exactly when tier two is unlocked and none is owned, retaining native callbacks.");
                JacksonCodeStorageStock.Shutdown();
                Assert(tail.endAction == native, "Disable restores only the mod-owned callback wrapper.");
                JacksonCodeStorageStock.Enable();
                if (eligibility.Owned) EmporiumEntry.Instance.backInvinvElement.Remove(purchased);
            }
        }
        JacksonCodeStorageStock.Shutdown();
        foreach (var item in stock.childItems) stock.Remove(item);
        ModHook.RaiseInventorStock(new List<GameItem>());
        Assert(stock.childItems.Count == 0, "Disable removes stock injection.");
        PlayerStore.Instance = null; PlayerStore.ForbidLazyCreation = true;
        int reads = PlayerStore.InstanceReads;
        JacksonCodeStorageStock.Enable(); JacksonCodeStorageStock.Offer(null); JacksonCodeStorageStock.Shutdown();
        Assert(PlayerStore.InstanceReads == reads, "Menu and cleanup checks do not create PlayerStore.");
    }

    private static void CheckNativeTargets()
    {
        using var stream = File.OpenRead(Program.GameAssemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var target in JacksonCodeStorageVisitPatch.TargetMethods())
        {
            var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
                .Single(definition => metadata.GetString(definition.Name) == target.DeclaringType.Name);
            Assert(type.GetMethods().Count(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == target.Name) == 1,
                "Installed game must expose each exact Jackson visit target.");
        }
    }

    private static void Put(GameInventory inventory, GameItem item) => Assert(GraphUtils.TryAcceptAll(inventory, item) == 0, "Expected storage insertion.");
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
