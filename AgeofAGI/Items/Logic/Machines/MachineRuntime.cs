using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.Memory;
using AgeOfAgi.Items.Logic.Cpu;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.Server;
using AgeOfAgi.Items.Logic.DistributedTerminal;
using AgeOfAgi.Items.Logic.BrainServer;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using Newtonsoft.Json;

using static AgeOfAgi.Items.Logic.Server.ServerState;

namespace AgeOfAgi.Items.Logic.Machines
{
    internal sealed class MachineRuntime
    {
        private readonly GameItem machine;
        private readonly MachineDefinition parameters;
        private readonly ServerDefinition definition;
        private readonly bool distributed;
        private readonly bool brain;
        private readonly GameSlotInventory blood, water;
        private readonly BrainServerLifeSupport lifeSupport;
        private bool Producing => brain || AiModelState.IsTrained(model.childItem);
        private readonly GameSlotInventory battery, network, model, dataContainer;
        private readonly GameGridInventory cpu, gpu, memory, input, output;
        private readonly PixelWindow window;
        private readonly DistributedTerminalRuntime terminal;
        private readonly ServerProduction production;
        private readonly GameInventory[] locked;
        private DistributedEndpoint endpoint;
        private ServerTrainingState state;
        private bool stateFault, busy, refreshing;
        private int cpuWidth = 1, cpuHeight = 1, gpuWidth = 2, memoryHeight = 2;
        private string spriteKey;
        private GameItem pendingOutput, progressItem;

        internal MachineRuntime(GameItem machine, MachineDefinition parameters, ServerDefinition definition,
            EncryptedItemCatalog encrypted, MachineWorkMode mode, BrainServerDefinition brainDefinition = null)
        {
            this.machine = machine;
            this.parameters = parameters;
            this.definition = definition;
            distributed = mode == MachineWorkMode.DistributedTerminal;
            brain = mode == MachineWorkMode.BrainServer;

            battery = new GameSlotInventory(1, 2);
            cpu = new GameGridInventory(1, 1);
            gpu = new GameGridInventory(2, 6);
            network = new GameSlotInventory(2, 2);
            memory = new GameGridInventory(4, 2);
            model = new GameSlotInventory(1, 1);
            input = new GameGridInventory(4, 4);
            output = new GameGridInventory(4, 4);
            dataContainer = new GameSlotInventory(1, 3);
            if (brain)
            {
                blood = new GameSlotInventory(2, 3);
                water = new GameSlotInventory(4, 4);
                lifeSupport = new BrainServerLifeSupport(machine, battery, blood, water, parameters.PowerPerCycle, brainDefinition);
            }
            terminal = distributed ? new DistributedTerminalRuntime(machine, network, dataContainer) : null;
            production = new ServerProduction(machine, definition, encrypted, battery, cpu, gpu, network, memory, model, input, output, dataContainer, brain);
            production.ReadHardware = ReadHardware;
            production.ReadData = ReadData;
            if (distributed)
            {
                MachineryHelper.InitMachinery(machine, parameters.PowerPerCycle);
                production.AddNetworkPower = terminal.AddPower;
                production.WorkCommitted = terminal.Committed;
                production.RequiredPower = cost => terminal.Dispatching ? cost : parameters.PowerPerCycle;
                production.BatchMultiplier = () => terminal.Dispatching ? terminal.Count : 1;
            }

            // Native CollectStorage traverses these panels in this exact order.
            // Keep the original eight nodes stable; append the data container as node 8.
            // Saves written with the retired cooler bay do not migrate.
            window = brain ? global::MachineHelper.CreateMachineryWindow(true,
                global::MachineHelper.PowerPanel(battery), Panel("blood", blood), Panel("water", water),
                Panel("network", network), Panel("input", input), Panel("data_container", dataContainer), Panel("output", output)) :
                distributed ? global::MachineHelper.CreateMachineryWindow(true,
                global::MachineHelper.PowerPanel(battery), Panel("network", network), Panel("model", model),
                Panel("input", input), Panel("data_container", dataContainer), Panel("output", output), production.GenePanel) :
                global::MachineHelper.CreateMachineryWindow(true,
                new[] { 0, 1, 2, 4, 3, 5, 6, 9, 7, 8 },
                global::MachineHelper.PowerPanel(battery),
                Panel("cpu", cpu), Panel("gpu", gpu), Panel("network", network),
                Panel("memory", memory), Panel("model", model),
                Panel("input", input), Panel("output", output), production.GenePanel, Panel("data_container", dataContainer));
            machine.SetContentWindow(window);
            if (production.GeneSelector != null) window.closeCallback += production.GeneSelector.Close;
            RestoreStructure(machine);
            global::MachineHelper.SetupBatterySlot(battery, machine);
            locked = new GameInventory[] { cpu, gpu, network, memory, model };
        }

