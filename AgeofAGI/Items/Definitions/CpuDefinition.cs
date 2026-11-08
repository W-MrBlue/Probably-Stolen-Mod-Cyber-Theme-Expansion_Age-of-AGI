namespace AgeOfAgi.Items.Definitions
{
    internal sealed class CpuDefinition
    {
        internal string ItemId { get; }
        internal float PowerBoostRate { get; }
        internal int PowerPerCycle { get; }

        internal CpuDefinition(string itemId, float powerBoostRate, int powerPerCycle)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "CPU");
            DefinitionValidation.RequireFiniteNonNegative(powerBoostRate, nameof(powerBoostRate), "CPU powerBoostRate");
            DefinitionValidation.RequireNonNegative(powerPerCycle, nameof(powerPerCycle), "CPU powerPerCycle");

            ItemId = itemId;
            PowerBoostRate = powerBoostRate;
            PowerPerCycle = powerPerCycle;
        }
    }
}
