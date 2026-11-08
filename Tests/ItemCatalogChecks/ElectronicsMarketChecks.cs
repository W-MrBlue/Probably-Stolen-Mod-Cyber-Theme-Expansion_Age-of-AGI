using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Npcs;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class ElectronicsMarketChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        var loaded = Program.PrepareLoadedMod(items);
        foreach (string locale in new[] { "zh", "en" }) ModHelper.Translations[locale] = Program.ReadLocale(locale);
        var mod = new AgeOfAgiMod(); mod.Init(loaded.Manifest); mod.OnEnable();
        ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
        PlayerStore.Instance = new PlayerStore { Attractiveness = 400 };
        EmporiumEntry.Instance = new EmporiumEntry();
        StoreStation.Instance.dayCounter = 20;
        Sprite[] ownedPortraits = Array.Empty<Sprite>();
        try
        {
            Assert(StoreClientList.generalPool.entries.Count == 3 && StoreClientListDict.storeClientDict.Count == 13 &&
                TypeHelper.MAJOR_TYPES_LIST.Count(type => type == ElectronicsMarket.MainType) == 1 &&
                TypeHelper.MAJOR_TYPES_LIST.Count(type => type == ElectronicsMarket.HardwareType) == 1,
                "Register three seller pools, twelve electronics factories and one enterprise factory without duplicating showcase categories.");
            ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
            Assert(StoreClientList.generalPool.entries.Count == 3 && StoreClientListDict.storeClientDict.Count == 13,
                "Repeated scene registration cannot duplicate factories or seller weights.");

            StoreClient scav = ElectronicsMarket.CreateScavengerBuyer();
            StoreClient repair = ElectronicsMarket.CreateRepairBuyer();
            StoreClient upper = ElectronicsMarket.CreateUpperBuyer();
            StoreClient secondhand = ElectronicsMarket.CreateSecondhandBuyer();
            ownedPortraits = CheckPortraits(loaded, secondhand);
            Assert(scav.GetBudget() == StoreClientListTier.CreateInjuredSpacer().GetBudget() &&
                repair.GetBudget() == StoreClientListTier.CreateClinicNurse().GetBudget() &&
                upper.GetBudget() == StoreClientListTier.CreateHospitalAdmin().GetBudget() &&
                secondhand.GetBudget() == StoreClientListTier.CreateStationEngineer().GetBudget(),
                "Use native medical tier budgets and an unmultiplied engineer budget for secondhand hardware.");
            Assert(new[] { scav, repair, upper }.All(client => client.isRequireVariety),
                "The deterministic native-style variety roll can attach the variety requirement to every general buyer tier.");
            Assert(scav.clientFaction == "FACTION_LOWER_LEVEL" && upper.clientFaction == "FACTION_UPPER_LEVEL" &&
                new[] { scav, repair, upper, secondhand }.All(client => client.clientIntent == StoreClient.ClientIntent.BUY &&
                    client.clientBuyingTagList.Count == 0 && client.clientBuyingIdList.Count == 0),
                "Buyers retain native factions and use predicate-only purchase rules.");

            var gpu = DirectoryMaster.Item("age_of_agi.gpu0");
            var raw = DirectoryMaster.Item("age_of_agi.dc_common_data_disk");
            var container = DirectoryMaster.Item("age_of_agi.data_container");
            var encrypted = DirectoryMaster.Item("age_of_agi.btc_normal_unfinish");
            var ordinaryProduct = DirectoryMaster.Item("age_of_agi.btc_normal");
            var model = DirectoryMaster.Item(AiModelState.TrainedId);
            AiModelState.ApplyTrained(model, "computing", 4, 20, 4);
            var part = DirectoryMaster.Item("age_of_agi.part_enc_0");
            foreach (StoreClient buyer in new[] { scav, repair, upper })
            {
                Assert(buyer.willAcceptItem(gpu) && buyer.willAcceptItem(raw) && buyer.willAcceptItem(encrypted) &&
                    buyer.willAcceptItem(ordinaryProduct), "General buyers accept electronics, hardware, raw carriers and encrypted carriers.");
                Assert(!buyer.willAcceptItem(container) && !buyer.willAcceptItem(model) && !buyer.willAcceptItem(part),
                    "General buyers exclude data containers, AI models and code components.");
            }
            Assert(secondhand.willAcceptItem(gpu) && !secondhand.willAcceptItem(raw) &&
                !secondhand.willAcceptItem(ordinaryProduct) && !secondhand.willAcceptItem(container),
                "The secondhand dealer accepts only actual hardware.");
            gpu.EnableTag("NOT_FOR_RESALE");
            Assert(!scav.willAcceptItem(gpu) && !secondhand.willAcceptItem(gpu), "Native resale restrictions remain authoritative.");
            gpu.DisableTag("NOT_FOR_RESALE");

            double actualCompute = GpuLabels.ReadValue(gpu.FindItemFeatureByID(GpuLabels.ComputeFeature).realCondition, 0);
            GpuLabels.SetClaim(gpu, GpuLabels.ComputeFeature, actualCompute + 20);
            scav.onItemAddedToWeighted(gpu);
            Assert(gpu.FindItemFeatureByID(GpuLabels.ComputeFeature).IsExposed() &&
                gpu.FindItemFeatureByID(GpuLabels.ComputeFeature).GetCurrentItemCondition() ==
                gpu.FindItemFeatureByID(GpuLabels.ComputeFeature).realCondition,
                "General buyers immediately expose an inflated compute claim and price the real hardware.");
            GpuLabels.MatchActualLabels(gpu);
            GpuLabels.SetClaim(gpu, GpuLabels.ComputeFeature, Math.Max(0, actualCompute - 1));
            repair.onItemAddedToWeighted(gpu);
            Assert(!gpu.FindItemFeatureByID(GpuLabels.ComputeFeature).IsExposed(),
                "An understated claim is not treated as an inflated sale price.");

            Assert(ElectronicsMarket.ShowcaseBuyer(350, 14, 100) == ElectronicsMarket.UpperBuyerId &&
                ElectronicsMarket.ShowcaseBuyer(350, 13, 100) == ElectronicsMarket.RepairBuyerId &&
                ElectronicsMarket.ShowcaseBuyer(145, 0, 100) == ElectronicsMarket.RepairBuyerId &&
                ElectronicsMarket.ShowcaseBuyer(1, 0, 400) == ElectronicsMarket.RepairBuyerId &&
                ElectronicsMarket.ShowcaseBuyer(1, 0, 399) == ElectronicsMarket.ScavengerBuyerId,
                "Mirror native medical showcase thresholds, including attractiveness promoting only to the middle tier.");
            string selected = "native";
            Assert(ElectronicsShowcasePatch.Prefix("MATERIAL", ref selected) && selected == "native" &&
                !ElectronicsShowcasePatch.Prefix(ElectronicsMarket.HardwareType, ref selected) &&
                selected == ElectronicsMarket.SecondhandBuyerId &&
                !ElectronicsShowcasePatch.Prefix(ElectronicsMarket.MainType, ref selected) &&
                selected == ElectronicsMarket.RepairBuyerId,
                "Hardware and general electronics use separate showcase routes without altering native categories.");

            CheckDialogues(scav, repair, upper, secondhand);
            CheckGpuSupplier();
            CheckSellers(items);
            Assert(!StoreClientListDict.storeClientDict.ContainsKey(ModelEngineer.ClientId) &&
                !AgeOfAgi.Debugging.ModDebugMenu.Characters.Actions.ContainsKey("Spawn model_engineer") &&
                ModelEngineer.CreateBuyer().willAcceptItem(model) && !ModelEngineer.CreateBuyer().willAcceptItem(gpu) &&
                ModelEngineer.CreateSeller().clientIntent == StoreClient.ClientIntent.SELL,
                "Model engineer factories exist for future use but are absent from pools, the dictionary and F9.");
            Assert(ElectronicsMarket.QueueDebug(ElectronicsMarket.ScavengerBuyerId) &&
                ElectronicsMarket.QueueDebug(ElectronicsMarket.GpuSupplierId), "Active buyers and the GPU supplier support F9 previews.");
        }
        finally
        {
            mod.OnDisable();
            Assert(ownedPortraits.All(portrait => portrait.Destroyed), "Disable releases the two generated market portraits.");
            Assert(StoreClientList.generalPool.entries.Count == 0 && StoreClientListDict.storeClientDict.Count == 0 &&
                !TypeHelper.MAJOR_TYPES_LIST.Contains(ElectronicsMarket.MainType) &&
                !TypeHelper.MAJOR_TYPES_LIST.Contains(ElectronicsMarket.HardwareType),
                "Disable removes owned factories, seller weights and showcase categories.");
            PlayerStore.Instance = null;
        }
        Console.WriteLine("PASS: medical-tier electronics buyers, predicate isolation, GPU claim exposure, honest GPU supply, configurable dialogues and inactive model engineer factories.");
    }

    private static Sprite[] CheckPortraits(LoadedMod loaded, StoreClient secondhand)
    {
        SpriteDict previous = SpriteDict.Instance;
        var native = new Sprite { pixelsPerUnit = 72 };
        SpriteDict.Instance = new SpriteDict();
        SpriteDict.Instance.spriteDictionary.Add("fixture_native_portrait", native);
        var gpuTexture = new Texture2D(39, 87, TextureFormat.RGBA32, false);
        var buyerTexture = new Texture2D(51, 93, TextureFormat.RGBA32, false);
        loaded.Textures["gpu_supplier"] = gpuTexture;
        loaded.Textures["secondhand_buyer"] = buyerTexture;
        try
        {
            StoreClient supplier = ElectronicsMarket.CreateGpuSupplier();
            Assert(supplier.spriteName == ElectronicsPortraits.GpuSupplierSpriteId &&
                secondhand.spriteName == ElectronicsPortraits.SecondhandBuyerSpriteId,
                "GPU suppliers and secondhand buyers select their own namespaced portraits.");
            Sprite gpu = null, buyer = null;
            Assert(!ElectronicsPortraitPatch.Prefix(supplier.spriteName, ref gpu) &&
                !ElectronicsPortraitPatch.Prefix(secondhand.spriteName, ref buyer) &&
                gpu.texture == gpuTexture && buyer.texture == buyerTexture &&
                gpu.rect.width == 39 && gpu.rect.height == 87 && buyer.rect.width == 51 && buyer.rect.height == 93 &&
                gpu.pixelsPerUnit == 72 && buyer.pixelsPerUnit == 72 &&
                gpuTexture.filterMode == FilterMode.Point && buyerTexture.wrapMode == TextureWrapMode.Clamp,
                "Both portraits use their entire texture at native scale with pixel-art filtering.");
            Sprite cached = null;
            ElectronicsPortraitPatch.Prefix(supplier.spriteName, ref cached);
            Assert(ReferenceEquals(gpu, cached), "Repeated sprite lookups reuse the generated portrait.");
            var replacement = new Texture2D(43, 91, TextureFormat.RGBA32, false);
            loaded.Textures["gpu_supplier"] = replacement;
            ElectronicsPortraitPatch.Prefix(supplier.spriteName, ref cached);
            Assert(gpu.Destroyed && cached != gpu && cached.texture == replacement && !gpuTexture.Destroyed,
                "Replacing the source texture releases only the old generated sprite.");
            Sprite untouched = native;
            Assert(ElectronicsPortraitPatch.Prefix("fixture_native_portrait", ref untouched) && untouched == native,
                "Native sprite lookups are not intercepted.");
            loaded.Textures.Remove("secondhand_buyer");
            Assert(ElectronicsPortraitPatch.Prefix(secondhand.spriteName, ref untouched) && untouched == native,
                "Missing custom art safely leaves the native lookup result alone.");
            loaded.Textures["secondhand_buyer"] = buyerTexture;
            loaded.IsEnabled = false;
            Assert(ElectronicsPortraitPatch.Prefix(supplier.spriteName, ref untouched), "Disabled mods do not intercept portrait lookup.");
            loaded.IsEnabled = true;
            ElectronicsPortraits.Shutdown();
            Assert(cached.Destroyed && buyer.Destroyed && !native.Destroyed && !replacement.Destroyed && !buyerTexture.Destroyed,
                "Portrait cleanup preserves native sprites and mod-loaded texture ownership.");
            Sprite regeneratedGpu = null, regeneratedBuyer = null;
            ElectronicsPortraitPatch.Prefix(supplier.spriteName, ref regeneratedGpu);
            ElectronicsPortraitPatch.Prefix(secondhand.spriteName, ref regeneratedBuyer);
            return new[] { regeneratedGpu, regeneratedBuyer };
        }
        finally { loaded.IsEnabled = true; SpriteDict.Instance = previous; }
    }

    private static void CheckDialogues(params StoreClient[] clients)
    {
        string previous = ModHelper.Locale;
        string[] ids = clients.Select(client => client.identifier).ToArray();
        foreach (string locale in new[] { "zh", "en" }) ModHelper.Translations[locale] = Program.ReadLocale(locale);
        try
        {
            foreach (string locale in new[] { "zh", "en" })
            {
                ModHelper.Locale = locale;
                foreach (string id in ids)
                {
                    StoreClient client = CreateBuyer(id);
                    client.OverrideBudget(23);
                    client.mainDialogue.startAction();
                    var lines = new List<string>();
                    for (Dialogue line = client.mainDialogue; line != null; line = line.nextDialogue) lines.Add(line.plainText);
                    Assert(lines.Count >= 2 && lines.Last() == string.Format(
                        ModHelper.Translations[locale]["electronics_buyer_available_credits"], 23) &&
                        client.acceptDealDialogue.plainText.EndsWith("?") == (locale == "en"),
                        "Configured greetings end with a separate live-budget bubble and completed trades ask for more goods.");
                }
            }
        }
        finally { ModHelper.Locale = previous; }
    }

    private static StoreClient CreateBuyer(string id)
    {
        if (id == ElectronicsMarket.ScavengerBuyerId) return ElectronicsMarket.CreateScavengerBuyer();
        if (id == ElectronicsMarket.RepairBuyerId) return ElectronicsMarket.CreateRepairBuyer();
        if (id == ElectronicsMarket.UpperBuyerId) return ElectronicsMarket.CreateUpperBuyer();
        return ElectronicsMarket.CreateSecondhandBuyer();
    }

    private static void CheckGpuSupplier()
    {
        var stock = EmporiumEntry.Instance.frontInvinvElement;
        stock.childItems.ToList().ForEach(item => item.Destroy());
        StoreClient supplier = ElectronicsMarket.CreateGpuSupplier();
        PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = supplier };
        supplier.mainDialogue.endAction();
        var delivered = stock.childItems.ToArray();
        Assert(delivered.Length == 4 && delivered.All(item => item.identifier != "age_of_agi.gpu0" &&
            item.identifier != "age_of_agi.cpu0" && item.itemFeatures.Where(GpuLabels.IsFeature).All(feature => feature.IsFeatureMatch()) &&
            !item.IsTag("AGE_OF_AGI_GPU_DISHONEST_CLAIMS")),
            "The dedicated supplier delivers four GPU1-3 cards with honest labels and no dishonest variant.");
        supplier.mainDialogue.endAction();
        Assert(stock.childItems.Count == 4, "The GPU supplier cannot deliver twice through repeated dialogue.");
        stock.childItems.ToList().ForEach(item => item.Destroy());
        PlayerStore.Instance.currentClientInstance = null;
    }

    private static void CheckSellers(IReadOnlyList<ItemDefinition> items)
    {
        Assert(ElectronicsMarket.CreateAdvancedSeller() == null, "Advanced hardware supply requires tier two.");
        PlayerStore.Instance.UnlockedServices.Add(NightShiftClient.UpgradeId);
        Assert(ElectronicsMarket.CreateAdvancedSeller() != null &&
            ElectronicsMarket.CreateAdvancedSeller().displayName == ModHelper.GetLocalized(ModConstants.ModId, "advanced_hardware_supplier_name"),
            "The advanced seller uses the hardware-supplier name after unlocking tier two.");
        foreach (StoreClient seller in new[] { ElectronicsMarket.CreateSeller(), ElectronicsMarket.CreateDishonestSeller(),
            ElectronicsMarket.CreateAdvancedSeller(), ElectronicsMarket.CreateGpuSupplier(), ModelEngineer.CreateSeller() })
            Assert(seller.isOfferWholesale && seller.wholesaleDiscount <= -15 && seller.wholesaleDiscount >= -25,
                "Every non-periodic, non-thief seller uses the native random 15-25 percent wholesale discount.");
        var catalog = DropSlotCsvFixture.LoadMarket(items, ElectronicsVisitRules.Load());
        for (int seed = 0; seed < 100; seed++)
        {
            var normal = catalog.Roll(seed, false);
            var advanced = catalog.Roll(seed, true);
            Assert(normal.Count >= 5 && normal.Count <= 7 && advanced.Count >= 5 && advanced.Count <= 9,
                "Existing dealer quantity rules remain intact.");
            Assert(catalog.RollGpuSupplier(seed).Count == 4, "GPU stock uses its independent four-offer rule.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
