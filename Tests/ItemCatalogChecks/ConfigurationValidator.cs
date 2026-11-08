using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.BaseModel;
using AgeOfAgi.Items.Logic.Memory;
using AgeOfAgi.Items.Logic.Cpu;
using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Logic.Compiler;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Npcs;

// Validate the supplied directory. Default tests supply only independent fixtures;
// inspecting the user's editable directory requires the explicit --config-only command.
internal static class ConfigurationValidator
{
    internal static void Run()
    {
        CheckCurrentConfiguration();
        CheckTuningIndependence();
        Console.WriteLine("PASS: supplied configuration schemas, references, localization, CSV-to-instance values and independent tuning examples.");
    }

    private static void CheckCurrentConfiguration()
    {
        IReadOnlyList<ItemDefinition> items = ItemCatalog.Load();
        LoadedMod loaded = Program.PrepareLoadedMod(items);
        var registration = new ItemRegistration(loaded.Manifest);
        var directory = new ModItemDirectory();
        try
        {
            CheckTranslations(items);
            registration.Register(directory, items); // Runs all definition mapping/schema validations.
            CheckFactoryValues(items, directory);
            CheckAiValues();
            CheckSuppliers(items);
        }
        finally
        {
            registration.Unregister();
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
    }

    private static void CheckTranslations(IReadOnlyList<ItemDefinition> items)
    {
        foreach (string locale in new[] { "en", "zh" })
        {
            var entries = Program.ReadLocale(locale);
            ModHelper.Translations[locale] = entries;
            ModHelper.Locale = locale;
            foreach (var item in items)
            {
                Require(entries.TryGetValue(item.NameKey, out string name) && !string.IsNullOrWhiteSpace(name),
                    "Missing item name: " + item.ItemId + " / " + locale);
                foreach (string key in new[] { item.DescriptionKey, item.CommentKey }.Where(key => key.Length > 0))
                    Require(entries.ContainsKey(key), "Missing localization: " + key + " / " + locale);
                foreach (string type in item.Types.Where(type => !TypeHelper.ALL_TYPES_LIST.Contains(type.ToUpperInvariant())))
                {
                    Require(entries.TryGetValue(type, out string text) && !string.IsNullOrWhiteSpace(text),
                        "Missing custom category translation: " + type + " / " + locale);
                    Require(ItemTypeHelper.TryGetDisplayName(type, out string displayName) && displayName == text,
                        "Custom category must resolve the current translation: " + type + " / " + locale);
                }
            }
        }
    }

    private static void CheckFactoryValues(IReadOnlyList<ItemDefinition> items, ModItemDirectory directory)
    {
        var gpus = GpuCatalog.Load();
        var cpus = CpuCatalog.Load();
        var memory = MemoryCatalog.Load();
        var machines = MachineCatalog.Load();
        var bases = BaseModelCatalog.Load();
        var parts = AiPartCatalog.Load();
        var data = DataContentCatalog.Load();
        foreach (var definition in items)
        {
            GameItem item = directory.Factories[definition.ItemId]();
            Require(item.unitValue == definition.BaseValue && item.SetValueCalls == 1,
                "Initialize the configured common price once: " + definition.ItemId);
            switch (definition.Kind)
            {
                case "gpu":
                    var assembly = GpuInspectionState.Read(item);
                    var gpu = gpus.GetByCore(assembly.Parts[1].Sprite);
                    var memoryParts = assembly.Parts.Where(part => part.Slot.StartsWith("memory_")).ToArray();
                    double memoryValue = memoryParts.Sum(part => part.Sprite.Length == 0 ? 0 :
                        part.Condition == "normal" ? 1 : part.Condition == "lowfrequency" ? 0.6 : 0);
                    double contacts = assembly.Parts.Where(part => part.Sprite.Length > 0 && part.Condition == "normal")
                        .Sum(part => part.Slot == "goldfinger_up" ? 0.3 : part.Slot == "goldfinger_down" ? 0.7 : 0);
                    double expectedCompute = memoryParts.Length == 0 ? 0 : gpu.ComputePower * memoryValue / memoryParts.Length * contacts;
                    var modules = assembly.Parts.Where(part => part.Slot.StartsWith("power_module_")).ToArray();
                    int working = modules.Count(part => part.Sprite.Length > 0 && part.Condition == "normal");
                    bool powered = assembly.Parts.Any(part => part.Slot == "power_interface" && part.Sprite.Length > 0 && part.Condition == "normal");
                    int expectedPower = !powered || working == 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Ceiling(gpu.PowerPerCycle * (double)modules.Length / working));
                    Require(assembly.CoreComputePower == gpu.ComputePower && assembly.CorePowerPerCycle == gpu.PowerPerCycle &&
                        Math.Abs(GpuRuntime.GetComputePower(item) - expectedCompute) < Math.Max(0.00001, expectedCompute * 0.000001) &&
                        GpuRuntime.GetPowerPerCycle(item) == expectedPower && assembly.PowerSupplyWorking == (powered && working > 0),
                        "GPU attributes must follow the configured core and frozen components: " + definition.ItemId);
                    break;
                case "cpu":
                    var cpu = cpus.Get(definition.ItemId);
                    Require(CpuState.GetPowerBoostRate(item) == cpu.PowerBoostRate && CpuState.GetPowerPerCycle(item) == cpu.PowerPerCycle,
                        "CPU attributes differ from CSV: " + definition.ItemId);
                    break;
                case "memory":
                    Require(MemoryState.GetMemory(item) == memory.Get(definition.ItemId).Memory,
                        "Memory differs from CSV: " + definition.ItemId);
                    break;
                case "machine":
                    // Servers replace this display tag with current installed-hardware consumption.
                    Require(MachineParameters.IsMachine(item) && (definition.ItemId == ServerCatalog.ServerId ||
                        MachineParameters.GetPowerPerCycle(item) == machines.Get(definition.ItemId).PowerPerCycle),
                        "Machine cost differs from CSV: " + definition.ItemId);
                    break;
                case "base_model":
                    var basis = bases.Get(definition.ItemId);
                    Require(BaseModelState.GetGridWidth(item) == basis.GridWidth && BaseModelState.GetGridHeight(item) == basis.GridHeight,
                        "Base grid differs from CSV: " + definition.ItemId);
                    break;
                case "ai_parts":
                    var part = parts.Get(definition.ItemId);
                    if (part.IsBasis)
                        Require(AiPartState.GetComputeBonus(item) == part.ComputeBonusPercent && AiPartState.GetMemoryGb(item) == part.MemoryGb &&
                            AiPartState.GetTrainingWork(item) == part.TrainingWork && AiPartState.GetTrainingData(item) == part.TrainingData,
                            "Component attributes differ from CSV: " + definition.ItemId);
                    break;
                case "data_carrier":
                case "data_container":
                    var content = data.Get(definition.ItemId);
                    Require(DataContentState.Capacity(item) == content.Capacity && DataContentState.Amount(item) == content.InitialData,
                        "Data content differs from CSV: " + definition.ItemId);
                    DataContentState.SetAmount(item, content.Capacity);
                    Require(item.GetBaseValue() == definition.BaseValue + (long)content.Capacity * data.GetUnitValue(definition.ItemId),
                        "Content valuation must use configured capacity and unit value: " + definition.ItemId);
                    break;
            }
        }
    }

