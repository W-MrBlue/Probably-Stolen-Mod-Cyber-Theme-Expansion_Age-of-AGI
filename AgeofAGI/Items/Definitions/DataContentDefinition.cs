using System;

namespace AgeOfAgi.Items.Definitions
{
    internal sealed class DataContentDefinition
    {
        internal string ItemId { get; }
        internal bool Container { get; }
        internal bool RequiresIntrusion { get; }
        internal int Capacity { get; }
        internal int InitialData { get; }
        internal string SpritePrefix { get; }

        internal DataContentDefinition(string itemId, string mode, int capacity, int initialData, string spritePrefix)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "Data content");
            DefinitionValidation.RequirePositive(capacity, nameof(capacity), "capacity");
            if ((mode != "container" && mode != "computing" && mode != "intrusion") ||
                initialData < 0 || initialData > capacity || (mode == "container" ? initialData != 0 : initialData != capacity) ||
                (mode == "container" ? spritePrefix != "data_container" && spritePrefix != "hypo_data_container" : spritePrefix.Length != 0))
                throw new ArgumentException("Invalid data content mode, initial data or sprite prefix.");
            ItemId = itemId;
            Container = mode == "container";
            RequiresIntrusion = mode == "intrusion";
            Capacity = capacity;
            InitialData = initialData;
            SpritePrefix = spritePrefix;
        }
    }
}
