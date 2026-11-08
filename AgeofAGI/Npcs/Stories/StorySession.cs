using System;

namespace AgeOfAgi.Npcs.Stories
{
    // Ordinary edges, including conditional ones, continue through native chains.
    // Only scene changes start a new conversation after their blackout finishes.
    internal sealed class StorySession
    {
        internal readonly StoryDefinition Definition;
        internal readonly StoryState State;
        internal readonly StoreClient Client;
        internal bool Current => StoryDirector.Enabled && PlayerStore.HasInstance &&
            ReferenceEquals(PlayerStore.Instance.currentClientInstance?.GetClientBlueprint(), Client);
        private bool transitioning;

        internal StorySession(StoryDefinition definition, StoryState state, StoreClient client)
        { Definition = definition; State = state; Client = client; }

        internal void Save() => StoryDirector.Save(Definition.Id, State);
        internal void Flag(string name) { State.Flags[name] = true; Save(); }

        internal Dialogue Build(string id, bool applyEnter = true)
        {
            StoryNode node = Definition.Nodes[id];
            Dialogue head, tail;
            if (node.UseClientGreeting)
            {
                tail = ClientDialogueCatalog.Apply(Client, Definition.Id);
                head = Client.mainDialogue;
            }
            else
            {
                head = new Dialogue();
                tail = ClientDialogueText.SetText(head, node.Speaker, Text(node.TextKey));
            }
            head.SetStartAction(() =>
            {
                if (!Current) return;
                State.Node = id;
                Save();
                if (applyEnter) node.Enter?.Invoke(this);
            });
            foreach (StoryChoice choice in node.Choices)
            {
                // Native bubble choices require a player dummy, then advance to its nextDialogue.
                var player = new Dialogue().SetText("PLAYER", Text(choice.TextKey)).SetDummy();
                if (!Definition.Nodes[choice.Target].Blackout) player.nextDialogue = Build(choice.Target);
                tail.AddChoice(DirectoryMaster.Item("yes"), Text(choice.TextKey), "", player, () =>
                {
                    if (!Current) return;
                    tail.ExecuteEndAction();
                    State.Node = choice.Target;
                    if (choice.Flag != null) Flag(choice.Flag);
                    else Save();
                    if (Definition.Nodes[choice.Target].Blackout) TransitionScene(choice.Target);
                });
            }
            if (node.Choices.Count == 0)
            {
                if (node.Outcome != null)
                    tail.SetEndAction(() => Finish(node));
                else if (node.SelectNext != null)
                {
                    Action start = tail.startAction;
                    tail.SetStartAction(() =>
                    {
                        start?.Invoke();
                        if (Current) LinkNext(tail, node.SelectNext(this));
                    });
                }
                else if (node.Next != null)
                {
                    if (Definition.Nodes[node.Next].Blackout) tail.SetEndAction(() => TransitionScene(node.Next));
                    else tail.nextDialogue = Build(node.Next);
                }
            }
            if (node.AfterText != null)
            {
                Action consequence = tail.endAction;
                tail.SetEndAction(() =>
                {
                    if (!Current) return;
                    node.AfterText(this);
                    consequence?.Invoke();
                });
            }
            return head;
        }

        private void LinkNext(Dialogue tail, string id)
        {
            if (!Definition.Nodes[id].Blackout)
            {
                tail.nextDialogue = Build(id);
                return;
            }
            Action end = tail.endAction;
            tail.SetEndAction(() =>
            {
                end?.Invoke();
                TransitionScene(id);
            });
        }

        private void TransitionScene(string id)
        {
            if (!Current || transitioning) return;
            transitioning = true;
            State.Node = id;
            Save();
            Action during = () =>
            {
                if (!Current) return;
                Definition.Nodes[id].Enter?.Invoke(this);
                if (Definition.Nodes[id].NormalArrival) StoreUIManager.Instance.HideClient();
                else StoreUIManager.Instance.clientGameObject.SetImmediatlyArrive();
            };
            Action after = () =>
            {
                transitioning = false;
                if (!Current) return;
                Client.mainDialogue = Build(id, applyEnter: false);
                if (Definition.Nodes[id].NormalArrival) StoreUIManager.Instance.QueueClientArriveAnimation();
                else DialogUIManager.Instance.StartDialogue(Client.mainDialogue, forcedStart: true);
            };
            StoreUIManager.Instance.StartBlackscreenSequence(true, during, after);
        }

        private void Finish(StoryNode node)
        {
            if (!Current) return;
            State.Complete(node.Outcome, StoreStation.Instance.dayCounter);
            Save();
            if (node.Finish != null) node.Finish(this);
            else PlayerStore.Instance.DismissCurrentClient(callNextWhenPossible: false);
        }

        internal static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }
}