        internal void Bind()
        {
            if (!distributed && !brain) ServerUpgrades.Bind(machine, definition);

            if (brain)
            {
                blood.mayInventoryAddItemFunc = (item, _) => BloodSupply.CanConsume(item);
                water.mayInventoryAddItemFunc = (item, _) => item != null && item.unitCount == 1 &&
                    item.IsTag("LIQUID_CONTAINER_TAG") && GeneralHelper.IsItemOwned(item);
                blood.SetBackgroundFadeSprite("Items/items_medical", "blood_bag");
                water.SetBackgroundFadeSprite("Items/water_container4", "xl");
                foreach (GameInventory supply in new[] { blood, water })
                {
                    supply.slotPriorityFunc = (_, __, ___) => 2;
                    supply.onSlotAddItemEndFunc += (_, __, ___) => Refresh();
                    supply.onSlotRemoveItemEndFunc += (_, __, ___) => Refresh();
                }
            }

            bool AcceptUnit(GameItem item) => !machine.IsTag(ActiveTaskTag) && item != null &&
                item.unitCount == 1 && GeneralHelper.IsItemOwned(item);
            cpu.mayInventoryAddItemFunc = (item, _) => AcceptUnit(item) && CpuState.IsCpu(item);
            gpu.mayInventoryAddItemFunc = (item, _) => AcceptUnit(item) && GpuRuntime.IsGpu(item);
            memory.mayInventoryAddItemFunc = (item, _) => AcceptUnit(item) && MemoryState.IsMemory(item);
            model.mayInventoryAddItemFunc = (item, _) => AcceptUnit(item) && AiModelState.IsModel(item);
            input.mayInventoryAddItemFunc = (item, _) => item != null && GeneralHelper.IsItemOwned(item);
            network.mayInventoryAddItemFunc = (item, _) => AcceptUnit(item) && NetworkSwitchUpgrades.IsNetworkSwitch(item);
            // An extraction destination may be replaced even while the batch is active.
            dataContainer.mayInventoryAddItemFunc = (item, _) => DataContentState.IsContainer(item) &&
                DataContentState.Valid(item) && GeneralHelper.IsItemOwned(item);
            output.mayInventoryAddItemFunc = (item, _) => (pendingOutput != null && ReferenceEquals(item, pendingOutput)) ||
                (production.PendingOutput != null && ReferenceEquals(item, production.PendingOutput));

            // Native MachineryPanelRow chooses the last valid bay at equal priority.
            // Prefer dedicated bays over the general input, as native machines do.
            foreach (GameInventory inventory in new GameInventory[] { battery, cpu, gpu, network, memory, model, dataContainer })
                inventory.slotPriorityFunc = (_, __, ___) => 2;
            input.slotPriorityFunc = (_, __, ___) => 1;

            production.StateChanged = SaveState;
            if (production.GeneSelector != null) production.GeneSelector.SelectionChanged = Refresh;

            endpoint = new DistributedEndpoint
            {
                Machine = machine, Battery = battery, Network = network, Terminal = distributed,
                ReadHardware = () => brain ? lifeSupport.ReadHardware() :
                    ServerHardware.TryRead(cpu, gpu, memory, network, definition, out var hardware) ? hardware : null,
                CanWork = () => { SynchronizeModel(); return Conditions(out _, out _) && OutputFits(); },
                Run = RunWork, Refresh = Refresh
            };
            if (brain) endpoint.PrepareCycle = lifeSupport.RunCycle;
            DistributedNetwork.Register(endpoint, terminal);
            machine.onCycleEndEarlySlotItemFunc += (_, _, _) => { if (!busy && !refreshing) endpoint.CyclePending = true; };
            machine.onCycleEndSlotItemFunc = (_, _, _) => DistributedNetwork.RunCycle(endpoint);

            foreach (GameInventory inventory in new GameInventory[] { battery, cpu, gpu, network, memory, model, input, output, dataContainer })
            {
                inventory.onSlotAddItemEndFunc += (_, _, _) => Refresh();
                inventory.onSlotRemoveItemEndFunc += (_, _, _) => Refresh();
            }
            window.AddInventoryStateChangeAction(Refresh);
        }

