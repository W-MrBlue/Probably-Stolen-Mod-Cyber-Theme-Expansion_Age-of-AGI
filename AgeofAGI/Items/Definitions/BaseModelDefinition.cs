namespace AgeOfAgi.Items.Definitions
{
    internal sealed class BaseModelDefinition
    {
        internal string ItemId { get; }
        internal int GridWidth { get; }
        internal int GridHeight { get; }
        internal float MemoryMultiplier { get; }

        internal BaseModelDefinition(string itemId, int gridWidth, int gridHeight, float memoryMultiplier)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "Base model");
            DefinitionValidation.RequirePositive(gridWidth, nameof(gridWidth), "Base model gridWidth");
            DefinitionValidation.RequirePositive(gridHeight, nameof(gridHeight), "Base model gridHeight");
            DefinitionValidation.RequireFinitePositive(memoryMultiplier, nameof(memoryMultiplier), "Base model memoryMultiplier");

            ItemId = itemId;
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            MemoryMultiplier = memoryMultiplier;
        }
    }
}
