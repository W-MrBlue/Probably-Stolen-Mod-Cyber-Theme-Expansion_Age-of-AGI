using System;

namespace AgeOfAgi.Items.Logic.Gpu
{
    internal readonly struct GpuPerformance
    {
        internal double ComputePower { get; }
        internal int PowerPerCycle { get; }
        internal bool PowerSupplyWorking { get; }

        internal GpuPerformance(double computePower, int powerPerCycle, bool powerSupplyWorking)
        {
            ComputePower = computePower;
            PowerPerCycle = powerPerCycle;
            PowerSupplyWorking = powerSupplyWorking;
        }
    }

    internal static class GpuPerformanceCalculator
    {
        internal static bool HasPowerInterface(GpuInspectionState state) => state?.Parts.Exists(part =>
            part.Slot == "power_interface" && part.Sprite.Length > 0 && part.Condition == "normal") == true;

        internal static bool HasMissingGoldfinger(GpuInspectionState state) => state != null &&
            (!state.Parts.Exists(part => part.Slot == "goldfinger_up" && part.Sprite.Length > 0 && part.Condition == "normal") ||
             !state.Parts.Exists(part => part.Slot == "goldfinger_down" && part.Sprite.Length > 0 && part.Condition == "normal"));

        internal static bool HasWorkingPowerModule(GpuInspectionState state) => state?.Parts.Exists(part =>
            part.Slot.StartsWith("power_module_", StringComparison.Ordinal) && part.Sprite.Length > 0 && part.Condition == "normal") == true;

        internal static int PowerMaximum(GpuInspectionState state)
        {
            int modules = 0;
            foreach (var part in state.Parts)
                if (part.Slot.StartsWith("power_module_", StringComparison.Ordinal)) modules++;
            return (int)Math.Min(int.MaxValue, (long)state.CorePowerPerCycle.Value * modules);
        }

        internal static double PowerMidpoint(GpuInspectionState state)
        {
            int maximum = PowerMaximum(state);
            if (maximum == 0) return 0;
            int initialPower = state.CorePowerPerCycle.Value;
            // All modules working is best; a single working module is worst.
            // Zero working modules is a supply fault, not an efficiency endpoint.
            return (initialPower + (double)maximum) / 2;
        }

        internal static bool TryCalculateInspection(GpuInspectionState state, int missingModules,
            int missingMemory, int mismatchedMemory, int slowMemory, out GpuPerformance result)
        {
            result = default;
            if (state?.CoreComputePower == null || state.CorePowerPerCycle == null) return false;
            int slots = 0, modules = 0;
            foreach (var part in state.Parts)
            {
                if (part.Slot.StartsWith("memory_", StringComparison.Ordinal)) slots++;
                if (part.Slot.StartsWith("power_module_", StringComparison.Ordinal)) modules++;
            }
            if (slots == 0 || modules == 0 || missingModules < 0 || missingModules > modules ||
                missingMemory < 0 || mismatchedMemory < 0 || slowMemory < 0 ||
                (long)missingMemory + mismatchedMemory + slowMemory > slots) return false;
            int effectiveTenths = (slots - missingMemory - mismatchedMemory - slowMemory) * 10 + slowMemory * 6;
            int working = modules - missingModules;
            bool supply = HasPowerInterface(state) && !HasMissingGoldfinger(state) && working > 0;
            double compute = state.CoreComputePower.Value * (double)effectiveTenths / (slots * 10d);
            int power = working > 0 ? (int)Math.Min(int.MaxValue,
                ((long)state.CorePowerPerCycle.Value * modules + working - 1) / working) : 0;
            result = new GpuPerformance(compute, power, supply);
            return true;
        }

        internal static GpuPerformance Calculate(GpuInspectionState state, int initialCompute, int initialPower)
        {
            int memorySlots = 0, memoryTenths = 0, modules = 0, workingModules = 0;
            bool powerInterface = false;
            foreach (GpuInspectionPart part in state.Parts)
            {
                bool installed = part.Sprite.Length > 0 && part.Condition == "normal";
                if (part.Slot.StartsWith("memory_", StringComparison.Ordinal))
                {
                    memorySlots++;
                    if (part.Sprite.Length > 0)
                        memoryTenths += part.Condition == "normal" ? 10 : part.Condition == "lowfrequency" ? 6 : 0;
                }
                else if (part.Slot.StartsWith("power_module_", StringComparison.Ordinal))
                {
                    modules++;
                    if (installed) workingModules++;
                }
                else if (part.Slot == "power_interface") powerInterface = installed;
            }
            // Integer tenths avoid accumulating errors from low-frequency memory's 0.6 weight.
            double compute = memorySlots == 0 ? 0 : initialCompute * (double)memoryTenths / (memorySlots * 10d);
            bool supplyWorking = powerInterface && !HasMissingGoldfinger(state) && workingModules > 0;
            int power = 0;
            if (workingModules > 0)
            {
                // Native powered tools consume integer charges: ceil(base * total / working).
                long demand = (long)initialPower * modules;
                power = (int)Math.Min(int.MaxValue, (demand + workingModules - 1) / workingModules);
            }
            return new GpuPerformance(compute, power, supplyWorking);
        }
    }
}
