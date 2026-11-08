using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.BrainServer;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.DistributedTerminal;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.Server;
using Newtonsoft.Json;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class BrainServerChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        CheckPatchTargets();
        var loaded = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(loaded.Manifest);
        registration.Register(new ModItemDirectory(), definitions);
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        try
        {
            CheckBlood();
            CheckLifeAndDeath();
            CheckProduction();
            CheckNetwork();
            CheckRestoreAndLifecycle();
            Console.WriteLine("PASS: brain life support, real water purity, persistent blood, permanent death, intrinsic production and distributed supply.");
        }
        finally { registration.Unregister(); ModHelper.Translations.Clear(); EmporiumEntry.Instance = new EmporiumEntry(); }
    }

    private static void Reset()
    {
        PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry();
        NetworkSwitchLinks.Enable(); DistributedNetwork.Enable(); StoreStation.Instance.dayCounter = 0;
    }

    private static void CheckPatchTargets()
    {
        foreach (Type patchType in new[] { typeof(BloodSupplyCreationPatch), typeof(BloodSupplyStackPatch) })
        {
            var patch = patchType.GetCustomAttributes<HarmonyPatch>().Single().info;
            Assert(patch.argumentTypes != null, "Native blood patches must select an explicit overload.");
            MethodInfo target = AccessTools.DeclaredMethod(patch.declaringType, patch.methodName, patch.argumentTypes);
            Assert(target != null && !target.IsGenericMethod, "Harmony resolves the concrete blood patch target unambiguously.");
        }

        // Verify the installed game contract as well as the overloaded scene substitute.
        using var stream = System.IO.File.OpenRead(Program.GameAssemblyPath);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        var directory = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "DirectoryMaster");
        var methods = directory.GetMethods().Select(metadata.GetMethodDefinition)
            .Where(method => metadata.GetString(method.Name) == "Item").ToArray();
        Assert(methods.Any(method => method.GetGenericParameters().Count > 0), "Installed game contains the generic Item overload.");
        var factory = methods.Single(method => method.GetGenericParameters().Count == 0);
        BlobReader signature = metadata.GetBlobReader(factory.Signature);
        signature.ReadSignatureHeader();
        Assert(signature.ReadCompressedInteger() == 2 && signature.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle,
            "Native item factory returns a game type and takes two parameters.");
        signature.ReadTypeHandle();
        Assert(signature.ReadSignatureTypeCode() == SignatureTypeCode.String && signature.ReadSignatureTypeCode() == SignatureTypeCode.Boolean,
            "The concrete native item factory signature is Item(string, bool).");
    }

    private static void CheckBlood()
    {
        var ordinary = Blood(false); var advanced = Blood(true);
        Assert(BloodSupply.Amount(ordinary) == BloodSupply.Capacity(ordinary) && BloodSupply.Amount(advanced) == BloodSupply.Capacity(advanced),
            "Both native blood types start full.");
        BloodSupply.Consume(ordinary); BloodSupplyCreationPatch.Postfix(ordinary);
        Assert(BloodSupply.Amount(ordinary) == BloodSupply.Capacity(ordinary) - BloodSupply.DailyUse(ordinary), "Repeated initialization cannot refill a used bag.");
        bool stack = true; BloodSupplyStackPatch.Postfix(ordinary, Blood(false), ref stack);
        Assert(!stack, "Partly used bags cannot merge with fresh blood.");
        foreach (GameItem bag in new[] { ordinary, advanced })
        {
            bag.ModifyTag(BloodSupply.AmountTag, tag => tag.Enable().SetInt(BloodSupply.DailyUse(bag) * 2));
            BloodSupply.Consume(bag);
            Assert(!bag.Destroyed && BloodSupply.Amount(bag) == BloodSupply.DailyUse(bag), "The final daily draw remains usable.");
            BloodSupply.Consume(bag);
            Assert(bag.Destroyed, "An exactly exhausted bag is removed after its final daily draw.");
            var insufficient = Blood(bag.identifier == "blue_blood_bag");
            insufficient.ModifyTag(BloodSupply.AmountTag, tag => tag.SetInt(BloodSupply.DailyUse(insufficient) - 1));
            Assert(!BloodSupply.CanConsume(insufficient), "Blood must cover the entire daily milliliter requirement.");
        }
        var unrelated = new GameItem { Id = "unrelated_native_item" }; BloodSupplyCreationPatch.Postfix(unrelated);
        Assert(!unrelated.IsTag(BloodSupply.AmountTag), "Blood changes are scoped to the two native items.");
    }

    private static void CheckLifeAndDeath()
    {
        Reset(); var rig = new Rig(false);
        Assert(rig.Row.children.OfType<GameInventory>().Count() == 7, "Brain has exactly seven saved inventories and no AI or hardware bays.");
        Assert(ServerState.GetInstalledModel(rig.Machine) == null, "No hidden AI item is installed.");
        Cycle(1, rig.Machine);
        Assert(rig.Energy == 989 && WaterHelper.GetCurrentCapacityML(rig.Water.childItem) == 1963 &&
            BloodSupply.Amount(rig.Blood.childItem) == BloodSupply.Capacity(rig.Blood.childItem) - BloodSupply.DailyUse(rig.Blood.childItem),
            "Idle brain pays the independently configured daily power, water and blood.");
        int remaining = BloodSupply.Amount(rig.Blood.childItem);
        rig.Machine.onCycleEndSlotItemFunc(rig.Machine, null, null);
        Assert(rig.Energy == 989 && BloodSupply.Amount(rig.Blood.childItem) == remaining, "A duplicate main callback cannot charge twice.");
        rig.Blood.childItem.ModifyTag(BloodSupply.AmountTag, tag => tag.SetInt(BloodSupply.DailyUse(rig.Blood.childItem)));
        Cycle(2, rig.Machine);
        Assert(rig.Blood.childItem == null && !rig.Dead, "The last blood unit sustains a full day before the bag is removed.");
        Cycle(3, rig.Machine);
        Assert(rig.Dead && rig.Energy == 978 && rig.Machine.spritePath == "brain_server_dead", "The next unfed day kills the brain permanently.");
        Put(rig.Blood, Blood(true)); Cycle(4, rig.Machine); rig.Machine.onLoaded(rig.Machine);
        Assert(rig.Dead && rig.Energy == 978 && rig.Machine.spritePath == "brain_server_dead", "Refilling or reloading cannot revive the dead brain.");

        foreach (string failure in new[] { "power", "water", "purity", "blood", "blood_amount" })
        {
            Reset(); rig = new Rig();
            if (failure == "power") rig.Battery.childItem.ModifyTag("power_source_item_energy", tag => tag.SetInt(10));
            if (failure == "water") rig.Water.childItem.ModifyTag("TEST_WATER_ML", tag => tag.SetInt(36));
            if (failure == "purity") rig.Water.childItem.ModifyTag("TEST_REAL_PURITY", tag => tag.SetInt(9899));
            if (failure == "blood") rig.Blood.childItem.Owned = false;
            if (failure == "blood_amount") rig.Blood.childItem.ModifyTag(BloodSupply.AmountTag,
                tag => tag.SetInt(BloodSupply.DailyUse(rig.Blood.childItem) - 1));
            int before = BloodSupply.Amount(rig.Blood.childItem);
            Cycle(1, rig.Machine);
            Assert(rig.Dead && BloodSupply.Amount(rig.Blood.childItem) == before, "Any invalid life supply kills without partial billing: " + failure);
        }
        Reset(); rig = new Rig(); rig.Water.childItem.ModifyTag("TEST_REAL_PURITY", tag => tag.SetInt(9900));
        rig.Water.childItem.ModifyTag("TEST_WATER_ML", tag => tag.SetInt(37));
        Cycle(1, rig.Machine);
        Assert(!rig.Dead && WaterHelper.GetCurrentCapacityML(rig.Water.childItem) == 0, "Exactly one daily draw of genuinely high-quality water is sufficient.");
    }

    private static void CheckProduction()
    {
        Reset(); var rig = new Rig();
        Put(rig.Input, DirectoryMaster.Item(ServerProduction.NormalInputId));
        Cycle(1, rig.Machine);
        var state = rig.State();
        Assert(state.Intrinsic && state.IsValid() && state.ModelId == rig.Machine.uniqueId && state.ComputeBonus == 0 && state.Durability == 0,
            "Intrinsic work persists the brain identity without creating or wearing an AI.");
        Assert(state.Progress == 31 && rig.Energy == 989, "Intrinsic compute uses the configured value without an AI multiplier or extra work fee.");
        Cycle(2, rig.Machine);
        Assert(rig.Output.childItems.Single().identifier == "age_of_agi.btc_normal" && rig.Energy == 978 && !rig.Dead,
            "BTC completes through the normal production transaction.");

        Reset(); rig = new Rig(); Put(rig.Input, DirectoryMaster.Item(ServerProduction.FolderEmptyId));
        Cycle(1, rig.Machine);
        Assert(rig.Output.childItems.Count == 0 && rig.Energy == 989, "Life support still pays when intelligence lacks network permissions.");
        var network = Switch(); Install(network, 2); Install(network, 3); Put(rig.Network, network);
        Assert(ReferenceEquals(ServerState.GetInstalledSwitch(rig.Machine), network), "Shared network lookup uses the brain's switch index.");
        Cycle(2, rig.Machine);
        Assert(rig.State().Progress == 31, "Intrinsic intrusion starts after the required switch upgrades are installed.");
        for (int day = 3; rig.Output.childItems.Count == 0 && day <= 20; day++)
        {
            if (rig.Blood.childItem == null) Put(rig.Blood, Blood(true));
            Cycle(day, rig.Machine);
        }
        Assert(rig.Output.childItems.Single().identifier == "age_of_agi.folder_security" && !rig.Dead,
            "Intrinsic intrusion delivers intelligence after enough effective work days.");

        Reset(); rig = new Rig(); Put(rig.Input, DirectoryMaster.Item(ServerProduction.NormalInputId));
        Cycle(1, rig.Machine);
        rig.Output.AfterAccept = _ => rig.Battery.childItem.Owned = false;
        Cycle(2, rig.Machine);
        Assert(rig.Output.childItems.Count == 0 && rig.Input.childItems.Count == 1 && rig.State().Progress == 31 && rig.Energy == 978,
            "Interrupted output insertion rolls back the product while retaining the day's unconditional life-support charge.");

        Reset(); rig = new Rig();
        var source = DirectoryMaster.Item("age_of_agi.dc_military_terminal"); DataContentState.SetAmount(source, 2);
        var destination = DirectoryMaster.Item("age_of_agi.data_container");
        Put(rig.Data, destination); Put(rig.Input, source); Cycle(1, rig.Machine);
        Assert(DataContentState.Amount(destination) == 2 && DataContentState.Amount(source) == 0 && !source.Destroyed,
            "Intrinsic intrusion extracts military data locally and preserves its depleted shell.");

        Reset(); rig = new Rig();
        var seed = new GameItem { Id = "fixture_seed" }; seed.EnableTag("SEED_ITEM_TAG");
        seed.EnableTag("TEST_VALID_SEED");
        seed.ModifyTag("TEST_COMPLEXITY", tag => tag.Enable().SetInt(0));
        seed.ModifyTag("GENETIC_HEAT_INT", tag => tag.Enable().SetInt(3));
        seed.ModifyTag("PLANT_TRAIT_growth", tag => tag.Enable().SetInt(2));
        Put(rig.Input, seed); Put(rig.Input, DirectoryMaster.Item("age_of_agi.complex_data"));
        Assert(rig.Row.GetPanel("production") == null && rig.Row.Panels.Length == 7,
            "Brain has only its seven requested bays; the gene selection interface remains deferred.");
        Cycle(1, rig.Machine);
        Assert(rig.Output.childItems.Count == 0 && !seed.Destroyed && rig.Input.childItems.Count == 2 && rig.Energy == 989,
            "No gene is silently selected while its interface is deferred; life support continues normally.");
    }

    private static void CheckNetwork()
    {
        Reset(); var brain = new Rig(); var terminal = DirectoryMaster.Item(DistributedTerminalState.ItemId); Place(terminal);
        var row = terminal.contentWindow.childElement;
        var battery = (GameSlotInventory)row.GetStorage(0); var network = (GameSlotInventory)row.GetStorage(1);
        var model = (GameSlotInventory)row.GetStorage(2); var input = (GameGridInventory)row.GetStorage(3);
        Put(battery, Power()); Put(network, Switch()); Put(brain.Network, Switch());
        NetworkSwitchLinks.Pair(brain.Network.childItem, network.childItem, PlayerStore.Instance.FindAllItem());
        var ai = DirectoryMaster.Item(AiModelState.TrainedId); AiModelState.ApplyTrained(ai, AiTaskTypes.Computing, 2, 100, 5); Put(model, ai);
        Put(input, DirectoryMaster.Item(ServerProduction.RareInputId)); Cycle(1, terminal, brain.Machine);
        var state = JsonConvert.DeserializeObject<ServerProductionState>(terminal.GetTagReadonly(ServerProduction.StateTag).GetString());
        Assert(state.Progress == 62 && brain.Energy == 989 &&
            BloodSupply.Amount(brain.Blood.childItem) == BloodSupply.Capacity(brain.Blood.childItem) - BloodSupply.DailyUse(brain.Blood.childItem),
            "Network receives intrinsic base compute, applies terminal AI and pays brain life support once.");
        Put(brain.Input, DirectoryMaster.Item(ServerProduction.RareInputId)); Cycle(2, brain.Machine, terminal);
        state = JsonConvert.DeserializeObject<ServerProductionState>(terminal.GetTagReadonly(ServerProduction.StateTag).GetString());
        Assert(brain.State().Progress == 31 && state.Progress == 62 && brain.Energy == 978,
            "A local task takes priority over contributing brain compute to a terminal.");
        brain.Water.Remove(brain.Water.childItem); Cycle(3, terminal, brain.Machine);
        Assert(brain.Dead && !brain.Input.childItems.Single().IsTag("LOCKED_TAG") && !brain.Network.IsRemoveLocked(),
            "Death stops contribution and releases locked inputs and the switch.");
    }

    private static void CheckRestoreAndLifecycle()
    {
        Reset(); var rig = new Rig(); Put(rig.Input, DirectoryMaster.Item(ServerProduction.RareInputId)); Cycle(1, rig.Machine);
        var restored = Restore(rig);
        restored.Machine.onCycleEndSlotItemFunc(restored.Machine, null, null);
        Assert(restored.State().Progress == 31 && restored.Energy == 989 &&
            BloodSupply.Amount(restored.Blood.childItem) == BloodSupply.Capacity(restored.Blood.childItem) - BloodSupply.DailyUse(restored.Blood.childItem),
            "Current-format load preserves work, daily guard and partially consumed blood.");
        Cycle(2, restored.Machine);
        Assert(restored.State().Progress == 62 && restored.Energy == 978, "Restored intrinsic production resumes normally.");
        DistributedNetwork.Shutdown(); Cycle(3, restored.Machine);
        Assert(restored.Energy == 978, "Disabling the mod stops its callbacks.");
        DistributedNetwork.Enable(); Cycle(4, restored.Machine);
        Assert(restored.Energy == 967, "Existing instances resume on enable.");
        restored.Water.Remove(restored.Water.childItem); Cycle(5, restored.Machine);
        var dead = Restore(restored); Cycle(6, dead.Machine);
        Assert(dead.Dead && dead.Machine.spritePath == "brain_server_dead" && dead.Energy == 967, "Dead state survives full reconstruction.");
    }

    private static Rig Restore(Rig original)
    {
        var next = new Rig(install: false); next.Machine.uniqueId = original.Machine.uniqueId;
        foreach (var pair in original.Machine.modifiedState.dict)
            next.Machine.ModifyTag(pair.Key, tag => { tag.Enabled = pair.Value.Enabled; tag.Value = pair.Value.Value;
                tag.FloatValue = pair.Value.FloatValue; tag.LongValue = pair.Value.LongValue; tag.DoubleValue = pair.Value.DoubleValue; tag.StringValue = pair.Value.StringValue; });
        for (int index = 0; index < 7; index++)
            foreach (var item in ((GameInventory)original.Row.GetStorage(index)).childItems)
                ((GameInventory)next.Row.GetStorage(index)).UncheckedAccept(item);
        original.Machine.Destroy(); next.Machine.onLoaded(next.Machine); return next;
    }

    private sealed class Rig
    {
        internal readonly GameItem Machine;
        internal MachineryPanelRow Row => Machine.contentWindow.childElement;
        internal GameSlotInventory Battery => (GameSlotInventory)Row.GetStorage(0);
        internal GameSlotInventory Blood => (GameSlotInventory)Row.GetStorage(1);
        internal GameSlotInventory Water => (GameSlotInventory)Row.GetStorage(2);
        internal GameSlotInventory Network => (GameSlotInventory)Row.GetStorage(3);
        internal GameGridInventory Input => (GameGridInventory)Row.GetStorage(4);
        internal GameSlotInventory Data => (GameSlotInventory)Row.GetStorage(5);
        internal GameGridInventory Output => (GameGridInventory)Row.GetStorage(6);
        internal int Energy => PowerHelper.GetAvailableEnergyFromItem(Battery.childItem);
        internal bool Dead => Machine.IsTag(BrainServerLifeSupport.DeadTag);
        internal ServerProductionState State() => JsonConvert.DeserializeObject<ServerProductionState>(Machine.GetTagReadonly(ServerProduction.StateTag).GetString());
        internal Rig(bool advanced = true, bool install = true)
        {
            Machine = DirectoryMaster.Item(BrainServerLifeSupport.ItemId); Place(Machine); Machine.GetUniqueID();
            if (!install) return;
            Put(Battery, Power()); Put(Blood, BrainServerChecks.Blood(advanced));
            var water = new GameItem { Id = "fixture_water" }; water.EnableTag("LIQUID_CONTAINER_TAG");
            water.ModifyTag("TEST_WATER_ML", tag => tag.Enable().SetInt(2000));
            water.ModifyTag("TEST_REAL_PURITY", tag => tag.Enable().SetInt(10000)); Put(Water, water);
        }
    }

    private static GameItem Blood(bool advanced)
    {
        var item = new GameItem { Id = advanced ? "blue_blood_bag" : "blood_bag" }; BloodSupplyCreationPatch.Postfix(item); return item;
    }
    private static GameItem Power()
    {
        var item = new GameItem { Id = "fixture_power" }; item.EnableTag("power_source_item");
        item.ModifyTag("power_source_item_energy", tag => tag.Enable().SetInt(1000)); return item;
    }
    private static GameItem Switch()
    {
        var item = DirectoryMaster.Item(NetworkSwitchUpgrades.SwitchId); Install(item, 1); return item;
    }
    private static void Install(GameItem target, int index)
    {
        var module = DirectoryMaster.Item(NetworkSwitchUpgrades.ModuleIdPrefix + index); module.onThisTargetItemFunc(module, target);
    }
    private static void Cycle(int day, params GameItem[] machines)
    {
        StoreStation.Instance.dayCounter = day;
        foreach (var machine in machines) machine.onCycleEndEarlySlotItemFunc(machine, null, null);
        foreach (var machine in machines) machine.onCycleEndSlotItemFunc(machine, null, null);
    }
    private static void Place(GameItem item) => Put(EmporiumEntry.Instance.backInvinvElement, item);
    private static void Put(GameInventory inventory, GameItem item) => Assert(GraphUtils.TryAcceptAll(inventory, item) == 0, "Fixture placement: " + item.identifier);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