        private ServerHardware ReadHardware() => brain ? lifeSupport.ReadHardware() : distributed ? terminal.ReadHardware() :
            ServerHardware.TryRead(cpu, gpu, memory, network, definition, out var hardware) ? hardware : null;

        private ServerDataPool ReadData() => distributed ? terminal.ReadData() : new ServerDataPool(() => dataContainer.childItems);

        private void SaveState()
        {
            machine.ModifyTag(TrainingStateTag, tag => tag.Enable().SetString(state == null ? "" : JsonConvert.SerializeObject(state)));
            bool dead = brain && lifeSupport.Dead;
            bool active = !dead && (stateFault || state?.Active == true || production.Active);
            if (active) machine.EnableTag(ActiveTaskTag);
            else machine.DisableTag(ActiveTaskTag);
            foreach (GameInventory inventory in locked)
            {
                if (active) inventory.LockInv();
                else inventory.UnlockInv();
            }
            // Keep queued inputs accessible; only the frozen batch source is locked.
            if (!stateFault && !production.HasFault)
            {
                GameItem processing = !dead && production.Active ? production.ProcessingItem : null;
                foreach (GameItem item in input.childItems)
                {
                    if (ReferenceEquals(item, processing))
                    {
                        if (!GeneralHelper.IsItemLocked(item)) item.EnableTag(ProcessingLockTag);
                        GeneralHelper.LockItem(item);
                    }
                    else if (item.IsTag(ProcessingLockTag))
                    {
                        GeneralHelper.UnlockItem(item);
                        item.DisableTag(ProcessingLockTag);
                    }
                }
            }
            GameItem nextProgressItem = null;
            string progressTextKey = "server_progress_tooltip";
            if (!dead && !stateFault && production.HasProgress && Producing)
            {
                machine.EnableTag(ProgressVisibleTag);
                machine.ModifyTag(ProgressCurrentTag, tag => tag.Enable().SetFloat(production.Progress));
                machine.ModifyTag(ProgressTargetTag, tag => tag.Enable().SetFloat(production.TotalWork));
                nextProgressItem = production.ProcessingItem;
                progressTextKey = production.ProgressTextKey;
            }
            else if (!stateFault && state != null && state.WorkNights > 0 && state.Matches(model.childItem))
            {
                machine.EnableTag(ProgressVisibleTag);
                machine.ModifyTag(ProgressCurrentTag, tag => tag.Enable().SetFloat(state.Progress));
                machine.ModifyTag(ProgressTargetTag, tag => tag.Enable().SetFloat(state.TotalWork));
                nextProgressItem = model.childItem;
            }
            else machine.DisableTag(ProgressVisibleTag);
            foreach (GameItem item in input.childItems.Concat(model.childItems))
            {
                if (item.IsTag(ProgressTextTag) && !ReferenceEquals(item, nextProgressItem))
                    item.DisableTag(ProgressVisibleTag);
            }
            if (progressItem != null && !ReferenceEquals(progressItem, nextProgressItem))
                progressItem.DisableTag(ProgressVisibleTag);
            progressItem = nextProgressItem;
            if (progressItem != null)
            {
                progressItem.EnableTag(ProgressVisibleTag);
                progressItem.ModifyTag(ProgressCurrentTag, tag => tag.Enable().SetFloat(machine.GetTagReadonly(ProgressCurrentTag).GetFloat()));
                progressItem.ModifyTag(ProgressTargetTag, tag => tag.Enable().SetFloat(machine.GetTagReadonly(ProgressTargetTag).GetFloat()));
                progressItem.ModifyTag(ProgressTextTag, tag => tag.Enable().SetString(progressTextKey));
            }
        }

