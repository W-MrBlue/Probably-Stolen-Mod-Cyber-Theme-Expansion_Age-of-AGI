using System;
using HarmonyLib;

using AgeOfAgi.Items.Logic.ComplexData;
using static AgeOfAgi.Items.Logic.ComplexData.ComplexDataState;

namespace AgeOfAgi.Items.Helpers
{
    internal static class ComplexDataHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            item.EnableTag(ContainerTag);
            initializeAudio(item);
            PlantGeneticsHelper.InitDisk(item);
            item.onLoaded += RefreshDisplay;
        }
    }
}
