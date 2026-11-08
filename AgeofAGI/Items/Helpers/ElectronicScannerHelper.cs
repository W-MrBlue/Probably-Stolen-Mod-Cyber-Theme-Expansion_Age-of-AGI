using System;
using AgeOfAgi.Items.Logic.ElectronicScanner;

using static AgeOfAgi.Items.Logic.ElectronicScanner.ElectronicScannerLogic;

namespace AgeOfAgi.Items.Helpers
{
    internal static class ElectronicScannerHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            initializeAudio(item);
            Configure(item);
            item.onLoaded += Configure;
        }

        private static void Configure(GameItem item)
        {
            item.EnableTag(ScannerTag);
            // Play the configured scanner sound at successful detection, including
            // after loading; native Target must not play the same clip a second time.
            item.soundUse = null;
            item.EnableTag("CAN_USE_ON_NON_OWNED_TAG");
            item.maySelectSlotItemFunc = (source, inventory, slot) => CanUse(source);
            item.canSelectSlotItemFunc = (source, inventory, slot) => CanUse(source);
            item.mayThisTargetItemFunc = CanScan;
            item.canThisTargetItemFunc = CanScan;
            item.onThisTargetItemFunc = ElectronicScannerLogic.Scan;
        }

        internal static void Shutdown() => ElectronicScannerLogic.Shutdown();
    }
}
