using System;

namespace AgeOfAgi.Items.Helpers
{
    internal static class GenericItemHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            initializeAudio(item);
        }
    }
}
