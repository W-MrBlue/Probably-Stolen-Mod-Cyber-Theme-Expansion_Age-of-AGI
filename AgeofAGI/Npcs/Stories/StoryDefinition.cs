using System;
using System.Collections.Generic;

namespace AgeOfAgi.Npcs.Stories
{
    internal sealed class StoryDefinition
    {
        internal string Id, UpgradeId, StartNode;
        internal int DelayDays = 1;
        internal Func<StoreClient> CreateClient;
        internal Func<StoryState, bool> Condition { get; set; }
        internal readonly Dictionary<string, StoryNode> Nodes = new Dictionary<string, StoryNode>(StringComparer.Ordinal);
    }

    internal sealed class StoryNode
    {
        internal string TextKey, Speaker = "CLIENT", Next, Outcome;
        internal bool Blackout;
        internal bool NormalArrival;
        internal bool UseClientGreeting;
        internal Func<StorySession, string> SelectNext;
        internal Action<StorySession> Enter;
        internal Action<StorySession> PrepareClient;
        internal Action<StorySession> AfterText;
        internal Action<StorySession> Finish;
        internal readonly List<StoryChoice> Choices = new List<StoryChoice>();
    }

    internal sealed class StoryChoice
    {
        internal string TextKey, Target, Flag;
        internal StoryChoice(string textKey, string target, string flag = null)
        { TextKey = textKey; Target = target; Flag = flag; }
    }
}
