internal static class DirectoryUtils
{
    // Native container windows have one grid child. Use the existing scene row
    // adapter so graph traversal remains shared with the other behavior checks.
    internal static (PixelWindow, GameInventory) CreateInventoryWindow(int width, int height)
    {
        var inventory = new GameGridInventory(width, height);
        var window = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel("", inventory, "storage"));
        return (window, inventory);
    }
}

internal static class ModuleHelper
{
    internal static bool IsModuleTypeTag(string tag) => tag == "MODULE" || tag == "NODE";
}

internal static class StoreClientListStory
{
    internal static StoreClient CreateInventorIntro() => new StoreClient { identifier = "inventorIntro" };
    internal static StoreClient CreateInventorModuleIntro() => new StoreClient { identifier = "inventorModuleIntro" };
}
