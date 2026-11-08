using AgeOfAgi.Items.Definitions;
using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Catalogs;

namespace AgeOfAgi.Items
{
    internal sealed class ItemDefinition
    {
        internal string ItemId { get; }

        internal string Kind { get; }
        internal string SpriteKey { get; }
        internal long BaseValue { get; }
        internal string NameKey { get; }
        internal string DescriptionKey { get; }
        internal string CommentKey { get; }
        internal string AudioPreset { get; }
        internal int ContrabandLevel { get; }
        internal string Manufacturer { get; }
        internal IReadOnlyList<string> Types { get; }

        internal ItemDefinition(string itemId, string kind, string spriteKey, long baseValue, string nameKey, string descriptionKey,
            string commentKey = "", IEnumerable<string> types = null, string audioPreset = "none", string contrabandLevel = "none",
            string manufacturer = "")
        {
            DefinitionValidation.RequireNonNegative(baseValue, nameof(baseValue), "Item base value");

            ItemId = itemId;
            Kind = kind.ToLowerInvariant();
            SpriteKey = spriteKey;
            BaseValue = baseValue;
            NameKey = nameKey;
            DescriptionKey = (descriptionKey ?? "").Trim();
            CommentKey = (commentKey ?? "").Trim();
            AudioPreset = (audioPreset ?? "").Trim();
            ContrabandLevel = ItemContrabandLevels.Resolve((contrabandLevel ?? "").Trim());
            Manufacturer = (manufacturer ?? "").Trim();
            Types = new List<string>(types ?? Array.Empty<string>()).AsReadOnly();
        }
    }
}
