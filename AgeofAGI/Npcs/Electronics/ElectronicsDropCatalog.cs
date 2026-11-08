using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;

namespace AgeOfAgi.Npcs
{
    internal sealed class ElectronicsDropCatalog
    {
        internal const string CatalogPath = "Data/electronics_suppliers.csv";
        private static readonly string[] StandardSlots = { "low", "medium", "high" };
        private readonly Dictionary<string, List<DropSlot>> sellers = new Dictionary<string, List<DropSlot>>(StringComparer.Ordinal);
        private readonly HashSet<string> carriers;
        private readonly HashSet<string> containers;

        private ElectronicsDropCatalog(IReadOnlyList<ItemDefinition> items)
        {
            carriers = new HashSet<string>(items.Where(item => item.Kind == "data_carrier").Select(item => item.ItemId));
            containers = new HashSet<string>(items.Where(item => item.Kind == "data_container").Select(item => item.ItemId));
        }

        internal static ElectronicsDropCatalog Load(IReadOnlyList<ItemDefinition> items) =>
            Parse(items, CatalogCsvReader.ReadModFile(CatalogPath), CatalogPath,
                "dealer", "advanced_dealer", "scavenger", "petty_thief", "thief", "gpu_supplier");

        internal static ElectronicsDropCatalog Parse(IReadOnlyList<ItemDefinition> items, string csv, string path, params string[] sellerIds)
        {
            var result = new ElectronicsDropCatalog(items);
            var definitions = items.ToDictionary(item => item.ItemId, StringComparer.Ordinal);
            var allowedSellers = new HashSet<string>(sellerIds, StringComparer.Ordinal);
            var seenItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in CatalogCsvReader.ReadRows(csv, path,
                new[] { "seller", "slot", "probability", "min_reputation", "item_ids" }))
            {
                string seller = row.ReadString("seller"), slot = row.ReadString("slot");
                double probability = row.ReadDouble("probability");
                int minimumReputation = row.ReadInt("min_reputation");
                bool reputationSlot = slot == "reputation" && seller == "thief";
                if (!allowedSellers.Contains(seller) || !StandardSlots.Contains(slot) && !reputationSlot)
                    throw row.Invalid("Require a known seller and low/medium/high slot; only thief supports a reputation slot.");
                if (probability < 0 || probability > 100 || minimumReputation < 0 || !reputationSlot && minimumReputation != 0)
                    throw row.Invalid("Require probability in 0..100 and a nonnegative reputation gate on the thief reputation slot only.");
                if (!result.sellers.TryGetValue(seller, out var slots))
                    result.sellers.Add(seller, slots = new List<DropSlot>());
                if (slots.Any(entry => entry.Name == slot)) throw row.Invalid("Duplicate seller slot: " + seller + "/" + slot);
                string text = row.ReadString("item_ids");
                string[] ids = text.Length == 0 ? Array.Empty<string>() : text.Split(';').Select(id => id.Trim()).ToArray();
                if (probability > 0 && ids.Length == 0) throw row.Invalid("A slot with positive probability requires at least one item.");
                foreach (string id in ids)
                {
                    if (!definitions.TryGetValue(id, out var definition)) throw row.Invalid("Unknown item in item_ids: " + id);
                    if (!seenItems.Add(seller + ":" + id)) throw row.Invalid("Duplicate item for seller " + seller + ": " + id);
                    // Display categories and prices never change explicitly configured slot membership.
                    if (definition.Kind == "ai_model") throw row.Invalid("Factory-empty AI models are not merchandise. Item: " + id);
                }
                slots.Add(new DropSlot(slot, probability, minimumReputation, ids));
            }
            foreach (string seller in sellerIds)
            {
                if (!result.sellers.TryGetValue(seller, out var slots) || StandardSlots.Any(name => !slots.Any(slot => slot.Name == name)))
                    throw new InvalidOperationException(path + ": require low, medium and high slots for " + seller + ".");
                if (seller == "thief" && !slots.Any(slot => slot.Name == "reputation"))
                    throw new InvalidOperationException(path + ": require a reputation slot for thief.");
                if (Math.Abs(slots.Sum(slot => slot.Probability) - 100) > 0.000001 ||
                    slots.Where(slot => slot.Name != "reputation").Sum(slot => slot.Probability) <= 0)
                    throw new InvalidOperationException(path + ": probabilities must total 100 with positive standard-slot probability for " + seller + ".");
                slots.Sort((first, second) => SlotOrder(first.Name).CompareTo(SlotOrder(second.Name)));
            }
            return result;
        }

        internal List<MarketOffer> Roll(int seed, string seller, ElectronicsVisitRule rule, int reputation = 0)
        {
            var random = new Random(seed);
            int count = random.Next(rule.MinimumCount, rule.MaximumCount + 1);
            var slots = sellers[seller].Where(slot => slot.Probability > 0 &&
                (slot.Name != "reputation" || reputation >= slot.MinimumReputation)).ToArray();
            double total = slots.Sum(slot => slot.Probability);
            var result = new List<MarketOffer>(count);
            for (int index = 0; index < count; index++)
            {
                double roll = random.NextDouble() * total;
                DropSlot chosen = slots[slots.Length - 1];
                foreach (var slot in slots)
                {
                    roll -= slot.Probability;
                    if (roll < 0) { chosen = slot; break; }
                }
                string id = chosen.Items[random.Next(chosen.Items.Length)];
                bool raw = carriers.Contains(id);
                result.Add(new MarketOffer
                {
                    Id = id,
                    State = raw ? "raw" : containers.Contains(id) ? "empty" : "factory",
                    Fraction = raw ? ElectronicsStock.RollRawFraction(random) : 0
                });
            }
            return result;
        }

        private static int SlotOrder(string name) => name == "reputation" ? 3 : Array.IndexOf(StandardSlots, name);

        private sealed class DropSlot
        {
            internal readonly string Name;
            internal readonly double Probability;
            internal readonly int MinimumReputation;
            internal readonly string[] Items;

            internal DropSlot(string name, double probability, int minimumReputation, string[] items)
            {
                Name = name; Probability = probability; MinimumReputation = minimumReputation; Items = items;
            }
        }
    }
}
