using AgeOfAgi.Items.Logic.DataCollector;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.DistributedTerminal;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.Server;
using Newtonsoft.Json;

internal static class DistributedTerminalChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        LoadedMod loaded = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(loaded.Manifest);
        registration.Register(new ModItemDirectory(), definitions);
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        try
        {
            CheckLinks();
            CheckTerminalTooltips();
            CheckAggregateAndDurability();
            CheckBatchDispatch();
            CheckPriorityAndPower();
            CheckStorageAndTraining();
            CheckTransactionsAndRestore();
            CheckLifecycle();
            Console.WriteLine("PASS: symmetric switch pairing, network cycles, raw hardware aggregation, terminal AI wear, local/exclusive scheduling, pooled data, transactions and current-state restore.");
        }
        finally
        {
            registration.Unregister(); ModHelper.Translations.Clear(); ModHelper.Locale = "en";
            EmporiumEntry.Instance = new EmporiumEntry();
        }
    }

    private static void Reset()
    {
        PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry();
        NetworkSwitchLinks.Enable(); DistributedNetwork.Enable(); StoreStation.Instance.dayCounter = 0;
    }

    private static void CheckLinks()
    {
        Reset();
        GameItem first = Switch(), second = Switch(false), third = Switch();
        Assert(!first.canThisTargetItemFunc(first, second), "Both switches require high speed.");
        Install(second, 1);
        first.onThisTargetItemFunc(first, second);
        second.onThisTargetItemFunc(second, third);
        third.onThisTargetItemFunc(third, first);
        first.onThisTargetItemFunc(first, second);
        Assert(NetworkSwitchLinks.Number(first) > 0 && new[] { first, second, third }.Select(NetworkSwitchLinks.Number).Distinct().Count() == 3,
            "Pairing assigns independent persistent numbers.");
        Assert(NetworkSwitchLinks.Partners(first).Count == 2 && NetworkSwitchLinks.Connected(first, PlayerStore.Instance.FindAllItem()).Count == 3,
            "Pairing is symmetric and cycles/duplicates do not duplicate nodes.");
        long number = NetworkSwitchLinks.Number(first);
        first.onLoaded?.Invoke(first);
        Assert(NetworkSwitchLinks.Number(first) == number && NetworkSwitchLinks.Partners(first).Count == 2, "Load preserves numbers and links.");
        var tooltip = new RichTextBuilder(); NetworkSwitchUpgrades.CreateTooltip(tooltip, first);
        Assert(tooltip.Lines.Any(line => line.Text == ModHelper.GetLocalized(ModConstants.ModId, "network_switch_number", number.ToString())),
            "Show the switch number in its property tab.");
        first.onActivateSlotItemFunc(first, null, null);
        Assert(NetworkSwitchLinks.Number(first) == number && NetworkSwitchLinks.Partners(first).Count == 0 &&
            !NetworkSwitchLinks.Partners(second).Contains(number) && !NetworkSwitchLinks.Partners(third).Contains(number),
            "Clear removes both sides while retaining the number.");
        second.Owned = false;
        Assert(!NetworkSwitchLinks.CanPair(first, second), "Unowned switches cannot pair.");
        second.Owned = true;
        first.onThisTargetItemFunc(first, second);
        GameItem copy = Switch(); copy.ModifyTag(NetworkSwitchLinks.NumberTag, tag => tag.Enable().SetLong(number));
        Assert(NetworkSwitchLinks.Connected(first, PlayerStore.Instance.FindAllItem()).Count == 0, "Copied duplicate numbers cannot provide phantom connectivity.");
    }

    private static void CheckTerminalTooltips()
    {
        Reset();
        var terminal = new Rig(true, false);
        Assert(!ServerMachineryTooltipPatch.Prefix(terminal.Machine) &&
            ServerMachineryTooltipPatch.Prefix(new Rig().Machine) && ServerMachineryTooltipPatch.Prefix(null),
            "Only terminals replace the native machinery tooltip; other machinery passes through.");
        LoadedMod loaded = ModLoader.Mods.Single(mod => mod.Manifest.ID == ModConstants.ModId);
        loaded.IsEnabled = false;
        Assert(ServerMachineryTooltipPatch.Prefix(terminal.Machine), "Disabling the mod restores native machinery tooltips.");
        loaded.IsEnabled = true;
        foreach (string locale in new[] { "zh", "en" })
        {
            ModHelper.Locale = locale;
            var tooltip = new RichTextBuilder();
            ServerTooltip.CreateTooltip(tooltip, terminal.Machine);
            string power = ModHelper.GetLocalized(ModConstants.ModId, "server_stat_power", MachineParameters.GetPowerPerCycle(terminal.Machine));
            string warning = LocHelper.GetLocalizedMechanic("mech_machinery_insufficient_power");
            Assert(tooltip.Lines.Count(line => line.Text == power) == 1 &&
                tooltip.Lines.Count(line => line.Text == warning) == 1 && tooltip.Lines.Last().Text == warning &&
                tooltip.Lines.Last().Color == RenderHandler.ColorPalette.LightRed,
                "An empty terminal shows one power row and one battery warning below all statistics.");
        }
        GameItem battery = new GameItem { Id = "test_power" };
        battery.EnableTag("power_source_item");
        PowerHelper.SetPowerSourceAt(battery, MachineParameters.GetPowerPerCycle(terminal.Machine));
        Put(terminal.Battery, battery);
        var powered = new RichTextBuilder();
        ServerTooltip.CreateTooltip(powered, terminal.Machine);
        Assert(!powered.Lines.Any(line => line.Text == LocHelper.GetLocalizedMechanic("mech_machinery_insufficient_power")),
            "A sufficient replacement battery clears the warning without requiring a model or a completed night.");
        ModHelper.Locale = "en";
    }

    private static void CheckAggregateAndDurability()
    {
        Reset();
        Rig a = new Rig(), b = new Rig(), terminal = new Rig(true);
        Pair(a, terminal); Pair(b, terminal);
        Model(a, 900, 9); Model(b, 700, 9); GameItem ai = Model(terminal, 50, 5);
        GpuTestState.SetReal(a.Gpu, compute: 1); GpuTestState.SetReal(b.Gpu, compute: 2);
        AiModelState.ApplyTrained(ai, AiTaskTypes.Computing, (int)(a.Hardware().MemoryGb + b.Hardware().MemoryGb), 50, 5);
        double raw = a.Hardware().ComputePerNight + b.Hardware().ComputePerNight;
        Put(terminal.Input, DirectoryMaster.Item(ServerProduction.RareInputId));
        Cycle(1, terminal, a, b);
        Near(terminal.State().Progress, raw * 1.5, "All recipe attempts share one raw aggregate compute budget per night");
        Near(AiModelState.GetCurrentDurability(ai), 5, "Intermediate work consumes no durability");
        Near(AiModelState.GetCurrentDurability(a.Model.childItem), 9, "Donor AI is unused");
        Near(Energy(a), 1000 - a.Hardware().Power, "A donor pays once per effective night, across multiple rounds");
        long memory = a.Hardware().MemoryGb + b.Hardware().MemoryGb;
        Assert(terminal.Machine.GetTagReadonly(ServerState.MemoryCapacityTag).GetLong() == memory, "Aggregate memory is displayed.");
        float progress = terminal.State().Progress;
        terminal.Machine.onCycleEndSlotItemFunc(terminal.Machine, null, null);
        Near(terminal.State().Progress, progress, "Repeated main callbacks cannot repeat the cycle");

        Reset(); a = new Rig(); b = new Rig(); terminal = new Rig(true);
        Pair(a, terminal); Pair(b, terminal);
        GpuTestState.SetReal(a.Gpu, compute: 1000); GpuTestState.SetReal(b.Gpu, compute: 1000);
        ai = Model(terminal, 50, 2);
        Put(terminal.Input, DirectoryMaster.Item(ServerProduction.NormalInputId).SetAmount(3));
        Cycle(1, b, terminal, a);
        Assert(terminal.Output.childItems.Count == 2 && ai.Destroyed && terminal.Model.childItem == null && terminal.Input.childItems.Single().unitCount == 1,
            "Two donors allow two batches; the final AI use delivers before model destruction.");
        Assert(Energy(terminal) == 1000 - MachineParameters.GetPowerPerCycle(terminal.Machine), "Terminal daily power is paid once for all rounds.");
    }

    private static void CheckBatchDispatch()
    {
        Reset();
        var a = new Rig(); var b = new Rig(); var terminal = new Rig(true);
        Pair(a, terminal); Pair(b, terminal);
        GpuTestState.SetReal(a.Gpu, compute: 10000); GpuTestState.SetReal(b.Gpu, compute: 10000);
        var ai = Model(terminal, 20, 10);
        AiModelState.ApplyTrained(ai, AiTaskTypes.Computing, 2, 20, 10, AiModelTraits.Batch, 3);
        Put(terminal.Input, DirectoryMaster.Item(ServerProduction.NormalInputId).SetAmount(8));
        Cycle(1, terminal, a, b);
        Assert(terminal.Output.childItems.Count == 6 && terminal.Input.childItems.Single().unitCount == 2 &&
            AiModelState.GetCurrentDurability(ai) == 4, "Two supplying servers times batch size three permits six recipes.");
        Near(Energy(a), 1000 - a.Hardware().Power, "Batch dispatch pays donor A once");
        Near(Energy(b), 1000 - b.Hardware().Power, "Batch dispatch pays donor B once");
        Near(Energy(terminal), 1000 - MachineParameters.GetPowerPerCycle(terminal.Machine), "Batch dispatch pays terminal once");

        Reset(); a = new Rig(); b = new Rig(); terminal = new Rig(true);
        Pair(a, terminal); Pair(b, terminal); ai = Model(terminal, 20, 10);
        AiModelState.ApplyTrained(ai, AiTaskTypes.Computing, 2, 20, 10, AiModelTraits.Batch, 3);
        Assert(EncryptedItemCatalog.Load().TryGet(ServerProduction.NormalInputId, out var recipe), "Independent fixture recipe.");
        double multiplier = a.Hardware().CpuMultiplier;
        double perGpu = recipe.Work * 2.5 / (2 * multiplier * 1.2);
        GpuTestState.SetReal(a.Gpu, compute: perGpu); GpuTestState.SetReal(b.Gpu, compute: perGpu);
        Put(terminal.Input, DirectoryMaster.Item(ServerProduction.NormalInputId).SetAmount(8));
        Cycle(1, terminal, b, a);
        Assert(terminal.Output.childItems.Count == 2, "Batch limit does not multiply aggregate nightly compute.");
        Near(terminal.State().Progress, recipe.Work * .5, "Terminal carries the shared budget tail into its next batch");
    }

    private static void CheckPriorityAndPower()
    {
        Reset();
        var donor = new Rig(); var first = new Rig(true); var second = new Rig(true);
        Pair(donor, first); Pair(first, second);
        Model(first, 20, 10); Model(second, 20, 10);
        Put(first.Input, DirectoryMaster.Item(ServerProduction.RareInputId)); Put(second.Input, DirectoryMaster.Item(ServerProduction.RareInputId));
        Cycle(1, second, donor, first);
        Assert(first.State().Progress > 0 && second.State().Progress == 0, "Lower terminal number wins regardless of callback order.");
        GameItem localAi = Model(donor, 30, 8); Put(donor.Input, DirectoryMaster.Item(ServerProduction.RareInputId));
        float previous = first.State().Progress;
        Cycle(2, first, donor, second);
        Assert(donor.State().Progress > 0 && first.State().Progress == previous, "Ready local production has priority over donation.");
        float local = donor.State().Progress;
        Cycle(3, second, donor, first);
        Assert(donor.State().Progress > local && first.State().Progress == previous, "Running local production also retains priority.");
        donor.Input.Remove(donor.Input.childItems.Single()); // frozen task becomes paused, freeing its hardware
        donor.Battery.childItem.ModifyTag("power_source_item_energy", tag => tag.SetInt(0));
        Cycle(4, first, donor, second);
        Assert(first.State().Progress == previous, "An underpowered donor contributes neither memory nor compute.");
        donor.Battery.childItem.ModifyTag("power_source_item_energy", tag => tag.SetInt(1000));
        Cycle(5, donor, second, first);
        Assert(first.State().Progress > previous, "Paused local tasks can lend available hardware.");
        Near(AiModelState.GetCurrentDurability(localAi), 8, "Neither incomplete local work nor donation wears the local AI.");

        Reset(); donor = new Rig(); first = new Rig(true); Pair(donor, first); Model(first, 20, 5);
        Put(first.Input, DirectoryMaster.Item(ServerProduction.NormalInputId));
        var blocker = new GameItem(); blocker.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1, 16).ToArray(), 4));
        first.Output.UncheckedAccept(blocker);
        Cycle(1, first, donor);
        Assert(Energy(donor) == 1000 && first.State().Progress == 0, "Full output pauses work without donor fees.");
        Assert(Energy(first) < 1000, "The terminal still has its configured daily draw while paused.");
        first.Battery.childItem.ModifyTag("power_source_item_energy", tag => tag.SetInt(0));
        first.Output.Remove(blocker); Cycle(2, donor, first);
        Assert(first.State().Progress == 0 && Energy(donor) == 1000, "Terminal power is required before dispatch.");
    }

    private static void CheckStorageAndTraining()
    {
        Reset();
        var donor = new Rig(); var terminal = new Rig(true); Pair(donor, terminal);
        GpuTestState.SetReal(donor.Gpu, compute: 1000); Model(terminal, 20, 4);
        GameItem collector = DirectoryMaster.Item(DataCollectorState.CollectorId); Place(collector);
        var row = collector.contentWindow.childElement;
        GameItem net = Switch(); Put((GameInventory)row.GetStorage(DataCollectorState.NetworkIndex), net);
        terminal.Network.childItem.onThisTargetItemFunc(terminal.Network.childItem, net);
        GameItem remote = Container(1), local = Container(0);
        Put((GameInventory)row.GetStorage(DataCollectorState.ContainerIndex), remote); Put(terminal.Data, local);
        DataContentState.SetAmount(remote, DataContentState.Capacity(remote) - 2);
        GameItem source = DirectoryMaster.Item("age_of_agi.dc_common_data_disk");
        DataContentState.SetAmount(source, 8); Put(terminal.Input, source);
        Cycle(1, terminal, donor);
        Assert(DataContentState.Amount(remote) == DataContentState.Capacity(remote) && DataContentState.Amount(local) == 6 && DataContentState.Amount(source) == 0,
            "Extraction fills remote storage first, then the local container, preserving the depleted source shell.");
        DataContentState.SetAmount(local, DataContentState.Capacity(local));
        DataContentState.SetAmount(source, 4); Cycle(2, donor, terminal);
        Assert(DataContentState.Amount(source) == 4 && AiModelState.GetCurrentDurability(terminal.Model.childItem) == 3,
            "A full aggregate destination pauses without AI wear.");
        DataContentState.SetAmount(remote, DataContentState.Capacity(remote) - 1); Cycle(3, terminal, donor);
        Assert(DataContentState.Amount(source) == 3 && DataContentState.Amount(remote) == DataContentState.Capacity(remote),
            "A tail batch is capped by aggregate free space like a standalone server.");

        Reset(); donor = new Rig(); terminal = new Rig(true); Pair(donor, terminal);
        collector = DirectoryMaster.Item(DataCollectorState.CollectorId); Place(collector); row = collector.contentWindow.childElement;
        net = Switch(); Put((GameInventory)row.GetStorage(DataCollectorState.NetworkIndex), net);
        terminal.Network.childItem.onThisTargetItemFunc(terminal.Network.childItem, net);
        remote = Container(6); local = Container(3);
        Put((GameInventory)row.GetStorage(DataCollectorState.ContainerIndex), remote); Put(terminal.Data, local);
        GameItem untrained = DirectoryMaster.Item("age_of_agi.ai_untrained");
        untrained.ModifyTag(AiModelState.VersionTag, tag => tag.Enable().SetInt(2));
        untrained.ModifyTag(AiModelState.TaskTag, tag => tag.Enable().SetString(AiTaskTypes.Computing));
        untrained.ModifyTag(AiModelState.TrainingTag, tag => tag.Enable().SetFloat(10));
        untrained.ModifyTag(AiModelState.TrainingDataTag, tag => tag.Enable().SetInt(9));
        untrained.ModifyTag(AiModelState.MemoryTag, tag => tag.Enable().SetInt(2));
        untrained.ModifyTag(AiModelState.PredictedComputeTag, tag => tag.Enable().SetFloat(40));
        untrained.ModifyTag(AiModelState.PredictedDurabilityTag, tag => tag.Enable().SetFloat(7));
        Put(terminal.Model, untrained); Cycle(1, donor, terminal);
        Assert(untrained.Destroyed && terminal.Output.childItems.Single().identifier == AiModelState.TrainedId &&
            DataContentState.Amount(remote) == 0 && DataContentState.Amount(local) == 0,
            "Untrained models use aggregate compute and pooled data with exact training fees.");
        Near(AiModelState.GetCurrentDurability(terminal.Output.childItems.Single()), 7, "Training inherits durability without production wear");
    }

    private static void CheckTransactionsAndRestore()
    {
        Reset();
        var donor = new Rig(); var terminal = new Rig(true); Pair(donor, terminal); Model(terminal, 20, 5);
        Put(terminal.Input, DirectoryMaster.Item(ServerProduction.RareInputId));
        Cycle(1, terminal, donor);
        float progress = terminal.State().Progress;
        var restored = new Rig(true, install: false); restored.Machine.uniqueId = terminal.Machine.uniqueId;
        foreach (var pair in terminal.Machine.modifiedState.dict)
            restored.Machine.ModifyTag(pair.Key, tag => { tag.Enabled = pair.Value.Enabled; tag.Value = pair.Value.Value;
                tag.FloatValue = pair.Value.FloatValue; tag.LongValue = pair.Value.LongValue; tag.DoubleValue = pair.Value.DoubleValue; tag.StringValue = pair.Value.StringValue; });
        for (int index = 0; index < 6; index++)
            foreach (GameItem item in ((GameInventory)terminal.Row.GetStorage(index)).childItems)
                ((GameInventory)restored.Row.GetStorage(index)).UncheckedAccept(item);
        terminal.Machine.Destroy(); restored.Machine.onLoaded(restored.Machine);
        Cycle(2, donor, restored);
        Assert(restored.State().Progress > progress && restored.State().ModelId == restored.Model.childItem.uniqueId,
            "Current-format restore retains frozen identities and paid progress.");

        Reset(); donor = new Rig(); terminal = new Rig(true); Pair(donor, terminal); Model(terminal, 20, 5);
        GpuTestState.SetReal(donor.Gpu, compute: 1000); Put(terminal.Input, DirectoryMaster.Item(ServerProduction.NormalInputId));
        terminal.Output.AfterAccept = _ => NetworkSwitchLinks.Clear(donor.Network.childItem, PlayerStore.Instance.FindAllItem());
        Cycle(1, terminal, donor);
        Assert(terminal.Output.childItems.Count == 0 && terminal.Input.childItems.Count == 1 && Energy(donor) == 1000 &&
            AiModelState.GetCurrentDurability(terminal.Model.childItem) == 5, "A network change during output insertion rolls back delivery without donor fees or wear.");
        Pair(donor, terminal); terminal.Output.AfterAccept = null;
        Cycle(2, donor, terminal);
        Assert(terminal.Output.childItems.Count == 1, "A restored network resumes the paused transaction.");
    }

    private static void CheckLifecycle()
    {
        Reset(); var donor = new Rig(); var terminal = new Rig(true); Pair(donor, terminal); Model(terminal, 20, 5);
        Put(terminal.Input, DirectoryMaster.Item(ServerProduction.RareInputId));
        DistributedNetwork.Shutdown(); NetworkSwitchLinks.Shutdown(); Cycle(1, terminal, donor);
        Assert(Energy(terminal) == 1000 && !NetworkSwitchLinks.CanPair(terminal.Network.childItem, donor.Network.childItem), "Disable suspends dispatch and pairing.");
        DistributedNetwork.Enable(); NetworkSwitchLinks.Enable(); Cycle(2, donor, terminal);
        Assert(terminal.State().Progress > 0, "Re-enable works with existing machine instances.");
    }

    private sealed class Rig
    {
        internal readonly GameItem Machine;
        internal readonly bool Terminal;
        internal MachineryPanelRow Row => Machine.contentWindow.childElement;
        internal GameSlotInventory Battery => (GameSlotInventory)Row.GetStorage(0);
        internal GameSlotInventory Network => (GameSlotInventory)Row.GetStorage(Terminal ? 1 : 3);
        internal GameSlotInventory Model => (GameSlotInventory)Row.GetStorage(Terminal ? 2 : 5);
        internal GameGridInventory Input => (GameGridInventory)Row.GetStorage(Terminal ? 3 : 6);
        internal GameSlotInventory Data => (GameSlotInventory)Row.GetStorage(Terminal ? 4 : 8);
        internal GameGridInventory Output => (GameGridInventory)Row.GetStorage(Terminal ? 5 : 7);
        internal GameItem Gpu => ((GameGridInventory)Row.GetStorage(2)).childItems.Single();
        internal Rig(bool terminal = false, bool install = true)
        {
            Terminal = terminal; Machine = DirectoryMaster.Item(terminal ? DistributedTerminalState.ItemId : ServerCatalog.ServerId);
            Place(Machine); Machine.GetUniqueID();
            if (!install) return;
            GameItem power = new GameItem { Id = "test_power" }; power.EnableTag("power_source_item");
            power.ModifyTag("power_source_item_energy", tag => tag.Enable().SetInt(1000)); Put(Battery, power);
            Put(Network, Switch());
            if (!terminal)
            {
                Put((GameInventory)Row.GetStorage(1), DirectoryMaster.Item("age_of_agi.cpu0"));
                Put((GameInventory)Row.GetStorage(2), DirectoryMaster.Item("age_of_agi.gpu0"));
                Put((GameInventory)Row.GetStorage(4), DirectoryMaster.Item("age_of_agi.mem0"));
            }
        }
        internal ServerHardware Hardware()
        {
            Assert(ServerHardware.TryRead((GameGridInventory)Row.GetStorage(1), (GameGridInventory)Row.GetStorage(2),
                (GameGridInventory)Row.GetStorage(4), Network, ServerCatalog.Load().Definition, out var hardware), "Fixture hardware must be valid.");
            return hardware;
        }
        internal ServerProductionState State() => JsonConvert.DeserializeObject<ServerProductionState>(Machine.GetTagReadonly(ServerProduction.StateTag).GetString());
    }

    private static GameItem Model(Rig rig, float bonus, int durability)
    {
        var ai = DirectoryMaster.Item(AiModelState.TrainedId); AiModelState.ApplyTrained(ai, AiTaskTypes.Computing, 2, bonus, durability);
        Put(rig.Model, ai); return ai;
    }
    private static GameItem Container(int amount)
    {
        var item = DirectoryMaster.Item("age_of_agi.data_container"); DataContentState.SetAmount(item, amount); return item;
    }
    private static GameItem Switch(bool upgraded = true)
    {
        var item = DirectoryMaster.Item(NetworkSwitchUpgrades.SwitchId); Place(item);
        if (upgraded) Install(item, 1); return item;
    }
    private static void Install(GameItem target, int index)
    {
        var module = DirectoryMaster.Item(NetworkSwitchUpgrades.ModuleIdPrefix + index); module.onThisTargetItemFunc(module, target);
    }
    private static void Pair(Rig a, Rig b) => a.Network.childItem.onThisTargetItemFunc(a.Network.childItem, b.Network.childItem);
    private static int Energy(Rig rig) => rig.Battery.childItem.GetTagReadonly("power_source_item_energy").GetInt();
    private static void Cycle(int day, params Rig[] order)
    {
        StoreStation.Instance.dayCounter = day;
        foreach (var rig in order) rig.Machine.onCycleEndEarlySlotItemFunc(rig.Machine, null, null);
        foreach (var rig in order) rig.Machine.onCycleEndSlotItemFunc(rig.Machine, null, null);
    }
    private static void Place(GameItem item) => Put(EmporiumEntry.Instance.backInvinvElement, item);
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
        Assert(GraphUtils.TryAcceptAll(inventory, item) == 0, "Fixture placement failed: " + item.identifier);
    }
    private static void Near(double actual, double expected, string message) => Assert(Math.Abs(actual - expected) < .0001, message + ": " + actual + " != " + expected);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
