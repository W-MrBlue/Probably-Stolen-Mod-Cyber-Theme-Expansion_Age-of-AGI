using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Server;
using AgeOfAgi.Npcs;
using Newtonsoft.Json;

internal static class GpuLabelChecks
{
    internal static void Run()
    {
        var previous = new Dictionary<string, Dictionary<string, string>>(ModHelper.Translations);
        string locale = ModHelper.Locale;
        try
        {
            foreach (string language in new[] { "zh", "en" }) ModHelper.Translations[language] = Program.ReadLocale(language);
            ModHelper.Locale = "zh";
            CheckPowerMidpointColors();
            CheckPrinterRanges();
            CheckEditorLabels();
            GameItem gpu = DirectoryMaster.Item("age_of_agi.gpu0");
            Assert(gpu.itemFeatures.Count(GpuLabels.IsFeature) == 4 && LabelerHelper.GetEditableLabelCount(gpu) == 4,
                "Native label machines expose compute, power, supply and edge-contact labels.");
            var baseline = Tooltip(gpu);
            Assert(baseline.Lines.Count == 2 && baseline.Lines[0].Color == RenderHandler.ColorPalette.LightGreen &&
                baseline.Lines[1].Color == RenderHandler.ColorPalette.LightCyan &&
                !baseline.Lines.Any(line => line.Text.Contains("供电接口正常")),
                "Maximum compute is above the midpoint; normal supply adds no tooltip row.");
            Choose(gpu, GpuLabels.ComputeFeature, 5);
            Assert(Tooltip(gpu).Lines[0].Color == RenderHandler.ColorPalette.LightCyan,
                "Half the frozen core compute is the midpoint between fully normal and fully ineffective memory.");
            Choose(gpu, GpuLabels.ComputeFeature, 7.5);
            Assert(Tooltip(gpu).Lines[0].Color == RenderHandler.ColorPalette.LightGreen,
                "Compute below the maximum but above the midpoint is beneficial.");
            Choose(gpu, GpuLabels.ComputeFeature, 0);
            Assert(Tooltip(gpu).Lines[0].Color == RenderHandler.ColorPalette.LightRed,
                "Fully missing or mismatched memory is below the compute midpoint.");
            Choose(gpu, GpuLabels.ComputeFeature, 20);
            Choose(gpu, GpuLabels.PowerFeature, 4);
            var above = Tooltip(gpu);
            Assert(above.Lines[0].Text == "算力：20" && above.Lines[0].Color == RenderHandler.ColorPalette.LightGreen &&
                above.Lines[1].Color == RenderHandler.ColorPalette.LightRed, "More compute is beneficial; more power is detrimental.");
            Choose(gpu, GpuLabels.ComputeFeature, 2.5);
            Choose(gpu, GpuLabels.PowerFeature, 1);
            var below = Tooltip(gpu);
            Assert(below.Lines[0].Color == RenderHandler.ColorPalette.LightRed && below.Lines[1].Color == RenderHandler.ColorPalette.LightGreen,
                "Less compute is detrimental; less power is beneficial.");
            Choose(gpu, GpuLabels.ComputeFeature, 5);
            Choose(gpu, GpuLabels.SupplyFeature, 0);
            var markedBroken = Tooltip(gpu);
            Assert(markedBroken.Lines.Count == 3 && markedBroken.Lines[1].Text.StartsWith("所需能量：") && markedBroken.Lines.Last().Text == "供电接口缺失" &&
                markedBroken.Lines.Last().Color == RenderHandler.ColorPalette.LightRed && !markedBroken.Lines.Any(line => line.Text.Contains("9999")),
                "A missing-connector warning supplements the numeric energy requirement instead of replacing it.");
            gpu.ModifyTag(GpuRuntime.ComputePowerTag, tag => tag.SetDouble(999));
            gpu.ModifyTag(GpuRuntime.PowerPerCycleTag, tag => tag.SetInt(0));
            Assert(GpuRuntime.GetComputePower(gpu) == 10 && GpuRuntime.GetPowerPerCycle(gpu) == 2 && ReadHardware(gpu, out var hardware) &&
                Math.Abs(hardware.ComputePerNight - 22) < 1e-9 && hardware.Power == 4,
                "Actual hardware ignores numeric Tags and every editable label, including a false supply claim.");
            string claim = gpu.FindItemFeatureByID(GpuLabels.ComputeFeature).fakeCondition.identifier;
            gpu.onLoaded(gpu);
            Assert(gpu.FindItemFeatureByID(GpuLabels.ComputeFeature).fakeCondition.identifier == claim && LabelerHelper.GetEditableLabelCount(gpu) == 4,
                "Reload keeps edited claims without adding duplicate features.");
            var feature = gpu.FindItemFeatureByID(GpuLabels.ComputeFeature);
            feature.SetExposed(true);
            Assert(Tooltip(gpu).Lines[0].Text == "算力：10", "Native exposure selects the actual condition.");
            gpu.onLoaded(gpu);
            Assert(feature.IsExposed(), "Reload preserves native exposure state.");
            feature.SetExposed(false);
            CheckDishonestyAndCalculator();
            foreach (bool connectorMissing in new[] { true, false })
            {
                GameItem broken = Broken(connectorMissing);
                var warning = Tooltip(broken);
                Assert(warning.Lines.Last().Text == (connectorMissing ? "供电接口缺失" : "供电模块全部缺失") && warning.Lines.Last().Color == RenderHandler.ColorPalette.LightRed &&
                    GpuRuntime.TryGetRealPerformance(broken, out var actual) && !actual.PowerSupplyWorking && actual.PowerPerCycle == (connectorMissing ? 2 : 0) &&
                    broken.GetTagReadonly(GpuRuntime.PowerPerCycleTag).GetInt() == (connectorMissing ? 2 : 0),
                    "Missing connector and zero modules migrate the old 9999 representation to a physical fault.");
                Choose(broken, GpuLabels.SupplyFeature, 1);
                Choose(broken, GpuLabels.PowerFeature, 0);
                Assert(!ReadHardware(broken, out _) && ServerHardware.PowerCost(Inventory(broken)) < 0,
                    "A falsified healthy/free label cannot run a server with a broken GPU.");
            }
            Assert(!GpuLabelTooltipPatch.Prefix(feature) && GpuLabelTooltipPatch.Prefix(new ItemFeature { identifier = "native" }),
                "Suppress duplicate native GPU tooltip rows without changing other labels.");
            ModHelper.Locale = "en";
            LabelerHelper.OnTargetItem(gpu);
            Assert(ItemFeatureList.CategoryNames[feature.category] == "GPU compute power" &&
                Tooltip(gpu).Lines[0].Text == "Compute power: 5", "Native editor names, options and tooltip refresh with locale.");
            Assert(gpu.itemFeatures.Where(GpuLabels.IsFeature).All(item => item.realCondition.modValue == 0 && item.fakeCondition.modValue == 0),
                "Hardware labels must not introduce an unconfigured price modifier.");
            string category = feature.category;
            var replacement = new List<ItemCondition>();
            ItemConditionList.conditionLookup[category] = replacement;
            ItemFeatureList.CategoryNames[category] = "replacement";
            string ownedCategory = gpu.FindItemFeatureByID(GpuLabels.PowerFeature).category;
            GpuLabels.Shutdown();
            Assert(LabelerUIManager.Instance.Closed && ReferenceEquals(ItemConditionList.conditionLookup[category], replacement) &&
                ItemFeatureList.CategoryNames[category] == "replacement" && !ItemConditionList.conditionLookup.ContainsKey(ownedCategory) &&
                !ItemFeatureList.CategoryNames.ContainsKey(ownedCategory), "Disable removes owned label tables and keeps replacements.");
            ItemConditionList.conditionLookup.Remove(category); ItemFeatureList.CategoryNames.Remove(category);
        }
        finally
        {
            ModHelper.Translations.Clear(); foreach (var entry in previous) ModHelper.Translations.Add(entry.Key, entry.Value);
            ModHelper.Locale = locale;
        }
        Console.WriteLine("PASS: native editable GPU labels, true/public separation, comparison colors, power-fault warnings, saved claims and server rejection.");
    }

