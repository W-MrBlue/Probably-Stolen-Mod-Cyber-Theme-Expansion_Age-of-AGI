using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class StoreClient
{
    internal bool useClientBudget;
    internal int Budget;
    internal readonly List<string> clientBuyingTagList = new List<string>();
    internal readonly List<string> clientBuyingIdList = new List<string>();
    internal Func<GameItem, bool> willAcceptItem;
    internal Action<GameItem> onItemAddedToWeighted;
    internal Dialogue acceptDealDialogue;
    internal bool isRequireVariety;
    internal void SetVariety() => isRequireVariety = true;
    internal int cooldown, wholesaleDiscount, Introductions;
    internal bool isOfferWholesale;
    internal string NativeTemplate;
    internal void OnIntroduced() => Introductions++;
    internal int GetBudget() => Budget;
    internal void OverrideBudget(int value) => Budget = value;
}

internal sealed partial class PlayerStore
{
    internal int Attractiveness;
    internal int GetCurrentStoreAttractiveness() => Attractiveness;
}

internal static class RNG
{
    internal static int GetRandomInt(int min, int max) => min;
    internal static bool Roll(int chance) => chance > 0;
    internal static int GetOdd(List<int> weights) => weights.IndexOf(weights.Max());
}

internal static class StoreClientList
{
    internal static StoreClient CreateInventorStorage() => new StoreClient { identifier = "inventorStorage" };
    internal static readonly RandomClientPool generalPool = new RandomClientPool("FACTION_LOWER_LEVEL");
    internal static void AddIfNotOwn(string itemId, HashSet<string> ownedIds)
    {
        if (!ownedIds.Contains(itemId)) PlayerStore.Instance.AddDirectSellingItemToTable(DirectoryMaster.Item(itemId));
    }
    internal static StoreClient CreatePettyThief() => new StoreClient { identifier = "pettyThief", clientFaction = "FACTION_BLACK_MARKET",
        spriteName = "native_shady", clientIntent = StoreClient.ClientIntent.SELL, cooldown = 2,
        isOfferWholesale = true, wholesaleDiscount = -10, NativeTemplate = "petty_thief" };
    internal static StoreClient CreateThief() => new StoreClient { identifier = "thief", clientFaction = "FACTION_BLACK_MARKET",
        spriteName = "native_shady", clientIntent = StoreClient.ClientIntent.SELL, cooldown = 2, NativeTemplate = "thief" };
}

internal static class StoreClientListSupplier
{
    internal static StoreClient CreateScavGenericSeller(string type, string id)
    {
        var client = new StoreClient { identifier = id, clientFaction = "FACTION_LOWER_LEVEL", spriteName = "native_scav",
            clientIntent = StoreClient.ClientIntent.SELL, isOfferWholesale = true, wholesaleDiscount = -15, NativeTemplate = "scavenger" };
        client.tag.Add("SCAV"); return client;
    }
}

internal static class StoreReputation
{
    internal static int LowerLevel, BlackMarket;
    internal static int CurrentClientReward, CurrentClientRewardCalls;
    internal static void ModCurrentClientReputation(int amount, bool notify = true)
    {
        CurrentClientReward += amount;
        CurrentClientRewardCalls++;
    }
    internal static int GetLLReputation() => LowerLevel;
    internal static int GetBMReputation() => BlackMarket;
}

internal static class StoreClientListDict
{
    internal static readonly Dictionary<string, Func<StoreClient>> storeClientDict = new Dictionary<string, Func<StoreClient>>();
}

internal static class StoreClientListTier
{
    // Substitute native scene/localization setup; budgets and category lists follow game 0.46D.
    internal static StoreClient CreateLowerInventor() => Create(145 + StoreStation.Instance.dayCounter / 4, "native_scientist");
    internal static StoreClient CreateInjuredSpacer() => Create(80, "native_scav");
    internal static StoreClient CreateClinicNurse() => Create(145 + StoreStation.Instance.dayCounter / 4, "native_scientist");
    internal static StoreClient CreateStationEngineer() => Create(750 + StoreStation.Instance.dayCounter, "native_engineer");
    internal static StoreClient CreateHospitalAdmin()
    {
        StoreClient client = Create(750 + StoreStation.Instance.dayCounter, "native_upper");
        client.clientFaction = "FACTION_UPPER_LEVEL";
        return client;
    }
    private static StoreClient Create(int budget, string sprite)
    {
        var client = new StoreClient { clientFaction = "FACTION_LOWER_LEVEL", clientIntent = StoreClient.ClientIntent.BUY,
            spriteName = sprite, Budget = budget, useClientBudget = true };
        client.clientBuyingTagList.AddRange(new[] { "MATERIAL", "MODULE", "NODE", "TOOL", "ENERGY_CELL" });
        return client;
    }
}

internal static class ShowcaseHelper
{
    internal static void InitShowcaseSpecialItem(GameItem item, string type) =>
        item.ModifyTag("SHOWCASE_SPECIAL_ITEM_TAG", tag => tag.Enable().SetString(type));
    internal static bool IsShowcaseContainItemById(string id) =>
        EmporiumEntry.Instance.showcaseElement.childItems.Any(item => item.identifier == id);
    internal static List<(string, int, string)> GetDisplayCasePrediction() => new List<(string, int, string)>();
    internal static void ClearPendingSupplierPicks() { }
    internal static int GetTypeTotalValue(string type) => EmporiumEntry.Instance.showcaseElement.childItems
        .Where(item => item.IsTag(type)).Sum(item => (int)item.GetCurrentValue());
    internal static string CreateClientByStockType(string type) => null;
}

internal static partial class ModHook
{
    internal static event Action<List<GameItem>> OnPlaceInventorInventoryItemLate;
    internal static void RaiseInventorStock(List<GameItem> ownedItems) => OnPlaceInventorInventoryItemLate?.Invoke(ownedItems);
}
