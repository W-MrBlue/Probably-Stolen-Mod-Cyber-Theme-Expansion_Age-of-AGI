using System;
using System.Collections.Generic;
using System.Linq;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsEvents
    {
        internal const string CryptoBoomId = ModConstants.ModId + ".crypto_boom";
        internal const string AstarBreakthroughId = ModConstants.ModId + ".astar_breakthrough";
        internal const string ElectronicsSurgeId = ModConstants.ModId + ".electronics_surge";
        internal const string CryptoCrashId = ModConstants.ModId + ".crypto_crash";
        internal const string AiFearId = ModConstants.ModId + ".ai_fear";
        internal const string GpuType = "gpu";
        internal const string CryptoType = "crypto";
        private static bool enabled;
        private static readonly List<StoreEventBlueprint> registeredBlueprints = new List<StoreEventBlueprint>();
        private static readonly EventSpec[] specs =
        {
            new EventSpec(CryptoBoomId, "event_crypto_boom",
                new[] { ElectronicsEventClients.CryptoFanId, ElectronicsEventClients.CryptoSecondhandId, ElectronicsEventClients.CryptoUpperId },
                new Modifier(GpuType, 35), new Modifier(CryptoType, 50)),
            new EventSpec(AstarBreakthroughId, "event_astar_breakthrough",
                new[] { ElectronicsEventClients.AstarFanId, ElectronicsEventClients.AstarInvestorId, ElectronicsEventClients.AstarDesperateId },
                new Modifier(ElectronicsMarket.MainType, 40), new Modifier(DataBuyers.MainType, 40)),
            new EventSpec(ElectronicsSurgeId, "event_electronics_surge",
                new[] { ElectronicsEventClients.ElectronicsEnthusiastId, ElectronicsEventClients.ElectronicsRepairOwnerId, ElectronicsEventClients.ElectronicsHoarderId },
                new Modifier(ElectronicsMarket.MainType, 25)),
            new EventSpec(CryptoCrashId, "event_crypto_crash",
                new[] { ElectronicsEventClients.CryptoDesperateSellerId, ElectronicsEventClients.CryptoAngrySellerId },
                new Modifier(CryptoType, -30)),
            new EventSpec(AiFearId, "event_ai_fear",
                new[] { ElectronicsEventClients.FearAngryId, ElectronicsEventClients.FearDataMerchantId, ElectronicsEventClients.FearResearcherId },
                new Modifier(ElectronicsMarket.MainType, -30), new Modifier(DataBuyers.MainType, -20))
        };

        internal static void Enable() => enabled = true;
        internal static bool Ready => enabled && ElectronicsEventClients.Ready;

        internal static void Configure(ElectronicsEventStockCatalog stock)
        {
            RemoveRegistrations();
            ElectronicsEventClients.Configure(stock);
            if (!enabled) return;
            foreach (EventSpec spec in specs)
            {
                if (StoreEventManager.normalEventBlueprints.Any(blueprint => blueprint.identifier == spec.Id))
                    throw new InvalidOperationException("Event ID already registered: " + spec.Id);
                if (StoreEventActionDict.actions.TryGetValue(spec.ActionId, out var existing) && existing != spec.QueueClient)
                    throw new InvalidOperationException("Event action ID already registered: " + spec.ActionId);
            }
            foreach (EventSpec spec in specs)
            {
                var blueprint = new StoreEventBlueprint(() => CreateEvent(spec), 15, spec.Id);
                registeredBlueprints.Add(blueprint);
                StoreEventManager.normalEventBlueprints.Add(blueprint);
                StoreEventActionDict.actions[spec.ActionId] = spec.QueueClient;
            }
        }

        internal static StoreEvent CreateEvent(EventSpec spec)
        {
            var storeEvent = new StoreEvent
            {
                identifier = spec.Id,
                newsName = Text(spec.TextPrefix + "_news"),
                displayName = Text(spec.TextPrefix + "_display"),
                newsDescription = Text(spec.TextPrefix + "_desc"),
                duration = 2,
                importance = 5,
                eventType = StoreEvent.EventType.NORMALE,
                eventArea = StoreEvent.EventArea.ALL,
                addClientFromEventActionId = spec.ActionId
            };
            foreach (Modifier modifier in spec.Modifiers)
                storeEvent.negociationDatas.Add(new NegociationData(modifier.ItemType, modifier.Value,
                    storeEvent.newsName, storeEvent.displayName));
            return storeEvent;
        }

        private static void QueueDailyClient(EventSpec spec)
        {
            if (!Ready || !PlayerStore.HasInstance) return;
            StoreClientManager manager = PlayerStore.Instance.storeClientManager;
            if (manager == null || spec.ClientIds.Length == 0) return;
            string clientId = spec.ClientIds[RNG.GetRandomInt(0, spec.ClientIds.Length - 1)];
            StoreClient client = ElectronicsEventClients.CreateClient(clientId);
            if (client == null) return;
            client.eventSourceId = spec.Id;
            manager.AddClient(client);
        }

        internal static void Shutdown()
        {
            enabled = false;
            RemoveRegistrations();
            ElectronicsEventClients.Shutdown();
            if (PlayerStore.HasInstance && PlayerStore.Instance.storeClientManager?.clientStack != null)
            {
                var eventIds = new HashSet<string>(specs.Select(spec => spec.Id), StringComparer.Ordinal);
                PlayerStore.Instance.storeClientManager.clientStack.RemoveAll(client => client != null &&
                    eventIds.Contains(client.eventSourceId));
            }
        }

        private static void RemoveRegistrations()
        {
            foreach (StoreEventBlueprint blueprint in registeredBlueprints)
                StoreEventManager.normalEventBlueprints.Remove(blueprint);
            registeredBlueprints.Clear();
            foreach (EventSpec spec in specs)
                if (StoreEventActionDict.actions.TryGetValue(spec.ActionId, out var current) && current == spec.QueueClient)
                    StoreEventActionDict.actions.Remove(spec.ActionId);
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);

        internal sealed class Modifier
        {
            internal readonly string ItemType;
            internal readonly int Value;

            internal Modifier(string itemType, int value)
            {
                ItemType = itemType;
                Value = value;
            }
        }

        internal sealed class EventSpec
        {
            internal readonly string Id, TextPrefix, ActionId;
            internal readonly string[] ClientIds;
            internal readonly Modifier[] Modifiers;
            internal readonly Action QueueClient;

            internal EventSpec(string id, string textPrefix, string[] clientIds, params Modifier[] modifiers)
            {
                Id = id;
                TextPrefix = textPrefix;
                ActionId = id + "addClientAction";
                ClientIds = clientIds;
                Modifiers = modifiers;
                QueueClient = () => QueueDailyClient(this);
            }
        }
    }
}