    private static RichTextBuilder Tooltip(GameItem gpu) { var builder = new RichTextBuilder(); GpuRuntime.CreateTooltip(builder, gpu); return builder; }

    private static void CheckPowerMidpointColors()
    {
        GameItem gpu = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
        var state = GpuInspectionState.Read(gpu);
        state.CorePowerPerCycle = 2;
        state.Parts.RemoveAll(part => part.Slot.StartsWith("power_module_", StringComparison.Ordinal));
        for (int i = 0; i < 8; i++)
            state.Parts.Add(new GpuInspectionPart { Slot = "power_module_test_" + i, Sprite = "test_module", Condition = "normal" });
        gpu.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
        foreach (var sample in new[] { (Power: 2, Color: RenderHandler.ColorPalette.LightGreen),
            (Power: 8, Color: RenderHandler.ColorPalette.LightGreen), (Power: 9, Color: RenderHandler.ColorPalette.LightCyan),
            (Power: 10, Color: RenderHandler.ColorPalette.LightRed), (Power: 16, Color: RenderHandler.ColorPalette.LightRed) })
        {
            GpuLabels.SetClaim(gpu, GpuLabels.PowerFeature, sample.Power);
            Assert(Tooltip(gpu).Lines[1].Color == sample.Color,
                "Eight modules with base power 2 compare visible power to 9, the midpoint of best 2 and worst 16.");
        }
        state.Parts.RemoveAll(part => part.Slot.StartsWith("power_module_test_", StringComparison.Ordinal) && part.Slot != "power_module_test_0");
        gpu.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
        GpuLabels.SetClaim(gpu, GpuLabels.PowerFeature, 2);
        Assert(Tooltip(gpu).Lines[1].Color == RenderHandler.ColorPalette.LightCyan,
            "A single saved module has equal best and worst power; the baseline does not hardcode eight slots.");
        state.CorePowerPerCycle = 0;
        Assert(GpuPerformanceCalculator.PowerMidpoint(state) == 0, "Zero-budget hardware keeps a zero power midpoint.");
        state.CorePowerPerCycle = int.MaxValue;
        state.Parts.Add(new GpuInspectionPart { Slot = "power_module_overflow", Sprite = "test_module", Condition = "normal" });
        Assert(GpuPerformanceCalculator.PowerMidpoint(state) == int.MaxValue,
            "The worst power endpoint uses the same Int32 saturation as actual hardware demand.");
    }

