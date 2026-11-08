using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgeOfAgi.Npcs
{
    internal sealed class ClientDialogueCatalog
    {
        internal const string CatalogPath = "Data/client_dialogues.csv";
        private readonly Dictionary<string, List<ClientDialogueVariant>> variants =
            new Dictionary<string, List<ClientDialogueVariant>>(StringComparer.Ordinal);

        internal static ClientDialogueCatalog Current { get; private set; }
        internal static void Configure() => Current = Load();
        internal static void Shutdown() => Current = null;
        internal static ClientDialogueCatalog Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath), CatalogPath);

        internal static ClientDialogueCatalog Parse(string csv, string path = CatalogPath)
        {
            var result = new ClientDialogueCatalog();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in CatalogCsvReader.ReadRows(csv, path,
                new[] { "client_id", "variant", "greeting_key", "deal_complete_key" }))
            {
                string clientId = row.ReadString("client_id");
                int variant = row.ReadInt("variant");
                string greeting = row.ReadString("greeting_key");
                string completed = row.ReadString("deal_complete_key");
                try
                {
                    DefinitionValidation.RequireItemId(clientId, nameof(clientId), "Client");
                    RequireKey(greeting, nameof(greeting));
                    if (completed.Length > 0) RequireKey(completed, nameof(completed));
                }
                catch (ArgumentException exception)
                {
                    throw row.Invalid("Invalid client dialogue mapping.", exception);
                }
                if (variant <= 0 || !seen.Add(clientId + ":" + variant))
                    throw row.Invalid("Require a positive, unique variant per client.");
                if (!result.variants.TryGetValue(clientId, out var entries))
                    result.variants.Add(clientId, entries = new List<ClientDialogueVariant>());
                entries.Add(new ClientDialogueVariant(variant, greeting, completed));
            }
            return result;
        }

        internal static void ApplyBuyer(StoreClient client, string clientId) => Apply(client, clientId, true);

        // Call after native client completion when replacing a completed-deal chain.
        // The returned node is the greeting tail, for delivery or dismissal callbacks.
        internal static Dialogue Apply(StoreClient client, string clientId, bool reportBudget = false)
        {
            if (Current == null) throw new InvalidOperationException("Client dialogue catalog is not configured.");
            return Current.ApplyTo(client, clientId, reportBudget, new Random(Guid.NewGuid().GetHashCode()));
        }

        internal Dialogue ApplyTo(StoreClient client, string clientId, bool reportBudget, Random random)
        {
            if (!variants.TryGetValue(clientId, out var entries))
                throw new InvalidOperationException("Missing client dialogue mapping: " + clientId);
            ClientDialogueVariant selected = entries[random.Next(entries.Count)];
            var greeting = new Dialogue();
            Dialogue Refresh()
            {
                Dialogue last = ClientDialogueText.SetText(greeting, client.displayName, ResolveText(selected.GreetingKey));
                return reportBudget ? last.NextDialogue().SetText(client.displayName,
                    Text("electronics_buyer_available_credits", client.GetBudget())) : last;
            }
            Dialogue greetingTail = Refresh();
            client.mainDialogue = greeting;
            if (reportBudget) greeting.SetStartAction(() => Refresh());
            if (selected.DealCompleteKey.Length > 0)
            {
                var completed = new Dialogue();
                Dialogue completedTail = ClientDialogueText.SetText(completed, client.displayName,
                    ResolveText(selected.DealCompleteKey));
                client.acceptDealDialogue = completed;
                client.acceptDealDialogueLastLine = completedTail;
                completedTail.SetStartAction(() => client.OnDealAccepted(completedTail));
            }
            return greetingTail;
        }

        private static string ResolveText(string key)
        {
            string text = Text(key);
            const string nativePrefix = "@native:";
            if (!text.StartsWith(nativePrefix, StringComparison.Ordinal)) return text;
            return string.Join(";", text.Substring(nativePrefix.Length)
                .Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(nativeKey => nativeKey.Trim()).Where(nativeKey => nativeKey.Length > 0)
                .Select(nativeKey => LocHelper.GetLocalizedDialogue(nativeKey)));
        }

        private static void RequireKey(string key, string parameterName)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Dialogue key is required.", parameterName);
            foreach (char character in key)
            {
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') || character == '_'))
                    throw new ArgumentException("Invalid dialogue key: " + key, parameterName);
            }
        }

        private static string Text(string key, params object[] args) => ModHelper.GetLocalized(ModConstants.ModId, key, args);

        private sealed class ClientDialogueVariant
        {
            internal readonly int Variant;
            internal readonly string GreetingKey;
            internal readonly string DealCompleteKey;

            internal ClientDialogueVariant(int variant, string greetingKey, string dealCompleteKey)
            {
                Variant = variant;
                GreetingKey = greetingKey;
                DealCompleteKey = dealCompleteKey;
            }
        }
    }
}
