using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.Cpu;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.Server;
using AgeOfAgi.Items.Logic.Compiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ServerChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        AgeOfAgi.Items.Logic.DistributedTerminal.DistributedNetwork.Enable();
        CheckNativeUpgradeTargets();
        CheckConfiguration(items);
        LoadedMod loaded = Program.PrepareLoadedMod(items);
        var registration = new ItemRegistration(loaded.Manifest);
        var directory = new ModItemDirectory();
        registration.Register(directory, items);
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        try
        {
            CheckStructure();
            CheckDropRouting();
            CheckStateSprites();
            CheckUpgradesAndVanillaBranches();
            CheckHardwareAggregation();
            CheckNetworkSwitch();
            CheckTrainingAndInheritance();
            CheckTrainingData();
            CheckIndependentTrainingData();
            CheckTwoNightCompletion();
            CheckPausesAndLocks();
            CheckOutputTransactions();
            CheckSavedTask();
            CheckWorkingNightsAndRestore();
            CheckTrainedTooltips();
            CheckMemoryStatus();
        }
        finally
        {
            registration.Unregister();
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
    }

    private static void CheckNativeUpgradeTargets()
    {
        string assemblyPath = Program.GameAssemblyPath;
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        var type = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Single(definition => metadata.GetString(definition.Name) == "MaterialDirectory");
        string[] methods = type.GetMethods().Select(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name)).ToArray();
        Assert(methods.Count(name => name == "Electronic") == 1 && methods.Count(name => name == "MetalIngot") == 1,
            "Installed game must provide the exact native material factory methods targeted by Harmony.");
    }

    private static void CheckConfiguration(IReadOnlyList<ItemDefinition> items)
    {
        ServerCatalog.Load().ValidateMappings(items);
        string source = File.ReadAllText(Path.Combine(ModHelper.Root, ServerCatalog.CatalogPath));
        foreach (string malformed in new[] { "", "id", "id,id", "id\nage_of_agi.invalid", source + " {}" })
            Invalid(() => ServerCatalog.Parse(malformed));
        foreach (var change in new[] { ("coolingMultiplier", "0"), ("coolingMultiplier", "1.1"),
            ("cpuUpgradeUnitsStep1", "0"), ("cpuUpgradeUnitsStep2", "-1"), ("cpuUpgradeUnitsStep1", "2147483647"),
            ("gpuUpgradeUnitsPerStep", "2147483647"),
            ("trainedOutputId", "other_mod.ai"), ("trainedOutputId", "age_of_agi.ai_untrained") })
        {
            var root = CsvFixture.Parse(source); root.Rows[0][change.Item1] = change.Item2;
            Invalid(() => ServerCatalog.Parse(root.ToString()));
        }
        foreach (string field in new[] { "trainedOutputId", "coolingMultiplier", "cpuUpgradeUnitsStep1", "cpuUpgradeUnitsStep2", "gpuUpgradeUnitsPerStep" })
        {
            var root = CsvFixture.Parse(source); root.RemoveColumn(field);
            Invalid(() => ServerCatalog.Parse(root.ToString()));
        }
        Invalid(() => ServerCatalog.Load().ValidateMappings(items.Where(item => item.ItemId != AiModelState.TrainedId).ToArray()));
    }

    private static void CheckStructure()
    {
        var rig = new Rig();
        Assert(rig.Battery.Width == 1 && rig.Battery.Height == 2 && rig.Cpu.Width == 1 && rig.Cpu.Height == 1 &&
            rig.Gpu.Width == 2 && rig.Gpu.Height == 6 && rig.Memory.Width == 4 && rig.Memory.Height == 2,
            "Hardware bay dimensions differ from the approved design.");
        Assert(rig.Network.Width == 2 && rig.Network.Height == 2 &&
            rig.Input.Width == 4 && rig.Input.Height == 4 && rig.Output.Width == 4 && rig.Output.Height == 4,
            "Network and software bay dimensions differ from the design.");
        Assert(rig.Row.GetStorage(8) is GameSlotInventory container && container.Width == 1 && container.Height == 3 &&
            rig.Row.GetStorage(9) == null, "Append only the data container as storage node 8.");
        Assert(rig.Row.GetPanel("training") == null && !rig.Machine.IsTag(ServerState.ProgressVisibleTag), "An empty machine hides training and progress.");
        Assert(rig.Row.DisplayPanels.Select(panel => panel.role).SequenceEqual(new[]
            { "power", "cpu", "gpu", "memory", "network", "model", "input", "data_container", "output", "production" }),
            "Display all bays in one row, with memory beside GPU and the data container beside input.");
        Assert(rig.Row.Panels.Select(panel => panel.role).SequenceEqual(new[]
            { "power", "cpu", "gpu", "network", "memory", "model", "input", "output", "production", "data_container" }),
            "Visual ordering must not change the existing save's storage traversal order.");
        Assert(MachineParameters.IsMachine(rig.Machine) && rig.Machine.GetGameItemType().Contains("MACHINE"), "Server is a regular native machine.");
        Assert(!rig.Machine.IsTag("STANDARD_MACHINE_TAG") && !rig.Machine.IsTag("machinery"), "Independent bays must not invoke fixed native module-bay or daily-power behavior.");
        Assert(!GraphUtils.CanAccept(rig.Network, DirectoryMaster.Item("age_of_agi.cpu0")), "The network bay rejects non-switch items.");
        Assert(GraphUtils.CanAccept(rig.Network, DirectoryMaster.Item(NetworkSwitchUpgrades.SwitchId)), "The network bay accepts the switch.");
        Assert(!GraphUtils.CanAccept(rig.Output, DirectoryMaster.Item(AiModelState.TrainedId)), "Output rejects external insertion.");
        Assert(!GraphUtils.CanAccept(rig.Model, DirectoryMaster.Item("age_of_agi.part_enc_0")), "Model slot rejects code parts.");
        Put(rig.Model, DirectoryMaster.Item(AiModelState.UntrainedId));
        Assert(rig.Row.GetPanel("training") == null, "An untrained model without a first working night does not reveal the screen.");
        rig.Night(1);
        Assert(rig.Output.childItems.Count == 0 && !rig.Machine.IsTag(ServerState.ActiveTaskTag), "Raw debug models cannot train without compiled attributes.");
        rig.Model.Remove(rig.Model.childItem);
        Put(rig.Model, DirectoryMaster.Item(AiModelState.TrainedId));
        Assert(rig.Row.GetPanel("training") == null, "Trained AI is accepted without displaying training.");
    }

    private static void CheckDropRouting()
    {
        foreach (var sample in new[]
        {
            ("age_of_agi.cpu0", ServerState.CpuIndex),
            ("age_of_agi.gpu0", ServerState.GpuIndex),
            ("age_of_agi.mem0", ServerState.MemoryIndex),
            (NetworkSwitchUpgrades.SwitchId, ServerState.NetworkIndex),
            (AiModelState.UntrainedId, ServerState.ModelIndex),
            (AiModelState.TrainedId, ServerState.ModelIndex),
            ("age_of_agi.data_container", ServerState.DataContainerIndex),
            ("age_of_agi.hypo_data_container", ServerState.DataContainerIndex),
            ("energy_credit", ServerState.BatteryIndex)
        })
        {
            var rig = new Rig();
            GameItem item = sample.Item1 == "energy_credit" ? Battery(100) : DirectoryMaster.Item(sample.Item1);
            // Scene grids do not auto-rotate like the native search, so preorient the GPU.
            if (sample.Item2 == ServerState.GpuIndex)
                item.SetShape(new GridShapeBuilder(item.modifiedShape).SetOrientation(1));
            var dedicated = (GameInventory)rig.Row.GetStorage(sample.Item2);
            Assert(GraphUtils.CanAccept(rig.Input, item) && GraphUtils.CanAccept(dedicated, item),
                "Routing regression must exercise two simultaneously valid bays: " + sample.Item1);
            Assert(dedicated.slotPriorityFunc(item, dedicated, null) > rig.Input.slotPriorityFunc(item, rig.Input, null),
                "A dedicated bay must outrank general input: " + sample.Item1);
            Assert(GraphUtils.TryAcceptAll(rig.Machine, item) == 0 && item.parentInventory == dedicated,
                "Dropping onto the server must prefer the item's dedicated bay: " + sample.Item1);
        }

        var fallback = new Rig();
        Put(fallback.Cpu, DirectoryMaster.Item("age_of_agi.cpu0"));
        GameItem spareCpu = DirectoryMaster.Item("age_of_agi.cpu0");
        Assert(GraphUtils.TryAcceptAll(fallback.Machine, spareCpu) == 0 && spareCpu.parentInventory == fallback.Input,
            "A full dedicated bay still permits the native general-input fallback.");
        fallback.Input.Remove(spareCpu);
        GameItem oversized = DirectoryMaster.Item("age_of_agi.cpu2");
        fallback.Cpu.Remove(fallback.Cpu.childItems.Single());
        Assert(GraphUtils.TryAcceptAll(fallback.Machine, oversized) == 0 && oversized.parentInventory == fallback.Input,
            "A CPU that does not fit the unexpanded bay must fall back to general input.");
        fallback.Input.Remove(oversized);
        GameItem disk = DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId);
        Assert(GraphUtils.TryAcceptAll(fallback.Machine, disk) == 0 && disk.parentInventory == fallback.Input,
            "Items without a matching dedicated bay still enter general input.");
        fallback.Input.Remove(disk);
        fallback.Cpu.LockInv();
        fallback.Input.LockInv();
        Assert(!GraphUtils.CanAccept(fallback.Machine, spareCpu), "Priority must not bypass task locks.");
        GameItem container = DirectoryMaster.Item("age_of_agi.data_container");
        Assert(GraphUtils.TryAcceptAll(fallback.Machine, container) == 0 && container.parentInventory == fallback.DataContainer,
            "Data destinations remain replaceable while ordinary input bays are locked.");
    }

    private static void CheckStateSprites()
    {
        var rig = new Rig();
        Assert(rig.Machine.SpriteKey == "serverbox_stage0", "An empty GPU bay uses stage 0.");
        rig.Machine.SetShape(new GridShapeBuilder(rig.Machine.shape).SetTransform(8, 10, true, 3));
        GridShape placement = rig.Machine.shape.Clone();
        // Spare bay capacity alone does not advance the chassis state.
        for (int i = 0; i < 5; i++) UseUpgrade(Material(ServerState.IngotId, 1), rig.Machine);
        Assert(rig.Machine.SpriteKey == "serverbox_stage0", "GPU expansion without installed items remains stage 0.");
        GameItem card = DirectoryMaster.Item("age_of_agi.gpu0");
        // A 4x4 bounding box with 12 occupied cells distinguishes actual area
        // from width*height. Rotation/translation must not change that count.
        card.SetShape(new GridShapeBuilder().SetData(new byte[]
            { 1, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1 }, 4).SetOrientation(1));
        Put(rig.Gpu, card);
        Assert(rig.Machine.SpriteKey == "serverbox_stage1", "Exactly twelve occupied cells use stage 1, even in a larger bounding box.");
        GameItem extra = DirectoryMaster.Item("age_of_agi.gpu0");
        extra.SetShape(new GridShapeBuilder().SetData(new byte[] { 1 }, 1));
        Put(rig.Gpu, extra);
        Assert(rig.Machine.SpriteKey == "serverbox_stage2", "Thirteen occupied cells summed across cards use stage 2.");
        Assert(rig.Machine.shape.minX == 8 && rig.Machine.shape.minY == 10 && rig.Machine.shape.flipped &&
            rig.Machine.shape.orientation == 3 && ItemShapeHelper.SameCells(placement, rig.Machine.shape),
            "Chassis state changes must preserve placement, rotation, reflection and footprint.");
        var restored = new Rig();
        CopyTags(rig.Machine, restored.Machine);
        restored.Gpu.UncheckedAccept(card);
        restored.Gpu.UncheckedAccept(extra);
        restored.Machine.onLoaded(restored.Machine);
        Assert(restored.Machine.SpriteKey == "serverbox_stage2", "Load derives stage from restored GPU contents without inventory events.");
        restored.Gpu.Remove(extra);
        Assert(restored.Machine.SpriteKey == "serverbox_stage1", "Removing the thirteenth cell returns to stage 1.");
        restored.Gpu.Remove(card);
        Assert(restored.Machine.SpriteKey == "serverbox_stage0", "Removing the last GPU returns to stage 0.");
    }

    private static void CheckUpgradesAndVanillaBranches()
    {
        var rig = new Rig();
        var definition = ServerCatalog.Load().Definition;
        GameItem installed = DirectoryMaster.Item("age_of_agi.cpu0"); Put(rig.Cpu, installed);
        GameItem electronics = Material(ServerState.ElectronicsId, definition.CpuUpgradeUnitsTotal + 2);
        for (int i = 0; i < definition.CpuUpgradeUnitsStep1 - 1; i++) UseUpgrade(electronics, rig.Machine);
        Assert(rig.Cpu.Width == 1 && rig.Cpu.Height == 1 && Count(rig.Machine, ServerState.CpuUnitsTag) == definition.CpuUpgradeUnitsStep1 - 1,
            "Partial CPU expansion persists individual units without premature resizing.");
        ExpectExpansion(rig, "server_stat_cpu_expansion", definition.CpuUpgradeUnitsStep1 - 1, definition.CpuUpgradeUnitsStep1);
        var rebuilt = new Rig();
        CopyTags(rig.Machine, rebuilt.Machine); rebuilt.Cpu.UncheckedAccept(installed); rebuilt.Machine.onLoaded(rebuilt.Machine);
        int windowValidations = rebuilt.Machine.contentWindow.ValidateCount;
        UseUpgrade(electronics, rebuilt.Machine);
        Assert(rebuilt.Cpu.Width == 2 && rebuilt.Cpu.Height == 1,
            "Completing stage one after reload adds one horizontal cell only.");
        Assert(rebuilt.Machine.contentWindow.ValidateCount > windowValidations,
            "Expanding a bay revalidates the window so an open UI follows the new width.");
        ExpectExpansion(rebuilt, "server_stat_cpu_expansion", 0, definition.CpuUpgradeUnitsStep2);
        Assert(!GraphUtils.CanAccept(rebuilt.Cpu, DirectoryMaster.Item("age_of_agi.cpu2")), "The first expansion cannot fit a 2x2 CPU.");
        GameItem secondCpu = DirectoryMaster.Item("age_of_agi.cpu1"); Put(rebuilt.Cpu, secondCpu);
        for (int i = 0; i < definition.CpuUpgradeUnitsStep2 - 1; i++) UseUpgrade(electronics, rebuilt.Machine);
        ExpectExpansion(rebuilt, "server_stat_cpu_expansion", definition.CpuUpgradeUnitsStep2 - 1, definition.CpuUpgradeUnitsStep2);
        var stageTwoRestore = new Rig();
        CopyTags(rebuilt.Machine, stageTwoRestore.Machine);
        stageTwoRestore.Cpu.UncheckedAccept(installed);
        stageTwoRestore.Cpu.UncheckedAccept(secondCpu);
        stageTwoRestore.Machine.onLoaded(stageTwoRestore.Machine);
        rebuilt = stageTwoRestore;
        Assert(rebuilt.Cpu.Width == 2 && rebuilt.Cpu.Height == 1, "Restore unfinished stage two without granting its second row.");
        ExpectExpansion(rebuilt, "server_stat_cpu_expansion", definition.CpuUpgradeUnitsStep2 - 1, definition.CpuUpgradeUnitsStep2);
        UseUpgrade(electronics, rebuilt.Machine);
        Assert(rebuilt.Cpu.Width == 2 && rebuilt.Cpu.Height == 2 && electronics.unitCount == 2 &&
            ReferenceEquals(secondCpu.parentInventory, rebuilt.Cpu) && ReferenceEquals(installed.parentInventory, rebuilt.Cpu),
            "Stage two adds a second row while preserving installed hardware and exact material payment.");
        ExpectExpansion(rebuilt, "server_stat_cpu_expansion");
        Assert(!electronics.canThisTargetItemFunc(electronics, rebuilt.Machine), "Completed CPU expansion refuses more material.");

        GameItem ingots = Material(ServerState.IngotId, 11);
        for (int i = 0; i < 4; i++) UseUpgrade(ingots, rebuilt.Machine);
        Assert(rebuilt.Gpu.Width == 2, "GPU starts at width two until five ingots.");
        ExpectExpansion(rebuilt, "server_stat_gpu_expansion", 4, 5);
        UseUpgrade(ingots, rebuilt.Machine);
        Assert(rebuilt.Gpu.Width == 4 && rebuilt.Gpu.Height == 6, "First five ingots expand GPU to 4x6.");
        ExpectExpansion(rebuilt, "server_stat_gpu_expansion", 0, 5);
        for (int i = 0; i < 5; i++) UseUpgrade(ingots, rebuilt.Machine);
        Assert(rebuilt.Gpu.Width == 6 && ingots.unitCount == 1, "Next five ingots expand GPU to 6x6; ten total are consumed.");
        Assert(!ingots.canThisTargetItemFunc(ingots, rebuilt.Machine), "Maximum GPU expansion refuses more ingots.");
        ExpectExpansion(rebuilt, "server_stat_gpu_expansion");

        GameItem contacts = DirectoryMaster.Item(ServerState.GoldFingerId).SetAmount(9);
        int target = ServerCatalog.Load().Definition.MemoryUpgradeUnits;
        for (int i = 0; i < target - 1; i++) UseUpgrade(contacts, rebuilt.Machine);
        Assert(rebuilt.Memory.Width == 4 && rebuilt.Memory.Height == 2,
            "Partial memory investment leaves two rows accessible.");
        var restoredMemory = new Rig();
        CopyTags(rebuilt.Machine, restoredMemory.Machine); restoredMemory.Machine.onLoaded(restoredMemory.Machine);
        int beforeMemoryExpansion = restoredMemory.Machine.contentWindow.ValidateCount;
        UseUpgrade(contacts, restoredMemory.Machine);
        Assert(restoredMemory.Memory.Width == 4 && restoredMemory.Memory.Height == 4 && contacts.unitCount == 1 &&
            restoredMemory.Machine.contentWindow.ValidateCount > beforeMemoryExpansion &&
            !contacts.canThisTargetItemFunc(contacts, restoredMemory.Machine),
            "Finishing the saved memory investment unlocks the remaining rows once and refreshes layout.");
        ExpectExpansion(restoredMemory, "server_stat_memory_expansion");

        int vanillaCalls = 0;
        var material = new GameItem { Id = ServerState.ElectronicsId };
        material.mayThisTargetItemFunc = (_, _) => true;
        material.canThisTargetItemFunc = (_, _) => true;
        material.onThisTargetItemFunc = (_, _) => vanillaCalls++;
        ServerElectronicUpgradePatch.Postfix(material);
        var nativeTarget = new GameItem { Id = "bottle_printer" };
        Assert(material.canThisTargetItemFunc(material, nativeTarget), "Preserve native material conditions.");
        material.onThisTargetItemFunc(material, nativeTarget);
        Assert(vanillaCalls == 1, "Preserve native upgrade execution outside server targets.");
        var wrong = Material("rare_electronic", 1);
        Assert(!ServerUpgrades.CanUpgrade(wrong, rig.Machine, ServerCatalog.Load().Definition), "Only common_electronic is accepted.");
    }

    private static void CheckHardwareAggregation()
    {
        var rig = Ready(work: 2000);
        ServerHardware baseline = rig.Hardware();
        Near(baseline.CpuMultiplier, 2.2, "120 means plus 120 percent");
        Near(baseline.ComputePerNight, 22, "10 GPU compute times 2.2 CPU times 1.0 cooling");
        Assert(baseline.Power == 4 && baseline.MemoryGb == 8, "Only CPU/GPU draw power; memory contributes capacity.");
        var zeroBonus = Ready(work: 2000);
        GameItem zeroCpu = zeroBonus.Cpu.childItems.Single();
        zeroCpu.ModifyTag(CpuState.PowerBoostRateTag, tag => tag.SetFloat(0));
        ServerHardware zeroHardware = zeroBonus.Hardware();
        Near(zeroHardware.CpuMultiplier, 1, "A zero CPU bonus retains the 1x baseline");
        Near(zeroHardware.ComputePerNight, 10, "A zero CPU bonus retains the GPU's full base compute");
        zeroBonus.Night(1);
        Near(zeroBonus.State().Progress, 10, "Training must perform effective work with a zero-bonus CPU");
        Assert(Energy(zeroBonus.Battery.childItem) == 100 - zeroHardware.Power,
            "A zero CPU bonus still pays the installed hardware's configured power cost.");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            zeroCpu.ModifyTag(CpuState.PowerBoostRateTag, tag => tag.SetFloat(invalid));
            Assert(!ServerHardware.TryRead(zeroBonus.Cpu, zeroBonus.Gpu, zeroBonus.Memory, zeroBonus.Network,
                ServerCatalog.Load().Definition, out _), "Negative and non-finite saved CPU rates remain invalid.");
        }
        for (int i = 0; i < ServerCatalog.Load().Definition.CpuUpgradeUnitsTotal; i++) UseUpgrade(Material(ServerState.ElectronicsId, 1), rig.Machine);
        Put(rig.Cpu, DirectoryMaster.Item("age_of_agi.cpu1"));
        Put(rig.Gpu, DirectoryMaster.Item("age_of_agi.gpu0"));
        Put(rig.Memory, DirectoryMaster.Item("age_of_agi.mem1"));
        ServerHardware combined = rig.Hardware();
        Near(combined.CpuMultiplier, 3.7, "CPU percentage bonuses add to a single 1x baseline");
        Near(combined.ComputePerNight, 74, "GPU compute sums before CPU boost");
        Assert(combined.Power == 9 && combined.MemoryGb == 24 && MachineParameters.GetPowerPerCycle(rig.Machine) == 9,
            "Dynamic machine power and summed memory follow all installed hardware.");
        Put(rig.Network, DirectoryMaster.Item(NetworkSwitchUpgrades.SwitchId));
        ServerHardware withSwitch = rig.Hardware();
        Near(withSwitch.ComputePerNight, 74, "An installed switch does not change compute");
        Assert(withSwitch.Power == 9, "An installed switch draws no power.");
        rig.Night(1);
        Near(rig.State().Progress, 74, "Untrained AI prediction does not multiply training speed");
        Assert(Energy(rig.Battery.childItem) == 91, "Night pays total installed hardware power.");
        var fractional = Ready(work: 2000);
        GpuTestState.SetReal(fractional.Gpu.childItems.Single(), compute: 0.75);
        Near(fractional.Hardware().ComputePerNight, 1.65, "Server hardware preserves fractional GPU compute before CPU multiplication");
        fractional.Night(1);
        Near(fractional.State().Progress, 1.65, "Training progress consumes fractional GPU work without truncation");
        Assert(Energy(fractional.Battery.childItem) == 96, "Fractional compute still pays native integer hardware charges.");
        var broken = Ready(work: 2000);
        GpuTestState.SetReal(broken.Gpu.childItems.Single(), power: 0, supply: false);
        // The public native label still claims the connector is functional.
        broken.Night(2);
        Assert(Energy(broken.Battery.childItem) == 100 && (broken.State() == null || broken.State().Progress == 0),
            "A supply fault pauses the server with no charge or progress even when its label claims normal operation.");
    }

    private static void CheckNetworkSwitch()
    {
        var mod = new AgeOfAgiMod(); mod.Init(new ModManifest()); mod.OnEnable();
        try
        {
        var rig = Ready();
        GameItem net = DirectoryMaster.Item(NetworkSwitchUpgrades.SwitchId);
        Assert(NetworkSwitchUpgrades.IsNetworkSwitch(net) && NetworkSwitchUpgrades.GetUpgradeMask(net) == 0,
            "A new switch carries no upgrades.");
        Assert(!NetworkSwitchUpgrades.HasUpgrade(null, 0) && !NetworkSwitchUpgrades.HasUpgrade(net, -1) &&
            !NetworkSwitchUpgrades.HasUpgrade(net, NetworkSwitchUpgrades.ModuleCount) &&
            !NetworkSwitchUpgrades.HasUpgrade(DirectoryMaster.Item("age_of_agi.cpu0"), 0),
            "Upgrade queries are safe on null, foreign and out-of-range inputs.");
        Put(rig.Network, net);
        Assert(ReferenceEquals(ServerState.GetInstalledSwitch(rig.Machine), net) && !ServerState.HasSwitchUpgrade(rig.Machine, 0),
            "The server exposes its installed switch to recipes.");
        ModHelper.Locale = "en";
        var pristine = new RichTextBuilder(); ModHook.RaiseCreateTooltip(pristine, net);
        Assert(pristine.Lines.All(line => !line.Text.Contains("Upgrade module")),
            "A pristine switch annotates no upgrade status.");

        GameItem module0 = DirectoryMaster.Item(NetworkSwitchUpgrades.ModuleIdPrefix + "0");
        Assert(module0.mayThisTargetItemFunc(module0, net) && module0.canThisTargetItemFunc(module0, net) &&
            net.mayItemTargetThisFunc(module0, net) && net.canItemTargetThisFunc(module0, net),
            "An upgrade module may target the switch from both interaction sides.");
        Assert(!module0.canThisTargetItemFunc(module0, rig.Machine) && !module0.canThisTargetItemFunc(module0, null),
            "Modules only target the switch.");
        module0.onThisTargetItemFunc(module0, net);
        Assert(module0.Destroyed && NetworkSwitchUpgrades.HasUpgrade(net, 0) && ServerState.HasSwitchUpgrade(rig.Machine, 0),
            "Applying a module consumes it and lets server recipes detect the upgrade.");

        GameItem repeat = DirectoryMaster.Item(NetworkSwitchUpgrades.ModuleIdPrefix + "0");
        Assert(!repeat.canThisTargetItemFunc(repeat, net), "An installed upgrade refuses a second identical module.");
        GameItem stack = DirectoryMaster.Item(NetworkSwitchUpgrades.ModuleIdPrefix + "1");
        stack.SetAmount(2);
        stack.onThisTargetItemFunc(stack, net);
        Assert(!stack.Destroyed && stack.unitCount == 1 && NetworkSwitchUpgrades.GetUpgradeMask(net) == 3,
            "A stacked module pays exactly one unit and upgrades accumulate.");

        ModHelper.Locale = "zh";
        var upgraded = new RichTextBuilder(); ModHook.RaiseCreateTooltip(upgraded, net);
        Assert(upgraded.Lines.Count(line => line.Text.StartsWith("已安装：")) == 2 &&
            upgraded.Lines.Any(line => line.Text == "已安装：防火墙升级") && upgraded.Lines.Any(line => line.Text == "已安装：高速连接升级"),
            "The switch tooltip annotates each installed upgrade.");
        ModHelper.Locale = "en";
        }
        finally { mod.OnDisable(); AgeOfAgi.Items.Logic.DistributedTerminal.DistributedNetwork.Enable(); ModHelper.Locale = "en"; }
    }

    private static void CheckTrainingAndInheritance()
    {
        var rig = Ready(work: 80, task: "intelligence_intrusion", durability: 7.29f);
        GameItem original = rig.Model.childItem;
        Assert(rig.Row.GetPanel("training") == null && !rig.Machine.IsTag(ServerState.ProgressVisibleTag), "Hide the screen and progress until the first effective night.");
        rig.Night(1);
        Assert(rig.Machine.IsTag(ServerState.ProgressVisibleTag) && rig.State().WorkNights == 1,
            "First effective night reveals progress on the machine.");
        Near(rig.State().Progress, 22, "First night");
        rig.Night(1);
        Near(rig.State().Progress, 22, "Duplicate same-day callback does not work twice");
        Assert(Energy(rig.Battery.childItem) == 96, "Duplicate callback does not pay twice.");
        rig.Night(2); rig.Night(3); rig.Night(4);
        GameItem trained = rig.Output.childItems.Single();
        Assert(original.Destroyed && rig.Model.childItem == null && AiModelState.GetTaskType(trained) == "intelligence_intrusion",
            "Completion consumes exactly the original model and outputs its task type.");
        Assert(AiModelState.GetMemoryGb(trained) == 2 && Energy(rig.Battery.childItem) == 84, "Memory inherits; final night pays full hardware cost.");
        Near(AiModelState.GetComputeBonus(trained), 20, "Predicted bonus becomes actual compute bonus");
        Near(AiModelState.GetComputeMultiplier(trained), 1.2, "Actual deployment multiplier");
        Near(AiModelState.GetInitialDurability(trained), 7, "Initial durability inheritance");
        Near(AiModelState.GetCurrentDurability(trained), 7, "Current durability starts full");
        Assert(!trained.IsTag(AiModelState.TrainingTag) && !trained.IsTag(AiModelState.TrainingDataTag) &&
            !trained.IsTag(AiModelState.PredictedComputeTag), "Finished models carry no training data charge into production.");
        Assert(rig.Row.GetPanel("training") == null && !rig.Machine.IsTag(ServerState.ActiveTaskTag) && !rig.Cpu.IsRemoveLocked(),
            "Completion hides the screen and unlocks hardware.");
        Put(rig.Model, Model(80)); rig.Night(4);
        Near(rig.State().Progress, 0, "A new model cannot use completion overflow or run again the same night");
        Assert(rig.Row.GetPanel("training") == null, "A new waiting task must not keep the previous screen visible.");
        var immediate = Ready(work: 10);
        var regularized = Ready(work: 10, durability: 12);
        regularized.Night(1);
        Near(AiModelState.GetInitialDurability(regularized.Output.childItems.Single()), 12, "Training inherits durability above ten without applying regularization twice");
        immediate.Night(5);
        Assert(immediate.Output.childItems.Count == 1 && immediate.Row.GetPanel("training") == null,
            "A task completed in its first night never leaves a training screen visible.");
    }

    private static void CheckTrainingData()
    {
        var rig = Ready();
        GameItem data = rig.DataContainer.childItem;
        DataContentState.SetAmount(data, 20);
        rig.Night(1);
        Assert(DataContentState.Amount(data) == 14 && rig.State().DataConsumed == 6, "First night rounds cumulative 22/4 up to six data.");
        rig.Night(1);
        Assert(DataContentState.Amount(data) == 14, "Duplicate callback cannot consume data twice.");
        rig.Night(2);
        Assert(DataContentState.Amount(data) == 9 && rig.State().DataConsumed == 11, "Second night charges only the cumulative difference.");
        rig.DataContainer.Remove(data);
        rig.Night(3);
        Assert(rig.State().Progress == 44 && Energy(rig.Battery.childItem) == 92 && rig.Cpu.IsRemoveLocked() &&
            !rig.DataContainer.IsRemoveLocked(), "Missing data pauses without fees and allows replacing the container.");
        GameItem replacement = DirectoryMaster.Item("age_of_agi.data_container");
        DataContentState.SetAmount(replacement, 9); Put(rig.DataContainer, replacement);
        rig.Night(4); rig.Night(5);
        Assert(rig.Output.childItems.Count == 1 && DataContentState.Amount(replacement) == 0 && !replacement.Destroyed &&
            replacement.SpriteKey == "data_container0" && replacement.GetBaseValue() == 60 && DataContentState.Amount(data) == 9,
            "Training spends exactly twenty total data, retains empty shell and updates sprite/value.");
        var fast = Ready(); Put(fast.Gpu, DirectoryMaster.Item("age_of_agi.gpu0"));
        DataContentState.SetAmount(fast.DataContainer.childItem, 20);
        fast.Night(1); fast.Night(2);
        Assert(DataContentState.Amount(fast.DataContainer.childItem) == 0 && fast.Output.childItems.Count == 1,
            "Faster hardware changes nights but not total data cost.");
        var shortData = Ready(); DataContentState.SetAmount(shortData.DataContainer.childItem, 5);
        shortData.Night(1);
        Assert(shortData.State().Progress == 0 && Energy(shortData.Battery.childItem) == 100 &&
            DataContentState.Amount(shortData.DataContainer.childItem) == 5, "Insufficient data for this night's work pauses before any charge.");
        var changed = Ready(work: 10);
        GameItem before = changed.DataContainer.childItem;
        changed.Output.AfterAccept = _ => DataContentState.SetAmount(before, 1);
        changed.Night(1);
        Assert(changed.Output.childItems.Count == 0 && changed.State().Progress == 0 && Energy(changed.Battery.childItem) == 100 &&
            DataContentState.Amount(before) == 1, "Data mutation during reserved-output placement aborts without charging or overwriting the mutation.");
        Console.WriteLine("PASS: progress-based training data, cumulative rounding, replacement/pause, empty shell/value, transaction checks and current-format saves.");
    }

    private static void CheckIndependentTrainingData()
    {
        var efficient = Ready(work: 60, trainingData: 20);
        efficient.Cpu.childItems.Single().ModifyTag(CpuState.PowerBoostRateTag, tag => tag.SetFloat(100));
        var data = efficient.DataContainer.childItem;
        DataContentState.SetAmount(data, 20);
        Assert(efficient.State().Version == 2 && efficient.State().TotalWork == 60 && efficient.State().TotalData == 20, "New tasks snapshot work and data independently.");
        efficient.Night(1);
        Assert(efficient.State().Progress == 20 && efficient.State().DataConsumed == 7 && DataContentState.Amount(data) == 13,
            "First twenty of sixty work pays ceil(20 times 20/60) = seven data.");
        Assert(efficient.Machine.GetTagReadonly("AGE_OF_AGI_SERVER_TRAINING_DATA_REMAINING").GetInt() == 13,
            "The server reports the frozen remaining data requirement.");
        var frozen = JsonConvert.DeserializeObject<ServerTrainingState>(JsonConvert.SerializeObject(efficient.State()));
        Assert(frozen.IsValid() && frozen.TotalData == 20 && frozen.DataForNight(40) == 7,
            "Current-format serialization preserves the frozen independent budget.");
        efficient.Machine.onLoaded(efficient.Machine);
        Assert(efficient.State().Version == 2 && efficient.State().TotalData == 20 && efficient.State().Progress == 20 &&
            efficient.State().DataConsumed == 7 && efficient.Cpu.IsRemoveLocked(), "In-progress independent training restores budget, payments and locks.");
        efficient.Night(2);
        Assert(efficient.State().DataConsumed == 14 && DataContentState.Amount(data) == 6, "Second night pays seven more data.");
        efficient.Night(3);
        Assert(efficient.Output.childItems.Count == 1 && DataContentState.Amount(data) == 0 && Energy(efficient.Battery.childItem) == 88,
            "Reduced work completes in three nights and consumes exactly twenty data with a six-data tail.");

        var fast = Ready(work: 60, trainingData: 20);
        Put(fast.Gpu, DirectoryMaster.Item("age_of_agi.gpu0"));
        DataContentState.SetAmount(fast.DataContainer.childItem, 20);
        fast.Night(1); fast.Night(2);
        Assert(fast.Output.childItems.Count == 1 && DataContentState.Amount(fast.DataContainer.childItem) == 0,
            "Different hardware and night counts preserve the independent total.");
        var shortData = Ready(work: 60, trainingData: 20);
        DataContentState.SetAmount(shortData.DataContainer.childItem, 7);
        shortData.Night(1);
        Assert(shortData.State().Progress == 0 && DataContentState.Amount(shortData.DataContainer.childItem) == 7 &&
            Energy(shortData.Battery.childItem) == 100, "A higher data-per-work requirement pauses before paying an insufficient night.");
        var sparse = Ready(work: 80, trainingData: 1);
        DataContentState.SetAmount(sparse.DataContainer.childItem, 1);
        sparse.Night(1); sparse.Night(2); sparse.Night(3); sparse.Night(4);
        Assert(sparse.Output.childItems.Count == 1 && DataContentState.Amount(sparse.DataContainer.childItem) == 0,
            "Cumulative rounding permits zero-data nights after the one-data budget is paid, retaining the empty shell.");

        var proportional = ServerTrainingState.Create(Model(82, trainingData: 21));
        Assert(proportional.DataNeededAt(44) == 12, "New 82-work models instead use ceil(21 times 44/82).");
        var boundary = ServerTrainingState.Create(Model(3, trainingData: int.MaxValue));
        Assert(boundary.IsValid() && boundary.DataNeededAt(1) == 715827883 && boundary.DataNeededAt(3) == int.MaxValue,
            "Proportional multiplication supports Int32 data budgets and exact completion without overflow.");

        var invalid = Model(60, trainingData: 20);
        invalid.DisableTag(AiModelState.TrainingDataTag);
        Assert(!AiModelState.CanTrain(invalid), "Models require an explicit positive training data budget.");
        foreach (int amount in new[] { 0, -1 })
        {
            invalid.ModifyTag(AiModelState.TrainingDataTag, tag => tag.Enable().SetInt(amount));
            Assert(!AiModelState.CanTrain(invalid), "Reject non-positive new model budgets.");
        }
        foreach (string field in new[] { "TotalData", "DataConsumed", "Version" })
        {
            JObject broken = JObject.Parse(JsonConvert.SerializeObject(frozen));
            broken[field] = -1;
            Assert(!JsonConvert.DeserializeObject<ServerTrainingState>(broken.ToString()).IsValid(), "Reject inconsistent saved data budgets.");
        }
        JObject missingVersion = JObject.Parse(JsonConvert.SerializeObject(frozen));
        missingVersion.Remove("Version");
        var downgraded = JsonConvert.DeserializeObject<ServerTrainingState>(missingVersion.ToString());
        Assert(!downgraded.IsValid() && downgraded.DataConsumed == 7 && !downgraded.Matches(Model(60, trainingData: 20)),
            "Missing task version is invalid and cannot change already paid data.");
        var corrupt = Ready(work: 60, trainingData: 20);
        var corruptState = corrupt.State();
        corruptState.TotalData = 0;
        corrupt.Machine.ModifyTag(ServerState.TrainingStateTag, tag => tag.SetString(JsonConvert.SerializeObject(corruptState)));
        corrupt.Machine.onLoaded(corrupt.Machine); corrupt.Night(1);
        Assert(Energy(corrupt.Battery.childItem) == 100 && DataContentState.Amount(corrupt.DataContainer.childItem) == 2000 &&
            corrupt.Output.childItems.Count == 0 && corrupt.Cpu.IsRemoveLocked(), "Invalid saved independent budgets pause without charging or unlocking active inputs.");
        var changed = Ready(work: 10, trainingData: 20);
        changed.Output.AfterAccept = _ => changed.Model.childItem.ModifyTag(AiModelState.TrainingDataTag, tag => tag.SetInt(21));
        int before = DataContentState.Amount(changed.DataContainer.childItem);
        changed.Night(1);
        Assert(changed.Output.childItems.Count == 0 && changed.State().Progress == 0 && Energy(changed.Battery.childItem) == 100 &&
            DataContentState.Amount(changed.DataContainer.childItem) == before,
            "Model budget changes during output placement abort without charging data or energy.");
        Console.WriteLine("PASS: independent training work/data, proportional cumulative fees, exact tails, budget validation and callback conservation.");
    }

    private static void CheckTwoNightCompletion()
    {
        var rig = Ready(work: 80);
        Put(rig.Gpu, DirectoryMaster.Item("age_of_agi.gpu0"));
        rig.Night(20);
        Near(rig.State().Progress, 44, "Two GPUs advance an 80-work task to 44 on its first night");
        Assert(Energy(rig.Battery.childItem) == 94, "First night consumes six energy.");
        rig.Night(21);
        Assert(rig.Output.childItems.Count == 1 && rig.Model.childItem == null && Energy(rig.Battery.childItem) == 88,
            "The next night must complete 44/80, produce trained AI and pay exactly once.");

        var debug = Ready(work: 80);
        Put(debug.Gpu, DirectoryMaster.Item("age_of_agi.gpu0"));
        StoreStation.Instance.dayCounter = 30;
        debug.Machine.onCycleEndEarlySlotItemFunc(debug.Machine, null, null);
        debug.Night(30);
        Near(debug.State().Progress, 44, "First Machines Only cycle");
        debug.Night(30);
        Near(debug.State().Progress, 44, "Duplicate main callback in one cycle remains ignored");
        debug.Machine.onCycleEndEarlySlotItemFunc(debug.Machine, null, null);
        debug.Night(30);
        Assert(debug.Output.childItems.Count == 1 && debug.Model.childItem == null && Energy(debug.Battery.childItem) == 88,
            "A second native Machines Only cycle completes 44/80 without changing the calendar day.");
        Assert(StoreStation.Instance.dayCounter == 30, "Machine-only simulation cannot alter the game's calendar.");
    }

    private static void CheckPausesAndLocks()
    {
        var rig = Ready(work: 80, energy: 4);
        rig.Night(1);
        Assert(new GameInventory[] { rig.Cpu, rig.Gpu, rig.Network, rig.Memory, rig.Model }
            .All(inventory => inventory.IsInsertLocked() && inventory.IsRemoveLocked()), "Active task keeps the hardware and model bays locked.");
        Assert(!rig.Battery.IsInsertLocked() && !rig.Output.IsRemoveLocked(), "Power replacement and output extraction remain available.");
        GameItem material = Material(ServerState.ElectronicsId, 1);
        Assert(!material.canThisTargetItemFunc(material, rig.Machine), "No expansion while training is active.");
        Assert(GraphUtils.CanAccept(rig.Input, new GameItem()) && !rig.Input.IsRemoveLocked(), "Unprocessed inputs remain accessible during training.");
        rig.Night(2);
        Near(rig.State().Progress, 22, "No power pauses progress");
        Assert(rig.State().Progress == 22 && rig.State().WorkNights == 1 && Energy(rig.Battery.childItem) == 0,
            "A paused night consumes no resources and adds no working-night count.");
        GameItem exhausted = rig.Battery.childItem; rig.Battery.Remove(exhausted);
        rig.Night(3);
        Assert(rig.State().WorkNights == 1 && rig.Machine.IsTag(ServerState.ActiveTaskTag), "Missing battery keeps task and locks without advancing work.");
        Put(rig.Battery, Battery(100)); rig.Night(4);
        Near(rig.State().Progress, 44, "Replacement battery resumes the same task");
        var memory = Ready(work: 80, memory: 9);
        memory.Night(1);
        Assert(memory.State().Progress == 0 && Energy(memory.Battery.childItem) == 100 && !memory.Machine.IsTag(ServerState.ActiveTaskTag),
            "Insufficient memory prevents task start and payment.");
        Assert(memory.Row.GetPanel("training") == null, "A paused attempt before any effective work cannot reveal the screen.");
        Put(memory.Memory, DirectoryMaster.Item("age_of_agi.mem1")); memory.Night(2);
        Near(memory.State().Progress, 22, "Additional memory enables training");
    }

    private static void CheckOutputTransactions()
    {
        var full = Ready(work: 80);
        var blocker = new GameItem(); blocker.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1, 16).ToArray(), 4));
        full.Output.UncheckedAccept(blocker);
        full.Night(1);
        Assert(full.State().Progress == 0 && Energy(full.Battery.childItem) == 100, "Full output pauses even a non-completing night.");
        full.Output.Remove(blocker); full.Night(2);
        Near(full.State().Progress, 22, "Clearing output resumes work");
        var rejected = Ready(work: 10); rejected.Output.RejectTransfer = true;
        int created = ItemDirectory.CreatedInstances.Count;
        rejected.Night(1);
        Assert(rejected.Output.childItems.Count == 0 && rejected.Model.childItem != null && Energy(rejected.Battery.childItem) == 100 && rejected.State().Progress == 0,
            "Failed final output transfer preserves model, energy and progress.");
        Assert(ItemDirectory.CreatedInstances.Skip(created).All(item => item.Destroyed || item.parentInventory != null), "Failed training leaves no orphan products.");
        rejected.Output.RejectTransfer = false; rejected.Night(2);
        Assert(rejected.Output.childItems.Count == 1 && Energy(rejected.Battery.childItem) == 96, "Retry completes once at full final-night cost.");

        var reentrant = Ready(work: 10);
        reentrant.Output.AfterAccept = _ => reentrant.Night(1);
        reentrant.Night(1);
        Assert(reentrant.Output.childItems.Count == 1 && Energy(reentrant.Battery.childItem) == 96, "Output callback cannot reenter training.");
        var changed = Ready(work: 10);
        changed.Output.AfterAccept = _ => changed.Cpu.childItems.Single().ModifyTag(CpuState.PowerBoostRateTag, tag => tag.SetFloat(150));
        changed.Night(1);
        Assert(changed.Output.childItems.Count == 0 && changed.Model.childItem != null && Energy(changed.Battery.childItem) == 100,
            "Hardware changes during output callback abort without consuming.");
        var swapped = Ready(work: 10);
        swapped.Output.AfterAccept = _ => { swapped.Model.RawRemove(swapped.Model.childItem); swapped.Model.UncheckedAccept(Model(100)); };
        swapped.Night(1);
        Assert(swapped.Output.childItems.Count == 0 && swapped.State().TotalWork == 10 && swapped.State().Progress == 0,
            "Another model cannot inherit the old task's work or produce its output.");
    }

    private static void CheckSavedTask()
    {
        var rig = Ready(work: 80, durability: 5);
        rig.Night(10); rig.Night(11);
        string saved = rig.Machine.GetTagReadonly(ServerState.TrainingStateTag).GetString();
        var before = rig.State();
        var rebuilt = new Rig(); CopyTags(rig.Machine, rebuilt.Machine);
        for (int i = 0; i < 9; i++)
            foreach (GameItem item in ((GameInventory)rig.Row.GetStorage(i)).childItems)
                ((GameInventory)rebuilt.Row.GetStorage(i)).UncheckedAccept(item);
        rebuilt.Machine.onLoaded(rebuilt.Machine);
        Assert(rebuilt.Machine.GetTagReadonly(ServerState.TrainingStateTag).GetString() == saved && rebuilt.Cpu.IsRemoveLocked(),
            "Reload preserves task fields/history and restores bay locks.");
        int energy = Energy(rebuilt.Battery.childItem); rebuilt.Night(11);
        Assert(Energy(rebuilt.Battery.childItem) == energy && rebuilt.State().Progress == 44, "Saved night guard prevents replay.");
        rebuilt.Night(12); Near(rebuilt.State().Progress, 66, "Resumed task uses restored slot nodes");
        var fault = Ready();
        fault.Machine.ModifyTag(ServerState.TrainingStateTag, tag => tag.Enable().SetString("{broken"));
        fault.Machine.onLoaded(fault.Machine); fault.Night(1);
        Assert(Energy(fault.Battery.childItem) == 100 && fault.Machine.IsTag(ServerState.ActiveTaskTag) && fault.Cpu.IsRemoveLocked(),
            "Invalid saved task pauses and conserves resources without failing game loading.");
    }

    private static void CheckWorkingNightsAndRestore()
    {
        var rig = Ready(work: 80, energy: 0);
        rig.Night(1); rig.Night(2);
        Assert(rig.State().Progress == 0 && rig.State().WorkNights == 0,
            "Unpowered nights cannot increment paid work or working-night count.");
        rig.Battery.Remove(rig.Battery.childItem); Put(rig.Battery, Battery(100));
        rig.Night(3);
        Assert(rig.State().Progress == 22 && rig.State().WorkNights == 1 && Energy(rig.Battery.childItem) == 96,
            "The first effective night advances real progress and its count once.");
        rig.Battery.Remove(rig.Battery.childItem); rig.Night(4);
        Assert(rig.State().Progress == 22 && rig.State().WorkNights == 1,
            "Pauses preserve progress and effective-night count.");
        string snapshot = rig.Machine.GetTagReadonly(ServerState.TrainingStateTag).GetString();
        int paid = rig.State().DataConsumed;
        rig.Machine.onLoaded(rig.Machine);
        Assert(rig.Machine.GetTagReadonly(ServerState.TrainingStateTag).GetString() == snapshot &&
            rig.State().Progress == 22 && rig.State().WorkNights == 1 && rig.State().DataConsumed == paid && rig.Cpu.IsRemoveLocked(),
            "Current-format load retains paid work, data charges and locks without any migration.");
        Assert(rig.Row.GetPanel("training") == null && rig.Row.GetStorage(8) == rig.DataContainer && rig.Row.GetStorage(9) == null,
            "Training has no non-storage display panel and preserves all nine storage indices.");
        var idle = Ready(energy: 0);
        var invalid = idle.State(); invalid.WorkNights = 2;
        Assert(!invalid.IsValid(), "An effective-night count without paid progress is invalid.");
        idle.Machine.ModifyTag(ServerState.TrainingStateTag, tag => tag.SetString(JsonConvert.SerializeObject(invalid)));
        idle.Machine.onLoaded(idle.Machine); idle.Night(1);
        Assert(idle.Cpu.IsRemoveLocked() && Energy(idle.Battery.childItem) == 0 && !idle.Machine.IsTag(ServerState.ProgressVisibleTag),
            "Inconsistent effective-night counts pause without creating paid progress or charging resources.");
    }
    private static void CheckTrainedTooltips()
    {
        var rig = Ready(work: 10, task: "biological_analysis", durability: 9);
        var mod = new AgeOfAgiMod(); mod.Init(new ModManifest()); mod.OnEnable(); mod.OnEnable();
        try
        {
            ModHelper.Locale = "zh";
            var ongoing = Ready(work: 80);
            var before = new RichTextBuilder(); ModHook.RaiseCreateTooltip(before, ongoing.Machine);
            Assert(before.Lines.All(line => !line.Text.StartsWith("任务进度：")), "Hide task-progress tooltip before the first effective night.");
            Assert(before.ProgressBars.Count == 0, "Hide the native progress bar before the first working night.");
            Assert(new[] { "有效算力：22", "内存：8 GB", "所需能量：4 /天",
                ModHelper.GetLocalized(ModConstants.ModId, "server_stat_cpu_expansion", 0, ServerCatalog.Load().Definition.CpuUpgradeUnitsStep1),
                ModHelper.GetLocalized(ModConstants.ModId, "server_stat_gpu_expansion", 0, ServerCatalog.Load().Definition.GpuUpgradeUnitsPerStep), "就绪" }
                .All(expected => before.Lines.Count(line => line.Text == expected) == 1),
                "The machine tooltip owns all statistical fields, including exactly one power row.");
            Assert(before.Lines.All(line => !line.Text.Contains("CPU倍率")), "Hide CPU multiplier from the server's display.");
            Assert(before.Text.Contains("所需能量：4 /天\n\n有效算力：22") &&
                before.Text.Contains("内存：8 GB\n\n") &&
                before.Text.Contains(ModHelper.GetLocalized(ModConstants.ModId, "server_stat_memory_expansion", 0, 8) + "\n\n就绪") &&
                before.Text.Split("\n\n").Length == 4 && !before.Text.Contains("\n\n\n") && !before.Text.Contains("<size="),
                "Each server section has exactly one plain blank line without spacer text or extra height.");
            ongoing.Night(1);
            var running = new RichTextBuilder(); ModHook.RaiseCreateTooltip(running, ongoing.Machine);
            Assert(running.Lines.Any(line => line.Text == "运行中") && running.Lines.All(line => line.Text != "就绪"),
                "After the first working night show Running instead of Ready.");
            Assert(running.Lines.Count(line => line.Text == "任务进度：22/80（27.5%）") == 1,
                "Ordinary server tooltip shows saved progress exactly once after work.");
            Assert(running.ProgressBars.Count == 1 && running.ProgressBars[0].Length == 10 &&
                running.ProgressBars[0].Color == RenderHandler.ColorPalette.LightGreen &&
                running.ProgressBars[0].Background == RenderHandler.ColorPalette.VeryDarkGreen,
                "Use the native hydroponic-style rich-text progress bar exactly once.");
            Near(running.ProgressBars[0].Ratio, 22.0 / 80, "Native progress-bar ratio preserves fractional progress");
            GameItem battery = ongoing.Battery.childItem;
            ongoing.Battery.Remove(battery);
            var paused = new RichTextBuilder(); ModHook.RaiseCreateTooltip(paused, ongoing.Machine);
            Assert(paused.Lines.Any(line => line.Text == "电量不足" && line.Color == RenderHandler.ColorPalette.Yellow) && paused.ProgressBars.Count == 1,
                "Paused status and retained progress are both shown on the machine.");
            Put(ongoing.Battery, battery);
            var resumed = new RichTextBuilder(); ModHook.RaiseCreateTooltip(resumed, ongoing.Machine);
            Assert(resumed.Lines.Any(line => line.Text == "运行中") && resumed.Lines.All(line => line.Text != "就绪"),
                "Replacing power resumes Running and never resets to Ready.");
            ModHelper.Locale = "en";
            var english = new RichTextBuilder(); ModHook.RaiseCreateTooltip(english, ongoing.Machine);
            Assert(english.Lines.Any(line => line.Text == "Effective compute: 22") && english.Lines.Any(line => line.Text == "Memory: 8 GB") &&
                english.Lines.All(line => !line.Text.Contains("CPU multiplier")), "Localize machine properties while keeping CPU multiplier hidden.");
            ModHelper.Locale = "zh";
            var memoryOnly = new Rig(); Put(memoryOnly.Memory, DirectoryMaster.Item("age_of_agi.mem0"));
            var partial = new RichTextBuilder(); ModHook.RaiseCreateTooltip(partial, memoryOnly.Machine);
            Assert(partial.Lines.Any(line => line.Text == "内存：8 GB"), "Display installed memory even before CPU/GPU are present.");
            Assert(string.IsNullOrEmpty(memoryOnly.Machine.GetTagReadonly(ServerState.StatusTag).GetString()),
                "An empty model slot has no insert-model status text.");
            rig.Night(1);
            GameItem item = rig.Output.childItems.Single();
            Assert(item.name == "AI模型", "All trained tasks share one model name.");
            var builder = new RichTextBuilder(); ModHook.RaiseCreateTooltip(builder, item);
            Assert(builder.Lines.Select(line => line.Text).SequenceEqual(new[] { "内存要求：2 GB", "计算性能：+20%", "耐久：9/9", "", "生化", "" }),
                "Trained tooltip displays actual performance and durability exactly once.");
        }
        finally { mod.OnDisable(); AgeOfAgi.Items.Logic.DistributedTerminal.DistributedNetwork.Enable(); ModHelper.Locale = "en"; }
    }

    private static void CheckMemoryStatus()
    {
        foreach (bool trained in new[] { false, true })
        {
            var rig = Ready(memory: 9, energy: 0);
            if (trained)
            {
                rig.Model.Remove(rig.Model.childItem);
                GameItem model = DirectoryMaster.Item(AiModelState.TrainedId);
                AiModelState.ApplyTrained(model, "computing", 9, 20, 5);
                Put(rig.Model, model);
            }
            Assert(rig.Machine.GetTagReadonly(ServerState.StatusTag).GetString() == "server_insufficient_memory",
                "A valid model exceeding installed memory reports insufficient memory before input or power checks.");
            foreach (string locale in new[] { "zh", "en" })
            {
                ModHelper.Locale = locale;
                var tooltip = new RichTextBuilder(); ServerHelper.CreateTooltip(tooltip, rig.Machine);
                string memoryStatus = ModHelper.GetLocalized(ModConstants.ModId, "server_insufficient_memory");
                Assert(tooltip.Lines.Count(line => line.Text == memoryStatus && line.Color == RenderHandler.ColorPalette.LightRed) == 1,
                    "Training and production memory warnings use one localized pale-red status row.");
            }
            GameItem cpu = rig.Cpu.childItems.Single(); rig.Cpu.Remove(cpu);
            Assert(rig.Machine.GetTagReadonly(ServerState.StatusTag).GetString() == "server_missing_hardware",
                "Missing hardware retains priority until CPU, GPU and memory are all usable.");
            Put(rig.Cpu, cpu);
            if (trained)
            {
                Put(rig.Input, DirectoryMaster.Item("age_of_agi.dc_common_data_disk"));
                Assert(rig.Machine.GetTagReadonly(ServerState.StatusTag).GetString() == "server_insufficient_memory",
                    "Extraction's capacity-filtered recipe selection cannot turn insufficient memory into idle.");
            }
            rig.Night(1);
            Assert(Energy(rig.Battery.childItem) == 0 && !rig.Machine.IsTag(ServerState.ActiveTaskTag),
                "Insufficient memory starts no task and consumes no power.");
            Put(rig.Memory, DirectoryMaster.Item("age_of_agi.mem1"));
            Assert(rig.Machine.GetTagReadonly(ServerState.StatusTag).GetString() != "server_insufficient_memory",
                "Installing enough memory immediately clears the warning.");
        }
        var exact = Ready(memory: 8);
        Assert(exact.Machine.GetTagReadonly(ServerState.StatusTag).GetString() == "server_ready",
            "A model whose memory requirement equals installed capacity remains runnable.");
        ModHelper.Locale = "en";
    }

    private sealed class Rig
    {
        internal readonly GameItem Machine = DirectoryMaster.Item(ServerCatalog.ServerId);
        internal MachineryPanelRow Row => Machine.contentWindow.childElement;
        internal GameSlotInventory Battery => (GameSlotInventory)Row.GetStorage(ServerState.BatteryIndex);
        internal GameGridInventory Cpu => (GameGridInventory)Row.GetStorage(ServerState.CpuIndex);
        internal GameGridInventory Gpu => (GameGridInventory)Row.GetStorage(ServerState.GpuIndex);
        internal GameSlotInventory Network => (GameSlotInventory)Row.GetStorage(ServerState.NetworkIndex);
        internal GameGridInventory Memory => (GameGridInventory)Row.GetStorage(ServerState.MemoryIndex);
        internal GameSlotInventory Model => (GameSlotInventory)Row.GetStorage(ServerState.ModelIndex);
        internal GameGridInventory Input => (GameGridInventory)Row.GetStorage(ServerState.InputIndex);
        internal GameGridInventory Output => (GameGridInventory)Row.GetStorage(ServerState.OutputIndex);
        internal GameSlotInventory DataContainer => (GameSlotInventory)Row.GetStorage(ServerState.DataContainerIndex);
        internal ServerTrainingState State() => JsonConvert.DeserializeObject<ServerTrainingState>(Machine.GetTagReadonly(ServerState.TrainingStateTag).GetString());
        internal ServerHardware Hardware()
        {
            Assert(ServerHardware.TryRead(Cpu, Gpu, Memory, Network, ServerCatalog.Load().Definition, out var hardware), "Invalid hardware fixture.");
            return hardware;
        }
        internal void Night(int day)
        {
            StoreStation.Instance.dayCounter = day;
            Machine.onCycleEndSlotItemFunc(Machine, null, null);
        }
    }

    private static Rig Ready(float work = 80, int energy = 100, int memory = 2, string task = "computing", float durability = 10,
        int trainingData = 20)
    {
        var rig = new Rig();
        Put(rig.Battery, Battery(energy)); Put(rig.Cpu, DirectoryMaster.Item("age_of_agi.cpu0"));
        Put(rig.Gpu, DirectoryMaster.Item("age_of_agi.gpu0")); Put(rig.Memory, DirectoryMaster.Item("age_of_agi.mem0"));
        GameItem data = DirectoryMaster.Item("age_of_agi.hypo_data_container");
        DataContentState.RestoreFrozenOffer(data, 2000); Put(rig.DataContainer, data);
        Put(rig.Model, Model(work, memory, task, durability, trainingData));
        return rig;
    }
    private static GameItem Model(float work, int memory = 2, string task = "computing", float durability = 10,
        int trainingData = 20)
    {
        GameItem item = DirectoryMaster.Item(AiModelState.UntrainedId);
        AiModelState.Apply(item, new AiModelAttributes( task, work, memory, 20, durability,
            trainingData));
        return item;
    }
    private static GameItem Battery(int energy)
    {
        var item = new GameItem { Id = "energy_credit" };
        item.EnableTag("power_source_item"); item.ModifyTag("power_source_item_energy", tag => tag.Enable().SetInt(energy));
        return item;
    }
    private static GameItem Material(string id, int count)
    {
        var item = new GameItem { Id = id, unitCount = count };
        if (id == ServerState.IngotId) ServerIngotUpgradePatch.Postfix(item);
        else ServerElectronicUpgradePatch.Postfix(item);
        return item;
    }
    private static void ExpectExpansion(Rig rig, string key, int progress = -1, int target = 0)
    {
        var builder = new RichTextBuilder(); ServerTooltip.CreateTooltip(builder, rig.Machine);
        string prefix = ModHelper.GetLocalized(ModConstants.ModId, key, "", "").Split(':', '：')[0];
        var rows = builder.Lines.Where(line => line.Text.StartsWith(prefix)).ToArray();
        Assert(progress < 0 ? rows.Length == 0 : rows.Length == 1 && rows[0].Text == ModHelper.GetLocalized(ModConstants.ModId, key, progress, target),
            "Display only the current expansion stage and hide completed upgrades.");
    }

    private static void UseUpgrade(GameItem source, GameItem target)
    {
        Assert(source.mayThisTargetItemFunc(source, target) && source.canThisTargetItemFunc(source, target), "Upgrade material rejected.");
        source.onThisTargetItemFunc(source, target);
        target.onItemTargetThisFunc(source, target);
    }
    private static void CopyTags(GameItem source, GameItem target)
    {
        target.modifiedState.dict.Clear();
        foreach (var pair in source.modifiedState.dict)
            target.ModifyTag(pair.Key, tag => { tag.Enabled = pair.Value.Enabled; tag.Value = pair.Value.Value;
                tag.FloatValue = pair.Value.FloatValue; tag.LongValue = pair.Value.LongValue;
                tag.DoubleValue = pair.Value.DoubleValue; tag.StringValue = pair.Value.StringValue; });
    }
    private static int Count(GameItem item, string tag) => item.GetTagReadonly(tag).GetInt();
    private static int Energy(GameItem item) => item.GetTagReadonly("power_source_item_energy").GetInt();
    private static void Put(GameInventory inventory, GameItem item)
    {
        if (inventory is GameGridInventory && !GraphUtils.CanAccept(inventory, item))
        {
            GridShape original = item.modifiedShape.Clone();
            for (int rotation = 1; rotation < 4; rotation++)
            {
                item.SetShape(new GridShapeBuilder(original).SetOrientation(rotation));
                if (GraphUtils.CanAccept(inventory, item)) break;
            }
        }
        Assert(GraphUtils.TryAcceptAll(inventory, item) == 0, "Fixture item rejected: " + item.identifier);
    }
    private static void Near(double actual, double expected, string message) => Assert(Math.Abs(actual - expected) < 0.0001, message + ": " + actual + " != " + expected);
    private static void Invalid(Action action)
    {
        try { action(); }
        catch (InvalidOperationException error) { Assert(error.Message.Contains(ServerCatalog.CatalogPath), "Config error must identify server.csv."); return; }
        throw new Exception("Invalid server configuration accepted.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
