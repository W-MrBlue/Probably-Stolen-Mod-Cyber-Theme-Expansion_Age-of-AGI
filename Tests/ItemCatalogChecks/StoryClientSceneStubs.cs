using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class StoreClient
{
    internal enum ClientIntent { UNDEFINED, DIALOGUE, SELL, BUY, SELLNBUY }
    internal string identifier, displayName, spriteName, bubbleColor = "DEFAULT", clientFaction, sourceModId, eventSourceId;
    internal Action<GameItem, int> onItemSold;
    internal ClientIntent clientIntent;
    internal bool dismissable = true;
    internal bool nonShootable;
    internal readonly List<string> clientItemFeatureBuying = new List<string>();
    internal void AddBasicDialog() { }
    internal void CompleteClientCreation() { }
    internal Dialogue acceptDealDialogueLastLine;
    internal int DealAcceptedCalls;
    internal Dialogue DealAcceptedHost;
    internal void OnDealAccepted(Dialogue hostLine = null) { DealAcceptedCalls++; DealAcceptedHost = hostLine; }
    internal readonly List<string> tag = new List<string>();
    internal Dialogue mainDialogue = new Dialogue();
}

internal sealed class Dialogue
{
    internal string title, plainText;
    internal Dialogue nextDialogue;
    internal int priority = 5;
    internal Action startAction, endAction;
    internal float endActionDelay = 1.1f;
    internal bool isDummyChoiceDialogue;
    internal bool isEndActionExecuted;
    internal void ExecuteEndAction() { if (!isEndActionExecuted && endAction != null) { isEndActionExecuted = true; endAction(); } }
    internal readonly List<(GameItem Item1, Dialogue Item2, Action Item3)> choices = new();
    internal Dialogue SetDummy() { isDummyChoiceDialogue = true; return this; }
    internal Dialogue AddChoice(GameItem item, string name, string description = "", Dialogue choiceNextDialogue = null, Action choiceAction = null)
    {
        if (item != null) { item.name = name; choices.Add((item, choiceNextDialogue, choiceAction)); }
        return this;
    }
    internal Dialogue SetText(string title, string text) { this.title = title; plainText = text; return this; }
    internal Dialogue SetEndAction(Action action) { endAction = action; return this; }
    internal Dialogue SetStartAction(Action action) { startAction = action; return this; }
    internal Dialogue NextDialogue() => nextDialogue ??= new Dialogue { priority = priority };
}

internal static class StoreClientFactionSetup
{
    internal static void InitLowerLevelCitizen(StoreClient client)
    {
        client.clientFaction = "FACTION_LOWER_LEVEL";
        client.tag.Add("LOWER_LEVEL_CITIZEN");
    }
}

internal sealed class StoreClientManager
{
    internal readonly List<StoreClient> clientStack = new List<StoreClient>();
    internal void AddNextClient(StoreClient client) => clientStack.Insert(0, client);
    internal void AddClient(StoreClient client) => clientStack.Add(client);
}

internal sealed class StoreClientInstance
{
    internal StoreClient Client;
    internal StoreClient GetClientBlueprint() => Client;
}

internal sealed partial class PlayerStore
{
    private static PlayerStore instance;
    internal static bool HasInstance => instance != null;
    internal static bool ForbidLazyCreation;
    internal static int InstanceReads;
    internal static PlayerStore Instance
    {
        get
        {
            InstanceReads++;
            if (instance == null && ForbidLazyCreation)
                throw new NullReferenceException("PlayerStore cannot be constructed before the native scene is ready.");
            return instance;
        }
        set { instance = value; }
    }
    internal StoreClientManager storeClientManager = new StoreClientManager();
    internal StoreClientInstance currentClientInstance;
    internal bool isClientBeingArrested;
    internal int ClientFlags;
    internal void ClientFlagUp() => ClientFlags++;
    internal bool? CallNext;
    internal int Dismissals;
    internal void DismissCurrentClient(bool callNextWhenPossible = true)
    {
        CallNext = callNextWhenPossible;
        Dismissals++;
        currentClientInstance = null;
    }
}

internal sealed class SpriteDict
{
    internal static string GetRandomEngSpriteName() => "native_engineer";
    internal static string GetRandomSecSpriteName() => "native_officer";
    internal static SpriteDict Instance;
    internal readonly Dictionary<string, Sprite> spriteDictionary = new Dictionary<string, Sprite>();
    internal Sprite GetSprite(string key)
    {
        Sprite result = null;
        if (!AgeOfAgi.Npcs.QuorraPortraitPatch.Prefix(key, ref result)) return result;
        return spriteDictionary.TryGetValue(key, out var sprite) ? sprite : new Sprite { pixelsPerUnit = 80 };
    }
}

internal sealed class TextBubble : MonoBehaviour { }

internal sealed class DialogUIManager
{
    internal static readonly DialogUIManager Instance = new DialogUIManager();
    internal Dialogue LastDialogue;
    internal bool RejectStart;
    internal int StartCalls;
    internal bool StartDialogue(Dialogue dialogue, bool forcedStart = false)
    {
        if (RejectStart) return false;
        StartCalls++;
        LastDialogue = dialogue; dialogue.startAction?.Invoke(); return true;
    }
    internal TextBubble clientBubble = new GameObject("ClientBubble").AddComponent<TextBubble>();
    internal GameObject clientNameBubble = new GameObject("ClientName");
    internal void DisplayClientText(Dialogue dialogue) { }
}
