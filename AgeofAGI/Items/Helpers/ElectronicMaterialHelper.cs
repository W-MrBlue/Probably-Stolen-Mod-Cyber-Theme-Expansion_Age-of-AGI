using AgeOfAgi.Items.Logic.Server;
using System;

namespace AgeOfAgi.Items.Helpers
{
    internal static class ElectronicMaterialHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            initializeAudio(item);
            if (item.identifier == ServerState.GoldFingerId) ServerUpgrades.BindNativeMaterial(item);
        }
    }
}
