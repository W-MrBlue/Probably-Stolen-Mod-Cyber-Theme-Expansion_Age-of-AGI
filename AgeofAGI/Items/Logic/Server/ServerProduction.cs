using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.ComplexData;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.BrainServer;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Catalogs;
using Newtonsoft.Json;

namespace AgeOfAgi.Items.Logic.Server
{
    internal sealed class ServerProduction
    {
        internal const string StateTag = "AGE_OF_AGI_SERVER_PRODUCTION_STATE";
        // Version-one saved tasks retain their historical recipe/source identities.
        internal const string NormalInputId = ModConstants.ModId + ".btc_normal_unfinish";
        internal const string RareInputId = ModConstants.ModId + ".btc_rare_unfinish";
        internal const string FolderEmptyId = ModConstants.ModId + ".folder_empty";
        private readonly GameItem machine;
        private readonly GameSlotInventory battery, network, model, dataContainer;
        private readonly GameGridInventory cpu, gpu, memory, input, output;
        private readonly ServerDefinition definition;
        private readonly EncryptedItemCatalog encrypted;
        private readonly bool intrinsic;
        private GameItem Worker => intrinsic ? machine : model.childItem;
        private bool WorkerReady => intrinsic ? !machine.IsTag(BrainServerLifeSupport.DeadTag) : AiModelState.CanProduce(Worker);
        private int WorkerMemory => intrinsic ? 1 : AiModelState.GetMemoryGb(Worker);
        private float WorkerBonus => intrinsic ? 0 : AiModelState.GetComputeBonus(Worker);
        private float WorkerDurability => intrinsic ? 0 : AiModelState.GetCurrentDurability(Worker);
        private ServerProductionState state;
        private bool fault;
        private bool executing;
        private double workRemaining;
        private bool powerPaid;
        internal readonly ServerGeneSelector GeneSelector;
        internal readonly MachineryPanel GenePanel;
        internal Action StateChanged;
        internal Func<ServerHardware> ReadHardware;
        internal Func<ServerDataPool> ReadData;
        internal Action<CraftingRecipe, List<GameItem>> AddNetworkPower;
        internal Action WorkCommitted;
        internal Func<int> BatchMultiplier = () => 1;
        internal Func<int, int> RequiredPower = power => power;
        internal bool Active => fault || state?.Active == true;
        internal bool HasFault => fault;
        internal bool HasProgress => !fault && state != null && state.WorkNights > 0;
        internal float Progress => state?.Progress ?? 0;
        internal float TotalWork => state?.TotalWork ?? 0;
        internal int WorkNights => state?.WorkNights ?? 0;
        internal GameItem ProcessingItem => Source();
        internal string ProgressTextKey => state?.RecipeId == "data_extract" ? "server_extraction_progress_tooltip" :
            state?.RecipeId == "encrypted_item" ? "server_decoding_progress_tooltip" : "server_progress_tooltip";
        internal GameItem PendingOutput { get; private set; }

        internal ServerProduction(GameItem machine, ServerDefinition definition, EncryptedItemCatalog encrypted, GameSlotInventory battery,
            GameGridInventory cpu, GameGridInventory gpu, GameSlotInventory network, GameGridInventory memory,
            GameSlotInventory model, GameGridInventory input, GameGridInventory output, GameSlotInventory dataContainer, bool intrinsic = false)
        {
            this.machine = machine;
            this.definition = definition;
            this.encrypted = encrypted;
            this.intrinsic = intrinsic;
            this.battery = battery;
            this.cpu = cpu;
            this.gpu = gpu;
            this.network = network;
            this.memory = memory;
            this.model = model;
            this.input = input;
            this.output = output;
            this.dataContainer = dataContainer;
            ReadHardware = () => ServerHardware.TryRead(cpu, gpu, memory, network, definition, out var hardware) ? hardware : null;
            ReadData = () => new ServerDataPool(() => dataContainer.childItems);
            if (!intrinsic)
            {
                GeneSelector = new ServerGeneSelector(machine, FindSeed, () => !Active && !machine.IsTag(ServerState.ActiveTaskTag));
                GenePanel = global::MachineHelper.Panel(Text("server_gene_panel"), GeneSelector.Content, "production");
                GenePanel.SetHidden(true);
            }
        }