    private static void CheckAiValues()
    {
        var table = ReadTable(AiValueCatalog.CatalogPath);
        foreach (var row in table.Rows)
        {
            // Chosen sample attributes are test inputs, not expected defaults of any model.
            const float compute = 17.5f;
            const int durability = 3;
            double rate = Number(row["value_per_use"]), taskRate = Number(row["task_multiplier"]);
            double discount = Number(row["untrained_multiplier"]);
            var trained = DirectoryMaster.Item(AiModelState.TrainedId);
            AiModelState.ApplyTrained(trained, row["task"], 1, compute, durability);
            var untrained = DirectoryMaster.Item(AiModelState.UntrainedId);
            AiModelState.Apply(untrained, new AiModelAttributes(
                row["task"], 1, 1, compute, durability, 1));
            long Expected(double uses, double multiplier) => (long)Math.Max(1, Math.Min(int.MaxValue,
                Math.Round(rate * (1 + compute / 100d) * uses * taskRate * multiplier, MidpointRounding.AwayFromZero)));
            Require(trained.GetBaseValue() == Expected(durability, 1) && untrained.GetBaseValue() == Expected(durability, discount),
                "Model valuation must honor current CSV coefficients: " + row["task"]);
            AiModelState.SpendProductionUse(trained);
            Require(trained.GetBaseValue() == Expected(durability - 1, 1), "Wear must revalue using current coefficients.");
        }
    }

