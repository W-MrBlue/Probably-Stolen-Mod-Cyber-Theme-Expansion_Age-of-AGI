namespace AgeOfAgi.Items.Definitions
{
    internal sealed class GpuDefinition
    {
        internal string ItemId { get; }
        internal int ComputePower { get; }
        internal int PowerPerCycle { get; }
        internal string CoreSprite { get; }

        internal GpuDefinition(string itemId, int computePower, int powerPerCycle, string coreSprite)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "GPU");
            DefinitionValidation.RequirePositive(computePower, nameof(computePower), "GPU computePower");
            DefinitionValidation.RequireNonNegative(powerPerCycle, nameof(powerPerCycle), "GPU powerPerCycle");
            if (!Catalogs.GpuInspectionCatalog.IsKey(coreSprite))
                throw new System.ArgumentException("GPU coreSprite must be a sprite key.", nameof(coreSprite));

            ItemId = itemId;
            ComputePower = computePower;
            PowerPerCycle = powerPerCycle;
            CoreSprite = coreSprite;
        }
    }
}
