using System;

namespace AgeOfAgi.Items.Helpers
{
    internal static class DataItemHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            initializeAudio(item);
        }
    }
}
