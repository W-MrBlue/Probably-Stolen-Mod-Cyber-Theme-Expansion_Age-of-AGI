using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.ElectronicStorage;

namespace AgeOfAgi.Items.Helpers
{
    internal static class ElectronicStorageHelper
    {
        internal static void InitDiskBox(GameItem item, Action<GameItem> initializeAudio)
        {
            var runtime = new ElectronicStorageRuntime(item, 8, 8, new List<string>(),
                new List<string> { ElectronicStorageRuntime.EmptyDiskId });
            item.EnableTag("STORABLE_PACKAGE_TAG");
            initializeAudio(item);
            item.onLoaded += runtime.OnLoaded;
            item.onLoaded += loaded => loaded.EnableTag("STORABLE_PACKAGE_TAG");
            if (!SaveManager.IsDecoding) runtime.FillDisks();
        }

        internal static void InitCodeContainer(GameItem item, Action<GameItem> initializeAudio)
        {
            // Game 0.46D StorageBayLarge uses a 17x10 native inventory window.
            var runtime = new ElectronicStorageRuntime(item, 17, 10,
                new List<string> { "codeparts", AiModelState.ModelTag });
            initializeAudio(item);
            item.onLoaded += runtime.OnLoaded;
        }
    }
}