        private void SynchronizeModel()
        {
            production.Synchronize();
            if (brain) return;
            if (stateFault || state?.Active == true) return;
            GameItem item = model.childItem;
            if (!AiModelState.CanTrain(item))
            {
                if (state != null) { state = null; SaveState(); }
                return;
            }
            if (state == null || !state.Matches(item))
            {
                state = ServerTrainingState.Create(item);
                SaveState();
            }
        }

        private bool Conditions(out ServerHardware hardware, out string reason)
        {
            hardware = null;
            reason = "server_invalid_state";
            if (stateFault) return false;
            if (brain && lifeSupport.Dead) { reason = "brain_dead"; return false; }
            if (Producing) return production.Conditions(out hardware, out reason);
            GameItem item = model.childItem;
            if (state == null || !state.Matches(item))
            {
                reason = item == null ? null : AiModelState.IsUntrained(item) ? "server_invalid_model" : "server_trained_idle";
                return false;
            }
            reason = "server_missing_hardware";
            hardware = ReadHardware();
            if (hardware == null) return false;
            reason = "server_insufficient_memory";
            if (hardware.MemoryGb < state.MemoryGb) return false;
            reason = "server_insufficient_training_data";
            ServerDataPool data = ReadData();
            float next = (float)Math.Min(state.TotalWork, state.Progress + hardware.ComputePerNight);
            if (!data.Present || data.Amount < state.DataForNight(next)) return false;
            reason = "server_insufficient_power";
            GameItem power = battery.childItem;
            int requiredPower = distributed && !terminal.Dispatching ? parameters.PowerPerCycle : hardware.Power;
            if (power == null || !GeneralHelper.IsItemOwned(power) || !PowerHelper.CanDrawPowerSource(power, requiredPower)) return false;
            if (item == null || !GeneralHelper.IsItemOwned(item) || !GeneralHelper.IsItemOwned(machine)) return false;
            reason = null;
            return true;
        }

        private GameItem CreateProduct()
        {
            GameItem product = DirectoryMaster.Item(definition.TrainedOutputId);
            try
            {
                AiModelState.ApplyTrained(product, state.TaskType, state.MemoryGb, state.ComputeBonus, state.Durability, state.Traits, state.BatchSize);
                return product;
            }
            catch
            {
                product.Destroy();
                throw;
            }
        }

        private bool OutputFits()
        {
            if (Producing) return production.OutputFits();
            GameItem probe = CreateProduct();
            try
            {
                pendingOutput = probe;
                return GraphUtils.CanAccept(output, probe);
            }
            finally
            {
                pendingOutput = null;
                probe.Destroy();
            }
        }

