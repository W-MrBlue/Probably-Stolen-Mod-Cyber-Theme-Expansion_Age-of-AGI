namespace AgeOfAgi.Items.Definitions
{
    internal sealed class MemoryDefinition
    {
        internal string ItemId { get; }
        internal int Memory { get; }

        internal MemoryDefinition(string itemId, int memory)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "Memory");
            DefinitionValidation.RequirePositive(memory, nameof(memory), "Memory capacity (memory)");

            ItemId = itemId;
            Memory = memory;
        }
    }
}
