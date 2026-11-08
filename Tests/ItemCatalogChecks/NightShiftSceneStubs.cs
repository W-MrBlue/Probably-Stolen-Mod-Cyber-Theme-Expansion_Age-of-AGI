using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class ModHook
{
    internal static event Action<StoreClientManager> OnGenerateCustomerEarly;
    internal static event Action OnGameLoadedLate;
    internal static event Action<string> OnNewGameStarted;
    internal static event Action OnShutterOpenedLate;
    internal static void RaiseGenerate(StoreClientManager manager) => OnGenerateCustomerEarly?.Invoke(manager);
    internal static void RaiseLoaded() => OnGameLoadedLate?.Invoke();
    internal static void RaiseNewGame() => OnNewGameStarted?.Invoke("default");
    internal static void RaiseOpened() => OnShutterOpenedLate?.Invoke();
}

internal static partial class ModHelper
{
    internal static readonly Dictionary<string, Func<string>> TabLabels = new Dictionary<string, Func<string>>();
    internal static void SetNetworkTabLabel(string tab, Func<string> label)
    {
        if (label == null) TabLabels.Remove(tab);
        else TabLabels[tab] = label;
    }
    internal sealed class Service
    {
        internal int Cost;
        internal string Owner, Icon;
        internal string[] Prerequisites;
        internal Func<string> Title, Description;
        internal Action Unlock;
        internal NetworkUpgrade Definition;
    }
    internal static readonly Dictionary<string, Service> Services = new Dictionary<string, Service>();
    internal static void AddNetworkUpgrade(string modId, string id, int cost, string subtitle,
        Func<string> title, Func<string> description, Action onUnlock, string tab = null, string iconKey = null,
        string[] prerequisites = null, int cooldownDays = 0, Func<string> subtitleLabel = null,
        Func<string> alreadyBought = null, bool recommended = false)
    {
        // Native RegisterModUpgrade reads this lazy singleton even though this
        // API looks like a definition-only registration method.
        _ = PlayerStore.Instance;
        Services[id] = new Service { Owner = modId, Cost = cost, Title = title, Description = description,
            Unlock = onUnlock, Icon = iconKey, Prerequisites = prerequisites,
            Definition = new NetworkUpgrade { id = id, subtitle = subtitle, tab = tab ?? "", recommended = recommended,
                prerequisite = new List<string>(prerequisites ?? Array.Empty<string>()) } };
        if (!PlayerStore.Instance.networkUpgrade.ContainsKey(id))
            PlayerStore.Instance.networkUpgrade[id] = new NetworkUpgrade { id = id, state = 1 };
    }
    internal static int RemoveNetworkUpgrades(string modId)
    {
        var ids = Services.Where(pair => pair.Value.Owner == modId).Select(pair => pair.Key).ToArray();
        foreach (string id in ids) Services.Remove(id);
        return ids.Length;
    }
    internal static bool IsNetworkUpgradeUnlocked(string id) => PlayerStore.Instance?.UnlockedServices.Contains(id) == true ||
        PlayerStore.Instance?.networkUpgrade.TryGetValue(id, out var upgrade) == true && upgrade.state == 2;
    internal static string GetModData(string modId, string key, string fallback = null) =>
        PlayerStore.Instance != null && PlayerStore.Instance.ModData.TryGetValue(modId + ":" + key, out var value) ? value : fallback;
    internal static void SetModData(string modId, string key, string value) => PlayerStore.Instance.ModData[modId + ":" + key] = value;
}

internal sealed partial class PlayerStore
{
    internal readonly HashSet<string> UnlockedServices = new HashSet<string>();
    internal readonly Dictionary<string, NetworkUpgrade> networkUpgrade = new Dictionary<string, NetworkUpgrade>();
    internal readonly Dictionary<string, string> ModData = new Dictionary<string, string>();
    internal bool isStolenItemIntroduced;
    internal readonly Dictionary<GameItem, bool> StolenRequests = new Dictionary<GameItem, bool>();
    internal void AddDirectSellingItemToTable(GameItem item, bool isOwend = false, bool isStolen = false, bool ignorePerk = false, int forcedHeat = 0)
    {
        TryAddDirectSellingItemToTable(item, isOwend, isStolen, ignorePerk, forcedHeat);
    }
    internal bool TryAddDirectSellingItemToTable(GameItem item, bool isOwend = false, bool isStolen = false, bool ignorePerk = false, int forcedHeat = 0)
    {
        item.Owned = isOwend;
        StolenRequests[item] = isStolen;
        if (isStolen && isStolenItemIntroduced && item.GetBaseValue() > 17)
        {
            item.EnableTag("STOLEN_TAG");
            item.ModifyTag("STOLEN_VALUE_INT", tag => tag.Enable().SetInt(forcedHeat > 0 ? forcedHeat : (int)item.GetBaseValue()));
        }
        if (GraphUtils.TryAcceptAll(EmporiumEntry.Instance.frontInvinvElement, item) != 0) { item.Destroy(); return false; }
        return true;
    }
}

public sealed class NetworkUpgrade
{
    internal string id, subtitle, tab;
    internal bool recommended;
    internal int state;
    internal List<string> prerequisite = new List<string>();
    internal static NetworkUpgrade GetDefinition(string id) => ModHelper.Services.TryGetValue(id, out var service) ? service.Definition : null;
}

internal static class WildNetworkUI
{
    internal static void Open(bool debug = false) { }
}

internal sealed class EmporiumEntry
{
    internal static EmporiumEntry Instance;
    internal readonly GameGridInventory backInvinvElement = new GameGridInventory(30, 30);
    internal readonly GameGridInventory invElement = new GameGridInventory(30, 30);
    internal readonly GameGridInventory showcaseElement = new GameGridInventory(30, 30);
    internal readonly GameGridInventory frontInvinvElement = new GameGridInventory(30, 30);
    internal List<GameItem> GetAllItems() => new[] { backInvinvElement, invElement, showcaseElement, frontInvinvElement }
        .SelectMany(inv => inv.childItems).ToList();
}
