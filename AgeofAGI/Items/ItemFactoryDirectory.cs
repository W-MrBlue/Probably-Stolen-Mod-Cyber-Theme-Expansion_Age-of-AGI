using AgeOfAgi.Items.Logic.BrainServer;
using AgeOfAgi.Items.Logic.DistributedTerminal;
using AgeOfAgi.Items.Logic.DataCollector;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.ElectronicStorage;
using AgeOfAgi.Items.Logic.Gpu;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Helpers;

namespace AgeOfAgi.Items
{
    // Like native ItemDirectory implementations, bind each ID to one owning helper.
    // This is a registration-time plan, not a Unity scene directory or a live registry.
    internal sealed class ItemFactoryDirectory
    {
        private readonly IReadOnlyList<ItemDefinition> definitions;
        private readonly Dictionary<string, Action<GameItem>> initializers =
            new Dictionary<string, Action<GameItem>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Action<GameItem>> audioInitializers =
            new Dictionary<string, Action<GameItem>>(StringComparer.Ordinal);
        private MachineCatalog machineCatalog;
        private AiPartCatalog aiPartCatalog;
        private ServerCatalog serverCatalog;
        private EncryptedItemCatalog encryptedCatalog;

        private ItemFactoryDirectory(IReadOnlyList<ItemDefinition> definitions)
        {
            this.definitions = definitions;
            foreach (ItemDefinition definition in definitions)
            {
                if (!initializers.TryAdd(definition.ItemId, null))
                {
                    throw new InvalidOperationException("Duplicate item ID: " + definition.ItemId);
                }
                try
                {
                    audioInitializers.Add(definition.ItemId, ItemAudioPresets.Resolve(definition.AudioPreset));
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidOperationException("Item '" + definition.ItemId + "': " + exception.Message, exception);
                }
            }
        }

        internal static Dictionary<string, Func<GameItem>> CreateFactories(CatalogItemFactory itemFactory,
            IReadOnlyList<ItemDefinition> definitions)
        {
            var directory = new ItemFactoryDirectory(definitions);
            directory.RegisterGpus();
            directory.RegisterCpus();
            directory.RegisterMemory();
            directory.RegisterMachines();
            directory.RegisterAiParts();
            directory.RegisterDiskWriter();
            directory.RegisterBaseModels();
            directory.RegisterAiModels();
            directory.RegisterCompiler();
            directory.RegisterServer();
            directory.RegisterDataCollector();
            directory.RegisterDistributedTerminal();
            directory.RegisterBrainServer();
            directory.RegisterNetworkSwitch();
            directory.RegisterElectronicMaterials();
            directory.RegisterDataItems();
            directory.RegisterSigns();
            directory.RegisterElectronicTools();
            directory.RegisterElectronicStorage();
            if (directory.initializers.ContainsKey(Logic.StoryDocuments.QuorraDocuments.WantedItemId))
                directory.Add(Logic.StoryDocuments.QuorraDocuments.WantedItemId, StoryDocumentHelper.InitWanted);
            if (directory.initializers.ContainsKey(Logic.StoryDocuments.QuorraDocuments.PhoneCardItemId))
                directory.Add(Logic.StoryDocuments.QuorraDocuments.PhoneCardItemId, StoryDocumentHelper.InitPhoneCard);

            var factories = new Dictionary<string, Func<GameItem>>(StringComparer.Ordinal);
            foreach (ItemDefinition definition in definitions)
            {
                // Capture the single initialization owner once. Creating instances
                // performs no kind dispatch or configuration file reads.
                Action<GameItem> initialize = directory.initializers[definition.ItemId];
                if (definition.Kind == "machine" && initialize == null)
                    throw new InvalidOperationException("A configured machine requires its own helper: " + definition.ItemId);
                if (initialize == null)
                {
                    Action<GameItem> initializeAudio = directory.audioInitializers[definition.ItemId];
                    initialize = instance => GenericItemHelper.Init(instance, initializeAudio);
                }
                factories.Add(definition.ItemId, () => itemFactory.Create(definition, initialize));
            }

            return factories;
        }

        private void RegisterSigns()
        {
            foreach (ItemDefinition item in OfKind("electronic_sign"))
                Add(item.ItemId, ElectronicsSignHelper.Init);
        }

        private void RegisterElectronicTools()
        {
            if (initializers.ContainsKey(GpuGuide.ItemId))
                Add(GpuGuide.ItemId, GpuGuideHelper.Init);
            foreach (ItemDefinition item in OfKind("electronic_scanner"))
                Add(item.ItemId, ElectronicScannerHelper.Init);
            if (initializers.ContainsKey(GpuColorChecker.ItemId))
            {
                GpuColorCheckerHelper.PrepareAssets();
                Add(GpuColorChecker.ItemId, GpuColorCheckerHelper.Init);
            }
        }