        internal static bool IsSeed(GameItem item) => item != null && item.unitCount > 0 && item.IsTag("SEED_ITEM_TAG") &&
            !item.IsTag("CONTAINER_TAG") && !item.IsTag("GROWING_TAG") &&
            GeneralHelper.IsItemOwned(item) && HydroponicHelper.GetPlant(item) != null;
        private IEnumerable<GameItem> OrderedInputs() => input.childItems.OrderBy(item => item.modifiedShape.minY)
            .ThenBy(item => item.modifiedShape.minX).ThenBy(item => item.identifier, StringComparer.Ordinal);
        private GameItem Source() => state == null ? null : input.childItems.FirstOrDefault(item => item.uniqueId == state.SourceId);
        internal GameItem FindSeed() => Active && state != null ? input.childItems.FirstOrDefault(item => item.uniqueId == state.SeedId) :
            OrderedInputs().FirstOrDefault(IsSeed);
        private static string SeedSignature(GameItem seed) => string.Join(";", PlantGeneticsHelper.GetTraits(seed)
            .OrderBy(entry => entry.trait.id, StringComparer.Ordinal).Select(entry => entry.trait.id + ":" + entry.rank)) +
            "|" + PlantGeneticsHelper.GetHeat(seed) + "|" + seed.unitCount;

        internal void Synchronize()
        {
            if (Active) return;
            state = null;
            GameItem ai = Worker;
            if (!WorkerReady || !GeneralHelper.IsItemOwned(ai)) { Save(); return; }
            string task = intrinsic ? null : AiModelState.GetTaskType(ai);
            string recipe = null;
            string outputItemId = null;
            GameItem source = null;
            GameItem seed = null;
            PlantTrait trait = null;
            float work = 0;
            int batchData = 0;
            long batchMemory = ReadHardware()?.MemoryGb ?? 0;
            ServerDataPool data = ReadData();
            foreach (GameItem item in OrderedInputs())
            {
                if (item.unitCount < 1 || !GeneralHelper.IsItemOwned(item)) continue;
                if (intrinsic)
                    task = encrypted.TryGet(item.identifier, out var intrinsicRecipe) ? intrinsicRecipe.TaskType :
                        ComplexDataState.IsEmpty(item) ? AiTaskTypes.Biological : AiTaskTypes.Intrusion;
                if (DataContentState.CanExtract(item, task) && batchMemory >= 8 && batchMemory >= WorkerMemory)
                {
                    recipe = "data_extract";
                    long standardBatch = (long)((double)batchMemory * definition.DataUnitsPer8Gb / 8);
                    long destinationLimit = data.Free > 0 ? data.Free : standardBatch;
                    batchData = (int)Math.Min((long)DataContentState.Amount(item), Math.Min(standardBatch, destinationLimit));
                    work = batchData * definition.DataWorkPerUnit;
                }
                else if (encrypted.TryGet(item.identifier, out var processing) && task == processing.TaskType)
                {
                    recipe = "encrypted_item";
                    work = processing.Work;
                    outputItemId = processing.OutputItemId;
                }
                else if (task == AiTaskTypes.Intrusion && item.identifier == FolderEmptyId)
                { recipe = "intelligence"; work = definition.IntelligenceWork; }
                else if (task == AiTaskTypes.Biological && ComplexDataState.IsEmpty(item))
                {
                    seed = FindSeed();
                    trait = ServerGeneSelector.SelectedTrait(machine, seed);
                    if (trait == null) continue;
                    recipe = "bio";
                    work = (float)(definition.BioBaseWork + (double)definition.BioComplexityWork *
                        Math.Max(0, PlantGeneticsHelper.GetEffectiveComplexity(seed)));
                }
                if (recipe != null) { source = item; break; }
            }
            if (source != null && work > 0 && !float.IsInfinity(work))
            {
                state = new ServerProductionState
                {
                    Version = recipe == "encrypted_item" ? 2 : 1, OutputItemId = outputItemId,
                    RecipeId = recipe, SourceId = source.GetUniqueID(), SourceItemId = source.identifier,
                    Intrinsic = intrinsic, ModelId = ai.GetUniqueID(), TaskType = task, MemoryGb = WorkerMemory,
                    ComputeBonus = WorkerBonus, Durability = WorkerDurability,
                    BatchSize = intrinsic ? 1 : AiModelState.GetBatchSize(ai),
                    Traits = intrinsic ? AiModelTraits.None : AiModelState.GetTraits(ai),
                    TotalWork = work, SeedId = seed?.GetUniqueID() ?? 0, TraitId = trait?.id,
                    SeedSignature = seed == null ? null : SeedSignature(seed),
                    BatchData = batchData, BatchMemoryGb = batchData > 0 ? batchMemory : 0,
                    SourceData = batchData > 0 ? DataContentState.Amount(source) : 0
                };
            }
            Save();
        }

