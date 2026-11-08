using System;
using System.Collections.Generic;

namespace AgeOfAgi.Items
{
    internal sealed class ItemRegistration
    {
        private readonly CatalogItemFactory itemFactory;
        private ModItemDirectory registeredDirectory;
        private Dictionary<string, Func<GameItem>> registeredFactories = new Dictionary<string, Func<GameItem>>(StringComparer.Ordinal);

        internal ItemRegistration(ModManifest manifest)
        {
            itemFactory = new CatalogItemFactory(manifest);
        }

        internal int Register(ModItemDirectory directory, IReadOnlyList<ItemDefinition> definitions)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            if (definitions == null || definitions.Count == 0)
            {
                throw new ArgumentException("The item catalog must not be empty.", nameof(definitions));
            }

            // Directory.Add removes a matching global entry before trying to add. Check the
            // complete batch first so a conflict cannot delete another mod's factory.
            itemFactory.ValidateSprites(definitions);
            var plannedFactories = ItemFactoryDirectory.CreateFactories(itemFactory, definitions);
            foreach (ItemDefinition definition in definitions)
            {
                Func<GameItem> globalFactory = DirectoryMaster.GetFactory<GameItem>(definition.ItemId);
                directory.factoryDictionary.TryGetValue(definition.ItemId, out var localFactory);
                if ((globalFactory != null && !Owns(definition.ItemId, globalFactory)) ||
                    (localFactory != null && !Owns(definition.ItemId, localFactory)))
                {
                    throw new InvalidOperationException("Item ID is already registered by another factory: " + definition.ItemId);
                }
            }

            Unregister();
            try
            {
                foreach (var entry in plannedFactories)
                {
                    if (!directory.Add(entry.Key, entry.Value))
                    {
                        throw new InvalidOperationException("Could not register item: " + entry.Key);
                    }
                }
            }
            catch
            {
                RemoveOwnedFactories(directory, plannedFactories);
                throw;
            }

            registeredDirectory = directory;
            registeredFactories = plannedFactories;
            return registeredFactories.Count;
        }

        internal void Unregister()
        {
            if (registeredDirectory != null)
            {
                RemoveOwnedFactories(registeredDirectory, registeredFactories);
            }

            registeredDirectory = null;
            registeredFactories.Clear();
        }

        private bool Owns(string itemId, Func<GameItem> factory)
        {
            return registeredFactories.TryGetValue(itemId, out var ownedFactory) &&
                ReferenceEquals(factory, ownedFactory);
        }

        private static void RemoveOwnedFactories(ModItemDirectory directory, Dictionary<string, Func<GameItem>> factories)
        {
            foreach (var entry in factories)
            {
                if (directory.factoryDictionary.TryGetValue(entry.Key, out var currentFactory) &&
                    ReferenceEquals(currentFactory, entry.Value))
                {
                    directory.Remove(entry.Key);
                }
            }
        }
    }
}