        private void RegisterElectronicStorage()
        {
            if (initializers.ContainsKey(ElectronicStorageRuntime.DiskBoxId))
            {
                if (!initializers.ContainsKey(ElectronicStorageRuntime.EmptyDiskId))
                    throw new InvalidOperationException("Disk boxes require the blank disk factory.");
                Add(ElectronicStorageRuntime.DiskBoxId, ElectronicStorageHelper.InitDiskBox);
            }
            if (initializers.ContainsKey(ElectronicStorageRuntime.CodeContainerId))
                Add(ElectronicStorageRuntime.CodeContainerId, ElectronicStorageHelper.InitCodeContainer);
        }

        private void RegisterGpus()
        {
            ItemDefinition[] items = OfKind("gpu");
            if (items.Length == 0)
            {
                return;
            }

            GpuCatalog catalog = GpuCatalog.Load();
            catalog.ValidateMappings(definitions);
            GpuInspectionCatalog inspection = GpuInspectionCatalog.Load(definitions);
            inspection.ValidateCores(catalog);
            foreach (ItemDefinition item in items)
            {
                var gpu = catalog.Get(item.ItemId);
                Add(item.ItemId, (instance, audio) => GpuHelper.InitGpu(instance, gpu, audio, inspection, catalog));
            }
        }

        private void RegisterCpus()
        {
            ItemDefinition[] items = OfKind("cpu");
            if (items.Length == 0)
            {
                return;
            }

            CpuCatalog catalog = CpuCatalog.Load();
            catalog.ValidateMappings(definitions);
            foreach (ItemDefinition item in items)
            {
                var cpu = catalog.Get(item.ItemId);
                Add(item.ItemId, (instance, audio) => CpuHelper.InitCpu(instance, cpu, audio));
            }
        }

        private void RegisterMemory()
        {
            ItemDefinition[] items = OfKind("memory");
            if (items.Length == 0)
            {
                return;
            }

            MemoryCatalog catalog = MemoryCatalog.Load();
            catalog.ValidateMappings(definitions);
            foreach (ItemDefinition item in items)
            {
                var memory = catalog.Get(item.ItemId);
                Add(item.ItemId, (instance, audio) => MemoryHelper.InitMemory(instance, memory, audio));
            }
        }

        private void RegisterMachines()
        {
            ItemDefinition[] items = OfKind("machine");
            if (items.Length == 0)
            {
                return;
            }

            machineCatalog = MachineCatalog.Load();
            machineCatalog.ValidateMappings(definitions);
        }

        private void RegisterAiParts()
        {
            if (OfKind("ai_parts").Length > 0)
            {
                aiPartCatalog = AiPartCatalog.Load();
                aiPartCatalog.ValidateMappings(definitions);
            }
            foreach (ItemDefinition item in OfKind("ai_parts").Concat(OfKind("ai_parts_chip")))
            {
                var part = item.Kind == "ai_parts" ? aiPartCatalog.Get(item.ItemId) : null;
                Add(item.ItemId, (instance, audio) => AiPartsHelper.Init(instance, item, audio, part));
            }
        }

        private void RegisterAiModels()
        {
            var values = AiValueCatalog.Load();
            AiModelState.ConfigureValues(values);
            foreach (ItemDefinition item in OfKind("ai_model"))
            {
                Add(item.ItemId, (instance, audio) => AiModelHelper.Init(instance, audio, values));
            }
        }

        private void RegisterDiskWriter()
        {
            if (!initializers.ContainsKey(DiskWriterCatalog.DiskWriterId))
            {
                return;
            }

            DiskWriterCatalog catalog = DiskWriterCatalog.Load();
            catalog.ValidateMappings(definitions);
            var machine = machineCatalog.Get(DiskWriterCatalog.DiskWriterId);
            Add(DiskWriterCatalog.DiskWriterId, (instance, audio) => DiskWriterHelper.Init(instance, machine, catalog, audio));
        }

        private void RegisterBaseModels()
        {
            ItemDefinition[] items = OfKind("base_model");
            if (items.Length == 0)
            {
                return;
            }

            BaseModelCatalog catalog = BaseModelCatalog.Load();
            catalog.ValidateMappings(definitions);
            foreach (ItemDefinition item in items)
            {
                var baseModel = catalog.Get(item.ItemId);
                Add(item.ItemId, (instance, audio) => BaseModelHelper.InitBaseModel(instance, baseModel, audio));
            }
        }

