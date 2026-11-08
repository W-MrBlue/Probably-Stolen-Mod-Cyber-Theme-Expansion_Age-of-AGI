using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Catalogs;

namespace AgeOfAgi.Npcs
{
    internal sealed class ElectronicsVisitRules
    {
        internal const string CatalogPath = "Data/electronics_visit_rules.csv";
        private readonly Dictionary<string, ElectronicsVisitRule> rules = new Dictionary<string, ElectronicsVisitRule>(StringComparer.Ordinal);
        internal ElectronicsVisitRule Get(string seller) => rules[seller];
        internal static ElectronicsVisitRules Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static ElectronicsVisitRules Parse(string csv)
        {
            var result = new ElectronicsVisitRules();
            var sellers = new HashSet<string>(new[] { "scavenger", "petty_thief", "thief", "dealer", "advanced_dealer", "gpu_supplier" });
            foreach (var row in CatalogCsvReader.ReadRows(csv, CatalogPath,
                new[] { "seller", "min_count", "max_count" }))
            {
                string seller = row.ReadString("seller");
                int minimum = row.ReadInt("min_count"), maximum = row.ReadInt("max_count");
                if (!sellers.Contains(seller) || minimum < 1 || maximum < minimum || maximum > 100 ||
                    !result.rules.TryAdd(seller, new ElectronicsVisitRule(minimum, maximum)))
                    throw row.Invalid("Require a unique known seller and count range 1..100.");
            }
            if (result.rules.Count != sellers.Count) throw new InvalidOperationException(CatalogPath + ": require all six random seller rules.");
            return result;
        }
    }

    internal sealed class ElectronicsVisitRule
    {
        internal int MinimumCount { get; }
        internal int MaximumCount { get; }
        internal ElectronicsVisitRule(int minimum, int maximum)
        {
            MinimumCount = minimum; MaximumCount = maximum;
        }
    }
}
