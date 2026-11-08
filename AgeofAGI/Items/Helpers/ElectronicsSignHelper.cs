using System;

namespace AgeOfAgi.Items.Helpers
{
    internal static class ElectronicsSignHelper
    {
        internal const string ItemId = ModConstants.ModId + ".sign_electronics";

        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            initializeAudio(item);
            Mark(item);
            item.onLoaded += Mark;
        }

        private static void Mark(GameItem item)
        {
            // Match ToolDirectory's native supplier signs; neither tag changes saved prices.
            item.EnableTag("IMPORTANT_TAG");
            ShowcaseHelper.InitShowcaseSpecialItem(item, "electronics");
        }
    }
}
