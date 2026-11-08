using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using Newtonsoft.Json;

namespace AgeOfAgi.Npcs
{
    internal sealed class NightShiftCatalog
    {
        internal const string Path = "Data/night_shift.csv";
        internal NightShiftStock[] Gifts { get; private set; }
        internal NightShiftStock[] Stock { get; private set; }

        internal static NightShiftCatalog Load(IReadOnlyList<ItemDefinition> items, string path = Path) =>
            Parse(CatalogCsvReader.ReadModFile(path), items, path);

        internal static NightShiftCatalog Parse(string csv, IReadOnlyList<ItemDefinition> items, string path = Path)
        {
            var gifts = new List<NightShiftStock>();
            var stock = new List<NightShiftStock>();
            foreach (var row in CatalogCsvReader.ReadRows(csv, path,
                new[] { "section", "itemIds", "count", "chance", "dataAmount", "minimumProductionSpeed",
                    "memoryGb", "computeBonus", "durability", "trainingWork", "trainingData" }))
            {
                string section = row.ReadString("section");
                if (section != "gifts" && section != "stock")
                    throw row.Invalid("section must be gifts or stock.");
                var entry = new NightShiftStock
                {
                    ItemIds = row.ReadString("itemIds").Split(';').Select(id => id.Trim()).ToArray(),
                    Count = row.ReadInt("count"),
                    Chance = row.ReadInt("chance"),
                    DataAmount = row.ReadInt("dataAmount"),
                    MinimumProductionSpeed = row.ReadFloat("minimumProductionSpeed"),
                    MemoryGb = row.ReadInt("memoryGb"),
                    ComputeBonus = row.ReadFloat("computeBonus"),
                    Durability = row.ReadInt("durability"),
                    TrainingWork = row.ReadFloat("trainingWork"),
                    TrainingData = row.ReadInt("trainingData")
                };
                if (entry.Count < 1 || entry.Count > 100 ||
                    entry.Chance < 0 || entry.Chance > 100 || entry.DataAmount < -1 || entry.MinimumProductionSpeed < 0 ||
                    float.IsNaN(entry.MinimumProductionSpeed) || float.IsInfinity(entry.MinimumProductionSpeed))
                    throw row.Invalid("Invalid count, chance, dataAmount or minimumProductionSpeed.");
                foreach (string id in entry.ItemIds)
                {
                    if (!items.Any(item => item.ItemId == id)) throw row.Invalid("Unknown item in itemIds: " + id);
                    if (entry.DataAmount >= 0 && id != "age_of_agi.data_container" && id != "age_of_agi.hypo_data_container" &&
                        !(id == "age_of_agi.serverbox" && section == "gifts"))
                        throw row.Invalid("Only data containers may override dataAmount.");
                    bool trained = id == "age_of_agi.ai_trained", untrained = id == "age_of_agi.ai_untrained";
                    if (trained || untrained)
                    {
                        if (entry.MemoryGb < 1 || entry.ComputeBonus <= 0 || entry.Durability < 1 || entry.DataAmount != -1 ||
                            (untrained ? entry.TrainingWork <= 0 || entry.TrainingData < 1 :
                                entry.TrainingWork != 0 || entry.TrainingData != 0))
                            throw row.Invalid("Supplier AI requires positive memoryGb, computeBonus and durability, with valid training budgets.");
                    }
                    else if (entry.MemoryGb != 0 || entry.ComputeBonus != 0 || entry.Durability != 0 ||
                        entry.TrainingWork != 0 || entry.TrainingData != 0)
                        throw row.Invalid("Only AI model rows may specify model attributes.");
                }
                if (section == "gifts" && (entry.Chance != 100 || entry.ItemIds.Length != 1 || entry.MinimumProductionSpeed != 0))
                    throw row.Invalid("gifts must be unconditional and deterministic.");
                (section == "gifts" ? gifts : stock).Add(entry);
            }
            if (gifts.Count == 0 || stock.Count == 0)
                throw CatalogCsvReader.Error(path, "", "gifts and stock must not be empty.");
            return new NightShiftCatalog { Gifts = gifts.ToArray(), Stock = stock.ToArray() };
        }

        internal List<NightShiftOffer> Roll(int seed, float productionSpeed)
        {
            var random = new Random(seed);
            var result = new List<NightShiftOffer>();
            foreach (NightShiftStock entry in Stock)
            {
                // Roll every row even when ineligible so a hardware change cannot
                // change the rest of this visit's deterministic stock.
                int roll = random.Next(100);
                string id = entry.ItemIds[random.Next(entry.ItemIds.Length)];
                if (roll >= entry.Chance || productionSpeed < entry.MinimumProductionSpeed) continue;
                for (int i = 0; i < entry.Count; i++) result.Add(entry.Offer(id));
            }
            return result;
        }

        internal List<NightShiftOffer> GiftItems() => Gifts.SelectMany(entry =>
            Enumerable.Range(0, entry.Count).Select(_ => entry.Offer(entry.ItemIds[0]))).ToList();
    }

    internal sealed class NightShiftStock
    {
        internal string[] ItemIds { get; set; }
        internal int Count { get; set; }
        internal int Chance { get; set; }
        internal int DataAmount = -1;
        internal float MinimumProductionSpeed { get; set; }
        internal int MemoryGb, Durability, TrainingData;
        internal float ComputeBonus, TrainingWork;
        internal NightShiftOffer Offer(string id) => new NightShiftOffer(id, DataAmount)
        {
            MemoryGb = MemoryGb, ComputeBonus = ComputeBonus, Durability = Durability,
            TrainingWork = TrainingWork, TrainingData = TrainingData
        };
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class NightShiftOffer
    {
        [JsonProperty] internal string ItemId;
        [JsonProperty] internal int DataAmount = -1;
        [JsonProperty] internal int MemoryGb, Durability, TrainingData;
        [JsonProperty] internal float ComputeBonus, TrainingWork;

        internal NightShiftOffer() { }
        internal NightShiftOffer(string itemId, int dataAmount) { ItemId = itemId; DataAmount = dataAmount; }
    }
}