        internal void RefreshSelector()
        {
            if (intrinsic) return;
            bool biological = AiModelState.IsTrained(model.childItem) && AiModelState.GetTaskType(model.childItem) == AiTaskTypes.Biological;
            if (GenePanel.isHidden == biological)
            {
                GenePanel.SetHidden(!biological);
                machine.contentWindow?.Validate();
            }
            GeneSelector.Refresh(biological);
        }

        private bool Matches()
        {
            GameItem ai = Worker;
            GameItem source = Source();
            if (state == null || state.Intrinsic != intrinsic || !WorkerReady || ai.uniqueId != state.ModelId ||
                (!intrinsic && AiModelState.GetTaskType(ai) != state.TaskType) || WorkerMemory != state.MemoryGb ||
                WorkerBonus != state.ComputeBonus || WorkerDurability != state.Durability ||
                (!intrinsic && (AiModelState.GetBatchSize(ai) != state.BatchSize || AiModelState.GetTraits(ai) != state.Traits)) ||
                source == null || source.identifier != state.SourceItemId || source.unitCount < 1 ||
                !GeneralHelper.IsItemOwned(source) || !GeneralHelper.IsItemOwned(ai)) return false;
            if (state.RecipeId == "data_extract") return DataContentState.CanExtract(source, state.TaskType) &&
                DataContentState.Amount(source) == state.SourceData && state.BatchData > 0 && state.BatchData <= state.SourceData;
            if (state.RecipeId != "bio") return true;
            GameItem seed = FindSeed();
            PlantTrait trait = ServerGeneSelector.SelectedTrait(machine, seed);
            return IsSeed(seed) && seed.uniqueId == state.SeedId && ComplexDataState.IsEmpty(source) &&
                trait?.id == state.TraitId && SeedSignature(seed) == state.SeedSignature;
        }

        internal bool Conditions(out ServerHardware hardware, out string reason)
        {
            hardware = null;
            reason = "server_invalid_state";
            if (fault) return false;
            GameItem ai = Worker;
            reason = "server_invalid_production_model";
            if (!WorkerReady) return false;
            // Model capacity is required even when no input recipe has been selected.
            reason = "server_missing_hardware";
            hardware = ReadHardware();
            if (hardware == null) return false;
            reason = "server_insufficient_memory";
            if (hardware.MemoryGb < WorkerMemory) return false;
            reason = !intrinsic && AiModelState.GetTaskType(ai) == AiTaskTypes.Biological ? "server_choose_gene" : "server_no_recipe";
            if (!Matches()) return false;
            if (state.RecipeId == "encrypted_item" && DirectoryMaster.GetFactory<GameItem>(state.OutputItemId) == null)
            { reason = "server_invalid_state"; return false; }
            if (state.RecipeId == "data_extract")
            {
                reason = "server_invalid_state";
                if (hardware.MemoryGb != state.BatchMemoryGb) return false;
                reason = "server_data_container_missing";
                ServerDataPool data = ReadData();
                if (!data.Present) return false;
                reason = "server_data_container_full";
                if (data.Free < state.BatchData) return false;
            }
            reason = "server_network_upgrades_required";
            if (state.RecipeId == "intelligence" &&
                (!NetworkSwitchUpgrades.HasUpgrade(network.childItem, NetworkSwitchUpgrades.NetworkAccessUpgrade) ||
                 !NetworkSwitchUpgrades.HasUpgrade(network.childItem, NetworkSwitchUpgrades.PacketDisguiseUpgrade))) return false;
            reason = "server_insufficient_power";
            GameItem power = battery.childItem;
            if (power == null || !GeneralHelper.IsItemOwned(power) || !PowerHelper.CanDrawPowerSource(power, powerPaid ? 0 : RequiredPower(hardware.Power)) ||
                !GeneralHelper.IsItemOwned(machine)) return false;
            reason = null;
            return true;
        }

