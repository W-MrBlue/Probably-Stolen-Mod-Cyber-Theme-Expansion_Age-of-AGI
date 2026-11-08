namespace AgeOfAgi.Items.Logic.Gpu
{
    internal static class GpuGuide
    {
        internal const string ItemId = "age_of_agi.gpu_guide";
        // Keep the book ID authored by the paper editor; the item has its own catalog ID.
        internal const string BookId = "gpu_apprasial_guide";

        internal static void Open()
        {
            if (!PlayerStore.HasInstance || !HealthData.CanRead() ||
                !ModLoader.Mods.Exists(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled)) return;
            ModHelper.OpenPaper(ModConstants.ModId, BookId);
        }

        internal static void Shutdown() => ModHelper.ClosePaper(ModConstants.ModId, BookId);
    }
}
