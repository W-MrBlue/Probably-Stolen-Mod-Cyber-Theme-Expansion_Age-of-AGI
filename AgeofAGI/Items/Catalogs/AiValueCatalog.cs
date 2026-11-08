using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class AiValueCatalog
    {
        internal const string CatalogPath = "Data/Definitions/ai_value.csv";
        private readonly Dictionary<string, Rule> rules = new Dictionary<string, Rule>(StringComparer.Ordinal);

        internal static AiValueCatalog Load() => Parse(CatalogCsvReader.ReadModFile(CatalogPath));

        internal static AiValueCatalog Parse(string csv)
        {
            var catalog = new AiValueCatalog();
            foreach (var row in CatalogCsvReader.ReadRows(csv, CatalogPath,
                new[] { "task", "value_per_use", "task_multiplier", "untrained_multiplier" }))
            {
                string task = row.ReadString("task");
                var rule = new Rule { PerUse = row.ReadDouble("value_per_use"),
                    TaskMultiplier = row.ReadDouble("task_multiplier"), UntrainedMultiplier = row.ReadDouble("untrained_multiplier") };
                if (!AiTaskTypes.IsValid(task) || rule.PerUse <= 0 || rule.TaskMultiplier <= 0 ||
                    rule.UntrainedMultiplier <= 0 || rule.UntrainedMultiplier >= 1 || !catalog.rules.TryAdd(task, rule))
                    throw row.Invalid("Require unique valid tasks, positive prices and an untrained multiplier between zero and one.");
            }
            foreach (string task in new[] { "computing", "sensory_analysis", "biological_analysis", "intelligence_intrusion" })
                if (!catalog.rules.ContainsKey(task)) throw new InvalidOperationException(CatalogPath + ": missing task " + task);
            return catalog;
        }

        internal long Value(string task, double performance, double uses, bool trained)
        {
            if (!rules.TryGetValue(task ?? "", out var rule) || performance <= 0 || uses <= 0 ||
                double.IsNaN(performance) || double.IsNaN(uses) || double.IsInfinity(performance) || double.IsInfinity(uses)) return 0;
            double value = rule.PerUse * (1 + performance / 100) * uses * rule.TaskMultiplier *
                (trained ? 1 : rule.UntrainedMultiplier);
            // The native misc-value aggregation is Int32, even though base prices are Int64.
            return (long)Math.Max(1, Math.Min(int.MaxValue, Math.Round(value, MidpointRounding.AwayFromZero)));
        }

        private sealed class Rule
        {
            internal double PerUse, TaskMultiplier, UntrainedMultiplier;
        }
    }
}