        private void RegisterCompiler()
        {
            if (!initializers.ContainsKey(CompilerCatalog.CompilerId))
            {
                return;
            }

            CompilerCatalog catalog = CompilerCatalog.Load();
            catalog.ValidateMappings(definitions);
            AiModelTraitText.Configure(catalog.Recipe);
            if (aiPartCatalog == null)
                throw new InvalidOperationException("Compiler requires configured AI parts.");
            var machine = machineCatalog.Get(CompilerCatalog.CompilerId);
            Add(CompilerCatalog.CompilerId, (instance, audio) => CompilerHelper.Init(instance, machine, catalog, aiPartCatalog, audio));
        }

        private void RegisterServer()
        {
            if (!initializers.ContainsKey(ServerCatalog.ServerId))
            {
                return;
            }
            ServerCatalog catalog = ServerCatalog.Load();
            catalog.ValidateMappings(definitions);
            EncryptedItemCatalog encrypted = EncryptedItemCatalog.Load();
            encrypted.ValidateMappings(definitions);
            serverCatalog = catalog;
            encryptedCatalog = encrypted;
            var machine = machineCatalog.Get(ServerCatalog.ServerId);
            Add(ServerCatalog.ServerId, (instance, audio) => ServerHelper.Init(instance, machine, catalog.Definition, audio, encrypted));
        }

        private void RegisterDataCollector()
        {
            if (!initializers.ContainsKey(DataCollectorState.CollectorId))
            {
                return;
            }
            var machine = machineCatalog.Get(DataCollectorState.CollectorId);
            Add(DataCollectorState.CollectorId, (instance, audio) => DataCollectorHelper.Init(instance, machine, audio));
        }

        private void RegisterDistributedTerminal()
        {
            if (!initializers.ContainsKey(DistributedTerminalState.ItemId)) return;
            var catalog = serverCatalog ?? ServerCatalog.Load();
            var encrypted = encryptedCatalog ?? EncryptedItemCatalog.Load();
            var parameters = machineCatalog.Get(DistributedTerminalState.ItemId);
            Add(DistributedTerminalState.ItemId, (instance, audio) =>
                DistributedTerminalHelper.Init(instance, parameters, catalog.Definition, audio, encrypted));
        }

        private void RegisterBrainServer()
        {
            if (!initializers.ContainsKey(BrainServerLifeSupport.ItemId)) return;
            var catalog = serverCatalog ?? ServerCatalog.Load();
            var encrypted = encryptedCatalog ?? EncryptedItemCatalog.Load();
            var parameters = machineCatalog.Get(BrainServerLifeSupport.ItemId);
            var brain = BrainServerCatalog.Load();
            Add(BrainServerLifeSupport.ItemId, (instance, audio) =>
                BrainServerHelper.Init(instance, parameters, catalog.Definition, audio, encrypted, brain));
        }

        private void RegisterNetworkSwitch()
        {
            foreach (ItemDefinition item in OfKind("net_switch"))
            {
                Add(item.ItemId, NetworkSwitchHelper.Init);
            }
            foreach (ItemDefinition item in OfKind("ns_upgrade_module"))
            {
                int index = NetworkSwitchUpgrades.ModuleIndex(item.ItemId);
                Add(item.ItemId, (instance, audio) => NetworkSwitchHelper.InitModule(instance, index, audio));
            }
        }

        private void RegisterElectronicMaterials()
        {
            foreach (ItemDefinition item in OfKind("electronic_material"))
            {
                Add(item.ItemId, ElectronicMaterialHelper.Init);
            }
        }

        private void RegisterDataItems()
        {
            foreach (ItemDefinition item in OfKind("complex_data")) Add(item.ItemId, ComplexDataHelper.Init);
            foreach (ItemDefinition item in OfKind("data_item")) Add(item.ItemId, DataItemHelper.Init);
            var dataItems = OfKind("data_carrier").Concat(OfKind("data_container")).ToArray();
            if (dataItems.Length == 0) return;
            DataContentCatalog catalog = DataContentCatalog.Load();
            catalog.ValidateMappings(definitions);
            foreach (ItemDefinition item in dataItems)
            {
                var content = catalog.Get(item.ItemId);
                int unitValue = catalog.GetUnitValue(item.ItemId);
                Add(item.ItemId, (instance, audio) => DataContentHelper.Init(instance, content, unitValue, audio));
            }
        }

        private ItemDefinition[] OfKind(string kind)
        {
            return definitions.Where(definition => definition.Kind == kind).ToArray();
        }

        private void Add(string itemId, Action<GameItem, Action<GameItem>> initialize)
        {
            if (initializers[itemId] != null)
                throw new InvalidOperationException("An item must have a single initialization owner: " + itemId);
            Action<GameItem> initializeAudio = audioInitializers[itemId];
            initializers[itemId] = instance => initialize(instance, initializeAudio);
        }
    }
}