    private static void CheckSuppliers(IReadOnlyList<ItemDefinition> items)
    {
        foreach (string path in new[] { NightShiftCatalog.Path, ElectronicsTrader.CatalogPath })
        {
            var catalog = NightShiftCatalog.Load(items, path);
            Require(catalog.GiftItems().Count == catalog.Gifts.Sum(row => row.Count), "Gift quantities must come from CSV: " + path);
            float eligibleSpeed = catalog.Stock.Max(row => row.MinimumProductionSpeed);
            var first = catalog.Roll(19, eligibleSpeed);
            Require(first.Select(offer => (offer.ItemId, offer.DataAmount)).SequenceEqual(
                catalog.Roll(19, eligibleSpeed).Select(offer => (offer.ItemId, offer.DataAmount))), "Seeded stock must be repeatable.");
            foreach (var offer in first)
            {
                Require(catalog.Stock.Any(row => row.Chance > 0 && row.MinimumProductionSpeed <= eligibleSpeed &&
                    row.ItemIds.Contains(offer.ItemId) && row.DataAmount == offer.DataAmount && row.MemoryGb == offer.MemoryGb &&
                    row.ComputeBonus == offer.ComputeBonus && row.Durability == offer.Durability &&
                    row.TrainingWork == offer.TrainingWork && row.TrainingData == offer.TrainingData),
                    "Each offer must come from an eligible row and retain its configured attributes.");
            }
        }
        var rules = ElectronicsVisitRules.Load();
        var ruleTable = ReadTable(ElectronicsVisitRules.CatalogPath);
        foreach (var row in ruleTable.Rows)
        {
            var rule = rules.Get(row["seller"]);
            Require(rule.MinimumCount == Number(row["min_count"]) && rule.MaximumCount == Number(row["max_count"]),
                "Visit rules must use the current configuration: " + row["seller"]);
        }
        var suppliers = ElectronicsDropCatalog.Load(items);
        var market = ElectronicsMarketCatalog.Create(items, rules, suppliers);
        EncryptedItemCatalog.Load().ValidateMappings(items);
        var salvage = ElectronicsSalvageCatalog.Create(rules, suppliers);
        foreach (int seed in Enumerable.Range(0, 20))
        {
            CheckCount(market.Roll(seed, false).Count, rules.Get("dealer"));
            CheckCount(market.Roll(seed, true).Count, rules.Get("advanced_dealer"));
            foreach (ElectronicsSalvageKind kind in Enum.GetValues(typeof(ElectronicsSalvageKind)))
                CheckCount(salvage.Roll(seed, kind, 100).Count, salvage.GetRule(kind));
        }
    }

    private static void CheckCount(int count, ElectronicsVisitRule rule) =>
        Require(count >= rule.MinimumCount && count <= rule.MaximumCount, "Attempt count must respect its configured bounds.");

    private static CsvFixture ReadTable(string path) => CsvFixture.Parse(ModHelper.ReadModFileText(ModConstants.ModId, path));

