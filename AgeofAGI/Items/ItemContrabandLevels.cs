using System;

namespace AgeOfAgi.Items
{
    internal static class ItemContrabandLevels
    {
        internal static int Resolve(string level)
        {
            switch (level)
            {
                case "none": return 0;
                case "low": return 1;
                case "mid": return 2;
                case "high": return 3;
                case "critical": return 4;
                default:
                    throw new ArgumentException("Unknown contraband_level: '" + level +
                        "'. Expected none, low, mid, high or critical.", nameof(level));
            }
        }
    }
}
