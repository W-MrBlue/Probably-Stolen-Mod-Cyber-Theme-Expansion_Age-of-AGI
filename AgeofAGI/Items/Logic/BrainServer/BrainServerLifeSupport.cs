using AgeOfAgi.Items.Logic.Server;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.BrainServer
{
    internal sealed class BrainServerLifeSupport
    {
        internal const string ItemId = ModConstants.ModId + ".brain_server";
        internal const string DeadTag = "AGE_OF_AGI_BRAIN_DEAD";
        internal const string DeathReasonTag = "AGE_OF_AGI_BRAIN_DEATH_REASON";
        internal const int NetworkIndex = 3;
        private readonly GameItem machine;
        private readonly GameSlotInventory battery, blood, water;
        private readonly int power;
        private readonly BrainServerDefinition definition;
        internal bool Dead => machine.IsTag(DeadTag);

        internal BrainServerLifeSupport(GameItem machine, GameSlotInventory battery, GameSlotInventory blood,
            GameSlotInventory water, int power, BrainServerDefinition definition)
        {
            this.machine = machine;
            this.battery = battery;
            this.blood = blood;
            this.water = water;
            this.power = power;
            this.definition = definition ?? throw new System.ArgumentNullException(nameof(definition));
        }

        internal string MissingSupply()
        {
            if (Dead) return "brain_dead";
            if (!BloodSupply.CanConsume(blood.childItem)) return "brain_no_blood";
            GameItem liquid = water.childItem;
            if (liquid == null || liquid.unitCount != 1 || !GeneralHelper.IsItemOwned(liquid) ||
                !liquid.IsTag("LIQUID_CONTAINER_TAG") || WaterHelper.GetCurrentCapacityML(liquid) < definition.WaterMlPerDay ||
                WaterHelper.GetWaterPurity(liquid) < WaterFeatureHelper.HIGH_QUALITY_WATER_PURITY) return "brain_no_water";
            GameItem source = battery.childItem;
            if (source == null || !GeneralHelper.IsItemOwned(source) || !PowerHelper.CanDrawPowerSource(source, power)) return "brain_no_power";
            return null;
        }

        // Called once by the shared network cycle, before selecting local tasks or donors.
        internal void RunCycle()
        {
            if (Dead) return;
            string missing = MissingSupply();
            if (missing != null || !PowerHelper.DrawPowerSource(battery.childItem, power))
            {
                machine.EnableTag(DeadTag);
                machine.ModifyTag(DeathReasonTag, tag => tag.Enable().SetString(missing ?? "brain_no_power"));
                return;
            }
            WaterHelper.RemoveMl(water.childItem, definition.WaterMlPerDay);
            BloodSupply.Consume(blood.childItem);
        }

        internal ServerHardware ReadHardware() => Dead ? null : ServerHardware.Fixed(machine, definition.Compute, definition.MemoryGb);
    }
}