        internal void Refresh()
        {
            if (busy || refreshing) return;
            refreshing = true;
            try
            {
                int cpuUnits = Count(machine, CpuUnitsTag);
                int wantedCpuWidth = cpuUnits >= definition.CpuUpgradeUnitsStep1 ? 2 : 1;
                int wantedCpuHeight = cpuUnits >= definition.CpuUpgradeUnitsTotal ? 2 : 1;
                int gpuStage = (int)Math.Min(2, (long)Count(machine, GpuUnitsTag) / definition.GpuUpgradeUnitsPerStep);
                int wantedGpu = 2 + 2 * gpuStage;
                int wantedMemoryHeight = Count(machine, MemoryUnitsTag) >= definition.MemoryUpgradeUnits ? 4 : 2;
                bool resized = false;
                if (wantedCpuWidth != cpuWidth || wantedCpuHeight != cpuHeight)
                {
                    cpu.SetShape(wantedCpuWidth, wantedCpuHeight);
                    cpuWidth = wantedCpuWidth;
                    cpuHeight = wantedCpuHeight;
                    resized = true;
                }
                if (wantedGpu != gpuWidth) { gpu.SetShape(wantedGpu, 6); gpuWidth = wantedGpu; resized = true; }
                if (wantedMemoryHeight != memoryHeight) { memory.SetShape(4, wantedMemoryHeight); memoryHeight = wantedMemoryHeight; resized = true; }
                if (resized)
                {
                    // SetShape revalidates only the grid itself. Native upgrades
                    // (MachineRAS.ApplyTankSize) revalidate the window too, so an
                    // open UI lays out the new bay widths instead of misaligning.
                    machine.contentWindow?.Validate();
                }
                int gpuCells = 0;
                foreach (GameItem item in gpu.childItems)
                {
                    GridShape shape = item.modifiedShape;
                    for (int y = shape.minY; y < shape.minY + shape.globalHeight; y++)
                    {
                        for (int x = shape.minX; x < shape.minX + shape.globalWidth; x++)
                        {
                            if (shape.Get(x, y) != 0) gpuCells++;
                        }
                    }
                }
                string key = brain ? lifeSupport.Dead ? "brain_server_dead" : "brain_server" : distributed ? "distributed_terminal" : gpuCells > 12 ? "serverbox_stage2" :
                    gpu.childItems.Count > 0 ? "serverbox_stage1" : "serverbox_stage0";
                if (spriteKey != key)
                {
                    spriteKey = key;
                    // Stages 0-2 change appearance only, preserving placement and rotation.
                    machine.SetSprite(ModConstants.ModId, key);
                    machine.Validate();
                }
                SynchronizeModel();
                if (brain && lifeSupport.Dead) SaveState();
                production.RefreshSelector();
                int cost = brain || distributed ? parameters.PowerPerCycle : ServerHardware.PowerCost(cpu, gpu, network);
                int displayedCost = Math.Max(0, cost);
                if (!machine.IsTag(MachineParameters.PowerPerCycleTag) || MachineParameters.GetPowerPerCycle(machine) != displayedCost)
                    machine.ModifyTag(MachineParameters.PowerPerCycleTag, tag => tag.Enable().SetInt(displayedCost));
                bool valid = Conditions(out var hardware, out string reason);
                if (hardware == null) hardware = ReadHardware();
                if (valid && !OutputFits()) { valid = false; reason = "server_output_full"; }
                // Mirror display values on the machine, as vanilla machine
                // tooltips do. Keep CPU boost internal to the compute formula.
                double work = hardware?.ComputePerNight ?? 0;
                if (AiModelState.CanProduce(model.childItem)) work *= AiModelState.GetComputeMultiplier(model.childItem);
                long capacity = Math.Max(0, brain || distributed ? hardware?.MemoryGb ?? 0 : ServerHardware.MemoryCapacity(memory));
                if (distributed)
                {
                    machine.ModifyTag("AGE_OF_AGI_TERMINAL_SERVER_COUNT", tag => tag.Enable().SetInt(terminal.Count));
                    long attempts = AiModelState.CanProduce(model.childItem) ? (long)terminal.Count * AiModelState.GetBatchSize(model.childItem) : 0;
                    machine.ModifyTag("AGE_OF_AGI_TERMINAL_BATCH_LIMIT", tag => tag.Enable().SetLong(attempts));
                    machine.ModifyTag("AGE_OF_AGI_TERMINAL_DATA_AMOUNT", tag => tag.Enable().SetLong(ReadData().Amount));
                    machine.ModifyTag("AGE_OF_AGI_TERMINAL_DATA_FREE", tag => tag.Enable().SetLong(ReadData().Free));
                }
                machine.ModifyTag(TrainingDataTag, tag => tag.Enable().SetInt(
                    !stateFault && state != null ? state.DataNeededAt(state.TotalWork) - state.DataConsumed : 0));
                int workNights = Producing ? production.WorkNights : state?.WorkNights ?? 0;
                string statusKey = valid ? (production.HasShortExtractionBatch() ? "server_insufficient_extraction_data" :
                    workNights > 0 ? "server_running" : "server_ready") : reason ?? "";
                if (brain) statusKey = lifeSupport.MissingSupply() ?? statusKey;
                if (!machine.IsTag(WorkPerNightTag) || machine.GetTagReadonly(WorkPerNightTag).GetDouble() != work)
                    machine.ModifyTag(WorkPerNightTag, tag => tag.Enable().SetDouble(work));
                if (!machine.IsTag(MemoryCapacityTag) || machine.GetTagReadonly(MemoryCapacityTag).GetLong() != capacity)
                    machine.ModifyTag(MemoryCapacityTag, tag => tag.Enable().SetLong(capacity));
                if (!machine.IsTag(StatusTag) || machine.GetTagReadonly(StatusTag).GetString() != statusKey)
                    machine.ModifyTag(StatusTag, tag => tag.Enable().SetString(statusKey));
                if (!machine.IsTag(CpuUpgradeStep1TargetTag) || machine.GetTagReadonly(CpuUpgradeStep1TargetTag).GetInt() != definition.CpuUpgradeUnitsStep1)
                    machine.ModifyTag(CpuUpgradeStep1TargetTag, tag => tag.Enable().SetInt(definition.CpuUpgradeUnitsStep1));
                if (!machine.IsTag(CpuUpgradeTargetTag) || machine.GetTagReadonly(CpuUpgradeTargetTag).GetInt() != definition.CpuUpgradeUnitsTotal)
                    machine.ModifyTag(CpuUpgradeTargetTag, tag => tag.Enable().SetInt(definition.CpuUpgradeUnitsTotal));
                if (!machine.IsTag(GpuUpgradeStep1TargetTag) || machine.GetTagReadonly(GpuUpgradeStep1TargetTag).GetInt() != definition.GpuUpgradeUnitsPerStep)
                    machine.ModifyTag(GpuUpgradeStep1TargetTag, tag => tag.Enable().SetInt(definition.GpuUpgradeUnitsPerStep));
                int gpuTarget = 2 * definition.GpuUpgradeUnitsPerStep;
                if (!machine.IsTag(GpuUpgradeTargetTag) || machine.GetTagReadonly(GpuUpgradeTargetTag).GetInt() != gpuTarget)
                    machine.ModifyTag(GpuUpgradeTargetTag, tag => tag.Enable().SetInt(gpuTarget));
                if (!machine.IsTag(MemoryUpgradeTargetTag) || machine.GetTagReadonly(MemoryUpgradeTargetTag).GetInt() != definition.MemoryUpgradeUnits)
                    machine.ModifyTag(MemoryUpgradeTargetTag, tag => tag.Enable().SetInt(definition.MemoryUpgradeUnits));
            }
            finally
            {
                refreshing = false;
            }
        }

