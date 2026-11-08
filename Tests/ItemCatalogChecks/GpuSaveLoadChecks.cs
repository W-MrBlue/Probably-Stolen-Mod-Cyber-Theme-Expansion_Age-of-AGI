using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Helpers;
using Newtonsoft.Json;

internal static class GpuSaveLoadChecks
{
    internal static void Run()
    {
        for (int model = 0; model < 4; model++)
        {
            var legacy = new GameItem { Id = "age_of_agi.gpu" + model, uniqueId = 7100 + model, unitValue = 321 + model };
            legacy.EnableTag(GpuRuntime.GpuTag);
            legacy.ModifyTag(GpuRuntime.ComputePowerTag, tag => tag.Enable().SetInt(73 + model));
            legacy.ModifyTag(GpuRuntime.PowerPerCycleTag, tag => tag.Enable().SetInt(19 + model));
            string originalSave = Capture(legacy);
            string migrated = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // Reload the same pre-feature save, without saving the migrated result.
                DirectoryMaster.Item(legacy.identifier); // Unrelated fresh spawns must not affect migration.
                GameItem loaded = Decode(originalSave);
                string snapshot = loaded.GetTagReadonly(GpuInspectionState.StateTag).GetString();
                Assert(GpuRuntime.GetComputePower(loaded) == 73 + model && GpuRuntime.GetPowerPerCycle(loaded) == 19 + model &&
                    loaded.unitValue == legacy.unitValue && loaded.uniqueId == legacy.uniqueId,
                    "An old saved card must retain its numeric hardware, price and identity on every reload.");
                Assert(GpuInspectionState.Read(loaded).Parts.All(part => part.Condition == "normal") &&
                    (migrated == null || migrated == snapshot), "Legacy component repair must be fixed rather than use spawn probabilities.");
                migrated = snapshot;
            }
        }

        var oldDouble = new GameItem { Id = "age_of_agi.gpu0", uniqueId = 7200 };
        oldDouble.EnableTag(GpuRuntime.GpuTag);
        oldDouble.ModifyTag(GpuRuntime.ComponentStatsTag, tag => tag.Enable().SetInt(1));
        oldDouble.ModifyTag(GpuRuntime.ComputePowerTag, tag => tag.Enable().SetDouble(7.125));
        oldDouble.ModifyTag(GpuRuntime.PowerPerCycleTag, tag => tag.Enable().SetInt(9999));
        var oldFault = Decode(Capture(oldDouble));
        Assert(GpuRuntime.TryGetRealPerformance(oldFault, out var fault) && fault.ComputePower == 7.125 && !fault.PowerSupplyWorking &&
            fault.PowerPerCycle == 0, "Legacy double compute and known supply faults survive missing-snapshot migration.");

