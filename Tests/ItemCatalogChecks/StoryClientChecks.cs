using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.StoryDocuments;
using AgeOfAgi.Npcs;
using AgeOfAgi.Npcs.Stories;
using UnityEngine;

internal static class StoryClientChecks
{
    private static readonly List<string> seen = new();
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        LoadedMod mod = Program.PrepareLoadedMod(definitions);
        PaperUIManager.PrepareFixture();
        var lifecycle = new AgeOfAgiMod(); lifecycle.Init(mod.Manifest); lifecycle.OnEnable();
        var directory = new ModItemDirectory(); ModHook.RaiseItemDirectoryInit(directory);
        // Synthetic text and factories exercise control flow independently of editable content.
        ModHelper.Translations["en"] = new Dictionary<string, string>();
        foreach (string key in new[] { "intro_dialogue", "officer_search", "officer_doubt", "officer_leave",
            "arrest_reaction", "arrest_warning", "relief", "contraband", "server", "card", "hide_dialogue" })
            ModHelper.Translations["en"]["npc_quorra_" + key] = "fixture_" + key + "_a;fixture_" + key + "_b";
        ModHelper.Translations["en"]["npc_quorra_unknown_name"] = "fixture_unknown";
        ModHelper.Translations["en"]["npc_quorra_officer_name"] = "fixture_officer";
        directory.Add("yes", () => new GameItem { Id = "yes" });
        // Independently chosen prices and detail sizes; no production art or editable CSV is read.
        const string header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer\n";
        var documents = ItemCatalog.Parse(header +
            "age_of_agi.quorra_wanted,story_document,Quorra_wanted_small,17,fixture_wanted_name,,,document,paper_sheet,none,\n" +
            "age_of_agi.quorra_number,story_document,Quorra_number_small,23,fixture_card_name,,,document,carton_card,none,\n", _ => true);
        mod.Sprites["Quorra_wanted_small"] = new TestSprite { rect = new TestRect { width = 32, height = 32 },
            Shape = new GridShapeBuilder().SetData(new byte[] { 1, 1, 1, 1 }, 2) };
        mod.Sprites["Quorra_number_small"] = new TestSprite { rect = new TestRect { width = 32, height = 16 },
            Shape = new GridShapeBuilder().SetData(new byte[] { 1, 1 }, 2) };
        mod.PaperSprites["Quorra_wanted"] = new Sprite { rect = new Rect(0, 0, 192, 240) };
        mod.PaperSprites["Quorra_number"] = new Sprite { rect = new Rect(0, 0, 96, 48) };
        mod.PaperSprites[QuorraDocuments.CloseSpriteKey] = new Sprite { rect = new Rect(0, 0, 14, 15) };
        ModHelper.Translations["en"]["npc_quorra_officer_doubt"] = "... ...;fixture_officer_doubt_a;fixture_officer_doubt_b";
        ModHelper.Translations["en"]["npc_quorra_hide_dialogue"] = "fixture_hide_a;fixture_hide_b;fixture_hide_c;";
        ModHelper.Translations["en"]["quorra_choice_hide"] = "???";
        foreach (var factory in ItemFactoryDirectory.CreateFactories(new CatalogItemFactory(mod.Manifest), documents))
            directory.Add(factory.Key, factory.Value);
        try
        {
            PlayerStore.Instance = null; PlayerStore.ForbidLazyCreation = true;
            Assert(!QuorraStoryClient.QueueFirst(), "Main-menu queueing must not instantiate PlayerStore.");
            PlayerStore.ForbidLazyCreation = false; ResetScene();
            Sprite portrait = null;
            Assert(!QuorraPortraitPatch.Prefix(QuorraStoryClient.SpriteId, ref portrait) && portrait.pixelsPerUnit == 80 &&
                portrait.texture == mod.Textures[QuorraStoryClient.TextureKey], "Use the owned portrait at native scale.");
            Sprite reused = null; QuorraPortraitPatch.Prefix(QuorraStoryClient.SpriteId, ref reused);
            Assert(ReferenceEquals(portrait, reused) && QuorraPortraitPatch.Prefix("mentor", ref reused), "Cache only the mod portrait.");
            StoreStation.Instance.dayCounter = 41;
            ModHook.RaiseUpgradeUnlocked("unrelated"); ModHook.RaiseStoryGenerate(PlayerStore.Instance.storeClientManager);
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Count == 0, "Unrelated upgrade cannot trigger arrival.");
            ModHook.RaiseUpgradeUnlocked(ElectronicsTrader.UpgradeId); ModHook.RaiseStoryGenerate(PlayerStore.Instance.storeClientManager);
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Count == 0, "Do not arrive on purchase day.");
            StoreStation.Instance.dayCounter++;
            ModHook.RaiseUpgradeUnlocked(ElectronicsTrader.UpgradeId);
            var prior = new StoreClient { identifier = "prior" };
            PlayerStore.Instance.storeClientManager.clientStack.Add(prior);
            ModHook.RaiseStoryGenerate(PlayerStore.Instance.storeClientManager); ModHook.RaiseStoryGenerate(PlayerStore.Instance.storeClientManager);
            var queue = PlayerStore.Instance.storeClientManager.clientStack;
            Assert(queue.Count == 2 && queue[1] == prior && StoryDirector.Read(QuorraStoryClient.ClientId).TriggerDay == 41,
                "Queue once on the next day, with priority; repeated unlock cannot reset the date.");
            StoreClient girl = queue[0]; queue.RemoveAt(0); Activate(girl);
            Assert(!girl.dismissable && girl.nonShootable && girl.clientFaction == "FACTION_LOWER_LEVEL", "Use a safe dialogue-only citizen visit.");
            Dialogue hide = Play(girl.mainDialogue);
            Assert(hide.choices.Count == 1 && hide.choices[0].Item1.name == "???", "Arrival offers the requested question-mark response.");
            hide.choices[0].Item3();
            Assert(StoreUIManager.Instance.Blackouts == 0 && StoryDirector.Read(QuorraStoryClient.ClientId).Node == "hide",
                "Selecting the response commits the hiding reply without starting the blackout early.");
            seen.Clear();
            Dialogue police = Play(hide.choices[0].Item2.nextDialogue);
            Assert(StoreUIManager.Instance.Blackouts == 1 && girl.displayName == "fixture_officer" && police.choices.Count == 2,
                "Shelter cuts through one blackout to the officer and report choices.");
            Assert(seen.Take(3).SequenceEqual(new[] { "fixture_hide_a", "fixture_hide_b", "fixture_hide_c" }) &&
                seen[3] == "fixture_officer_search_a", "All three reply bubbles finish before the officer arrival and dialogue.");
            GameItem notice = PlayerStore.Instance.FindAllItem().Single(item => item.identifier == QuorraDocuments.WantedItemId);
            Assert(AudioManager.Instance.Played.Count(clip => clip == notice.soundDragEnd) == 1,
                "The successfully delivered wanted notice plays its configured drop sound once.");
            Assert(notice.shape.width == 2 && notice.shape.height == 2 && notice.mayActivateSlotItemFunc(notice, null, null), "The owned notice is a readable 2x2 item.");
            notice.onActivateSlotItemFunc(notice, null, null);
            PaperUI wantedPanel = PaperUI.Created.Last();
            Assert(wantedPanel.Data.chrome.Single(element => element.action == "close").sprite == "age_of_agi:paper_buttons_close3",
                "The notice close control uses the supplied X artwork in its persistent chrome definition.");
            Assert(wantedPanel.Visible && wantedPanel.Background == "age_of_agi:Quorra_wanted" &&
                wantedPanel.Width == 192 && wantedPanel.Height == 240 && wantedPanel.Texts.Count == 0 &&
                notice.SpriteKey == "Quorra_wanted_small" && notice.soundDragStart == "paper_sheet_drag",
                "Use the supplied thumbnail and full detail image at its own aspect ratio without generated text overlays.");
            QuorraDocuments.DeliverPending();
            Assert(PlayerStore.Instance.FindAllItem().Count(item => item.identifier == QuorraDocuments.WantedItemId) == 1, "Delivery is idempotent.");
            Assert(AudioManager.Instance.Played.Count(clip => clip == notice.soundDragEnd) == 1, "Duplicate claims do not replay drop audio.");
            var saved = PlayerStore.Instance.ModData.ToDictionary(pair => pair.Key, pair => pair.Value);
            ResetScene(); foreach (var pair in saved) PlayerStore.Instance.ModData[pair.Key] = pair.Value;
            ModHook.RaiseStoryGenerate(PlayerStore.Instance.storeClientManager);
            girl = PlayerStore.Instance.storeClientManager.clientStack[0]; Activate(girl);
            Assert(girl.displayName == "fixture_officer" && girl.spriteName == "native_officer",
                "A resumed officer visit uses the officer identity before the native arrival animation starts.");
            police = Play(girl.mainDialogue);
            Assert(police.choices.Count == 2 && girl.displayName == "fixture_officer" && PlayerStore.Instance.FindAllItem().Count == 0,
                "Load resumes the officer decision without duplicating its previously delivered document.");
            seen.Clear();
            Dialogue doubt = Choose(police, 1);
            Assert(seen[0] == "... ..." && seen[1] == "fixture_officer_doubt_a",
                "The officer's authored pause is displayed once as one bubble before the follow-up question.");
            var server = new GameItem { Id = "age_of_agi.serverbox", Owned = true };
            var illegal = new GameItem { Id = "fixture_illegal", Owned = true }; illegal.EnableTag("CONTRABAND_ITEM_TAG");
            GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, server); GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, illegal);
            seen.Clear(); Choose(doubt, 1);
            StoryState state = StoryDirector.Read(QuorraStoryClient.ClientId);
            Assert(state.Outcome == "protected" && state.Has("contact_received") && state.CompletedDay == 42 &&
                seen.Contains("fixture_contraband_a") && seen.Contains("fixture_server_a"), "Protection records contact and conditional shop lines.");
            GameItem phone = PlayerStore.Instance.FindAllItem().Single(item => item.identifier == QuorraDocuments.PhoneCardItemId);
            var front = EmporiumEntry.Instance.frontInvinvElement;
            Assert(phone.parentInventory == front && phone.modifiedShape.minX == (front.Width - 2) / 2 && phone.modifiedShape.minY == front.Height - 1 &&
                AudioManager.Instance.Played.Count(clip => clip == phone.soundDragEnd) == 1,
                "The phone card is delivered at the front counter's bottom centre with one configured drop sound.");
            Assert(state.Has("phone_card_delivered") && phone.shape.width == 2 && phone.shape.height == 1 &&
                phone.SpriteKey == "Quorra_number_small" && phone.soundDragStart == "carton_card_drag",
                "The protection branch hands over one owned 2x1 phone card with its configured thumbnail and audio.");
            phone.onActivateSlotItemFunc = null; phone.onLoaded(phone);
            phone.onActivateSlotItemFunc(phone, null, null);
            GameObject nativeCard = PaperUIManager.Instance.JoeWildBusinessCard;
            GameObject phonePanel = nativeCard.transform.parent.Children.Select(child => child.gameObject)
                .Single(child => ReferenceEquals(child.CloneSource, nativeCard));
            Assert(phonePanel.activeSelf && phonePanel.transform.sizeDelta.x == 420 && phonePanel.transform.sizeDelta.y == 240 &&
                phonePanel.transform.localScale.x == 1.25f &&
                phonePanel.GetComponent<UnityEngine.UI.Image>().sprite == mod.PaperSprites["Quorra_number"] &&
                !phonePanel.GetComponent<StoreUI>().zoomable && !nativeCard.activeSelf,
                "Use the native business-card layout and authored scale with Quorra artwork, without opening or modifying Joe's card.");
            var closeButton = phonePanel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(button => button.gameObject.activeSelf);
            var closeImage = closeButton.targetGraphic as UnityEngine.UI.Image;
            Assert(closeImage != null && closeImage.enabled && closeImage.sprite == mod.PaperSprites[QuorraDocuments.CloseSpriteKey] &&
                closeButton.transition == UnityEngine.UI.Selectable.Transition.None,
                "The phone card has a visible supplied X icon even when the native template used a text glyph.");
            Assert(phonePanel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Count(button => button.gameObject.activeSelf) == 1,
                "The phone card has only its native close control, with no zoom button.");
            closeButton.onClick.Invoke();
            Assert(!phonePanel.activeSelf && !nativeCard.activeSelf, "Close only the owned phone-card panel.");
            phone.onActivateSlotItemFunc(phone, null, null);
            Assert(phonePanel.activeSelf, "Closed business-card panels can reopen.");
            QuorraDocuments.DeliverPending();
            Assert(PlayerStore.Instance.FindAllItem().Count(item => item.identifier == QuorraDocuments.PhoneCardItemId) == 1,
                "Reopening or retrying never duplicates an awarded phone card.");
            PlayerStore.Instance.storeClientManager.clientStack.Clear(); ModHook.RaiseStoryGenerate(PlayerStore.Instance.storeClientManager);
            Assert(PlayerStore.Instance.storeClientManager.clientStack.Count == 0, "Completed stories do not return naturally.");
            foreach (bool denyFirst in new[] { false, true })
            {
                ResetScene(); girl = QuorraStoryClient.Create(); Activate(girl);
                police = Choose(Play(girl.mainDialogue), 0); if (denyFirst) police = Choose(police, 1);
                Choose(police, 0); state = StoryDirector.Read(QuorraStoryClient.ClientId);
                Assert(state.Outcome == "arrested" && state.Has("reported") && !state.Has("contact_received") &&
                    !state.Has("phone_card_pending") && !PlayerStore.Instance.FindAllItem().Any(item => item.identifier == QuorraDocuments.PhoneCardItemId) &&
                    StoreUIManager.Instance.ArrestFinishes == 1 && StoreUIManager.Instance.secGameObject.Snaps == 1 &&
                    StoreUIManager.Instance.secGameObject2.Snaps == 1 && PlayerStore.Instance.ClientFlags == 0,
                    "Both report routes use two officers and native arrest cleanup without contact or leaked flags.");
            }
            ResetScene();
            StoryDirector.Save(QuorraStoryClient.ClientId, new StoryState { TriggerDay = 40, Node = "arrest_warning" });
            ModHook.RaiseStoryGenerate(PlayerStore.Instance.storeClientManager);
            girl = PlayerStore.Instance.storeClientManager.clientStack[0]; Activate(girl); Play(girl.mainDialogue);
            Assert(StoryDirector.Read(QuorraStoryClient.ClientId).Outcome == "arrested" && StoreUIManager.Instance.secGameObject.Snaps == 1,
                "Reload in the arrest warning reconstructs both actors and completes the saved route.");
            ResetScene();
            foreach (var inv in Inventories()) inv.overrideLockInsert = true;
            girl = QuorraStoryClient.Create(); Activate(girl); police = Choose(Play(girl.mainDialogue), 0);
            Assert(!StoryDirector.Read(QuorraStoryClient.ClientId).Has("wanted_delivered"), "Full storage cannot claim delivery success.");
            EmporiumEntry.Instance.invElement.overrideLockInsert = false; QuorraDocuments.DeliverPending();
            seen.Clear(); Choose(Choose(police, 1), 1);
            Assert(StoryDirector.Read(QuorraStoryClient.ClientId).Has("wanted_delivered") &&
                !seen.Contains("fixture_contraband_a") && !seen.Contains("fixture_server_a") && seen.Contains("fixture_card_a"),
                "Retry survives writes from an active session; an empty shop skips optional claims and still receives contact.");
            CheckPendingPhoneReload();
            CheckOccupiedCardPosition();
            CheckNativeConditionalContinuation();
            ResetScene(); girl = QuorraStoryClient.Create(); Activate(girl); hide = Play(girl.mainDialogue);
            Activate(prior); hide.choices[0].Item3(); StoreUIManager.Instance.FlushStoryActions();
            Assert(StoryDirector.Read(QuorraStoryClient.ClientId).Node == "arrival" && PlayerStore.Instance.Dismissals == 0,
                "Stale callbacks cannot alter another customer or dismiss it.");
            Activate(girl); lifecycle.OnDisable();
            Assert(PlayerStore.Instance.currentClientInstance == null && portrait.Destroyed, "Disable releases the owned visit and portrait.");
            Assert(phonePanel.Destroyed && !wantedPanel.Visible && !mod.PaperSprites["Quorra_number"].Destroyed,
                "Disable closes owned document panels while leaving loader-owned artwork intact.");
        }
        finally
        {
            PlayerStore.ForbidLazyCreation = false; lifecycle.OnDisable(); PlayerStore.Instance = null; SpriteDict.Instance = null;
            ModHelper.Translations.Clear(); ModHelper.Locale = "en";
        }
        Console.WriteLine("PASS: next-day story, arrest/protection routes, supplied document images, 2x2 notice/2x1 phone card, separate detail panels, pending delivery/reload and lifecycle guards.");
    }

    private static void CheckPendingPhoneReload()
    {
        ResetScene();
        var girl = QuorraStoryClient.Create(); Activate(girl);
        Dialogue police = Choose(Play(girl.mainDialogue), 0);
        Dialogue doubt = Choose(police, 1);
        foreach (var inv in Inventories()) inv.overrideLockInsert = true;
        Choose(doubt, 1);
        StoryState state = StoryDirector.Read(QuorraStoryClient.ClientId);
        Assert(state.Outcome == "protected" && state.Has("contact_received") && state.Has("phone_card_pending") &&
            !state.Has("phone_card_delivered") && !AudioManager.Instance.Played.Contains("carton_card_drop"),
            "Full storage preserves the protected outcome and pending phone-card transaction without a false drop sound.");
        var saved = PlayerStore.Instance.ModData.ToArray();
        ResetScene(); foreach (var entry in saved) PlayerStore.Instance.ModData[entry.Key] = entry.Value;
        ModHook.RaiseLoaded();
        QuorraDocuments.DeliverPending();
        Assert(StoryDirector.Read(QuorraStoryClient.ClientId).Has("phone_card_delivered") &&
            PlayerStore.Instance.FindAllItem().Count(item => item.identifier == QuorraDocuments.PhoneCardItemId) == 1 &&
            !PlayerStore.Instance.FindAllItem().Any(item => item.identifier == QuorraDocuments.WantedItemId),
            "Loading delivers only the pending phone card, once, without replaying the story or restoring the already-awarded notice.");
        Assert(AudioManager.Instance.Played.Count(clip => clip == "carton_card_drop") == 1, "A successful retry sounds once, not on subsequent claims.");
    }

    private static void CheckOccupiedCardPosition()
    {
        ResetScene();
        var front = EmporiumEntry.Instance.frontInvinvElement;
        var obstruction = new GameItem { Id = "fixture_obstruction" };
        var position = new GridShapeBuilder(obstruction.modifiedShape).SetPosition((front.Width - 2) / 2, front.Height - 1);
        front.TryInventorySlot(obstruction, position).TryAcceptOnce();
        var girl = QuorraStoryClient.Create(); Activate(girl);
        Dialogue police = Choose(Play(girl.mainDialogue), 0);
        Choose(Choose(police, 1), 1);
        var card = front.childItems.Single(item => item.identifier == QuorraDocuments.PhoneCardItemId);
        Assert(StoryDirector.Read(QuorraStoryClient.ClientId).Has("phone_card_delivered") &&
            card.modifiedShape.minX == (front.Width - 2) / 2 + 1 && card.modifiedShape.minY == front.Height - 1 &&
            obstruction.parentInventory == front && obstruction.modifiedShape.minX == (front.Width - 2) / 2 &&
            obstruction.modifiedShape.minY == front.Height - 1 &&
            AudioManager.Instance.Played.Count(clip => clip == card.soundDragEnd) == 1,
            "An occupied centre chooses the nearest valid position, favouring the bottom row, without moving existing items.");
        QuorraDocuments.DeliverPending();
        Assert(front.childItems.Count(item => item.identifier == QuorraDocuments.PhoneCardItemId) == 1 &&
            AudioManager.Instance.Played.Count(clip => clip == card.soundDragEnd) == 1,
            "A nearby placement is committed once and retries cannot duplicate the card or its drop sound.");
    }

    private static void CheckNativeConditionalContinuation()
    {
        ResetScene();
        ModHelper.Translations["en"]["fixture_timing_first"] = "Timing first;Timing tail";
        ModHelper.Translations["en"]["fixture_timing_next"] = "Timing next";
        var definition = new StoryDefinition { Id = "age_of_agi.fixture_timing", StartNode = "first" };
        int nextEntries = 0;
        definition.Nodes.Add("first", new StoryNode { TextKey = "fixture_timing_first", SelectNext = _ => "next" });
        definition.Nodes.Add("next", new StoryNode { TextKey = "fixture_timing_next", Enter = _ => nextEntries++ });
        var client = new StoreClient { identifier = definition.Id };
        var session = new StorySession(definition, new StoryState(), client);
        Activate(client);
        Dialogue head = session.Build("first");
        DialogUIManager.Instance.StartDialogue(head);
        int starts = DialogUIManager.Instance.StartCalls;
        Dialogue tail = head.nextDialogue;
        tail.startAction();
        Assert(tail.endAction == null && tail.nextDialogue?.plainText == "Timing next" && nextEntries == 0 &&
            StoreUIManager.Instance.PendingStoryActions.Count == 0 && DialogUIManager.Instance.StartCalls == starts,
            "Conditional continuation links the native chain without a timed end action, queued delay, forced restart or premature next-node effects.");
        tail.ExecuteEndAction();
        Assert(session.State.Node == "first" && nextEntries == 0, "Finishing text cannot force advancement while the native UI is waiting.");
        tail.nextDialogue.startAction();
        Assert(session.State.Node == "next" && nextEntries == 1 && DialogUIManager.Instance.StartCalls == starts,
            "Only native chain advancement enters the next conditional node; it does not start another conversation.");
    }

    private static GameInventory[] Inventories() => new GameInventory[] { EmporiumEntry.Instance.frontInvinvElement,
        EmporiumEntry.Instance.backInvinvElement, EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.showcaseElement };
    private static void ResetScene()
    {
        PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry(); StoreUIManager.Instance = new StoreUIManager();
        AudioManager.Instance.Played.Clear();
        SpriteDict.Instance = new SpriteDict(); SpriteDict.Instance.spriteDictionary.Add("mentor", new Sprite { pixelsPerUnit = 80 });
        StoreStation.Instance.dayCounter = 42; ModHelper.Locale = "en";
    }
    private static void Activate(StoreClient client) => PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = client };
    private static Dialogue Play(Dialogue head, bool started = false)
    {
        if (head == null) return null;
        if (!started) DialogUIManager.Instance.StartDialogue(head);
        for (Dialogue line = head; line != null; line = line.nextDialogue)
        {
            if (line != head) line.startAction?.Invoke();
            DialogUIManager.Instance.DisplayClientText(line);
            seen.Add(line.plainText);
            if (line.plainText.StartsWith("fixture_hide_"))
                Assert(StoreUIManager.Instance.Blackouts == 0 && !StoreUIManager.Instance.AwaitingArrival,
                    "Each hiding reply line plays before the blackout or officer walk begins.");
            if (line.plainText.StartsWith("fixture_officer_search") && !StoryDirector.Read(QuorraStoryClient.ClientId).Has("wanted_delivered"))
                Assert(!PlayerStore.Instance.FindAllItem().Any(item => item.identifier == QuorraDocuments.WantedItemId),
                    "No wanted document is placed before the final officer question has finished.");
            line.ExecuteEndAction();
            if (line.choices.Count > 0) return line;
            if (StoreUIManager.Instance.PendingStoryActions.Count > 0)
                return ResumeSceneTransition();
        }
        return null;
    }
    private static Dialogue Choose(Dialogue choice, int index)
    {
        var selected = choice.choices[index]; selected.Item3?.Invoke();
        if (StoreUIManager.Instance.PendingStoryActions.Count > 0)
            return ResumeSceneTransition();
        return Play(selected.Item2.nextDialogue);
    }

    private static Dialogue ResumeSceneTransition()
    {
        StoreUIManager.Instance.FlushStoryActions();
        if (StoreUIManager.Instance.AwaitingArrival)
        {
            Assert(StoreUIManager.Instance.NormalArrivals == 1 && !StoryDirector.Read(QuorraStoryClient.ClientId).Has("wanted_pending"),
                "After the shelter blackout the officer walks in normally before speaking or handing over the notice.");
            StoreUIManager.Instance.CompleteNormalArrival();
        }
        return Play(DialogUIManager.Instance.LastDialogue, true);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