        private GameItem CreateProduct()
        {
            string id = state.RecipeId == "encrypted_item" ? state.OutputItemId : state.RecipeId == "bio" ? ComplexDataState.ItemId : state.RecipeId == "intelligence" ?
                ModConstants.ModId + ".folder_security" : ModConstants.ModId + "." + state.RecipeId;
            GameItem product = DirectoryMaster.Item(id);
            try
            {
                if (state.RecipeId == "bio") ComplexDataState.Write(product, PlantTraitRegistry.Get(state.TraitId));
                return product;
            }
            catch { product.Destroy(); throw; }
        }

        internal bool OutputFits()
        {
            if (state == null) return false;
            if (state.RecipeId == "data_extract") return ReadData().Free >= state.BatchData;
            GameItem probe = CreateProduct();
            try { PendingOutput = probe; return GraphUtils.CanAccept(output, probe); }
            finally { PendingOutput = null; probe.Destroy(); }
        }

        internal bool HasShortExtractionBatch()
        {
            if (state?.RecipeId != "data_extract") return false;
            long standardBatch = (long)((double)state.BatchMemoryGb * definition.DataUnitsPer8Gb / 8);
            return state.BatchData < standardBatch;
        }

        internal void RunNight()
        {
            if (executing) return;
            executing = true;
            powerPaid = false;
            try
            {
                if (!Conditions(out var hardware, out _)) return;
                GameItem originalWorker = Worker;
                workRemaining = hardware.ComputePerNight * (1 + WorkerBonus / 100.0);
                long limit = (long)(intrinsic ? 1 : AiModelState.GetBatchSize(originalWorker)) * Math.Max(1, BatchMultiplier());
                if (!intrinsic) limit = Math.Min(limit, (long)WorkerDurability);
                for (long step = 0; step < limit && workRemaining > 0; step++)
                {
                    if (!ReferenceEquals(Worker, originalWorker) || !WorkerReady) break;
                    Synchronize();
                    if (!hardware.Matches(ReadHardware())) break;
                    double previous = workRemaining;
                    RunStep();
                    // An unfinished batch or a failed transaction ends this night's processing.
                    if (workRemaining >= previous || state != null) break;
                }
            }
            finally
            {
                powerPaid = false;
                workRemaining = 0;
                executing = false;
            }
        }

