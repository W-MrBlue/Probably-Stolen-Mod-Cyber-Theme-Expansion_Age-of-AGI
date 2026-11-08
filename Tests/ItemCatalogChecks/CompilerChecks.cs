using AgeOfAgi.Items.Logic.BaseModel;
using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.Compiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;

internal static partial class CompilerChecks
{
    private const string ValidBaseModels = "id,gridWidth,gridHeight,memoryMultiplier\nage_of_agi.base_grid,4,6,1\n";

    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        CheckBaseModelCatalog(items);
        CheckCatalogs(items);
        LoadedMod loaded = Program.PrepareLoadedMod(items);
        byte[] png = File.ReadAllBytes(Path.Combine(ModHelper.Root, "Textures/Items/compiler_working.png"));
        loaded.Sprites[CompilerState.WorkingSpriteKey] = new TestSprite
        {
            Shape = PngGridReader.Read(png, 16), rect = new TestRect { width = 48, height = 48 }
        };
        var registration = new ItemRegistration(loaded.Manifest);
        var directory = new ModItemDirectory();
        ModHelper.FileReads = 0;
        registration.Register(directory, items);
        Assert(ModHelper.FileReads == 14, "Read all fourteen catalogs once per batch.");
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        try
        {
            CheckBaseModelItems(directory);
            CheckBaseModelTooltip(directory);
            CheckStructureAndExecution();
            CheckEmptyBatteryLifecycle();
            CheckComponentAttributes();
            CheckBaseMemoryMultiplier();
            CheckDuplicateDurability();
            CheckLinkPairing();
            CheckFeedback();
            CheckArchitectures();
            CheckTaskHeads();
            CheckFailuresAndReentrancy();
            CheckRestoredState();
            CheckTooltips();
            Func<GameItem> previous = directory.Factories[CompilerCatalog.CompilerId];
            foreach (string path in new[] { CompilerCatalog.CatalogPath, AiPartCatalog.CatalogPath, BaseModelCatalog.CatalogPath })
            {
                ModHelper.FileOverrides[path] = "{";
                ExpectFailure(() => registration.Register(directory, items), path);
                Assert(ReferenceEquals(previous, directory.Factories[CompilerCatalog.CompilerId]), "Failed config must preserve registered factories.");
                ModHelper.FileOverrides.Remove(path);
            }
        }
        finally
        {
            ModHelper.FileOverrides.Clear();
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
            registration.Unregister();
        }
    }

    private static void CheckCatalogs(IReadOnlyList<ItemDefinition> items)
    {
        string csv = File.ReadAllText(Path.Combine(ModHelper.Root, CompilerCatalog.CatalogPath));
        CompilerCatalog.Load().ValidateMappings(items);
        AiPartCatalog.Load().ValidateMappings(items);
        foreach (string invalid in new[] { "", "id", "id,id", "id\nage_of_agi.invalid", csv + " {}" })
            ExpectFailure(() => CompilerCatalog.Parse(invalid), CompilerCatalog.CatalogPath);
        foreach (string field in new[] { "id", "output", "initialDurability", "duplicateDurabilityRetention", "linkComputeMultiplier", "feedbackTrainingRetention", "regularizationMultiplier" })
        {
            var root = CsvFixture.Parse(csv);
            root.RemoveColumn(field);
            ExpectFailure(() => CompilerCatalog.Parse(root.ToString()), CompilerCatalog.CatalogPath);
        }
        foreach (var change in new[] { ("initialDurability", "0"), ("duplicateDurabilityRetention", "1.1"),
            ("linkComputeMultiplier", "0.9"), ("feedbackTrainingRetention", "-1"), ("regularizationMultiplier", "0.9"), ("initialDurability", "\"10\""),
            ("residualTrainingMultiplier", "0"), ("batchTrainingMultiplier", "NaN"), ("heavyDecoderMemoryMultiplier", "-1") })
        {
            var root = CsvFixture.Parse(csv);
            root.Rows[0][change.Item1] = change.Item2;
            ExpectFailure(() => CompilerCatalog.Parse(root.ToString()), CompilerCatalog.CatalogPath);
        }
        var legacy = CsvFixture.Parse(csv);
        legacy.AddColumn("power", "10");
        ExpectFailure(() => CompilerCatalog.Parse(legacy.ToString()), CompilerCatalog.CatalogPath);
        var duplicate = CsvFixture.Parse(csv);
        duplicate.DuplicateFirstRow();
        duplicate.Rows[1]["id"] = "second_recipe";
        ExpectFailure(() => CompilerCatalog.Parse(duplicate.ToString()), CompilerCatalog.CatalogPath);
        ExpectFailure(() => CompilerCatalog.Load().ValidateMappings(items.Where(item => item.ItemId != "age_of_agi.ai_untrained").ToArray()), CompilerCatalog.CatalogPath);
        ExpectFailure(() => AiPartCatalog.Load().ValidateMappings(items.Where(item => item.ItemId != CompilerCatalog.LinkId).ToArray()), AiPartCatalog.CatalogPath);

        string parts = File.ReadAllText(Path.Combine(ModHelper.Root, AiPartCatalog.CatalogPath));
        foreach (string field in new[] { "role", "computeBonusPercent", "memoryGb", "trainingWork", "trainingData", "taskType" })
        {
            var root = CsvFixture.Parse(parts);
            root.RemoveColumn(field);
            ExpectFailure(() => AiPartCatalog.Parse(root.ToString()), AiPartCatalog.CatalogPath);
        }
        foreach (var change in new[] { ("role", "unknown"), ("computeBonusPercent", "0"),
            ("computeBonusPercent", "\"10\""), ("memoryGb", "1.5"), ("trainingWork", "-1"),
            ("trainingData", "0"), ("trainingData", "-1"), ("trainingData", "1.5"), ("trainingData", "2147483648"),
            ("id", "age_of_agi.part_enc_chip") })
        {
            var root = CsvFixture.Parse(parts);
            root.Rows[0][change.Item1] = change.Item2;
            ExpectFailure(() => AiPartCatalog.Parse(root.ToString()), AiPartCatalog.CatalogPath);
        }
    }

    private static void CheckStructureAndExecution()
    {
        var empty = new Rig();
        Assert(empty.Row.Panels.Select(panel => panel.role).SequenceEqual(new[] { "power", "base", "grid", "output", "control" }), "Keep stable native panel/storage order.");
        Assert(empty.Row.GetStorage(4) == null && empty.Grid.Width == 0 && empty.GridPanel.isHidden, "Control must not create storage; empty grid is hidden.");
        Assert(!GraphUtils.CanAccept(empty.Grid, DirectoryMaster.Item("age_of_agi.part_enc_0")), "Base is required before inserting components.");
        Assert(!GraphUtils.CanAccept(empty.Base, DirectoryMaster.Item("age_of_agi.part_enc_0")), "Base slot whitelist.");
        Assert(!GraphUtils.CanAccept(empty.Output, DirectoryMaster.Item("age_of_agi.ai_untrained")), "Output is externally read-only.");
        foreach (string id in new[] { "base_grid", "base_neural", "base_tsfm", "base_jepa" })
        {
            GameItem baseItem = DirectoryMaster.Item("age_of_agi." + id);
            Put(empty.Base, baseItem);
            Assert(empty.Grid.Width == BaseModelState.GetGridWidth(baseItem) && empty.Grid.Height == BaseModelState.GetGridHeight(baseItem), "Grid follows base only.");
            empty.Base.Remove(baseItem);
        }
        var rig = Filled();
        Assert(GeneralHelper.IsItemLocked(rig.Base.childItem), "Populated grid locks base.");
        ModHelper.Locale = "zh";
        Assert(ItemTooltipHelper.GetLocalizedMechanic("mech_tooltip_locked", Array.Empty<object>(), rig.Base.childItem) ==
            "[已锁定——需要先移除代码组件]", "Explain how to unlock the compiler base in the native locked row.");
        ModHelper.Locale = "en";
        Assert(!GraphUtils.CanAccept(rig.Grid, DirectoryMaster.Item("age_of_agi.part_enc_chip")), "Source chips are not assembly parts.");
        Assert(MachineParameters.GetPowerPerCycle(rig.Machine) == 10 && rig.Machine.SpriteKey == CompilerState.WorkingSpriteKey, "Machine cost is ten; runnable compiler works.");
        MachineryText preview = ((MachineryStack)rig.Row.GetPanel("control").content).Elements.OfType<MachineryText>().Single();
        Assert(preview.Text.Contains("Predicted compute: +20%") && preview.Text.Contains('\n') && !preview.Text.Contains("\\n"), "Render a multiline attribute preview.");
        rig.Grid.Items[0].ModifyTag(AiPartState.ComputeBonusTag, state => state.SetFloat(11));
        rig.Machine.contentWindow.stateChangeActionFunc();
        Assert(preview.Text.Contains("Predicted compute: +21%"), "Native inventory-state notifications refresh current attributes.");
        rig.Grid.Items[0].ModifyTag(AiPartState.ComputeBonusTag, state => state.SetFloat(10));
        Assert(rig.Machine.onCycleEndSlotItemFunc == null, "Compilation must not run overnight.");
        rig.Machine.onCycleEndSlotItemFunc?.Invoke(rig.Machine, null, null);
        Assert(rig.Output.childItems.Count == 0 && rig.Grid.childItems.Count == 2, "Night must leave assembly untouched.");
        rig.Click();
        GameItem product = rig.Output.childItems.Single();
        Assert(AiModelState.GetTaskType(product) == "computing" && AiModelState.GetMemoryGb(product) == 2, "Default task and summed memory.");
        Near(AiModelState.GetTrainingWork(product), 80, "Baseline work");
        Assert(AiModelState.GetTrainingData(product) == 20 && AiModelState.UsesIndependentTrainingData(product),
            "Compilation freezes a separate data budget on a version-two model.");
        Near(AiModelState.GetPredictedComputeBonus(product), 20, "Summed percentage points");
        Near(AiModelState.GetPredictedComputeMultiplier(product), 1.2, "Deployment multiplier includes 1x baseline");
        Near(AiModelState.GetPredictedDurability(product), 10, "Different families are not duplicates");
        Assert(rig.Grid.childItems.Count == 0 && Energy(rig.Battery.childItem) == 10 && !GeneralHelper.IsItemLocked(rig.Base.childItem), "Instant craft pays ten and unlocks retained base.");
        Assert(!rig.Base.childItem.IsTag(CompilerState.BaseLockedTag), "Clearing components removes the compiler-specific lock explanation.");
        Assert(!GraphUtils.CanAccept(rig.Output, DirectoryMaster.Item("age_of_agi.ai_untrained")), "Close output after crafting.");

        var all = new Rig(); all.Install();
        foreach (string id in new[] { "part_enc_0", "part_enc_1", "part_dec_0", "part_dec_2", "part_link", "part_feedback" }) Put(all.Grid, DirectoryMaster.Item("age_of_agi." + id));
        GameItem[] inputs = all.Grid.childItems.ToArray();
        var expected = all.Calculate();
        all.Click();
        Assert(inputs.All(item => item.Destroyed) && all.Output.childItems.Count == 1, "All placed parts participate and are consumed once.");
        Near(AiModelState.GetTrainingWork(all.Output.childItems.Single()), expected.TrainingWork, "Actual result equals preview");
    }

    private static void CheckEmptyBatteryLifecycle()
    {
        // SaveManager creates an empty instance before restoring any children.
        var rig = new Rig();
        Assert(rig.Battery.childItem == null && rig.Machine.SpriteKey == CompilerState.IdleSpriteKey,
            "Constructing a compiler with empty restored slots must not throw.");
        Put(rig.Base, DirectoryMaster.Item("age_of_agi.base_grid"));
        Put(rig.Grid, DirectoryMaster.Item("age_of_agi.part_enc_0"));
        Put(rig.Grid, DirectoryMaster.Item("age_of_agi.part_dec_0"));
        ExpectQuietClick(rig);
        rig.Machine.contentWindow.stateChangeActionFunc();
        Assert(rig.Machine.SpriteKey == CompilerState.IdleSpriteKey, "Complete assembly without a battery stays idle.");
        rig.Machine.onLoaded?.Invoke(rig.Machine);
        ExpectQuietClick(rig);

        GameItem battery = MakeBattery(20);
        Put(rig.Battery, battery);
        Assert(rig.Machine.SpriteKey == CompilerState.WorkingSpriteKey, "Adding power enables the restored assembly.");
        rig.Battery.Remove(battery);
        Assert(rig.Machine.SpriteKey == CompilerState.IdleSpriteKey, "Removing the battery refreshes safely.");
        ExpectQuietClick(rig);
        Put(rig.Battery, battery);
        rig.Click();
        Assert(rig.Output.childItems.Count == 1 && Energy(battery) == 10, "Reinserting power restores normal compilation.");
    }

    private static void CheckComponentAttributes()
    {
        GameItem part = DirectoryMaster.Item("age_of_agi.part_enc_0");
        Near(AiPartState.GetComputeBonus(part), 10, "Component factory attributes");
        part.ModifyTag(AiPartState.ComputeBonusTag, state => state.SetFloat(55));
        part.ModifyTag(AiPartState.MemoryTag, state => state.SetInt(7));
        part.ModifyTag(AiPartState.TrainingTag, state => state.SetFloat(12.5f));
        part.ModifyTag(AiPartState.TrainingDataTag, state => state.SetInt(19));
        part.onLoaded?.Invoke(part);
        var rig = new Rig(); rig.Install();
        Put(rig.Grid, part); Put(rig.Grid, DirectoryMaster.Item("age_of_agi.part_dec_0"));
        var result = rig.Calculate();
        Near(result.ComputeBonusPercent, 65, "Read saved part values rather than CSV defaults");
        Near(result.TrainingWork, 52.5, "Fractional component training");
        Assert(result.TrainingData == 29, "Read saved data independently of modified component work.");
        Assert(result.MemoryGb == 8, "Saved component memory contributes.");
        part.DisableTag(AiPartState.ComputeBonusTag);
        part.onLoaded?.Invoke(part);
        Near(AiPartState.GetComputeBonus(part), 10, "Backfill missing legacy part fields only");
        Assert(AiPartState.GetMemoryGb(part) == 7, "Backfill must preserve existing fields.");
        Assert(AiPartState.GetTrainingData(part) == 19, "Loading preserves the saved data requirement.");
        part.DisableTag(AiPartState.TrainingDataTag);
        part.onLoaded(part);
        Assert(AiPartState.GetTrainingData(part) == 10 && AiPartState.GetTrainingWork(part) == 12.5f,
            "Older components backfill only missing data from configuration without resetting saved work.");
    }

    private static void CheckBaseMemoryMultiplier()
    {
        var rig = Filled();
        var baseline = rig.Calculate();
        rig.Base.childItem.ModifyTag(BaseModelState.MemoryMultiplierTag, state => state.SetFloat(3));
        var multiplied = rig.Calculate();
        Assert(multiplied.MemoryGb == baseline.MemoryGb * 3, "Base memory multiplier applies after component memory is summed.");
        Near(multiplied.InitialDurability, baseline.InitialDurability, "Base memory multiplier does not impose a durability limit.");
        Near(multiplied.TrainingWork, baseline.TrainingWork, "Base memory multiplier leaves training work unchanged.");
        Near(multiplied.ComputeBonusPercent, baseline.ComputeBonusPercent, "Base memory multiplier leaves compute unchanged.");
    }

    private static void CheckDuplicateDurability()
    {
        var rig = new Rig(); rig.Install();
        rig.Place("part_enc_0", 0, 0); rig.Place("part_enc_0", 1, 0, rotation: 1);
        rig.Place("part_enc_0", 2, 0); rig.Place("part_dec_0", 3, 0); rig.Place("part_dec_0", 4, 0);
        Near(rig.Calculate().InitialDurability, 7, "Round only after duplicate penalties; rotations do not reset counts");
        rig.Place("part_dec_1", 5, 0);
        Near(rig.Calculate().InitialDurability, 7, "Different numeric shape suffix has no extra penalty");
        rig.Place("part_regularization", 6, 0);
        Near(rig.Calculate().InitialDurability, 9, "Apply regularization before final rounding: 7.29 times 1.2 rounds to nine");
        var plain = new Rig(); plain.Install();
        plain.Place("part_enc_0", 0, 0); plain.Place("part_dec_0", 1, 0); plain.Place("part_regularization", 2, 0);
        Near(plain.Calculate().InitialDurability, 12, "Regularization can exceed ten");
        Near(AiModelState.RoundDurability(2.5), 3, "Half values round away from zero rather than to even");
        plain.Place("part_regularization", 3, 0);
        Assert(!plain.TryCalculate(out _, out string reason) && reason == "compiler_multiple_regularizers", "Reject multiple regularizers.");
    }

    private static void CheckLinkPairing()
    {
        var cross = new Rig(); cross.Install();
        cross.Place("part_link", 2, 2, new byte[] { 1, 1 }, 2);
        cross.Place("part_link", 2, 5, new byte[] { 1, 1 }, 2);
        cross.Place("part_enc_4", 1, 2); cross.Place("part_enc_0", 1, 5);
        cross.Place("part_dec_2", 4, 2); cross.Place("part_dec_1", 4, 5);
        var result = cross.Calculate();
        Assert(result.LinkedPairs == 2, "Two endpoint connectors form two distinct pairs.");
        Near(result.ComputeBonusPercent, 75 * 1.2 * 1.5, "Fully paired symmetric assembly gains U-shape after link bonuses");

        var priority = new Rig(); priority.Install();
        priority.Place("part_link", 2, 2, new byte[] { 1, 1 }, 2);
        priority.Place("part_enc_0", 1, 2); priority.Place("part_enc_4", 2, 1); // Long side.
        priority.Place("part_dec_0", 4, 2);
        priority.Place("part_dec_4", 4, 3); // Diagonal to the connector.
        result = priority.Calculate();
        Assert(result.LinkedPairs == 1, "Only the two short-edge endpoints are candidates.");
        Near(result.ComputeBonusPercent, 80 + 20 * 0.2, "A higher-compute neighbor at the long side cannot steal the endpoint pair");

        var overlap = new Rig(); overlap.Install();
        overlap.Place("part_link", 2, 2, new byte[] { 1, 1 }, 2);
        overlap.Place("part_link", 2, 4, new byte[] { 1, 1 }, 2);
        overlap.Place("part_enc_0", 1, 2, new byte[] { 1, 1, 1 }, 1);
        overlap.Place("part_dec_0", 4, 2, new byte[] { 1, 1, 1 }, 1);
        Near(overlap.Calculate().ComputeBonusPercent, 24, "Shared pair must receive link bonus only once");

        var reroute = new Rig(); reroute.Install();
        foreach (int y in new[] { 1, 3, 5 }) reroute.Place("part_link", 2, y, new byte[] { 1, 1 }, 2);
        reroute.Place("part_enc_4", 1, 1, new byte[] { 1, 0, 1 }, 1); reroute.Place("part_enc_0", 1, 5);
        reroute.Place("part_dec_4", 4, 1, new byte[] { 1, 0, 0, 0, 1 }, 1); reroute.Place("part_dec_0", 4, 3);
        result = reroute.Calculate();
        Assert(result.LinkedPairs == 2, "Global matching must reassign an early high-value pair to allow both pairs.");
        Near(result.ComputeBonusPercent, 96 * 1.5, "Overlapping connectors maximize total benefit before U-shape");
        reroute.Grid.Items.Reverse();
        Near(reroute.Calculate().ComputeBonusPercent, result.ComputeBonusPercent, "Insertion order must not change matching");

        for (int rotation = 0; rotation < 4; rotation++)
            foreach (bool flipped in new[] { false, true })
            {
                var rotated = new Rig(); rotated.Install();
                var connector = rotated.Place("part_link", 3, 3, new byte[] { 1, 1 }, 2, rotation);
                connector.SetShape(new GridShapeBuilder(connector.shape).SetTransform(3, 3, flipped, rotation));
                var shape = connector.modifiedShape;
                bool horizontal = shape.globalHeight == 1;
                rotated.Place("part_enc_1", horizontal ? 2 : 3, horizontal ? 3 : 2);
                rotated.Place("part_dec_3", horizontal ? 5 : 3, horizontal ? 3 : 5);
                result = rotated.Calculate();
                Assert(result.LinkedPairs == 1, "Rotations and flips retain the two short-edge endpoints.");
                Near(result.ComputeBonusPercent, 40 * 1.2, "Endpoint pairing ignores component shape IDs");
            }
        var sides = new Rig(); sides.Install();
        sides.Place("part_link", 2, 2, new byte[] { 1, 1 }, 2);
        sides.Place("part_enc_0", 2, 1); sides.Place("part_dec_0", 3, 3);
        Assert(sides.Calculate().LinkedPairs == 0, "Two long-side neighbors do not pair.");
        var reversed = new Rig(); reversed.Install();
        reversed.Place("part_link", 2, 2, new byte[] { 1, 1 }, 2);
        reversed.Place("part_dec_0", 1, 2); reversed.Place("part_enc_0", 4, 2);
        Assert(reversed.Calculate().LinkedPairs == 1, "Either endpoint may hold the encoder.");
        foreach (var mask in new[] { new byte[] { 1 }, new byte[] { 1, 0 }, new byte[] { 1, 1, 1 }, new byte[] { 1, 0, 0, 1 } })
        {
            var invalid = new Rig(); invalid.Install();
            invalid.Place("part_link", 0, 0, mask, mask.Length == 4 ? 2 : mask.Length);
            invalid.Place("part_enc_0", 4, 2); invalid.Place("part_dec_0", 6, 2);
            Assert(!invalid.TryCalculate(out _, out string reason) && reason == "compiler_invalid_parts", "Reject non-two-cell or non-solid connector shapes.");
        }
    }

    private static void CheckFeedback()
    {
        var legacy = new GameItemElement { Id = "age_of_agi.part_feedback" };
        legacy.SetShape(new GridShapeBuilder().SetData(new byte[] { 1, 1, 1 }, 3).SetTransform(2, 4, true, 1));
        AiPartsHelper.InitAttributes(legacy, AiPartCatalog.Load().Get("age_of_agi.part_feedback"));
        Assert(legacy.modifiedShape.globalWidth == 1 && legacy.modifiedShape.globalHeight == 4 &&
            legacy.modifiedShape.minX == 2 && legacy.modifiedShape.minY == 4 && legacy.modifiedShape.flipped,
            "Restore legacy feedback to four cells while preserving saved transform.");
        Assert(legacy.background.rectTransform.sizeDelta.x == 64 && legacy.fakeBackground.rectTransform.sizeDelta.y == 16,
            "Both normal and drag sprites fit the four-cell footprint.");
        var rig = new Rig(); rig.Install();
        rig.Place("part_feedback", 2, 2, new byte[] { 1, 1, 1, 1 }, 4);
        rig.Place("part_enc_0", 1, 2, new byte[] { 1, 0, 1, 1 }, 2);
        rig.Place("part_dec_0", 5, 2, new byte[] { 0, 1, 1, 1 }, 2);
        rig.Place("part_link", 3, 3, new byte[] { 1, 1 }, 2);
        var result = rig.Calculate();
        Assert(result.LinkedPairs == 1 && result.FeedbackPairs == 1, "Different functions may act on the same pair.");
        Near(result.ComputeBonusPercent, 24, "Link and feedback coexist");
        Near(result.TrainingWork, 64, "Reduce only paired training by twenty percent");
        Assert(result.TrainingData == 20, "Feedback lowers work while retaining the original twenty-data budget.");
        rig.Place("part_feedback", 2, 4, new byte[] { 1, 1, 1, 1 }, 4);
        // Extend both endpoints to touch both feedbacks.
        rig.Grid.Items.First(item => item.identifier == "age_of_agi.part_enc_0").SetShape(new GridShapeBuilder().SetData(new byte[] { 1, 0, 1, 1, 1, 0 }, 2).SetPosition(1, 2));
        rig.Grid.Items.First(item => item.identifier == "age_of_agi.part_dec_0").SetShape(new GridShapeBuilder().SetData(new byte[] { 0, 1, 1, 1, 0, 1 }, 2).SetPosition(5, 2));
        Near(rig.Calculate().TrainingWork, 64, "Shared components receive feedback only once");

        for (int rotation = 0; rotation < 4; rotation++)
        {
            var rotated = new Rig(); rotated.Install();
            GameItem function = rotated.Place("part_feedback", 3, 3, new byte[] { 1, 1, 1, 1 }, 4, rotation);
            bool horizontal = function.modifiedShape.globalWidth == 4;
            rotated.Place("part_enc_1", horizontal ? 2 : 3, horizontal ? 3 : 2);
            rotated.Place("part_dec_3", horizontal ? 7 : 3, horizontal ? 3 : 7);
            var actual = rotated.Calculate();
            Assert(actual.FeedbackPairs == 1, "Feedback endpoints must rotate with the shape.");
            Near(actual.TrainingWork, (60 + 100) * 0.8, "Feedback ignores numeric shape suffixes");
            Assert(actual.TrainingData == 40, "Feedback never discounts the independent component data sum.");
        }
        var sides = new Rig(); sides.Install();
        sides.Place("part_feedback", 2, 2, new byte[] { 1, 1, 1, 1 }, 4);
        sides.Place("part_enc_0", 2, 1); sides.Place("part_dec_0", 5, 3);
        Assert(sides.Calculate().FeedbackPairs == 0, "Long edges do not trigger feedback.");
    }

    private static void CheckTaskHeads()
    {
        foreach (var head in new[] { ("awareness", "sensory_analysis"), ("aggression", "intelligence_intrusion"), ("biology", "biological_analysis") })
        {
            var rig = Filled();
            Put(rig.Grid, DirectoryMaster.Item("age_of_agi.part_head_" + head.Item1));
            rig.Click();
            Assert(AiModelState.GetTaskType(rig.Output.childItems.Single()) == head.Item2 && rig.Grid.childItems.Count == 0, "Task head is consumed and its type is persisted.");
        }
        var conflict = new Rig(); conflict.Install();
        conflict.Place("part_enc_0", 0, 0); conflict.Place("part_dec_0", 1, 0);
        conflict.Place("part_head_aggression", 2, 0); conflict.Place("part_head_biology", 3, 0);
        Assert(!conflict.TryCalculate(out _, out string reason) && reason == "compiler_conflicting_heads", "Reject conflicting task heads explicitly.");
        ExpectQuietClick(conflict);
        var same = new Rig(); same.Install();
        same.Place("part_enc_0", 0, 0); same.Place("part_dec_0", 1, 0);
        same.Place("part_head_biology", 2, 0); same.Place("part_head_biology", 3, 0);
        Assert(!same.TryCalculate(out _, out reason) && reason == "compiler_conflicting_heads", "Two heads of the same type are also rejected.");
    }

    private static void CheckFailuresAndReentrancy()
    {
        ExpectQuietClick(Filled(energy: 9));
        var rejected = Filled(); rejected.Output.RejectTransfer = true; ExpectQuietClick(rejected);
        var full = Filled();
        var blocker = new GameItem(); blocker.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1, 24).ToArray(), 6));
        full.Output.UncheckedAccept(blocker); ExpectQuietClick(full);
        var unowned = Filled(); unowned.Grid.Items[0].Owned = false; ExpectQuietClick(unowned);
        var missing = Filled(); missing.Base.RawRemove(missing.Base.childItem); ExpectQuietClick(missing);
        var foreign = Filled(); foreign.Grid.UncheckedAccept(new GameItem { Id = "foreign.part" }); ExpectQuietClick(foreign);
        var invalid = Filled(); invalid.Grid.Items[0].ModifyTag(AiPartState.TrainingTag, state => state.SetFloat(float.NaN)); ExpectQuietClick(invalid);
        var invalidData = Filled(); invalidData.Grid.Items[0].ModifyTag(AiPartState.TrainingDataTag, state => state.SetInt(0)); ExpectQuietClick(invalidData);
        var overflowData = Filled(); overflowData.Grid.Items[0].ModifyTag(AiPartState.TrainingDataTag, state => state.SetInt(int.MaxValue)); ExpectQuietClick(overflowData);
        var stacked = Filled(); stacked.Grid.Items[0].SetAmount(2); ExpectQuietClick(stacked);

        var rig = Filled(); rig.Output.AfterAccept = _ => rig.Click(); rig.Click();
        Assert(rig.Output.childItems.Count == 1 && Energy(rig.Battery.childItem) == 10, "Reentrant compile produces once.");
        var changed = Filled();
        changed.Output.AfterAccept = _ => changed.Grid.Items[0].ModifyTag(AiPartState.ComputeBonusTag, state => state.SetFloat(99));
        ExpectQuietClick(changed);
        changed.Output.AfterAccept = null;
        changed.Click();
        Near(AiModelState.GetPredictedComputeBonus(changed.Output.childItems.Single()), 109, "Retry recomputes updated attributes");
        var changedData = Filled();
        changedData.Output.AfterAccept = _ => changedData.Grid.Items[0].ModifyTag(AiPartState.TrainingDataTag, state => state.SetInt(99));
        ExpectQuietClick(changedData);
        changedData.Output.AfterAccept = null;
        changedData.Click();
        Assert(AiModelState.GetTrainingData(changedData.Output.childItems.Single()) == 109,
            "Data mutation during output placement aborts payment and retry freezes the new budget.");
        var moved = Filled();
        moved.Output.AfterAccept = _ => moved.Grid.Items[0].SetShape(new GridShapeBuilder(moved.Grid.Items[0].modifiedShape).SetPosition(6, 10));
        ExpectQuietClick(moved);
        var failing = Filled(); failing.Output.AfterAccept = _ => throw new InvalidOperationException("transfer callback");
        try { failing.Click(); throw new Exception("Expected transfer callback exception."); }
        catch (InvalidOperationException) { }
        Assert(failing.Output.childItems.Count == 0 && failing.Grid.childItems.Count == 2 && Energy(failing.Battery.childItem) == 20, "Transfer exceptions conserve inputs and clear product.");
        failing.Output.AfterAccept = null; failing.Click();
        Assert(failing.Output.childItems.Count == 1, "Recover after callback failure.");
    }

    private static void CheckRestoredState()
    {
        var previous = Filled();
        var rebuilt = new Rig();
        for (int index = 0; index < 4; index++)
            foreach (GameItem item in ((GameInventory)previous.Row.GetStorage(index)).childItems)
                ((GameInventory)rebuilt.Row.GetStorage(index)).UncheckedAccept(item);
        rebuilt.Machine.onLoaded?.Invoke(rebuilt.Machine);
        Assert(rebuilt.Grid.Width == 8 && rebuilt.Grid.Height == 12 && GeneralHelper.IsItemLocked(rebuilt.Base.childItem), "Restore base layout and lock using rebuilt slot nodes.");
        rebuilt.Click();
        Assert(rebuilt.Output.childItems.Count == 1 && previous.Output.childItems.Count == 0, "Rebuilt callback targets restored inventories.");
        GameItem model = rebuilt.Output.childItems.Single();
        model.ModifyTag(AiModelState.TrainingDataTag, state => state.SetInt(37));
        model.ModifyTag(AiModelState.PredictedComputeTag, state => state.SetFloat(123.5f));
        model.onLoaded?.Invoke(model);
        Near(AiModelState.GetPredictedComputeBonus(model), 123.5, "Loading must not recompute frozen AI attributes");
        Assert(AiModelState.GetTrainingData(model) == 37, "Loading preserves independent model data.");
        rebuilt.Machine.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(8));
        rebuilt.Machine.onLoaded?.Invoke(rebuilt.Machine);
        Assert(MachineParameters.GetPowerPerCycle(rebuilt.Machine) == 8, "Keep existing saved machine cost.");
        rebuilt.Machine.DisableTag(MachineParameters.PowerPerCycleTag);
        rebuilt.Machine.onLoaded?.Invoke(rebuilt.Machine);
        Assert(MachineParameters.GetPowerPerCycle(rebuilt.Machine) == 10, "Backfill missing cost from current machine configuration.");
        var customCost = Filled(energy: 8);
        customCost.Machine.ModifyTag(MachineParameters.PowerPerCycleTag, state => state.SetInt(8));
        customCost.Click();
        Assert(customCost.Output.childItems.Count == 1 && Energy(customCost.Battery.childItem) == 0, "Execution reads cost from machine instance.");
    }

    private static void CheckTooltips()
    {
        var mod = new AgeOfAgiMod(); mod.Init(new ModManifest()); mod.OnEnable(); mod.OnEnable();
        try
        {
            var rig = Filled();
            ModHelper.Locale = "zh"; rig.Click();
            GameItem model = rig.Output.childItems.Single();
            var pending = Filled();
            string preview = ((MachineryStack)pending.Row.GetPanel("control").content).Elements.OfType<MachineryText>().Single().Text;
            Assert(preview.Contains("训练数据需求：20"), "Compiler preview includes the separate data budget.");
            Assert(model.name == "未训练的AI模型", "All untrained tasks share one model name.");
            ExpectTooltip(model, "内存要求：2 GB", "预测计算性能：+20%", "预测初始耐久：10", "训练量：80", "训练数据需求：20", "", "计算", "");
            ExpectTooltip(DirectoryMaster.Item("age_of_agi.part_enc_0"), "基础预测计算性能：+10%", "内存要求：1 GB", "训练量：40", "训练数据需求：10");
            ModHelper.Locale = "en";
            ExpectTooltip(model, "Memory required: 2 GB", "Predicted compute: +20%", "Predicted initial durability: 10", "Training work: 80", "Training data required: 20", "", "Computing", "");
            ModHelper.Locale = "zh";
            foreach (var task in new[]
            {
                (Id: "intelligence_intrusion", Name: "恶意", Color: RenderHandler.ColorPalette.LightRed),
                (Id: "sensory_analysis", Name: "感知", Color: RenderHandler.ColorPalette.LightBlue),
                (Id: "biological_analysis", Name: "生化", Color: RenderHandler.ColorPalette.LightGreen)
            })
            {
                var trained = DirectoryMaster.Item(AiModelState.TrainedId);
                AiModelState.ApplyTrained(trained, task.Id, 2, 20, 9,
                    AiModelTraits.Batch | AiModelTraits.UShape | AiModelTraits.HighRank, 3);
                var tooltip = new RichTextBuilder(); ModHook.RaiseCreateTooltip(tooltip, trained);
                // Native GetTooltipBasic adds the item's description immediately after the late hook.
                tooltip.AddLine(trained.shortDescription, color: RenderHandler.ColorPalette.White);
                Assert(trained.name == "AI模型" && tooltip.Lines.Select(line => line.Text).SequenceEqual(new[]
                {
                    "内存要求：2 GB", "计算性能：+20%", "耐久：9/9", "", task.Name,
                    "批处理：3 次/晚", "U型架构：算力加成×1.5", "高秩架构：耐久×1.5；训练量×1.2", "", trained.shortDescription
                }), "Model tooltip separates attributes, task/trait lines and native description in the requested order.");
                Assert(tooltip.Lines[4].Color == task.Color && tooltip.Lines[9].Color == RenderHandler.ColorPalette.White,
                    "Task heads use their individual colors without coloring the native description.");
                trained.name = "old display name"; trained.onLoaded?.Invoke(trained);
                Assert(trained.name == "AI模型" && AiModelState.GetCurrentDurability(trained) == 9 &&
                    AiModelState.GetComputeBonus(trained) == 20, "Name refresh and presentation preserve actual model attributes.");
            }
            mod.OnDisable(); ExpectTooltip(model);
        }
        finally { mod.OnDisable(); ModHelper.Locale = "en"; }
    }

    private static Rig Filled(int energy = 20)
    {
        var rig = new Rig(); rig.Install(energy);
        Put(rig.Grid, DirectoryMaster.Item("age_of_agi.part_enc_0"));
        Put(rig.Grid, DirectoryMaster.Item("age_of_agi.part_dec_0"));
        return rig;
    }

    private sealed class Rig
    {
        internal readonly GameItem Machine = DirectoryMaster.Item(CompilerCatalog.CompilerId);
        internal MachineryPanelRow Row => Machine.contentWindow.childElement;
        internal GameSlotInventory Battery => (GameSlotInventory)Row.GetStorage(0);
        internal GameSlotInventory Base => (GameSlotInventory)Row.GetStorage(1);
        internal GameGridInventory Grid => (GameGridInventory)Row.GetStorage(2);
        internal GameGridInventory Output => (GameGridInventory)Row.GetStorage(3);
        internal MachineryPanel GridPanel => Row.GetPanel("grid");
        internal void Click() => ((MachineryStack)Row.GetPanel("control").content).Buttons.Single().Click();
        internal void Install(int energy = 20)
        {
            Put(Battery, MakeBattery(energy));
            Put(Base, DirectoryMaster.Item("age_of_agi.base_jepa"));
        }
        // Synthetic one-cell basis masks isolate adjacency/matching rules. Separate
        // execution fixtures above use the real PNG-derived component shapes.
        internal GameItem Place(string id, int x, int y, byte[] cells = null, int width = 1, int rotation = 0)
        {
            GameItem item = DirectoryMaster.Item("age_of_agi." + id);
            item.SetShape(new GridShapeBuilder().SetData(cells ?? new byte[] { 1 }, width).SetTransform(x, y, false, rotation));
            Grid.UncheckedAccept(item);
            return item;
        }
        internal bool TryCalculate(out CompilerAssemblyResult result, out string reason) => CompilerAssemblyCalculator.TryCalculate(
            Grid, BaseModelState.GetGridWidth(Base.childItem), BaseModelState.GetGridHeight(Base.childItem),
            BaseModelState.GetMemoryMultiplier(Base.childItem), AiPartCatalog.Load(), CompilerCatalog.Load().Recipe, out result, out reason);
        internal CompilerAssemblyResult Calculate()
        {
            Assert(TryCalculate(out var result, out string reason), "Invalid fixture: " + reason + " " + string.Join("; ", Grid.childItems.Select(item => item.identifier + " " + item.modifiedShape.globalWidth + "x" + item.modifiedShape.globalHeight + " @" + item.modifiedShape.minX + "," + item.modifiedShape.minY)));
            return result;
        }
    }

    private static void ExpectQuietClick(Rig rig)
    {
        int energy = Energy(rig.Battery.childItem), inputs = rig.Grid.childItems.Count, outputs = rig.Output.childItems.Count;
        int created = ItemDirectory.CreatedInstances.Count;
        rig.Click();
        Assert(Energy(rig.Battery.childItem) == energy && rig.Grid.childItems.Count == inputs && rig.Output.childItems.Count == outputs, "Failed compile must conserve parts and power.");
        Assert(ItemDirectory.CreatedInstances.Skip(created).All(item => item.Destroyed || item.parentInventory != null), "Failed compile must destroy its temporary output.");
    }
    private static GameItem MakeBattery(int energy)
    {
        var item = new GameItem { Id = "energy_credit" };
        item.EnableTag("power_source_item");
        item.ModifyTag("power_source_item_energy", state => state.Enable().SetInt(energy));
        return item;
    }
    private static int Energy(GameItem item) => item?.GetTagReadonly("power_source_item_energy").GetInt() ?? 0;
    private static void Put(GameInventory slot, GameItem item) => Assert(GraphUtils.TryAcceptAll(slot, item) == 0, "Fixture rejected by inventory.");
    private static void Near(double actual, double expected, string message) => Assert(Math.Abs(actual - expected) < 0.0001, message + ": " + actual + " != " + expected);
    private static void ExpectTooltip(GameItem item, params string[] expected)
    {
        var builder = new RichTextBuilder(); ModHook.RaiseCreateTooltip(builder, item);
        Assert(builder.Lines.Select(line => line.Text).SequenceEqual(expected), "Tooltip differs: " + string.Join(" | ", builder.Lines.Select(line => line.Text)));
    }
    private static void ExpectArgument(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid initialization accepted.");
    }
    private static void ExpectFailure(Action action, string path)
    {
        try { action(); }
        catch (InvalidOperationException exception) { Assert(exception.Message.Contains(path), "Missing error path: " + exception.Message); return; }
        throw new Exception("Invalid configuration accepted: " + path);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
