using System;
using AgeOfAgi.Items.Logic.Gpu;

namespace AgeOfAgi.Items.Helpers
{
    internal static class GpuGuideHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            initializeAudio(item);
            Configure(item);
            item.onLoaded += Configure;
        }

        private static void Configure(GameItem item)
        {
            item.EnableTag("DOCUMENT");
            item.EnableTag("IMPORTANT_TAG");
            item.EnableTag("GUIDE_PAPER_TAG");
            item.mayActivateSlotItemFunc = (_, _, _) => HealthData.CanRead();
            item.canActivateSlotItemFunc = (_, _, _) => HealthData.CanRead();
            item.onActivateSlotItemFunc = (_, _, _) => GpuGuide.Open();
        }

        internal static void Shutdown() => GpuGuide.Shutdown();
    }
}
