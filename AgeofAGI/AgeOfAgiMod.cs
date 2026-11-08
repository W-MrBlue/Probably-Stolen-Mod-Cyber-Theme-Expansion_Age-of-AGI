using AgeOfAgi.Debugging;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Npcs;

namespace AgeOfAgi
{
    public class AgeOfAgiMod : IMod
    {
        private ModLog log;
        private ItemRegistration itemRegistration;

        public void Init(ModManifest manifest)
        {
            itemRegistration = new ItemRegistration(manifest);
            log = new ModLog(manifest);
        }

        public void OnEnable()
        {
            Items.Logic.DistributedTerminal.DistributedNetwork.Enable();
            NetworkSwitchLinks.Enable();
            NightShiftClient.Enable(log);
            Npcs.Stories.StoryDirector.Enable(log, QuorraStory.CreateDefinition());
            StoryDocumentHelper.Enable();
            ElectronicsMarket.Enable();
            DataBuyers.Enable();
            ElectronicsEvents.Enable();
            ElectronicsSignAttraction.Enable();
            JacksonCodeStorageStock.Enable();
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnModItemDirectoryInit += RegisterItems;
            ModHook.OnCreateTooltipEarly -= ItemTooltipHelper.CreateZeroValueTooltip;
            ModHook.OnCreateTooltipEarly += ItemTooltipHelper.CreateZeroValueTooltip;
            ModHook.OnCreateTooltipLate -= GpuHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += GpuHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= CpuHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += CpuHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= MemoryHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += MemoryHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= DiskWriterHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += DiskWriterHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= CompilerHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += CompilerHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= BaseModelHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += BaseModelHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= AiPartsHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += AiPartsHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= AiModelHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += AiModelHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= ServerHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= BrainServerHelper.CreateBloodTooltip;
            ModHook.OnCreateTooltipLate += ServerHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += BrainServerHelper.CreateBloodTooltip;
            ModHook.OnCreateTooltipLate -= NetworkSwitchHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += NetworkSwitchHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= DataContentHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += DataContentHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= DataCollectorHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate += DataCollectorHelper.CreateTooltip;
        }

        public void OnDisable()
        {
            Items.Logic.DistributedTerminal.DistributedNetwork.Shutdown();
            NetworkSwitchLinks.Shutdown();
            NightShiftClient.Shutdown();
            Npcs.Stories.StoryDirector.Shutdown();
            ElectronicsEvents.Shutdown();
            ElectronicsMarket.Shutdown();
            DataBuyers.Shutdown();
            ClientDialogueCatalog.Shutdown();
            ElectronicsSignAttraction.Shutdown();
            JacksonCodeStorageStock.Shutdown();
            StoryDocumentHelper.Shutdown();
            QuorraStoryClient.Shutdown();
            GpuInspectionHelper.Shutdown();
            GpuHelper.ShutdownLabels();
            ElectronicScannerHelper.Shutdown();
            GpuColorCheckerHelper.Shutdown();
            GpuGuideHelper.Shutdown();
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnCreateTooltipEarly -= ItemTooltipHelper.CreateZeroValueTooltip;
            ModHook.OnCreateTooltipLate -= GpuHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= CpuHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= MemoryHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= DiskWriterHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= CompilerHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= BaseModelHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= AiPartsHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= AiModelHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= ServerHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= BrainServerHelper.CreateBloodTooltip;
            ModHook.OnCreateTooltipLate -= NetworkSwitchHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= DataContentHelper.CreateTooltip;
            ModHook.OnCreateTooltipLate -= DataCollectorHelper.CreateTooltip;
            ModDebugActions.Unregister();
            itemRegistration?.Unregister();
        }

        private void RegisterItems(ModItemDirectory directory)
        {
            // Init/OnEnable can run before this mod is available to ModHelper's file lookup.
            // The item-directory hook runs after registration and asset loading.
            ModDebugActions.Unregister();
            var itemDefinitions = ItemCatalog.Load();
            var nightShiftCatalog = NightShiftCatalog.Load(itemDefinitions);
            var entryCatalog = NightShiftCatalog.Load(itemDefinitions, ElectronicsTrader.CatalogPath);
            var visitRules = ElectronicsVisitRules.Load();
            var supplierCatalog = ElectronicsDropCatalog.Load(itemDefinitions);
            var marketCatalog = ElectronicsMarketCatalog.Create(itemDefinitions, visitRules, supplierCatalog);
            var salvageCatalog = ElectronicsSalvageCatalog.Create(visitRules, supplierCatalog);
            var eventStockCatalog = ElectronicsEventStockCatalog.Load(itemDefinitions);
            ClientDialogueCatalog.Configure();
            int registeredCount = itemRegistration.Register(directory, itemDefinitions);
            NightShiftClient.Configure(nightShiftCatalog);
            NightShiftClient.ConfigureEntry(entryCatalog);
            ElectronicsMarket.Configure(marketCatalog, salvageCatalog);
            DataBuyers.Configure();
            ElectronicsEvents.Configure(eventStockCatalog);
            ModDebugActions.Register(itemDefinitions);
            log.Log($"{registeredCount} catalog items registered.");
        }
    }
}
