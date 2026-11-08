using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;

internal static class FactoryBindingChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        var mutableSource = new List<ItemDefinition>(definitions);
        LoadedMod loaded = Program.PrepareLoadedMod(mutableSource);
        var registration = new ItemRegistration(loaded.Manifest);
        var directory = new ModItemDirectory();
        int createdBefore = ItemDirectory.CreatedItems;
        int textBefore = ModHelper.LocalizationReads;
        ModHelper.FileReads = 0;
        UISettings.current = null;
        registration.Register(directory, mutableSource);
        Assert(ItemDirectory.CreatedItems == createdBefore && ModHelper.LocalizationReads == textBefore,
            "Binding item factories must not create scene objects or resolve translations.");
        Assert(ModHelper.FileReads == 14, "Bind every configuration catalog once per batch.");

        string[] paths = { GpuCatalog.CatalogPath, GpuInspectionCatalog.CatalogPath, CpuCatalog.CatalogPath, MemoryCatalog.CatalogPath,
            MachineCatalog.CatalogPath, DiskWriterCatalog.CatalogPath, BaseModelCatalog.CatalogPath, CompilerCatalog.CatalogPath, AiPartCatalog.CatalogPath, ServerCatalog.CatalogPath, EncryptedItemCatalog.CatalogPath, DataContentCatalog.CatalogPath, AiValueCatalog.CatalogPath, BrainServerCatalog.CatalogPath };
        try
        {
            // Registered delegates must stand on their own, even after the input list
            // changes or the configuration files become unreadable.
            mutableSource.Clear();
            foreach (string path in paths) ModHelper.FileOverrides[path] = "{";
            UISettings.current = new UISettings();
            ModHelper.LocalizationPrefix = "late:";
            foreach (ItemDefinition definition in definitions)
            {
                GameItem item = directory.Factories[definition.ItemId]();
                Assert(item.identifier == definition.ItemId && item.name == "late:" + definition.NameKey &&
                    item.unitValue == definition.BaseValue && item.SetValueCalls == 1,
                    "A bound factory lost its ID, current localization or common price: " + definition.ItemId);
            }
            Assert(ModHelper.FileReads == paths.Length, "Bound factories must not reload configuration.");
        }
        finally
        {
            foreach (string path in paths) ModHelper.FileOverrides.Remove(path);
            ModHelper.LocalizationPrefix = "";
            UISettings.current = new UISettings();
            registration.Unregister();
        }

        var factory = new CatalogItemFactory(loaded.Manifest);
        var order = new List<int>();
        GameItem failed = null;
        ItemDefinition sample = definitions[0];
        Action<GameItem> initialize = item =>
        {
            failed = item;
            Assert(item.SourceId == "age_of_agi" && item.unitValue == sample.BaseValue && item.SpriteKey == sample.SpriteKey,
                "Common properties must be initialized before specialized helpers run.");
            order.Add(1);
        };
        initialize += item => { order.Add(2); throw new InvalidOperationException("initializer failure"); };
        initialize += item => order.Add(3);
        bool threw = false;
        try { factory.Create(sample, initialize); }
        catch (InvalidOperationException exception) when (exception.Message == "initializer failure") { threw = true; }
        Assert(threw && failed != null && failed.Destroyed && order.SequenceEqual(new[] { 1, 2 }),
            "Initializer failure must stop later steps and destroy the partial item.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