        private void PauseNight()
        {
            // Pauses preserve paid progress and consume no resources.
            if (state != null) SaveState();
        }

        // Both normal sleep and the native Machines Only command run early,
        // main and late callbacks. The command does not advance dayCounter.
        // An early callback authorizes one main pass even on the same day;
        // repeated main callbacks still use the saved day guard.
        private void RunWork()
        {
            if (busy || refreshing) return;
            busy = true;
            GameItem product = null;
            bool committed = false;
            try
            {
                SynchronizeModel();
                if (Producing)
                {
                    if (!stateFault) production.RunNight();
                    return;
                }
                if (!Conditions(out var hardware, out _) || !OutputFits()) { PauseNight(); return; }
                GameItem original = model.childItem;
                GameItem power = battery.childItem;
                ServerDataPool data = ReadData();
                float next = (float)Math.Min(state.TotalWork, state.Progress + hardware.ComputePerNight);
                int dataCost = state.DataForNight(next);
                if (next <= state.Progress) { PauseNight(); return; }
                state.Active = true;
                SaveState();
                bool completing = next >= state.TotalWork;
                var inputs = new List<GameItem> { power, original };
                CraftingRecipe recipe = new CraftingRecipe()
                    .AddTool(new CraftingCustomFilter(Text("server_power"), hardware.Power, item => ReferenceEquals(item, power)));
                data.AddTools(recipe, inputs);
                if (distributed) terminal.AddPower(recipe, inputs);
                if (completing)
                {
                    product = CreateProduct();
                    recipe.AddMaterial(original.name, 1, item => ReferenceEquals(item, original))
                        .SetCraftingFunc((_, _) => new List<GameItem> { product });
                    pendingOutput = product;
                    if (!GraphUtils.CanAccept(output, product) || GraphUtils.TryAcceptAll(output, product) != 0)
                    { PauseNight(); return; }
                }
                else recipe.AddTool(original.name, item => ReferenceEquals(item, original));
                if (!ReferenceEquals(model.childItem, original) || !ReferenceEquals(battery.childItem, power) ||
                    !data.Matches() ||
                    (completing && !ReferenceEquals(product.parentInventory, output)) ||
                    !Conditions(out var currentHardware, out _) || !hardware.Matches(currentHardware) || !recipe.CanCraft(inputs))
                { PauseNight(); return; }
                List<GameItem> products = recipe.Craft(inputs);
                if (completing && !products.Contains(product)) { PauseNight(); return; }
                committed = true;
                if (distributed) terminal.Committed();
                state.DataConsumed += dataCost;
                state.Record(next);
                if (completing) state.Active = false;
                // SetAmount writes the data tag before refreshing visuals.
                // Even a failing UI refresh must persist the paid progress.
                try { data.Consume(dataCost); }
                finally { SaveState(); }
                PowerHelper.UpdateEnergyCreditSprite(power);
            }
            finally
            {
                pendingOutput = null;
                try { if (!committed) product?.Destroy(); }
                finally { busy = false; Refresh(); }
            }
        }