        private void RunStep()
        {
            GameItem product = null;
            bool committed = false;
            try
            {
                if (!Conditions(out var hardware, out _) || !OutputFits()) return;
                if (state.RecipeId == "data_extract")
                {
                    RunExtractionNight(hardware);
                    return;
                }
                GameItem ai = Worker, source = Source(), power = battery.childItem, installedSwitch = network.childItem;
                GameItem seed = state.RecipeId == "bio" ? FindSeed() : null;
                int sourceCount = source.unitCount;
                float next = (float)Math.Min(state.TotalWork, state.Progress + workRemaining);
                double workUsed = next - (double)state.Progress;
                if (next <= state.Progress) return;
                state.Active = true;
                Save();
                bool completing = next >= state.TotalWork;
                var inputs = new List<GameItem> { ai, source, power };
                var recipe = new CraftingRecipe()
                    .AddTool(ai.name, item => ReferenceEquals(item, ai))
                    .AddTool(new CraftingCustomFilter(Text("server_power"), powerPaid ? 0 : hardware.Power, item => ReferenceEquals(item, power)));
                AddNetworkPower?.Invoke(recipe, inputs);
                if (seed != null) { inputs.Add(seed); recipe.AddTool(seed.name, item => ReferenceEquals(item, seed)); }
                if (completing)
                {
                    product = CreateProduct();
                    recipe.AddMaterial(source.name, 1, item => ReferenceEquals(item, source))
                        .SetCraftingFunc((_, _) => new List<GameItem> { product });
                    PendingOutput = product;
                    if (!GraphUtils.CanAccept(output, product) || GraphUtils.TryAcceptAll(output, product) != 0) return;
                }
                else recipe.AddTool(source.name, item => ReferenceEquals(item, source));
                if (!ReferenceEquals(Worker, ai) || !ReferenceEquals(battery.childItem, power) ||
                    !ReferenceEquals(network.childItem, installedSwitch) || source.unitCount != sourceCount ||
                    !Matches() || !Conditions(out var currentHardware, out _) || !hardware.Matches(currentHardware) ||
                    (completing && !ReferenceEquals(product.parentInventory, output)) || !recipe.CanCraft(inputs)) return;
                List<GameItem> products = recipe.Craft(inputs);
                if (completing && !products.Contains(product)) return;
                committed = true;
                workRemaining = Math.Max(0, workRemaining - workUsed);
                powerPaid = true;
                WorkCommitted?.Invoke();
                if (completing)
                {
                    if (!intrinsic) AiModelState.SpendProductionUse(ai);
                    state = null;
                }
                else { state.Progress = next; state.WorkNights++; }
                Save();
                PowerHelper.UpdateEnergyCreditSprite(power);
            }
            finally
            {
                PendingOutput = null;
                if (!committed) product?.Destroy();
            }
        }

        private void RunExtractionNight(ServerHardware hardware)
        {
            GameItem ai = Worker, source = Source(), power = battery.childItem;
            ServerDataPool data = ReadData();
            float next = (float)Math.Min(state.TotalWork, state.Progress + workRemaining);
            double workUsed = next - (double)state.Progress;
            if (next <= state.Progress) return;
            var recipe = new CraftingRecipe()
                .AddTool(ai.name, item => ReferenceEquals(item, ai))
                .AddTool(source.name, item => ReferenceEquals(item, source))
                .AddTool(new CraftingCustomFilter(Text("server_power"), powerPaid ? 0 : hardware.Power, item => ReferenceEquals(item, power)));
            var inputs = new List<GameItem> { ai, source, power };
            data.AddTools(recipe, inputs);
            AddNetworkPower?.Invoke(recipe, inputs);
            state.Active = true;
            Save();
            if (!ReferenceEquals(Worker, ai) || !ReferenceEquals(battery.childItem, power) ||
                !data.Matches() || !Conditions(out var currentHardware, out _) ||
                !hardware.Matches(currentHardware) || !recipe.CanCraft(inputs)) return;
            recipe.Craft(inputs);
            workRemaining = Math.Max(0, workRemaining - workUsed);
            powerPaid = true;
            WorkCommitted?.Invoke();
            if (next >= state.TotalWork)
            {
                DataContentState.SetAmount(source, state.SourceData - state.BatchData);
                data.Deliver(state.BatchData);
                if (!intrinsic) AiModelState.SpendProductionUse(ai);
                state = null;
            }
            else { state.Progress = next; state.WorkNights++; }
            Save();
            PowerHelper.UpdateEnergyCreditSprite(power);
        }

        private void Save()
        {
            machine.ModifyTag(StateTag, tag => tag.Enable().SetString(state == null ? "" : JsonConvert.SerializeObject(state)));
            StateChanged?.Invoke();
        }

        internal void Load()
        {
            GeneSelector?.Close();
            string saved = machine.GetTagReadonly(StateTag).GetString();
            state = null;
            fault = false;
            if (string.IsNullOrEmpty(saved)) return;
            try { state = JsonConvert.DeserializeObject<ServerProductionState>(saved); fault = state == null || !state.IsValid(); }
            catch (JsonException) { fault = true; }
            if (fault)
            {
                state = null;
                LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId);
                if (mod != null) new ModLog(mod.Manifest).Warning("Server production state is invalid; production paused without consuming resources.");
            }
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }
}
