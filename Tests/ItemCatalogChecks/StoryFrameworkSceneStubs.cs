using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

internal static partial class ModHook
{
    internal static event Action<string> OnNetworkUpgradeUnlocked;
    internal static event Action<StoreClientManager> OnGenerateCustomerVeryLate;
    internal static void RaiseUpgradeUnlocked(string id) => OnNetworkUpgradeUnlocked?.Invoke(id);
    internal static void RaiseStoryGenerate(StoreClientManager manager) => OnGenerateCustomerVeryLate?.Invoke(manager);
}

internal static class MetaHelper
{
    internal static IEnumerator WaitAndDo(float time, Action action) { yield return null; action(); }
}

internal sealed class StorySceneActor
{
    internal sealed class Image { internal Sprite sprite; }
    internal readonly Image image = new();
    internal int Snaps;
    internal void SetImmediatlyArrive() => Snaps++;
}

internal sealed class StoreGlass
{
    internal bool IsClosed;
    internal void ToggleShutterDoor() => IsClosed = !IsClosed;
}

internal sealed partial class StoreUIManager
{
    internal readonly StorySceneActor clientGameObject = new(), secGameObject = new(), secGameObject2 = new();
    internal readonly StoreGlass StoreGlass = new();
    internal readonly Queue<Action> PendingStoryActions = new();
    internal int Blackouts, ArrestFinishes;
    internal bool AwaitingArrival, ClientHidden;
    internal int NormalArrivals;
    internal void HideClient() => ClientHidden = true;
    internal void QueueClientArriveAnimation() { NormalArrivals++; AwaitingArrival = true; ClientHidden = false; }
    internal void CompleteNormalArrival()
    {
        AwaitingArrival = false;
        DialogUIManager.Instance.StartDialogue(PlayerStore.Instance.currentClientInstance.GetClientBlueprint().mainDialogue);
    }
    internal void StartBlackscreenSequence(bool playFullSequence = true, Action actionDuringBlack = null,
        Action actionAfterBlack = null, float waitTime = .25f, float sequenceTime = .2f)
    {
        Blackouts++;
        PendingStoryActions.Enqueue(() => { actionDuringBlack?.Invoke(); actionAfterBlack?.Invoke(); });
    }
    internal void StartCoroutine(IEnumerator routine)
    { PendingStoryActions.Enqueue(() => { while (routine.MoveNext()) { } }); }
    internal void FlushStoryActions() { while (PendingStoryActions.Count > 0) PendingStoryActions.Dequeue()(); }
    internal void ClearArrestState()
    { StoreGlass.IsClosed = false; PlayerStore.Instance.isClientBeingArrested = false; PlayerStore.Instance.ClientFlags = 0; }
    internal void OnArrestDialoguePlayed()
    { ArrestFinishes++; ClearArrestState(); PlayerStore.Instance.DismissCurrentClient(false); }
}