    private static void CheckTuningIndependence()
    {
        var saved = new Dictionary<string, string>(ModHelper.FileOverrides);
        try
        {
            // Independent legal sample values, not a new set of expected production defaults.
            Tune("Data/items.csv", (row, index) => Set(row, "base_value", 11 + index * 13));
            Tune(GpuCatalog.CatalogPath, (row, index) =>
            { Set(row, "computePower", 17 + index * 9); Set(row, "powerPerCycle", index % 2 == 0 ? 0 : 7); });
            Tune(CpuCatalog.CatalogPath, (row, index) =>
            { Set(row, "powerBoostRate", 3.25 + index); Set(row, "powerPerCycle", index % 2 == 0 ? 0 : 5); });
            Tune(MemoryCatalog.CatalogPath, (row, index) => Set(row, "memory", 24 + index * 8));
            Tune(MachineCatalog.CatalogPath, (row, index) => Set(row, "powerPerCycle", 2 + index));
            Tune(BaseModelCatalog.CatalogPath, (row, index) =>
            { Set(row, "gridWidth", 2 + index); Set(row, "gridHeight", 3 + index); });
            Tune(AiPartCatalog.CatalogPath, (row, index) =>
            {
                if (row["role"] != "encoder" && row["role"] != "decoder") return;
                Set(row, "computeBonusPercent", 37.5 + index); Set(row, "memoryGb", 3);
                Set(row, "trainingWork", 17.25 + index); Set(row, "trainingData", 5 + index);
            });
            Tune(CompilerCatalog.CatalogPath, (row, _) =>
            {
                Set(row, "initialDurability", 8.5); Set(row, "duplicateDurabilityRetention", 0.63);
                Set(row, "linkComputeMultiplier", 1.07); Set(row, "feedbackTrainingRetention", 0.42);
                Set(row, "regularizationMultiplier", 1.3);
            });
            Tune(ServerCatalog.CatalogPath, (row, _) =>
            {
                Set(row, "coolingMultiplier", 0.72); Set(row, "cpuUpgradeUnitsStep1", 7); Set(row, "cpuUpgradeUnitsStep2", 11); Set(row, "gpuUpgradeUnitsPerStep", 13);
                foreach (string field in new[] { "intelligenceWork", "bioBaseWork", "bioComplexityWork" })
                    Set(row, field, 71.5);
                Set(row, "dataUnitsPer8Gb", 7); Set(row, "dataWorkPerUnit", 2.75);
            });
            Tune(EncryptedItemCatalog.CatalogPath, (row, index) => Set(row, "work", 71.5 + index * 71.75));
            Tune(DataContentCatalog.CatalogPath, (row, index) =>
            {
                int capacity = 31 + index * 7;
                Set(row, "capacity", capacity); Set(row, "initial_data", row["mode"] == "container" ? 0 : capacity);
                Set(row, "data_unit_value", row["mode"] == "container" ? 7 : 2);
            });
            Tune(AiValueCatalog.CatalogPath, (row, index) =>
            {
                Set(row, "value_per_use", 11 + index); Set(row, "task_multiplier", 1.1 + index * 0.1);
                Set(row, "untrained_multiplier", 0.37);
            });
            foreach (string path in new[] { NightShiftCatalog.Path, ElectronicsTrader.CatalogPath })
                Tune(path, (row, index) =>
                {
                    Set(row, "count", 1 + index % 3);
                    if (row["section"] == "stock")
                    { Set(row, "chance", index % 2 == 0 ? 0 : 100); Set(row, "minimumProductionSpeed", 17 + index); }
                    if (row["dataAmount"] != "-1") Set(row, "dataAmount", 0);
                    if (row["itemIds"] == AiModelState.TrainedId || row["itemIds"] == AiModelState.UntrainedId)
                    {
                        Set(row, "memoryGb", 3); Set(row, "computeBonus", 17.75); Set(row, "durability", 3);
                        if (row["itemIds"] == AiModelState.UntrainedId)
                        { Set(row, "trainingWork", 7.5); Set(row, "trainingData", 5); }
                    }
                });
            Tune(ElectronicsVisitRules.CatalogPath, (row, index) =>
            { Set(row, "min_count", 1 + index); Set(row, "max_count", 2 + index); });
            Tune(ElectronicsDropCatalog.CatalogPath, (row, index) =>
            {
                Set(row, "probability", row["slot"] == "medium" ? 100 : 0);
                Set(row, "min_reputation", row["slot"] == "reputation" ? 17 : 0);
            });
            CheckCurrentConfiguration();
        }
        finally
        {
            ModHelper.FileOverrides.Clear();
            foreach (var entry in saved) ModHelper.FileOverrides.Add(entry.Key, entry.Value);
        }

        void Tune(string path, Action<Dictionary<string, string>, int> change)
        {
            var table = ReadTable(path);
            for (int index = 0; index < table.Rows.Count; index++) change(table.Rows[index], index);
            ModHelper.FileOverrides[path] = table.ToString();
        }
    }

    private static void Set(Dictionary<string, string> row, string field, double value) =>
        row[field] = value.ToString("R", CultureInfo.InvariantCulture);

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
