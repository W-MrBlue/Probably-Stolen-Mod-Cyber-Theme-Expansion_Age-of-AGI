using System;
using System.Collections.Generic;

namespace AgeOfAgi.Npcs.Stories
{
    // Scene-independent state contract. Definitions and Unity objects never enter saves.
    internal sealed class StoryState
    {
        public int TriggerDay = -1;
        public string Node = "";
        public string Outcome = "";
        public int CompletedDay = -1;
        public Dictionary<string, bool> Flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);

        internal bool IsDue(int day, int delay) => TriggerDay >= 0 && day >= (long)TriggerDay + delay && CompletedDay < 0;
        internal void Arm(int day) { if (TriggerDay < 0) TriggerDay = day; }
        internal bool Has(string flag) => Flags.TryGetValue(flag, out bool value) && value;
        internal void Complete(string outcome, int day) { Outcome = outcome; CompletedDay = day; }
    }
}
