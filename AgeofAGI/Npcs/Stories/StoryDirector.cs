using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AgeOfAgi.Npcs.Stories
{
    internal static class StoryDirector
    {
        private const string StateKey = "story_states";
        private static readonly List<StoryDefinition> definitions = new List<StoryDefinition>();
        private static bool enabled;
        private static ModLog log;
        internal static bool Enabled => enabled;

        internal static void Enable(ModLog value, params StoryDefinition[] stories)
        {
            log = value;
            Shutdown();
            enabled = true;
            definitions.AddRange(stories);
            ModHook.OnNetworkUpgradeUnlocked += Unlocked;
            ModHook.OnGenerateCustomerVeryLate += Generate;
            ModHook.OnGameLoadedLate += Ready;
            ModHook.OnShutterOpenedLate += Ready;
        }

        private static Dictionary<string, StoryState> Load()
        {
            string json = ModHelper.GetModData(ModConstants.ModId, StateKey);
            return string.IsNullOrEmpty(json) ? new Dictionary<string, StoryState>() :
                JsonConvert.DeserializeObject<Dictionary<string, StoryState>>(json) ??
                throw new InvalidOperationException("Story state is null.");
        }

        internal static StoryState Read(string id) => Load().TryGetValue(id, out var state) ? state : new StoryState();
        internal static void Save(string id, StoryState state)
        {
            var states = Load();
            if (states.TryGetValue(id, out var saved))
            {
                foreach (var flag in saved.Flags)
                    if (flag.Value) state.Flags[flag.Key] = true;
            }
            states[id] = state;
            ModHelper.SetModData(ModConstants.ModId, StateKey, JsonConvert.SerializeObject(states));
        }

        private static void Unlocked(string upgradeId)
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance == null) return;
            foreach (var definition in definitions.Where(value => value.UpgradeId == upgradeId))
            {
                StoryState state = Read(definition.Id);
                state.Arm(StoreStation.Instance.dayCounter);
                Save(definition.Id, state);
            }
        }

        private static void Ready()
        {
            if (!PlayerStore.HasInstance) return;
            Generate(PlayerStore.Instance.storeClientManager);
        }

        private static void Generate(StoreClientManager manager)
        {
            if (!enabled || !PlayerStore.HasInstance || manager == null || StoreStation.Instance == null) return;
            foreach (StoryDefinition definition in definitions)
            {
                // A queued/current visit is runtime-only; its persisted node is rebuilt after loading.
                StoryState state = Read(definition.Id);
                if (!state.IsDue(StoreStation.Instance.dayCounter, definition.DelayDays) ||
                    (definition.Condition != null && !definition.Condition(state)) ||
                    IsVisit(PlayerStore.Instance.currentClientInstance?.GetClientBlueprint(), definition.Id) ||
                    manager.clientStack.Any(client => IsVisit(client, definition.Id))) continue;
                manager.AddNextClient(Create(definition));
            }
        }

        private static bool IsVisit(StoreClient client, string id) => client?.identifier == id && client.sourceModId == ModConstants.ModId;

        internal static StoreClient Create(StoryDefinition definition)
        {
            var session = new StorySession(definition, Read(definition.Id), definition.CreateClient());
            string node = string.IsNullOrEmpty(session.State.Node) ? definition.StartNode : session.State.Node;
            // Identity must be ready before the native walk-in renders the actor.
            // Keep delivery and other scene effects in Enter/AfterText.
            definition.Nodes[node].PrepareClient?.Invoke(session);
            session.Client.mainDialogue = session.Build(node);
            return session.Client;
        }

        internal static void Report(string message) => log?.Warning(message);

        internal static void Shutdown()
        {
            enabled = false;
            ModHook.OnNetworkUpgradeUnlocked -= Unlocked;
            ModHook.OnGenerateCustomerVeryLate -= Generate;
            ModHook.OnGameLoadedLate -= Ready;
            ModHook.OnShutterOpenedLate -= Ready;
            if (PlayerStore.HasInstance)
            {
                PlayerStore.Instance.storeClientManager?.clientStack.RemoveAll(client =>
                    client?.sourceModId == ModConstants.ModId && definitions.Any(value => value.Id == client.identifier));
                StoreClient current = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
                if (definitions.Any(value => IsVisit(current, value.Id)))
                {
                    StoreUIManager.Instance.ClearArrestState();
                    PlayerStore.Instance.DismissCurrentClient(callNextWhenPossible: false);
                }
            }
            definitions.Clear();
        }
    }
}