    private static void CheckDishonestyAndCalculator()
    {
        foreach (bool higherIsBetter in new[] { true, false })
        {
            var outcomes = new int[100];
            for (int roll = 0; roll < outcomes.Length; roll++)
            {
                var random = new ClaimRandom(roll);
                double value = GpuLabels.RollClaim(20, higherIsBetter, random);
                Assert(random.Calls == 1 && random.LastMaximum == 100,
                    "Numeric claim probabilities must use one draw over [0, 100).");
                outcomes[roll] = value > 20 ? 0 : value == 20 ? 1 : value < 20 ? 2 : -1;
            }
            AssertProbabilityIntervals(outcomes, 3);
        }
        Assert(GpuLabels.Price(1000, 100, 100, false) == 1000 && GpuLabels.Price(1000, 100, 50, false) == 800 &&
            GpuLabels.Price(1000, 100, 0, false) == 600 && GpuLabels.Price(1000, 100, 50, true) == 320,
            "Forty percent tracks compute ratio, then a missing connector multiplies the whole value by 0.4.");
        var supplyOutcomes = new int[100];
        for (int roll = 0; roll < 100; roll++)
        {
            GameItem broken = Broken(true);
            string snapshot = broken.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            var random = new ClaimRandom(60, 90, roll);
            GpuLabels.ApplyDishonestClaims(broken, random);
            Assert(random.Calls == 3 && random.LastMaximum == 100,
                "A missing connector gets its own [0, 100) draw after the two numeric labels.");
            double supply = GpuLabels.ReadValue(broken.FindItemFeatureByID(GpuLabels.SupplyFeature).fakeCondition, -1);
            supplyOutcomes[roll] = supply == 0 ? 0 : supply == 1 ? 1 : -1;
            Assert(broken.GetTagReadonly(GpuInspectionState.StateTag).GetString() == snapshot,
                "Dishonest labels never edit real components or hardware attributes.");
            string claim = broken.FindItemFeatureByID(GpuLabels.PowerFeature).fakeCondition.identifier;
            GpuLabels.ApplyDishonestClaims(broken, new ClaimRandom(0, 0, 0));
            broken.onLoaded(broken);
            Assert(broken.FindItemFeatureByID(GpuLabels.PowerFeature).fakeCondition.identifier == claim,
                "Claims freeze at merchandise creation and survive load without another roll.");
        }
        AssertProbabilityIntervals(supplyOutcomes, 2);

        GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
        card.SetValue(1000);
        var assembly = GpuInspectionState.Read(card);
        var calculatorAssembly = JsonConvert.DeserializeObject<GpuInspectionState>(JsonConvert.SerializeObject(assembly));
        for (int i = 0; i < 4; i++) calculatorAssembly.Parts.Add(new GpuInspectionPart
            { Slot = "memory_test_" + i, Sprite = "test_memory", Condition = "normal" });
        for (int i = 0; i < 2; i++) calculatorAssembly.Parts.Add(new GpuInspectionPart
            { Slot = "power_module_test_" + i, Sprite = "test_module", Condition = "normal" });
        int slots = calculatorAssembly.Parts.Count(part => part.Slot.StartsWith("memory_"));
        int modules = calculatorAssembly.Parts.Count(part => part.Slot.StartsWith("power_module_"));
        Assert(GpuPerformanceCalculator.TryCalculateInspection(calculatorAssembly, 1, 1, 2, 3, out var calculated),
            "The calculator accepts independently chosen valid counts.");
        double expected = assembly.CoreComputePower.Value * (slots - 1 - 2 - 3 + 3 * 0.6) / slots;
        Assert(Math.Abs(calculated.ComputePower - expected) < 1e-9 && calculated.PowerPerCycle ==
            (int)Math.Ceiling(assembly.CorePowerPerCycle.Value * (double)modules / (modules - 1)),
            "Manual counts use the same memory weights and final power ceiling as hardware calculations.");
        Assert(!GpuPerformanceCalculator.TryCalculateInspection(calculatorAssembly, modules + 1, 0, 0, 0, out _) &&
            !GpuPerformanceCalculator.TryCalculateInspection(calculatorAssembly, 0, slots, 1, 0, out _) &&
            !GpuPerformanceCalculator.TryCalculateInspection(calculatorAssembly, -1, 0, 0, 0, out _) &&
            GpuPerformanceCalculator.TryCalculateInspection(calculatorAssembly, modules, 0, 0, 0, out var unplugged) &&
            unplugged.PowerPerCycle == 0 && !unplugged.PowerSupplyWorking,
            "Reject impossible counts and handle zero working modules without dividing by zero.");

        var previousPlayer = PlayerStore.Instance;
        var previousStore = EmporiumEntry.Instance;
        try
        {
            PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry();
            var seller = new StoreClient { displayName = "Seller" };
            PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = seller };
            card.Owned = false;
            EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(card);
            GpuLabels.SetClaim(card, GpuLabels.ComputeFeature, GpuRuntime.GetComputePower(card) * 1.25);
            GpuLabels.SetClaim(card, GpuLabels.PowerFeature, GpuRuntime.GetPowerPerCycle(card) * 0.75);
            Assert(card.GetBaseValue() == 1100 && card.unitValue == 1000,
                "Inflated compute raises only the compute-linked portion without overwriting base price.");
            InspectionUIManager.TradeLocked = true;
            Assert(!GpuInspectionCalculator.TryConfront(card, false, true, false, false), "Trade locks block confrontation.");
            InspectionUIManager.TradeLocked = false;
            Assert(!GpuInspectionCalculator.TryConfront(card, true, true, true, false) &&
                card.itemFeatures.Where(GpuLabels.IsFeature).All(feature => !feature.IsExposed()) &&
                card.GetTagReadonly(GpuInspectionCalculator.QuestionedTag).GetInt() == 0,
                "Multiple simultaneous choices are rejected without consuming attempts or partially changing price.");
            Assert(GpuInspectionCalculator.TryConfront(card, false, true, false, false) && card.GetBaseValue() == 1000 &&
                card.FindItemFeatureByID(GpuLabels.ComputeFeature).IsExposed() && !card.FindItemFeatureByID(GpuLabels.PowerFeature).IsExposed() &&
                GpuInspectionCalculator.TryConfront(card, false, false, true, false) && card.FindItemFeatureByID(GpuLabels.PowerFeature).IsExposed() &&
                !card.FindItemFeatureByID(GpuLabels.SupplyFeature).IsExposed() &&
                !GpuInspectionCalculator.TryConfront(card, false, true, false, false),
                "Separate correct questions expose only their selected claim and each question is available once.");
            GameItem connector = Broken(true); connector.SetValue(1000); connector.Owned = false;
            EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(connector);
            GpuLabels.SetClaim(connector, GpuLabels.SupplyFeature, 1);
            long inflated = connector.GetBaseValue();
            Assert(GpuInspectionCalculator.TryConfront(connector, false, false, false, true) &&
                connector.GetBaseValue() == (long)Math.Round(inflated * 0.4),
                "A proven missing connector applies the independent forty-percent final value.");
            GameItem noModules = Broken(false); noModules.SetValue(1000);
            Assert(noModules.GetBaseValue() == 1000, "Missing modules alone do not receive the connector discount.");

            foreach (var missing in new[] { new[] { "goldfinger_up" }, new[] { "goldfinger_down" },
                new[] { "goldfinger_up", "goldfinger_down" } })
            {
                GameItem damaged = MissingContacts(missing);
                var real = GpuInspectionState.Read(damaged);
                Assert(damaged.GetBaseValue() == 1000 && GpuRuntime.GetComputePower(damaged) == real.CoreComputePower &&
                    GpuRuntime.GetPowerPerCycle(damaged) == real.CorePowerPerCycle,
                    "One or both missing contacts leave compute and power intact and do not discount before confrontation.");
                real.RealComputePower *= 0.3;
                damaged.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(real)));
                damaged.onLoaded(damaged);
                damaged.Owned = true;
                bool canRun = ReadHardware(damaged, out var hardware);
                damaged.Owned = false;
                EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(damaged);
                Assert(GpuRuntime.GetComputePower(damaged) == real.CoreComputePower && !canRun && ServerHardware.PowerCost(Inventory(damaged)) < 0,
                    "Loading retains compute and energy requirements but prevents servers from using missing-contact hardware.");
                EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(damaged);
                Assert(GpuInspectionCalculator.TryConfront(damaged, true, false, false, false) && damaged.GetBaseValue() == 200 &&
                    damaged.IsTag(GpuLabels.GoldfingerExposedTag), "Either missing contact receives twenty-percent value only after successful confrontation.");
                damaged.onLoaded(damaged);
                var damagedTooltip = Tooltip(damaged);
                Assert(damaged.GetBaseValue() == 200 && damagedTooltip.Lines.Any(line => line.Text == "金手指缺失") &&
                    damagedTooltip.Lines.Count == 3 && !damagedTooltip.Lines.Any(line => line.Text.Contains("供电模块全部缺失")),
                    "Current-format reload preserves exposed physical damage, its value and tooltip warning.");
            }
            GameItem combined = MissingContacts(new[] { "goldfinger_up", "power_interface" });
            GpuLabels.SetClaim(combined, GpuLabels.SupplyFeature, 1);
            Assert(combined.GetBaseValue() == 1000 && GpuInspectionCalculator.TryConfront(combined, true, false, false, false) &&
                combined.GetBaseValue() == 200 && GpuInspectionCalculator.TryConfront(combined, false, false, false, true) &&
                combined.GetBaseValue() == 80, "Goldfinger and connector discounts multiply after successful atomic exposure.");
            Assert(GpuLabels.Price(123, 100, 50, true, true) == 8, "Combined discounts round once at the end.");

            var ui = new InspectionUIManager();
            ui.OnInspectItem(card);
            var first = new NativeCalculatorView(ui);
            Assert(first != null && first.Has("gpu_calculator_modules_missing_minus") &&
                first.Has("gpu_calculator_memory_wrong_plus") && !first.Has("confront") &&
                !first.Has("missing_interface") && ui.confrontPanel.activeSelf && !ui.toggle2.gameObject.activeSelf &&
                !ui.toggle3.gameObject.activeSelf && ui.toggle1.gameObject.activeSelf && ui.toggle4.gameObject.activeSelf,
                "Only previously processed questions disappear; other questions remain usable before buying.");
            ui.OnInspectItem(connector);
            Assert(first.Closed && !ReferenceEquals(first.Root, new NativeCalculatorView(ui).Root), "Switching cards releases the previous calculator.");
            var second = new NativeCalculatorView(ui);
            ui.CloseUI();
            Assert(second.Closed && ui.confrontTitleTMP.gameObject.activeSelf,
                "Closing native inspection closes the owned calculator and restores the title for other native inspection types.");

            GameItem challenged = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
            challenged.SetValue(1000); challenged.Owned = false;
            EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(challenged);
            GpuLabels.SetClaim(challenged, GpuLabels.ComputeFeature, GpuRuntime.GetComputePower(challenged) * 1.25);
            GpuLabels.SetClaim(challenged, GpuLabels.PowerFeature, GpuRuntime.GetPowerPerCycle(challenged) * 0.75);
            ui.OnInspectItem(challenged);
            var calculator = new NativeCalculatorView(ui);
            CheckCalculatorTextAndPosition(ui, calculator);
            foreach (var location in new[] { new UnityEngine.Vector3(-400, -90, 0), new UnityEngine.Vector3(400, -300, 0) })
            {
                ui.confrontPanel.transform.localPosition = location;
                ui.OnInspectItem(challenged);
                calculator = new NativeCalculatorView(ui);
                CheckCalculatorTextAndPosition(ui, calculator);
            }
            ui.confrontPanel.transform.localPosition = new UnityEngine.Vector3(400, -90, 0);
            ui.OnInspectItem(challenged);
            calculator = new NativeCalculatorView(ui);
            Assert(ReferenceEquals(calculator.Root.CloneSource, ui.appraisalPanel) && !ui.appraisalPanel.activeSelf &&
                calculator.Root.GetComponentsInChildren<TMPro.TMP_InputField>(false).Length == 4 &&
                calculator.Root.GetComponentsInChildren<TMPro.TMP_InputField>(false).All(input => ReferenceEquals(input.gameObject.CloneSource,
                    ui.apprisalServiceInputField.gameObject) && input.onValueChanged != ui.apprisalServiceInputField.onValueChanged) &&
                calculator.Root.GetComponentsInChildren<UnityEngine.UI.Button>(false).All(button => button.onClick.GetPersistentEventCount() == 0),
                "The calculator clones the native appraisal frame and four original inputs; cloned callbacks replace all native price events.");
            string snapshot = challenged.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            string key = "gpu_calculator_memory_missing";
            Assert(calculator.Get(key).Text == "0" && ui.confrontPanel.activeSelf && !ui.toggle5.gameObject.activeSelf &&
                ui.toggle1.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Single().text == "金手指",
                "Counters start at zero and the native panel provides exactly four named accusation choices.");
            calculator.Get(key + "_minus").Click();
            Assert(calculator.Get(key).Text == "0", "Minus at zero stays zero.");
            calculator.Get(key + "_plus").Click();
            Assert(calculator.Get(key).Text == "1" && calculator.Get("compute").Text == "有效算力：5",
                "Plus adds exactly one and immediately refreshes effective compute.");
            calculator.Get(key + "_minus").Click();
            Assert(calculator.Get(key).Text == "0" && calculator.Get("compute").Text == "有效算力：10",
                "Minus subtracts exactly one and restores the calculated result.");
            calculator.Get("gpu_calculator_modules_missing_plus").Click();
            Assert(calculator.Get("power").Text == "有效耗电量：0", "Missing every module calculates zero effective power demand.");
            calculator.Get("gpu_calculator_modules_missing_minus").Click();
            for (int i = 0; i < 10; i++) calculator.Get(key + "_plus").Click();
            Assert(calculator.Get(key).Text == "2", "Plus cannot exceed this independent fixture's slot count.");
            calculator.Get("gpu_calculator_memory_wrong_plus").Click();
            Assert(calculator.Get("compute").Text.Contains("—") && calculator.Get("status").Text.Length > 0,
                "Combined memory defect counts above slot count do not produce a misleading calculation.");
            Assert(challenged.GetTagReadonly(GpuInspectionState.StateTag).GetString() == snapshot,
                "Using the calculator never modifies hardware or claims.");
            int victoryBefore = EmporiumAudioPlayer.Instance.audioBeatVictory.Plays;
            int failureBefore = EmporiumAudioPlayer.Instance.audioFail.Plays;
            int rewardsBefore = StoreReputation.CurrentClientRewardCalls;
            ui.toggle1.isOn = true;
            ui.OnConfront();
            Assert(ui.Failures == 1 && !calculator.Closed && challenged.GetBaseValue() == 1100 &&
                !challenged.FindItemFeatureByID(GpuLabels.ComputeFeature).IsExposed() && !ui.toggle1.gameObject.activeSelf &&
                ui.toggle2.gameObject.activeSelf && ui.confrontPanel.activeSelf &&
                EmporiumAudioPlayer.Instance.audioFail.Plays == failureBefore + 1,
                "A failed single question is removed without changing price or closing other questions.");
            Assert(DialogUIManager.Instance.LastDialogue.plainText == "什么意思" &&
                DialogUIManager.Instance.LastDialogue.nextDialogue.plainText == "你再好好看看呢。",
                "Failure uses the exact two-line native dialogue chain.");
            FinishGpuDialogue();
            ui.toggle2.isOn = true;
            ui.toggle3.isOn = true;
            Assert(!ui.toggle2.isOn && ui.toggle3.isOn, "Selecting another native toggle deselects the previous question.");
            ui.toggle2.isOn = true;
            calculator.Get(key).Input("-1");
            calculator.Root.GetComponentsInChildren<TMPro.TMP_InputField>(false).First().onSelect.Invoke("0");
            Assert(StoreUIManager.Instance.Typing, "Native numeric input focus suppresses store keyboard shortcuts.");
            DialogUIManager.Instance.RejectStart = true;
            ui.OnConfront();
            Assert(ui.Successes == 0 && challenged.GetBaseValue() == 1100 && ui.toggle2.gameObject.activeSelf,
                "A rejected dialogue does not consume a question, expose a feature or change price.");
            DialogUIManager.Instance.RejectStart = false;
            ui.OnConfront();
            Assert(ui.Successes == 1 && challenged.GetBaseValue() == 1000 && !calculator.Closed && !StoreUIManager.Instance.Typing &&
                ui.ConfrontItem == challenged.identifier && ui.ConfrontType == "confront" && !ui.toggle2.gameObject.activeSelf &&
                ui.toggle3.gameObject.activeSelf && ui.confrontPanel.activeSelf &&
                EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore,
                "Success settles the selected question and leaves inspection usable without playing victory during the first line.");
            Assert(DialogUIManager.Instance.LastDialogue.plainText == "你说得对" &&
                DialogUIManager.Instance.LastDialogue.nextDialogue.plainText == "那我给你便宜点儿吧",
                "Success uses the exact two-line native dialogue chain.");
            DialogUIManager.Instance.LastDialogue.startAction();
            ui.toggle3.isOn = true;
            ui.OnConfront();
            Assert(ui.Successes == 1 && EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore,
                "Repeated start actions and clicks while the result dialogue is pending cannot settle another question.");
            FinishGpuDialogue();
            FinishGpuDialogue();
            Assert(EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore + 1,
                "Only the final dialogue end plays victory, and a repeated end callback cannot replay it.");
            ui.OnConfront();
            Assert(ui.Successes == 2 && !ui.toggle3.gameObject.activeSelf && ui.toggle4.gameObject.activeSelf &&
                EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore + 1,
                "After the result chain ends a remaining question can be settled independently.");
            FinishGpuDialogue();
            Assert(EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore + 2,
                "Each successful question waits until its own result chain finishes to play victory.");
            ui.toggle4.isOn = true;
            ui.OnConfront();
            Assert(ui.Failures == 2 && GpuInspectionCalculator.AvailableQuestions(challenged) == 0 && ui.confrontPanel.activeSelf &&
                new[] { ui.toggle1, ui.toggle2, ui.toggle3, ui.toggle4 }.All(toggle => !toggle.gameObject.activeSelf),
                "Successes and failures all consume their individual options while the panel stays available until ownership changes.");
            FinishGpuDialogue();
            Assert(StoreReputation.CurrentClientRewardCalls == rewardsBefore &&
                EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore + 2,
                "Overstated hardware and failed questions grant no beneficial-correction reward, and failures never play victory.");
            challenged.Owned = true;
            GpuInspectionAvailabilityPatch.Postfix(ui);
            Assert(!ui.confrontPanel.activeSelf && !GpuInspectionCalculator.CanConfront(challenged),
                "Buying the GPU hides confrontation and blocks further attempts immediately.");
            challenged.Owned = false;
            ui.OnInspectItem(challenged);
            Assert(GpuInspectionCalculator.AvailableQuestions(challenged) == 0,
                "Reopening inspection cannot recover already consumed successful or failed questions.");
            GameItem physical = MissingContacts(new[] { "goldfinger_down" });
            ui.OnInspectItem(physical);
            Assert(!new NativeCalculatorView(ui).Has("close") && ui.confrontPanel.activeSelf,
                "The compact calculator has no close button and retains independent native confrontation.");
            ui.toggle1.isOn = true;
            ui.OnConfront();
            Assert(ui.NativePriceWrites == 0 && ui.NativeEstimates == 0, "GPU input, +/- buttons and close never invoke original appraisal price or estimate callbacks.");
            Assert(physical.GetBaseValue() == 200, "Physical confrontation applies the saved goldfinger discount.");
            FinishGpuDialogue();
            var ownedGroup = ui.toggle1.group;
            ui.CloseUI();
            Assert(ui.toggle1.group == null && ui.toggle2.group == null && ownedGroup.Destroyed,
                "Closing GPU inspection restores native toggle groups and destroys the mod-owned single-choice group.");
            Assert(GpuInspectionConfrontPatch.Prefix(new InspectionUIManager()), "The confrontation patch leaves other inspection flows alone.");
            CheckBeneficialCorrections();
            CheckConfrontDiscounts();
            CheckOwnedMatching();
        }
        finally
        {
            InspectionUIManager.TradeLocked = false;
            GpuInspectionCalculator.Close();
            PlayerStore.Instance = previousPlayer; EmporiumEntry.Instance = previousStore;
        }
    }

    private static void CheckBeneficialCorrections()
    {
        foreach (int choice in new[] { 1, 2, 4, 8 })
        {
            GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
            card.SetValue(1000); card.Owned = false;
            string id = choice == 1 ? GpuLabels.GoldfingerFeature : choice == 2 ? GpuLabels.ComputeFeature : choice == 4 ? GpuLabels.PowerFeature : GpuLabels.SupplyFeature;
            double actual = GpuLabels.ReadValue(card.FindItemFeatureByID(id).realCondition, -1);
            double claimed = choice == 2 ? actual * 0.75 : choice == 4 ? actual * 1.25 : 0;
            GpuLabels.SetClaim(card, id, claimed);
            string snapshot = card.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(card);
            var ui = new InspectionUIManager();
            ui.OnInspectItem(card);
            // Moving an inspected offer to the player's counter must retain confrontation.
            EmporiumEntry.Instance.backInvinvElement.UncheckedAccept(card);
            GpuInspectionAvailabilityPatch.Postfix(ui);
            Assert(!EmporiumEntry.Instance.frontInvinvElement.childItems.Contains(card) &&
                ui.confrontPanel.activeSelf && GpuInspectionCalculator.CanConfront(card),
                "An unpurchased GPU remains confrontable after moving to the player-side counter.");
            ui.OnInspectItem(card);
            Assert(ui.confrontPanel.activeSelf, "Opening inspection directly on the player-side counter also enables confrontation.");
            if (choice == 1) ui.toggle1.isOn = true;
            else if (choice == 2) ui.toggle2.isOn = true;
            else if (choice == 4) ui.toggle3.isOn = true;
            else ui.toggle4.isOn = true;
            int rewardBefore = StoreReputation.CurrentClientReward;
            int callsBefore = StoreReputation.CurrentClientRewardCalls;
            int victoryBefore = EmporiumAudioPlayer.Instance.audioBeatVictory.Plays;
            long quoteBefore = card.GetBaseValue();
            ui.OnConfront();
            var result = DialogUIManager.Instance.LastDialogue;
            ItemFeature feature = card.FindItemFeatureByID(id);
            Assert(result.plainText == "真的吗" && result.nextDialogue.plainText == "谢谢你的提醒" &&
                ui.Successes == 1 && feature.IsExposed() && feature.IsFeatureDiscovered() && feature.IsFeatureMatch() &&
                GpuLabels.ReadValue(feature.fakeCondition, -1) == actual &&
                StoreReputation.CurrentClientReward == rewardBefore + 2 && StoreReputation.CurrentClientRewardCalls == callsBefore + 1,
                "Better actual compute, lower actual power or a healthy connector matches the label and grants the native +2 faction reward.");
            Assert(card.GetTagReadonly(GpuInspectionState.StateTag).GetString() == snapshot &&
                card.FindItemFeatureByID(GpuLabels.ConfrontDiscountFeature) == null &&
                (choice == 4 ? card.GetBaseValue() == quoteBefore : card.GetBaseValue() > quoteBefore) &&
                (GpuInspectionCalculator.AvailableQuestions(card) & choice) == 0 &&
                EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore,
                "Beneficial corrections update the quoted value, preserve hardware, consume only their question and defer victory.");
            result.startAction();
            Assert(StoreReputation.CurrentClientRewardCalls == callsBefore + 1,
                "Repeating an accepted result cannot duplicate reputation rewards.");
            if (choice == 8) ui.CloseUI();
            FinishGpuDialogue();
            FinishGpuDialogue();
            Assert(EmporiumAudioPlayer.Instance.audioBeatVictory.Plays == victoryBefore + 1,
                "The completed successful chain plays victory once even if inspection closed during the dialogue.");
            card.onLoaded(card);
            Assert(card.FindItemFeatureByID(id).IsFeatureMatch(), "Rebinding labels retains a beneficially corrected public claim.");
            card.Owned = true;
            GpuInspectionAvailabilityPatch.Postfix(ui);
            Assert(!GpuInspectionCalculator.CanConfront(card) && !ui.confrontPanel.activeSelf,
                "Purchasing a GPU on the player's counter blocks further confrontation.");
            ui.CloseUI();
        }
    }

    private static void CheckEditorLabels()
    {
        foreach (string language in new[] { "en", "zh" })
        {
            ModHelper.Locale = language;
            GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
            GpuLabels.SetClaim(card, GpuLabels.SupplyFeature, 0);
            GpuLabels.SetClaim(card, GpuLabels.GoldfingerFeature, 0);
            card.SetValue(1000);
            string hardware = card.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            var manager = new NegociationUIManager();
            foreach (string id in new[] { GpuLabels.ComputeFeature, GpuLabels.PowerFeature, GpuLabels.SupplyFeature, GpuLabels.GoldfingerFeature })
            {
                var element = new LabelerElement();
                element.InitElement(card.FindItemFeatureByID(id));
                Assert(element.dropdown.Refreshes == 1 && element.dropdown.options.All(option => !option.text.Contains("(价值：0%)") &&
                    !option.text.Contains("(Value: 0%)")) &&
                    (id == GpuLabels.SupplyFeature ? element.currentFeatureDisplay.text.Contains("-60%") :
                    id == GpuLabels.GoldfingerFeature ? element.currentFeatureDisplay.text.Contains("-80%") :
                    !element.currentFeatureDisplay.text.Contains("%") && element.dropdown.options.All(option => !option.text.Contains("%"))),
                    "The printer omits numeric zero-value suffixes while defect choices show their actual price penalties.");
                var row = new ItemFeatureUI { itemFeature = card.FindItemFeatureByID(id) };
                manager.Rows.Add(row);
            }
            var nativeRow = new ItemFeatureUI { itemFeature = new ItemFeature { identifier = "native" } };
            nativeRow.TMP.text = "native-original";
            manager.Rows.Add(nativeRow);
            GpuNegotiationLabelsPatch.Postfix(manager);
            Assert(manager.Rows[2].TMP.text.Contains("-60%") && manager.Rows[3].TMP.text.Contains("-80%") &&
                nativeRow.TMP.text == "native-original" && Tooltip(card).Lines.All(line => !line.Text.Contains("%")),
                "Only negotiation defect rows use dedicated value keys; property tooltips and native items keep their own text.");
            var goldfinger = card.FindItemFeatureByID(GpuLabels.GoldfingerFeature);
            goldfinger.fakeCondition = ItemConditionList.conditionLookup[goldfinger.category].Single(condition => GpuLabels.ReadValue(condition, -1) == 1);
            Assert(card.GetBaseValue() == 400 && card.GetTagReadonly(GpuInspectionState.StateTag).GetString() == hardware,
                "The printer's normal contact option removes only the claimed contact penalty without changing actual hardware.");
            goldfinger.fakeCondition = ItemConditionList.conditionLookup[goldfinger.category].Single(condition => GpuLabels.ReadValue(condition, -1) == 0);
            Assert(card.GetBaseValue() == 80 && LabelerHelper.GetEditableLabelCount(card) == 4,
                "The printer's missing-contact option applies its independent claimed penalty and remains editable.");
        }
        ModHelper.Locale = "zh";
    }

    private static void CheckPrinterRanges()
    {
        GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
        var state = GpuInspectionState.Read(card);
        state.CoreComputePower = 70;
        state.CorePowerPerCycle = 3;
        state.RealComputePower = 7.125;
        state.RealPowerPerCycle = 5;
        state.Parts.RemoveAll(part => part.Slot.StartsWith("power_module_", StringComparison.Ordinal));
        for (int i = 0; i < 8; i++) state.Parts.Add(new GpuInspectionPart
            { Slot = "power_module_test_" + i, Sprite = "test_module", Condition = "normal" });
        card.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
        GpuLabels.Ensure(card);
        var compute = card.FindItemFeatureByID(GpuLabels.ComputeFeature);
        var power = card.FindItemFeatureByID(GpuLabels.PowerFeature);
        var computeOptions = ItemConditionList.conditionLookup[compute.category];
        var powerOptions = ItemConditionList.conditionLookup[power.category];
        Assert(computeOptions.Select(option => GpuLabels.ReadValue(option, -1)).SequenceEqual(new double[] { 0, 10, 20, 30, 40, 50, 60, 70 }) &&
            powerOptions.Select(option => GpuLabels.ReadValue(option, -1)).SequenceEqual(new double[] { 3, 6, 9, 12, 15, 18, 21, 24 }),
            "Independent core budgets and eight module slots produce eight evenly spaced in-range printer choices, including both endpoints.");
        GpuLabels.SetClaim(card, GpuLabels.ComputeFeature, 999);
        GpuLabels.SetClaim(card, GpuLabels.PowerFeature, 0);
        GpuLabels.Ensure(card);
        Assert(computeOptions.Count == 8 && powerOptions.Count == 8 && GpuLabels.ReadValue(compute.fakeCondition, -1) == 999 &&
            GpuLabels.ReadValue(compute.realCondition, -1) == 7.125 && GpuLabels.ReadValue(power.realCondition, -1) == 5,
            "Exact true and custom claims are preserved without leaking extra, fractional or out-of-range values into printer options.");
        state.Parts.RemoveAll(part => part.Slot.StartsWith("power_module_test_", StringComparison.Ordinal) && part.Slot != "power_module_test_0");
        card.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
        GpuLabels.Ensure(card);
        var narrowOptions = ItemConditionList.conditionLookup[card.FindItemFeatureByID(GpuLabels.PowerFeature).category];
        Assert(narrowOptions.Count == 1 && GpuLabels.ReadValue(narrowOptions[0], -1) == 3 && powerOptions.Count == 8,
            "A single-module zero-width range has one valid choice and cannot corrupt another assembly's wider range.");
        state.CorePowerPerCycle = 0;
        card.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
        GpuLabels.Ensure(card);
        var zeroOptions = ItemConditionList.conditionLookup[card.FindItemFeatureByID(GpuLabels.PowerFeature).category];
        Assert(zeroOptions.Count == 1 && GpuLabels.ReadValue(zeroOptions[0], -1) == 0,
            "Zero power budgets do not create positive printer choices.");
    }

    private static void CheckConfrontDiscounts()
    {
        StoreClient seller = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
        string previousId = seller.identifier;
        try
        {
            foreach (string id in new[] { ElectronicsMarket.DishonestSellerId, ElectronicsSalvageSellers.DishonestScavengerId })
            {
                seller.identifier = id;
                GameItem card = MissingContacts(new[] { "goldfinger_down", "power_interface" });
                GpuLabels.SetClaim(card, GpuLabels.ComputeFeature, 20);
                GpuLabels.SetClaim(card, GpuLabels.PowerFeature, 0);
                GpuLabels.SetClaim(card, GpuLabels.SupplyFeature, 1);
                string hardware = card.GetTagReadonly(GpuInspectionState.StateTag).GetString();
                Assert(GpuInspectionCalculator.TryConfront(card, false, true, false, false) && card.GetBaseValue() == 1000 &&
                    card.GetCurrentValue() == 900 && card.FindItemFeatureByID(GpuLabels.ComputeFeature).IsFeatureMatch(),
                    "Both dishonest seller types match an overstated label and concede 10 percent of the corrected price.");
                Assert(!GpuInspectionCalculator.TryConfront(card, false, true, false, false) && card.GetCurrentValue() == 900,
                    "Repeating the same accusation cannot duplicate its concession.");
                Assert(GpuInspectionCalculator.TryConfront(card, false, false, true, false) && card.GetCurrentValue() == 800 &&
                    card.FindItemFeatureByID(GpuLabels.PowerFeature).IsFeatureMatch(),
                    "Two proven lies add to 20 percent rather than multiplying to 19 percent.");
                Assert(GpuInspectionCalculator.TryConfront(card, false, false, false, true) && card.GetBaseValue() == 400 &&
                    card.GetCurrentValue() == 280, "The 30 percent concession applies after the connector's physical 60 percent loss.");
                Assert(GpuInspectionCalculator.TryConfront(card, true, false, false, false) && card.GetBaseValue() == 80 &&
                    card.GetCurrentValue() == 48, "Four accusations total 40 percent after both physical discounts.");
                var concession = card.FindItemFeatureByID(GpuLabels.ConfrontDiscountFeature);
                Assert(concession.featureType == ItemFeature.FeatureType.TemporaryBuying && concession.valueStage == ItemFeature.ValueStage.Final &&
                    concession.GetCurrentValueMod() == -40 && card.itemFeatures.Count(feature => feature.identifier == concession.identifier) == 1 &&
                    card.GetTagReadonly(GpuInspectionState.StateTag).GetString() == hardware,
                    "One native buying-only final-stage concession accumulates all questions without touching real hardware.");
                card.itemFeatures.RemoveAll(feature => feature.featureType == ItemFeature.FeatureType.TemporaryBuying);
                card.Owned = true;
                Assert(card.GetCurrentValue() == 80, "Native purchase cleanup removes the seller's concession from later resale value.");

                GameItem honest = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
                honest.Owned = false;
                EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(honest);
                Assert(!GpuInspectionCalculator.TryConfront(honest, false, true, false, false) &&
                    honest.FindItemFeatureByID(GpuLabels.ConfrontDiscountFeature) == null,
                    "An incorrect accusation creates no discount even for a dishonest seller.");
            }
            CheckBeneficialCorrections();
        }
        finally { seller.identifier = previousId; }
    }

    private static void CheckOwnedMatching()
    {
        GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
        card.Owned = false;
        EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(card);
        var ui = new InspectionUIManager();
        ui.OnInspectItem(card);
        var calculator = new NativeCalculatorView(ui);
        var button = calculator.Root.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(control => control.gameObject.name == "match");
        string hardware = card.GetTagReadonly(GpuInspectionState.StateTag).GetString();
        Assert(!button.gameObject.activeSelf, "Unpurchased offers have no visible label-match button.");
        calculator.Get("gpu_calculator_memory_missing").Input("1");
        calculator.Get("match").Click();
        Assert(GpuLabels.ReadValue(card.FindItemFeatureByID(GpuLabels.ComputeFeature).fakeCondition, -1) == 10,
            "The match action also refuses unowned cards when directly invoked.");
        card.Owned = true;
        GpuInspectionAvailabilityPatch.Postfix(ui);
        Assert(button.gameObject.activeSelf && button.interactable && !ui.confrontPanel.activeSelf,
            "Purchasing during inspection immediately enables matching and hides confrontation.");
        GpuLabels.SetClaim(card, GpuLabels.SupplyFeature, 0);
        card.FindItemFeatureByID(GpuLabels.ComputeFeature).SetExposed(true);
        card.FindItemFeatureByID(GpuLabels.PowerFeature).SetExposed(true);
        calculator.Get("gpu_calculator_modules_missing").Input("1");
        calculator.Get("match").Click();
        Assert(GpuLabels.ReadValue(card.FindItemFeatureByID(GpuLabels.ComputeFeature).fakeCondition, -1) == 5 &&
            GpuLabels.ReadValue(card.FindItemFeatureByID(GpuLabels.PowerFeature).fakeCondition, -1) == 0 &&
            !card.FindItemFeatureByID(GpuLabels.ComputeFeature).IsExposed() && !card.FindItemFeatureByID(GpuLabels.PowerFeature).IsExposed() &&
            GpuLabels.ReadValue(card.FindItemFeatureByID(GpuLabels.SupplyFeature).fakeCondition, -1) == 0 &&
            card.GetTagReadonly(GpuInspectionState.StateTag).GetString() == hardware && GpuRuntime.GetComputePower(card) == 10,
            "Matching writes the player's calculated compute/power into public labels, clears exposure, preserves supply labels and real hardware.");
        calculator.Get("gpu_calculator_memory_wrong").Input("2");
        Assert(!button.interactable, "Invalid combined defect counts disable matching.");
        calculator.Get("match").Click();
        Assert(GpuLabels.ReadValue(card.FindItemFeatureByID(GpuLabels.ComputeFeature).fakeCondition, -1) == 5,
            "Invalid calculations cannot replace existing claims even through a direct click.");
        calculator.Get("gpu_calculator_memory_wrong").Input("0");
        var rect = button.gameObject.transform;
        Assert(rect.anchoredPosition.x == 266 && rect.anchoredPosition.y == -210 && rect.sizeDelta.x == 72 && rect.sizeDelta.y == 26 &&
            button.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).All(text => text.text == "匹配" &&
                ReferenceEquals(text.font, ui.confrontTitleTMP.font)), "The match button sits at the calculator's bottom right and uses the native Chinese font.");
        ui.CloseUI();
        calculator.Get("match").Click();
        Assert(calculator.Closed && card.GetTagReadonly(GpuInspectionState.StateTag).GetString() == hardware,
            "Closed calculator callbacks cannot change the inspected hardware.");
    }

    private static void FinishGpuDialogue()
    {
        Dialogue last = DialogUIManager.Instance.LastDialogue;
        while (last.nextDialogue != null) last = last.nextDialogue;
        last.endAction?.Invoke();
    }
    private static GameItem MissingContacts(string[] slots)
    {
        GameItem item = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
        var state = GpuInspectionState.Read(item);
        foreach (var part in state.Parts.Where(part => slots.Contains(part.Slot))) { part.Sprite = ""; part.Condition = "empty"; }
        state.RealComputePower = null; state.RealPowerPerCycle = null; state.PowerSupplyWorking = null;
        item.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
        item.onLoaded(item);
        item.SetValue(1000); item.Owned = false;
        // This helper models a seller concealing physical contact damage.
        GpuLabels.SetClaim(item, GpuLabels.GoldfingerFeature, 1);
        EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(item);
        return item;
    }
    private sealed class NativeCalculatorView
    {
        internal readonly UnityEngine.GameObject Root;
        internal bool Closed => Root.Destroyed || !Root.activeSelf;
        internal NativeCalculatorView(InspectionUIManager manager)
        {
            Root = manager.confrontPanel.GetComponentInParent<UnityEngine.Canvas>().transform.Children.Select(child => child.gameObject)
                .Single(node => node.name == "AgeOfAgiGpuCalculator" && node.activeSelf && !node.Destroyed);
        }
        private IEnumerable<UnityEngine.GameObject> Nodes(UnityEngine.Transform parent)
        {
            foreach (var child in parent.Children)
            {
                yield return child.gameObject;
                foreach (var descendant in Nodes(child)) yield return descendant;
            }
        }
        internal bool Has(string name) => Nodes(Root.transform).Any(node => node.name == name);
        internal NativeControl Get(string name) => new NativeControl(Nodes(Root.transform).Single(node => node.name == name));
    }
    private static void CheckCalculatorTextAndPosition(InspectionUIManager manager, NativeCalculatorView calculator)
    {
        var labelNames = new[] { "compute", "power", "status", "gpu_calculator_modules_missing_label",
            "gpu_calculator_memory_missing_label", "gpu_calculator_memory_wrong_label", "gpu_calculator_memory_slow_label" };
        foreach (string name in labelNames)
        {
            var text = calculator.Root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false).Single(entry => entry.gameObject.name == name);
            var localizer = text.gameObject.GetComponent<TMPLocalizer>();
            Assert(ReferenceEquals(text.font, manager.confrontTitleTMP.font) && ReferenceEquals(text.fontSharedMaterial, text.font.material) &&
                text.margin.x == 0 && text.margin.y == 0 && text.margin.z == 0 && text.margin.w == 0 &&
                text.fontSize >= 12 && text.fontSize <= 18 && text.enabled && localizer.enabled && localizer.ChangeFontOnly &&
                !text.gameObject.GetComponent<LocalizeStringEvent>().enabled,
                "Descriptions use the native language-aware font and matching atlas, retain font localization, and reset amount-field margins and sizing.");
        }
        Assert(!calculator.Has("title") && !calculator.Has("hint") && !calculator.Has("close") &&
            !manager.confrontTitleTMP.gameObject.activeSelf && calculator.Get("gpu_calculator_modules_missing_label").Text == "供电模块缺失数目" &&
            calculator.Get("compute").Text.StartsWith("有效算力：") && calculator.Get("power").Text.StartsWith("有效耗电量："),
            "The calculator includes complete descriptive labels and result names rather than bare numbers.");
        var rect = calculator.Root.transform;
        Assert(rect.parent == manager.magnifyingGlass.transform.parent && rect.GetSiblingIndex() < manager.magnifyingGlass.transform.GetSiblingIndex() &&
            calculator.Root.GetComponentsInChildren<UnityEngine.Canvas>(true).All(canvas => !canvas.overrideSorting),
            "The calculator renders below the native magnifier and cannot override sibling draw order.");
        var parent = (UnityEngine.RectTransform)rect.parent;
        var corners = new UnityEngine.Vector3[4];
        manager.confrontPanel.transform.GetWorldCorners(corners);
        float expectedTop = parent.InverseTransformPoint(corners[1]).y;
        if (expectedTop - rect.rect.height >= parent.rect.yMin && expectedTop <= parent.rect.yMax)
            Assert(Math.Abs(rect.localPosition.y - expectedTop) < 0.001,
                "The calculator aligns with the visible confrontation top under translated/scaled ancestors, independently of the hidden appraisal host.");
        Assert(rect.rect.width == 350 && rect.rect.height == 244 && rect.localPosition.x + rect.rect.xMin >= parent.rect.xMin &&
            rect.localPosition.x + rect.rect.xMax <= parent.rect.xMax && rect.localPosition.y + rect.rect.yMin >= parent.rect.yMin &&
            rect.localPosition.y + rect.rect.yMax <= parent.rect.yMax,
            "The compact native panel stays within canvas bounds when moved near the left or bottom edge.");
    }
    private sealed class NativeControl
    {
        private readonly UnityEngine.GameObject node;
        internal NativeControl(UnityEngine.GameObject node) { this.node = node; }
        internal string Text => node.GetComponent<TMPro.TMP_InputField>()?.text ?? node.GetComponent<TMPro.TextMeshProUGUI>()?.text;
        internal void Click() => node.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        internal void Input(string value)
        {
            var field = node.GetComponent<TMPro.TMP_InputField>();
            field.SetTextWithoutNotify(value);
            field.onValueChanged.Invoke(value);
        }
    }
    private static void AssertProbabilityIntervals(int[] outcomes, int categoryCount)
    {
        Assert(outcomes.Length == 100 && outcomes.All(outcome => outcome >= 0 && outcome < categoryCount),
            "Every integer draw in [0, 100) must select a valid probability outcome.");
        var coverage = new int[100];
        for (int category = 0; category < categoryCount; category++)
        {
            int first = Array.IndexOf(outcomes, category);
            if (first < 0) continue; // Zero-percent outcomes are valid tuning choices.
            int last = Array.LastIndexOf(outcomes, category);
            for (int roll = first; roll <= last; roll++) coverage[roll]++;
        }
        Assert(coverage.All(count => count == 1) && coverage.Sum() == 100,
            "Probability outcome intervals must be contiguous, disjoint and cover exactly 100 integer draws.");
    }

    private sealed class ClaimRandom : Random
    {
        private readonly Queue<int> values;
        internal int Calls, LastMaximum;
        internal ClaimRandom(params int[] rolls) { values = new Queue<int>(rolls); }
        public override int Next(int maxValue)
        {
            Calls++;
            LastMaximum = maxValue;
            int value = values.Dequeue();
            Assert(value >= 0 && value < maxValue, "A deterministic random draw must remain within its requested range.");
            return value;
        }
    }
    private static void Choose(GameItem item, string id, double value)
    {
        // Integration scenarios choose claims independently of the printer grid.
        GpuLabels.SetClaim(item, id, value);
    }
    private static GameItem Broken(bool connectorMissing)
    {
        GameItem item = DirectoryMaster.Item("age_of_agi.gpu0");
        var state = GpuInspectionState.Read(item);
        foreach (var part in state.Parts.Where(part => connectorMissing ? part.Slot == "power_interface" : part.Slot.StartsWith("power_module_")))
        { part.Sprite = ""; part.Condition = "empty"; }
        state.RealComputePower = null; state.RealPowerPerCycle = null; state.PowerSupplyWorking = null;
        item.itemFeatures.RemoveAll(GpuLabels.IsFeature);
        item.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
        item.ModifyTag(GpuRuntime.PowerPerCycleTag, tag => tag.SetInt(9999));
        item.onLoaded(item);
        return item;
    }
    private static GameGridInventory Inventory(GameItem item) { var inventory = new GameGridInventory(20, 20); inventory.Accept(item); return inventory; }
    private static bool ReadHardware(GameItem gpu, out ServerHardware hardware) => ServerHardware.TryRead(
        Inventory(DirectoryMaster.Item("age_of_agi.cpu0")), Inventory(gpu), Inventory(DirectoryMaster.Item("age_of_agi.mem0")),
        new GameSlotInventory(), ServerCatalog.Load().Definition, out hardware);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
