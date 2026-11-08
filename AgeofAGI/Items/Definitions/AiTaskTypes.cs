namespace AgeOfAgi.Items.Definitions
{
    internal static class AiTaskTypes
    {
        internal const string Computing = "computing";
        internal const string Biological = "biological_analysis";
        internal const string Intrusion = "intelligence_intrusion";
        internal const string Sensory = "sensory_analysis";
        internal static bool IsValid(string taskType)
        {
            return taskType == Computing || taskType == Sensory || taskType == Intrusion || taskType == Biological;
        }
    }
}
