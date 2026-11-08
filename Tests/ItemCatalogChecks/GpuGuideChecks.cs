using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Npcs;

internal static class GpuGuideChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> source)
    {
        // Independent metadata and existing generated art; no authored book or icon is a test fixture.
        var definition = new ItemDefinition(GpuGuide.ItemId, "gpu_guide", "gpu0", 17,
            "fixture_guide_name", "fixture_guide_desc", types: new[] { "document", "electronics" }, audioPreset: "paper_sheet");
        var definitions = source.Concat(new[] { definition }).ToArray();
        var loaded = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(loaded.Manifest);
        registration.Register(new ModItemDirectory(), definitions);
        try
        {
            var guide = DirectoryMaster.Item(GpuGuide.ItemId);
            Assert(guide.IsTag("DOCUMENT") && guide.IsTag("IMPORTANT_TAG") && guide.IsTag("GUIDE_PAPER_TAG") &&
                guide.GetGameItemType().Contains("DOCUMENT") && guide.SpriteKey == "gpu0" && guide.unitValue == 17,
                "The guide uses catalog metadata, a single owning helper and native document tags.");
            guide.SetValue(29);
            guide.DisableTag("GUIDE_PAPER_TAG");
            guide.onActivateSlotItemFunc = null;
            guide.onLoaded(guide);
            Assert(guide.IsTag("GUIDE_PAPER_TAG") && guide.unitValue == 29 && guide.onActivateSlotItemFunc != null,
                "Loading restores reading callbacks while preserving the saved price.");
            int panels = PaperUI.Created.Count;
            PlayerStore.Instance = null;
            PlayerStore.ForbidLazyCreation = true;
            int reads = PlayerStore.InstanceReads;
            guide.onActivateSlotItemFunc(guide, null, null);
            Assert(PaperUI.Created.Count == panels && PlayerStore.InstanceReads == reads,
                "Opening a guide without a scene must not construct PlayerStore or a paper panel.");
            PlayerStore.ForbidLazyCreation = false;
            PlayerStore.Instance = new PlayerStore();
            HealthData.ReadingAllowed = false;
            Assert(!guide.canActivateSlotItemFunc(guide, null, null) && !guide.mayActivateSlotItemFunc(guide, null, null),
                "The guide follows the native reading restriction.");
            guide.onActivateSlotItemFunc(guide, null, null);
            Assert(PaperUI.Created.Count == panels, "Invoking the callback directly must also respect reading restrictions.");
            HealthData.ReadingAllowed = true;
            guide.onActivateSlotItemFunc(guide, null, null);
            guide.onActivateSlotItemFunc(guide, null, null);
            var panel = PaperUI.Created.Last();
            Assert(PaperUI.Created.Count == panels + 1 && panel.Id == ModConstants.ModId + ":" + GpuGuide.BookId &&
                panel.Shows == 2 && guide.unitCount == 1 && guide.unitValue == 29,
                "Reading opens the authored book through the native registry without consuming or repricing the guide.");
            loaded.IsEnabled = false;
            GpuGuideHelper.Shutdown();
            guide.onActivateSlotItemFunc(guide, null, null);
            Assert(panel.Closed && panel.Shows == 2, "Disable closes the guide and stale item callbacks cannot reopen it.");
            loaded.IsEnabled = true;
            CheckGifts(definitions);
        }
        finally
        {
            GpuGuideHelper.Shutdown();
            registration.Unregister();
            HealthData.ReadingAllowed = true;
            PlayerStore.ForbidLazyCreation = false;
            PlayerStore.Instance = null;
        }
        Console.WriteLine("PASS: catalog guide activation, native book routing, reading restrictions, load callbacks, cleanup and frozen tier-one gift delivery.");
    }

    private static void CheckGifts(IReadOnlyList<ItemDefinition> definitions)
    {
        const string header = "section,itemIds,count,chance,dataAmount,minimumProductionSpeed,memoryGb,computeBonus,durability,trainingWork,trainingData\n";
        string csv = header + "gifts," + GpuGuide.ItemId + ",2,100,-1,0,0,0,0,0,0\n" +
            "stock,age_of_agi.disk_empty,1,100,-1,0,0,0,0,0,0\n";
        var supplier = new WeeklyElectronicsSupplier("fixture.guide_supplier", ElectronicsTrader.UpgradeId,
            "fixture.portrait", "fixture_guide_gifts", "fixture_supplier", "fixture_portrait_texture");
        supplier.Configure(NightShiftCatalog.Parse(csv, definitions, ElectronicsTrader.CatalogPath), null);
        PlayerStore.Instance = new PlayerStore();
        EmporiumEntry.Instance = new EmporiumEntry();
        supplier.Unlock();
        Assert(EmporiumEntry.Instance.GetAllItems().Count == 0, "An unpurchased service cannot deliver guides.");
        PlayerStore.Instance.UnlockedServices.Add(ElectronicsTrader.UpgradeId);
        var inventories = new[] { EmporiumEntry.Instance.backInvinvElement, EmporiumEntry.Instance.invElement,
            EmporiumEntry.Instance.showcaseElement, EmporiumEntry.Instance.frontInvinvElement };
        foreach (var inventory in inventories) inventory.RejectTransfer = true;
        supplier.Unlock();
        Assert(EmporiumEntry.Instance.GetAllItems().Count == 0, "A full inventory defers guide gifts.");
        EmporiumEntry.Instance.backInvinvElement.RejectTransfer = false;
        supplier.DeliverGifts();
        var gifts = EmporiumEntry.Instance.GetAllItems();
        Assert(gifts.Count == 2 && gifts.All(item => item.identifier == GpuGuide.ItemId && GeneralHelper.IsItemOwned(item) &&
            item.onActivateSlotItemFunc != null), "Deferred gifts must deliver owned, readable catalog guides.");
        supplier.Configure(NightShiftCatalog.Parse(csv.Replace(",2,100,", ",3,100,"), definitions, ElectronicsTrader.CatalogPath), null);
        supplier.Unlock();
        supplier.DeliverGifts();
        Assert(EmporiumEntry.Instance.GetAllItems().Count == 2, "Frozen gift progress prevents duplicate delivery after configuration changes.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
