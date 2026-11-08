namespace AgeOfAgi.Items.Definitions
{
    internal sealed class MachineDefinition
    {
        internal string ItemId { get; }
        internal int PowerPerCycle { get; }

        internal MachineDefinition(string itemId, int powerPerCycle)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "Machine");
            DefinitionValidation.RequireNonNegative(powerPerCycle, nameof(powerPerCycle), "Machine powerPerCycle");

            ItemId = itemId;
            PowerPerCycle = powerPerCycle;
        }
    }
}
