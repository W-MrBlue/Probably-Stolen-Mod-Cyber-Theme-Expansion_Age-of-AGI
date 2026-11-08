using AgeOfAgi.Npcs;
using System;
using System.Collections.Generic;
using System.Linq;

internal static class ClientDialogueChecks
{
    private const string Header = "client_id,variant,greeting_key,deal_complete_key\n";

    internal static void Run()
    {
        var catalog = ClientDialogueCatalog.Parse(Header +
            "age_of_agi.fixture_buyer,8,fixture_greeting_a,fixture_done_a\n" +
            "age_of_agi.fixture_buyer,2,fixture_greeting_b,fixture_done_b\n" +
            "age_of_agi.fixture_seller,1,fixture_greeting_a,\n" +
            "age_of_agi.fixture_seller,5,fixture_greeting_b,\n" +
            "age_of_agi.fixture_native,1,fixture_native,\n" +
            "age_of_agi.fixture_pause_seller,1,fixture_pauses,\n" +
            "age_of_agi.fixture_native_chain,1,fixture_native_chain,\n");
        var previousTranslations = ModHelper.Translations.ToArray();
        string previousLocale = ModHelper.Locale;
        try
        {
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
            ModHelper.Translations["en"] = new Dictionary<string, string>
            {
                { "fixture_greeting_a", "First A; ;Second A；Third A" },
                { "fixture_greeting_b", "First B;Second B" },
                { "fixture_done_a", "Delivered A;Anything else A?" },
                { "fixture_done_b", "Delivered B;Anything else B?" },
                { "fixture_pauses", "Before......After；... ...;再想想……继续;Finish" },
                { "fixture_native", "@native:dialog_scav_greet_1" },
                { "fixture_native_chain", "@native:dialog_thief_1; ; dialog_petty_thief_1；dialog_scav_greet_1" },
                { "electronics_buyer_available_credits", "Balance: {0}" }
            };
            for (int choice = 0; choice < 2; choice++)
            {
                string suffix = choice == 0 ? "A" : "B";
                var buyer = new StoreClient { displayName = "Fixture Buyer", Budget = 19 };
                Dialogue tail = catalog.ApplyTo(buyer, "age_of_agi.fixture_buyer", true, new ChoiceRandom(choice));
                Assert(buyer.mainDialogue.plainText == "First " + suffix && tail.plainText == "Balance: 19",
                    "Each selectable variant retains the greeting head and adds a separate buyer budget.");
                Assert(buyer.acceptDealDialogue.plainText == "Delivered " + suffix &&
                    buyer.acceptDealDialogue.nextDialogue.plainText == "Anything else " + suffix + "?" &&
                    buyer.acceptDealDialogue.nextDialogue.nextDialogue == null,
                    "Greeting and completed-deal variants stay paired, with every completion bubble reachable.");
                Assert(buyer.acceptDealDialogue.startAction == null && buyer.DealAcceptedCalls == 0,
                    "Native post-deal handling must not run on the first bubble of a multi-bubble completion.");
                buyer.acceptDealDialogueLastLine.startAction();
                Assert(buyer.DealAcceptedCalls == 1 && ReferenceEquals(buyer.DealAcceptedHost, buyer.acceptDealDialogueLastLine),
                    "The final completion bubble runs native post-deal handling against its own chain.");
                buyer.OverrideBudget(7);
                buyer.mainDialogue.startAction();
                buyer.mainDialogue.startAction();
                string[] lines = Lines(buyer.mainDialogue).ToArray();
                Assert(lines[0] == "First " + suffix && lines.Last() == "Balance: 7" &&
                    lines.Count(line => line.StartsWith("Balance:")) == 1 && lines.Length == (choice == 0 ? 4 : 3),
                    "Repeated budget refreshes keep the chosen variant and never accumulate stale bubbles.");

                var nativeDeal = new Dialogue();
                var nativeTail = new Dialogue();
                int nativeCalls = 0;
                nativeDeal.SetStartAction(() => nativeCalls++);
                var seller = new StoreClient { displayName = "Fixture Seller", acceptDealDialogue = nativeDeal,
                    acceptDealDialogueLastLine = nativeTail };
                Dialogue sellerTail = catalog.ApplyTo(seller, "age_of_agi.fixture_seller", false, new ChoiceRandom(choice));
                int delivered = 0;
                sellerTail.SetEndAction(() => delivered++);
                Assert(seller.mainDialogue.plainText == "First " + suffix && seller.mainDialogue.endAction == null &&
                    seller.mainDialogue.startAction == null && !Lines(seller.mainDialogue).Any(line => line.StartsWith("Balance:")),
                    "Seller variants never receive buyer budget text or an early delivery callback.");
                sellerTail.endAction();
                seller.acceptDealDialogue.startAction();
                Assert(delivered == 1 && nativeCalls == 1 && ReferenceEquals(seller.acceptDealDialogue, nativeDeal) &&
                    ReferenceEquals(seller.acceptDealDialogueLastLine, nativeTail),
                    "Empty completion keys preserve native dialogue nodes and callbacks; delivery remains at the greeting tail.");
            }
            var native = new StoreClient();
            catalog.ApplyTo(native, "age_of_agi.fixture_native", false, new ChoiceRandom(0));
            Assert(native.mainDialogue.plainText == "native-dialogue:dialog_scav_greet_1",
                "Mapped native references resolve through the game's dialogue localization.");
            var nativeChain = new StoreClient();
            Dialogue nativeChainTail = catalog.ApplyTo(nativeChain, "age_of_agi.fixture_native_chain", false, new ChoiceRandom(0));
            Assert(Lines(nativeChain.mainDialogue).SequenceEqual(new[]
                {
                    "native-dialogue:dialog_thief_1", "native-dialogue:dialog_petty_thief_1", "native-dialogue:dialog_scav_greet_1"
                }) && nativeChainTail == nativeChain.mainDialogue.nextDialogue.nextDialogue,
                "A native-key sequence resolves each key independently, preserves order and returns the last bubble.");
            int nativeDeliveries = 0;
            nativeChainTail.SetEndAction(() => nativeDeliveries++);
            Assert(nativeChain.mainDialogue.endAction == null && nativeChain.mainDialogue.nextDialogue.endAction == null,
                "Native multi-line introductions cannot deliver before their last bubble.");
            nativeChainTail.endAction();
            Assert(nativeDeliveries == 1, "Native dialogue sequences retain the mod-owned delivery callback.");
            var pauses = new StoreClient { displayName = "Fixture Supplier" };
            Dialogue pauseTail = catalog.ApplyTo(pauses, "age_of_agi.fixture_pause_seller", false, new ChoiceRandom(0));
            Assert(Lines(pauses.mainDialogue).SequenceEqual(new[] { "Before......After", "... ...", "再想想……继续", "Finish" }),
                "Each semicolon-delimited sentence preserves its exact text, including standalone and inline ellipses.");
            int pauseDeliveries = 0;
            pauseTail.SetEndAction(() => pauseDeliveries++);
            for (Dialogue line = pauses.mainDialogue; line != pauseTail; line = line.nextDialogue)
                Assert(line.startAction == null && line.endAction == null, "Pauses cannot skip typing or move supplier delivery before the real tail.");
            pauseTail.ExecuteEndAction(); pauseTail.ExecuteEndAction();
            Assert(pauseDeliveries == 1, "A supply callback still runs once after all pause and speech nodes.");
            var reused = new Dialogue();
            Dialogue last = ClientDialogueText.SetText(reused, "CLIENT", "... ...");
            Assert(ReferenceEquals(last, reused) && reused.nextDialogue == null && reused.plainText == "... ...",
                "A standalone six-dot phrase remains one authored bubble rather than two artificial three-dot bubbles.");
            ClientDialogueText.SetText(reused, "CLIENT", "Single... stays;v1.2.3; ;No pause");
            Assert(Lines(reused).SequenceEqual(new[] { "Single... stays", "v1.2.3", "No pause" }),
                "Single ellipses and ordinary punctuation are preserved; rebuilding clears stale pause nodes.");
            ClientDialogueText.SetText(reused, "CLIENT", "呼......;当然......；A...\t...B;......;C……D; …… ; ...\t... ");
            Assert(Lines(reused).SequenceEqual(new[] { "呼......", "当然......", "A...\t...B", "......", "C……D", "……", "...\t..." }),
                "Punctuation and authored whitespace are preserved for every sentence; only semicolons create new bubbles.");
            ExpectInvalid(Header + "age_of_agi.fixture,0,hello,\n");
            ExpectInvalid(Header + "age_of_agi.fixture,1,hello,\nage_of_agi.fixture,1,other,\n");
            ExpectInvalid(Header + "age_of_agi.fixture,1,,\n");
            ExpectInvalid(Header + "age_of_agi.fixture,1,hello,bad-key\n");
        }
        finally
        {
            ModHelper.Translations.Clear();
            foreach (var entry in previousTranslations) ModHelper.Translations.Add(entry.Key, entry.Value);
            ModHelper.Locale = previousLocale;
        }
        Console.WriteLine("PASS: dialogue variants, ordinary six-dot pause nodes across suppliers/stories, full completion chains, native callbacks, delivery tails, budget refresh and native references.");
    }

    private static IEnumerable<string> Lines(Dialogue head)
    {
        for (Dialogue line = head; line != null; line = line.nextDialogue) yield return line.plainText;
    }

    private static void ExpectInvalid(string csv)
    {
        try { ClientDialogueCatalog.Parse(csv); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected invalid dialogue mapping to fail.");
    }

    private sealed class ChoiceRandom : Random
    {
        private readonly int choice;
        internal ChoiceRandom(int choice) { this.choice = choice; }
        public override int Next(int maxValue) => choice < maxValue ? choice : throw new Exception("Invalid test choice.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
