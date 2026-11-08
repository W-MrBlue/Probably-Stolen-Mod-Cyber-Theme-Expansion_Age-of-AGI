using System.Linq;
using AgeOfAgi.Npcs.Stories;
using AgeOfAgi.Items.Logic.StoryDocuments;

namespace AgeOfAgi.Npcs
{
    internal static class QuorraStory
    {
        internal static StoryDefinition CreateDefinition()
        {
            var story = new StoryDefinition
            {
                Id = QuorraStoryClient.ClientId, UpgradeId = ElectronicsTrader.UpgradeId,
                StartNode = "arrival", CreateClient = QuorraStoryClient.CreateBlueprint
            };
            story.Nodes.Add("arrival", new StoryNode { UseClientGreeting = true, Enter = UnknownGirl });
            story.Nodes["arrival"].Choices.Add(new StoryChoice("quorra_choice_hide", "hide", "shelter_agreed"));
            story.Nodes.Add("hide", new StoryNode { TextKey = "npc_quorra_hide_dialogue", Enter = UnknownGirl, Next = "officer" });
            story.Nodes.Add("officer", new StoryNode { TextKey = "npc_quorra_officer_search", Blackout = true, NormalArrival = true,
                PrepareClient = PrepareOfficer, Enter = Officer, AfterText = session => QuorraDocuments.TryDeliver(session, QuorraDocuments.Wanted) });
            AddReportChoices(story.Nodes["officer"], "quorra_choice_deny", "doubt");
            story.Nodes.Add("doubt", new StoryNode { TextKey = "npc_quorra_officer_doubt", PrepareClient = PrepareOfficer, Enter = Officer });
            AddReportChoices(story.Nodes["doubt"], "quorra_choice_deny_again", "officer_leaves");
            story.Nodes.Add("officer_leaves", new StoryNode { TextKey = "npc_quorra_officer_leave", PrepareClient = PrepareOfficer, Enter = Officer, Next = "safe" });
            story.Nodes.Add("safe", new StoryNode { TextKey = "npc_quorra_relief", Blackout = true, Enter = UnknownGirl,
                SelectNext = session => HasContraband() ? "contraband" : AfterJoke() });
            story.Nodes.Add("contraband", new StoryNode { TextKey = "npc_quorra_contraband", Enter = UnknownGirl,
                SelectNext = session => AfterJoke() });
            story.Nodes.Add("server", new StoryNode { TextKey = "npc_quorra_server", Enter = UnknownGirl, Next = "card" });
            story.Nodes.Add("card", new StoryNode { TextKey = "npc_quorra_card", Enter = session =>
                { UnknownGirl(session); QuorraDocuments.TryDeliver(session, QuorraDocuments.PhoneCard); },
                AfterText = session => { Show(session, "npc_quorra_name", QuorraStoryClient.SpriteId); session.Flag("contact_received"); },
                Outcome = "protected" });
            story.Nodes.Add("arrest", new StoryNode { TextKey = "npc_quorra_arrest_reaction", Blackout = true,
                Enter = Arrest, Next = "arrest_warning" });
            story.Nodes.Add("arrest_warning", new StoryNode { TextKey = "npc_quorra_arrest_warning", Speaker = "SEC",
                Enter = Arrest, Outcome = "arrested", Finish = session => StoreUIManager.Instance.OnArrestDialoguePlayed() });
            return story;
        }

        private static void AddReportChoices(StoryNode node, string denyKey, string denyTarget)
        {
            node.Choices.Add(new StoryChoice("quorra_choice_report", "arrest", "reported"));
            node.Choices.Add(new StoryChoice(denyKey, denyTarget, "denied_sighting"));
        }

        private static void Show(StorySession session, string name, string sprite)
        {
            session.Client.displayName = StorySession.Text(name);
            session.Client.spriteName = sprite;
            var ui = StoreUIManager.Instance;
            ui.clientGameObject.image.sprite = SpriteDict.Instance.GetSprite(sprite);
        }

        private static void UnknownGirl(StorySession session)
        {
            session.Client.clientFaction = "FACTION_LOWER_LEVEL";
            Show(session, "npc_quorra_unknown_name", QuorraStoryClient.SpriteId);
        }
        private static void Officer(StorySession session)
        {
            PrepareOfficer(session);
            Show(session, "npc_quorra_officer_name", session.Client.spriteName);
        }

        private static void PrepareOfficer(StorySession session)
        {
            session.Client.clientFaction = "FACTION_SECURITY";
            const string key = "officer_sprite";
            if (!session.State.Values.TryGetValue(key, out string sprite))
            {
                session.State.Values[key] = sprite = SpriteDict.GetRandomSecSpriteName();
                session.Save();
            }
            session.Client.displayName = StorySession.Text("npc_quorra_officer_name");
            session.Client.spriteName = sprite;
        }

        private static void Arrest(StorySession session)
        {
            UnknownGirl(session);
            var ui = StoreUIManager.Instance;
            if (PlayerStore.Instance.isClientBeingArrested) return;
            PlayerStore.Instance.isClientBeingArrested = true;
            PlayerStore.Instance.ClientFlagUp();
            PlayerStore.Instance.ClientFlagUp();
            ui.secGameObject.image.sprite = SpriteDict.Instance.GetSprite(SpriteDict.GetRandomSecSpriteName());
            ui.secGameObject2.image.sprite = SpriteDict.Instance.GetSprite(SpriteDict.GetRandomSecSpriteName());
            // Use native snap/leave paths and flag accounting, without OnGenericArrived
            // dispatching an unrelated criminal dialogue or issuing a bounty.
            ui.secGameObject.SetImmediatlyArrive();
            ui.secGameObject2.SetImmediatlyArrive();
            if (!ui.StoreGlass.IsClosed) ui.StoreGlass.ToggleShutterDoor();
        }

        private static bool HasContraband() => PlayerStore.Instance.FindAllItem().Any(ContrabandHelper.IsContraband);
        private static string AfterJoke() => PlayerStore.Instance.FindAllItem().Any(item => item.identifier == "age_of_agi.serverbox")
            ? "server" : "card";
    }
}