        internal void OnLoaded(GameItem loaded)
        {
            RestoreStructure(loaded);
            if (distributed) loaded.EnableTag(DistributedTerminalState.TerminalTag);
            spriteKey = null;
            endpoint.CyclePending = false;
            production.Load();
            string saved = loaded.GetTagReadonly(TrainingStateTag).GetString();
            stateFault = false;
            state = null;
            if (!string.IsNullOrEmpty(saved))
            {
                try
                {
                    state = JsonConvert.DeserializeObject<ServerTrainingState>(saved);
                    stateFault = state == null || !state.IsValid();
                }
                catch (JsonException) { stateFault = true; }
                if (stateFault)
                {
                    LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId);
                    if (mod != null) new ModLog(mod.Manifest).Warning("Server training state is invalid; task paused without consuming resources.");
                }
            }
            else if (loaded.IsTag(ActiveTaskTag) && !production.Active) stateFault = true;
            if (stateFault) state = null;
            if (!stateFault)
            {
                SaveState();
            }
            else
            {
                loaded.EnableTag(ActiveTaskTag);
                loaded.DisableTag(ProgressVisibleTag);
                foreach (GameInventory inventory in locked) inventory.LockInv();
            }
            Refresh();
        }

        private static string Text(string key, params object[] args) => ModHelper.GetLocalized(ModConstants.ModId, key, args);

        private static MachineryPanel Panel(string role, PixelElement content) => global::MachineHelper.Panel(Text("server_" + role), content, role);
    }
}
