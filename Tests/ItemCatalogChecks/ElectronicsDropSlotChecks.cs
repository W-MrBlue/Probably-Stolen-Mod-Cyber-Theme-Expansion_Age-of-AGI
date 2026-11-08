using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Npcs;

internal static class ElectronicsDropSlotChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        int reads = ModHelper.FileReads;
        var unified = ElectronicsDropCatalog.Load(items);
        Assert(ModHelper.FileReads == reads + 1, "All six supplier pools load from one file read.");
        foreach (string seller in new[] { "dealer", "advanced_dealer", "scavenger", "petty_thief", "thief", "gpu_supplier" })
            Assert(unified.Roll(3, seller, new ElectronicsVisitRule(1, 1), 100).Count == 1,
                "A single parsed supplier table serves every seller.");
        Assert(ModHelper.FileReads == reads + 1, "Drawing from any seller never reloads the supplier table.");
        const string path = "isolated_drop_slots.csv";
        const string csv = DropSlotCsvFixture.Header +
            "dealer,low,60,0,age_of_agi.cpu0;age_of_agi.capacitors\n" +
            "dealer,medium,30,0,age_of_agi.mem0\n" +
            "dealer,high,10,0,age_of_agi.gpu0;age_of_agi.gpu1;age_of_agi.gpu2\n";
        var catalog = ElectronicsDropCatalog.Parse(items, csv, path, "dealer");
        var counts = new Dictionary<string, int>();
        var single = new ElectronicsVisitRule(1, 1);
        const int samples = 24000;
        for (int seed = 0; seed < samples; seed++)
        {
            string id = catalog.Roll(seed, "dealer", single).Single().Id;
            counts[id] = counts.TryGetValue(id, out int value) ? value + 1 : 1;
        }
        foreach (var point in new[] { ("cpu0", .30), ("capacitors", .30), ("mem0", .30),
            ("gpu0", 1d / 30), ("gpu1", 1d / 30), ("gpu2", 1d / 30) })
            Assert(Math.Abs(counts["age_of_agi." + point.Item1] / (double)samples - point.Item2) < .012,
                "First select the configured slot probability, then select uniformly within that slot regardless of its size or item value.");

        string reordered = DropSlotCsvFixture.Header + string.Join("\n", csv.Trim().Split('\n').Skip(1).Reverse()) + "\n";
        var same = ElectronicsDropCatalog.Parse(items, reordered, path, "dealer");
        Assert(catalog.Roll(47, "dealer", new ElectronicsVisitRule(9, 9)).Select(offer => offer.Id).SequenceEqual(
            same.Roll(47, "dealer", new ElectronicsVisitRule(9, 9)).Select(offer => offer.Id)),
            "CSV slot row order does not change a frozen seeded roll.");

        const string gated = DropSlotCsvFixture.Header +
            "thief,low,40,0,age_of_agi.cpu0\n" +
            "thief,medium,30,0,age_of_agi.mem0\n" +
            "thief,high,20,0,age_of_agi.gpu0\n" +
            "thief,reputation,10,42,age_of_agi.gpu1\n";
        var thief = ElectronicsDropCatalog.Parse(items, gated, path, "thief");
        int unlocked = 0, lowLocked = 0;
        for (int seed = 0; seed < 12000; seed++)
        {
            var locked = thief.Roll(seed, "thief", single, 41).Single();
            Assert(locked.Id != "age_of_agi.gpu1", "Locked reputation slots cannot be chosen.");
            if (locked.Id == "age_of_agi.cpu0") lowLocked++;
            if (thief.Roll(seed, "thief", single, 42).Single().Id == "age_of_agi.gpu1") unlocked++;
        }
        Assert(Math.Abs(unlocked / 12000d - .1) < .015 && Math.Abs(lowLocked / 12000d - 40d / 90) < .015,
            "Unlock the extra slot at its configured boundary and renormalize standard slots while it is locked.");
        Assert(thief.Roll(2, "thief", new ElectronicsVisitRule(10, 10), int.MinValue).Count == 10,
            "Negative reputation never disables standard slots or consumes extra attempts.");
        Assert(Enumerable.Range(0, 100).Any(seed => thief.Roll(seed, "thief", new ElectronicsVisitRule(20, 20), 42)
            .Count(offer => offer.Id == "age_of_agi.gpu1") > 1),
            "The reputation slot participates in ordinary independent draws rather than a separate once-per-visit roll.");

        foreach (string invalid in new[] { "", csv.Replace("low,60", "low,61"), csv.Replace("low,60", "low,-1"),
            csv.Replace("low,60", "low,NaN"), csv.Replace("low,60", "low,Infinity"),
            csv.Replace("medium,30,0", "medium,30,1"), csv.Replace("dealer,low", "unknown,low"),
            csv.Replace("dealer,low", "dealer,reputation"), csv.Replace("cpu0;age_of_agi.capacitors", "cpu0;age_of_agi.cpu0"),
            csv.Replace("age_of_agi.mem0", "age_of_agi.cpu0"), csv.Replace("age_of_agi.mem0", "age_of_agi.missing"),
            csv.Replace("age_of_agi.mem0", "age_of_agi.ai_trained"), csv.Replace("dealer,medium,30,0,age_of_agi.mem0\n", ""),
            csv.Replace("dealer,medium,30,0,age_of_agi.mem0", "dealer,medium,30,0,"),
            csv + "dealer,high,0,0,\n", gated.Replace("reputation,10,42", "reputation,10,-1"),
            gated.Replace("thief,reputation,10,42,age_of_agi.gpu1\n", "") })
        {
            bool rejected = false;
            try { ElectronicsDropCatalog.Parse(items, invalid, path, invalid.StartsWith(DropSlotCsvFixture.Header + "thief,") ? "thief" : "dealer"); }
            catch (InvalidOperationException exception) { rejected = exception.Message.Contains(path); }
            Assert(rejected, "Reject malformed slot probabilities, gates, duplicate slots/items, missing slots and unregistered or empty-model goods with a path diagnostic.");
        }

        var raw = ElectronicsDropCatalog.Parse(items, DropSlotCsvFixture.Header +
            "dealer,low,100,0,age_of_agi.dc_common_data_disk\n" +
            "dealer,medium,0,0,\n" + "dealer,high,0,0,\n", path, "dealer");
        var frozen = raw.Roll(94, "dealer", new ElectronicsVisitRule(8, 8));
        Assert(frozen.All(offer => offer.State == "raw" && offer.Fraction >= .5 && offer.Fraction <= 1) &&
            frozen.Select(offer => (offer.Id, offer.Fraction)).SequenceEqual(
                raw.Roll(94, "dealer", new ElectronicsVisitRule(8, 8)).Select(offer => (offer.Id, offer.Fraction))),
            "Carrier state is derived from its family and raw contents are frozen with seeded offers.");
        Console.WriteLine("PASS: configurable low/medium/high slots, uniform item probabilities, reputation unlock/renormalization, frozen raw contents and malformed configuration diagnostics.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
