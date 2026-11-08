using System;

namespace AgeOfAgi.Items.Definitions
{
    internal sealed class BrainServerDefinition
    {
        internal double Compute { get; }
        internal int MemoryGb { get; }
        internal int WaterMlPerDay { get; }

        internal BrainServerDefinition(double compute, int memoryGb, int waterMlPerDay)
        {
            if (compute <= 0 || double.IsNaN(compute) || double.IsInfinity(compute) || memoryGb <= 0 || waterMlPerDay <= 0)
                throw new ArgumentException("Brain server compute, memory and daily water must be positive.");
            Compute = compute;
            MemoryGb = memoryGb;
            WaterMlPerDay = waterMlPerDay;
        }
    }
}
