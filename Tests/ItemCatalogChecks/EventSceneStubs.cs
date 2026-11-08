using System;
using System.Collections.Generic;

public sealed class StoreEvent
{
    internal enum EventType { NORMALE, THREAT, COSMETIC, INNATE }
    internal enum EventArea { UPPER, LOWER, ALL }
    internal string identifier, newsName, displayName, newsDescription, addClientFromEventActionId;
    internal int duration, importance;
    internal EventType eventType;
    internal EventArea eventArea;
    internal readonly List<NegociationData> negociationDatas = new List<NegociationData>();
}

public sealed class StoreEventBlueprint
{
    internal readonly Func<StoreEvent> storeEventFunc;
    internal readonly int odd;
    internal readonly string identifier;

    internal StoreEventBlueprint(Func<StoreEvent> storeEventFunc, int odd, string identifier)
    {
        this.storeEventFunc = storeEventFunc;
        this.odd = odd;
        this.identifier = identifier;
    }
}

public sealed class NegociationData
{
    internal readonly string itemType, publicDisplay, reason, identifier;
    internal readonly int value;

    internal NegociationData(string itemType, int value, string publicDisplay, string displayName)
    {
        this.itemType = itemType;
        this.value = value;
        this.publicDisplay = publicDisplay;
        reason = displayName;
        identifier = Guid.NewGuid().ToString();
    }
}

internal sealed class StoreEventManager
{
    internal static readonly List<StoreEventBlueprint> normalEventBlueprints = new List<StoreEventBlueprint>();
    internal readonly List<StoreEvent> Activated = new List<StoreEvent>();
    internal void ActivateEvent(StoreEvent storeEvent, bool debugLog = false, bool force = false) => Activated.Add(storeEvent);
}

internal static class StoreEventActionDict
{
    internal static readonly Dictionary<string, Action> actions = new Dictionary<string, Action>();
}