        GameItem current = DirectoryMaster.Item("age_of_agi.gpu0");
        GpuTestState.SetReal(current, compute: 7.125, power: 19, supply: true);
        GpuLabels.Ensure(current);
        ItemFeature compute = current.FindItemFeatureByID(GpuLabels.ComputeFeature);
        GpuLabels.SetClaim(current, GpuLabels.ComputeFeature, 30);
        GpuLabels.SetClaim(current, GpuLabels.GoldfingerFeature, 0);
        compute.SetExposed(true);
        string expected = current.GetTagReadonly(GpuInspectionState.StateTag).GetString();
        string saved = Capture(current);
        for (int roundTrip = 0; roundTrip < 4; roundTrip++)
        {
            current = Decode(saved);
            compute = current.FindItemFeatureByID(GpuLabels.ComputeFeature);
            Assert(current.GetTagReadonly(GpuInspectionState.StateTag).GetString() == expected && GpuRuntime.GetComputePower(current) == 7.125 &&
                GpuRuntime.GetPowerPerCycle(current) == 19 && GpuLabels.ReadValue(compute.fakeCondition, -1) == 30 && compute.IsExposed() &&
                GpuLabels.ReadValue(current.FindItemFeatureByID(GpuLabels.GoldfingerFeature).fakeCondition, -1) == 0 &&
                current.itemFeatures.Count(GpuLabels.IsFeature) == 4,
                "Repeated full save/decode must preserve components, real fields, edited claims and exposure state.");
            saved = Capture(current);
        }
        GameItem physical = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
        physical.SetValue(1000);
        var physicalState = GpuInspectionState.Read(physical);
        var contact = physicalState.Parts.Single(part => part.Slot == "goldfinger_down");
        contact.Sprite = ""; contact.Condition = "empty";
        physical.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(physicalState)));
        physical.EnableTag(GpuLabels.GoldfingerExposedTag);
        physical.ModifyTag(GpuInspectionCalculator.QuestionedTag, tag => tag.Enable().SetInt(5));
        physical.onLoaded(physical);
        GpuLabels.SetClaim(physical, GpuLabels.GoldfingerFeature, 0);
        physical.FindItemFeatureByID(GpuLabels.GoldfingerFeature).SetExposed(true);
        string physicalSnapshot = physical.GetTagReadonly(GpuInspectionState.StateTag).GetString();
        for (int roundTrip = 0; roundTrip < 3; roundTrip++)
        {
            physical = Decode(Capture(physical));
            Assert(physical.IsTag(GpuLabels.GoldfingerExposedTag) &&
                physical.GetTagReadonly(GpuInspectionCalculator.QuestionedTag).GetInt() == 5 &&
                GpuInspectionCalculator.AvailableQuestions(physical) == 10 &&
                physical.GetBaseValue() == 200 && physical.GetTagReadonly(GpuInspectionState.StateTag).GetString() == physicalSnapshot,
                "Full current-format save/decode preserves successful and failed per-question attempts, remaining questions and the contact discount without rerolling parts.");
        }
        CheckMatchedLabelRoundTrip();
        CheckConcessionRoundTrip();
        Console.WriteLine("PASS: decode factories without random assemblies, full-state replacement, GPU save/label roundtrips, beneficial corrections and exposed contact discounts.");
    }

    private static void CheckMatchedLabelRoundTrip()
    {
        var previousPlayer = PlayerStore.Instance;
        var previousStore = EmporiumEntry.Instance;
        try
        {
            PlayerStore.Instance = new PlayerStore { currentClientInstance = new StoreClientInstance { Client = new StoreClient() } };
            EmporiumEntry.Instance = new EmporiumEntry();
            GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
            card.Owned = false;
            double actual = GpuRuntime.GetComputePower(card);
            GpuLabels.SetClaim(card, GpuLabels.ComputeFeature, actual * 0.75);
            EmporiumEntry.Instance.backInvinvElement.UncheckedAccept(card);
            Assert(GpuInspectionCalculator.TryConfront(card, false, true, false, false),
                "An understated label can be corrected before a current-format save.");
            int rewards = StoreReputation.CurrentClientRewardCalls;
            string hardware = card.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            for (int roundTrip = 0; roundTrip < 3; roundTrip++)
            {
                card = Decode(Capture(card));
                ItemFeature feature = card.FindItemFeatureByID(GpuLabels.ComputeFeature);
                Assert(feature.IsFeatureMatch() && feature.IsExposed() && GpuLabels.ReadValue(feature.fakeCondition, -1) == actual &&
                    (GpuInspectionCalculator.AvailableQuestions(card) & 2) == 0 &&
                    card.GetTagReadonly(GpuInspectionState.StateTag).GetString() == hardware &&
                    StoreReputation.CurrentClientRewardCalls == rewards,
                    "Save/decode preserves matched public truth and the consumed question without changing hardware or repeating reputation rewards.");
            }
        }
        finally
        {
            PlayerStore.Instance = previousPlayer;
            EmporiumEntry.Instance = previousStore;
        }
    }

    private static void CheckConcessionRoundTrip()
    {
        GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
        card.SetValue(1000);
        card.Owned = false;
        GpuLabels.AddConfrontDiscount(card, 2);
        GpuLabels.AddConfrontDiscount(card, 4);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            card = Decode(Capture(card));
            card.Owned = false;
            var concession = card.FindItemFeatureByID(GpuLabels.ConfrontDiscountFeature);
            Assert(card.GetBaseValue() == 1000 && card.GetCurrentValue() == 800 && concession.customIntValue1 == 6 &&
                concession.featureType == ItemFeature.FeatureType.TemporaryBuying && concession.valueStage == ItemFeature.ValueStage.Final,
                "Current-format save/decode preserves one cumulative buying concession independently of corrected hardware value.");
            GpuLabels.AddConfrontDiscount(card, 2);
            Assert(card.GetCurrentValue() == 800, "Repeating a saved concession's bit does not increase its percentage.");
        }
    }

    // Match the native sequence: factory while IsDecoding, replace the entire Tag
    // dictionary, onLoaded, clear generated features, restore saved native labels.
    private static GameItem Decode(string json)
    {
        SavedCard saved = JsonConvert.DeserializeObject<SavedCard>(json);
        GameItem item;
        bool wasDecoding = SaveManager.IsDecoding;
        SaveManager.IsDecoding = true;
        try
        {
            item = DirectoryMaster.Item(saved.Id);
            Assert(string.IsNullOrEmpty(item.GetTagReadonly(GpuInspectionState.StateTag).GetString()) &&
                !item.itemFeatures.Any(GpuLabels.IsFeature) && item.uniqueId == 0,
                "A decode factory must not roll components, create labels or allocate an identity before restoration.");
            item.modifiedState.dict.Clear();
            foreach (var entry in saved.Tags)
                item.ModifyTag(entry.Key, tag =>
                {
                    tag.Enabled = entry.Value.Enabled; tag.Value = entry.Value.Int; tag.FloatValue = entry.Value.Float;
                    tag.LongValue = entry.Value.Long; tag.DoubleValue = entry.Value.Double; tag.StringValue = entry.Value.Text;
                });
            item.uniqueId = saved.UniqueId;
            item.unitValue = saved.Price;
        }
        finally { SaveManager.IsDecoding = wasDecoding; }
        item.onLoaded(item);
        item.itemFeatures.Clear();
        foreach (var savedFeature in saved.Features)
        {
            var feature = new ItemFeature { identifier = savedFeature.Id, category = savedFeature.Category,
                featureType = savedFeature.Type, valueStage = savedFeature.Stage, customIntValue1 = savedFeature.Mask };
            if (savedFeature.Fake != null)
                feature.SetConditions(new ItemCondition { identifier = savedFeature.Fake, category = savedFeature.Category },
                    new ItemCondition { identifier = savedFeature.Real, category = savedFeature.Category });
            else
            {
                feature.SetPreExposedValueMod(savedFeature.Modifier);
                feature.SetValueModifier(savedFeature.Modifier);
            }
            feature.SetExposed(savedFeature.Exposed);
            item.AddItemFeature(feature);
        }
        GpuLabels.Ensure(item);
        return item;
    }

    private static string Capture(GameItem item) => JsonConvert.SerializeObject(new SavedCard
    {
        Id = item.identifier, UniqueId = item.uniqueId, Price = item.unitValue,
        Tags = item.modifiedState.dict.ToDictionary(entry => entry.Key, entry => new SavedTag
        {
            Enabled = entry.Value.Enabled, Int = entry.Value.Value, Float = entry.Value.FloatValue,
            Long = entry.Value.LongValue, Double = entry.Value.DoubleValue, Text = entry.Value.StringValue
        }),
        Features = item.itemFeatures.Where(feature => GpuLabels.IsFeature(feature) || feature.identifier == GpuLabels.ConfrontDiscountFeature).Select(feature => new SavedFeature
        {
            Id = feature.identifier, Category = feature.category, Fake = feature.fakeCondition?.identifier,
            Real = feature.realCondition?.identifier, Exposed = feature.IsExposed(), Type = feature.featureType,
            Stage = feature.valueStage, Mask = feature.customIntValue1, Modifier = feature.GetCurrentValueMod()
        }).ToArray()
    });

    private sealed class SavedCard
    {
        public string Id { get; set; }
        public int UniqueId { get; set; }
        public long Price { get; set; }
        public Dictionary<string, SavedTag> Tags { get; set; }
        public SavedFeature[] Features { get; set; }
    }
    private sealed class SavedTag
    {
        public bool Enabled { get; set; }
        public int Int { get; set; }
        public float Float { get; set; }
        public long Long { get; set; }
        public double Double { get; set; }
        public string Text { get; set; }
    }
    private sealed class SavedFeature
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string Fake { get; set; }
        public string Real { get; set; }
        public bool Exposed { get; set; }
        public ItemFeature.FeatureType Type { get; set; }
        public ItemFeature.ValueStage Stage { get; set; }
        public int Mask { get; set; }
        public int Modifier { get; set; }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
