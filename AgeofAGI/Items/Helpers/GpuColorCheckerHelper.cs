using System;
using AgeOfAgi.Items.Logic.Gpu;

namespace AgeOfAgi.Items.Helpers
{
    internal static class GpuColorCheckerHelper
    {
        internal static void PrepareAssets() => GpuColorChecker.PrepareAssets();

        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            initializeAudio(item);
            Configure(item);
            item.onLoaded += Configure;
        }

        private static void Configure(GameItem item)
        {
            // Match native MiscItemDirectory.CigaretteColor: an always-readable
            // reference card, without consuming the item or changing inspected GPUs.
            item.EnableTag("DOCUMENT");
            item.EnableTag("IMPORTANT_TAG");
            item.mayActivateSlotItemFunc = (_, _, _) => true;
            item.canActivateSlotItemFunc = (_, _, _) => true;
            item.onActivateSlotItemFunc = (_, _, _) => GpuColorChecker.Open();
        }

        internal static void Shutdown() => GpuColorChecker.Shutdown();
    }
}
