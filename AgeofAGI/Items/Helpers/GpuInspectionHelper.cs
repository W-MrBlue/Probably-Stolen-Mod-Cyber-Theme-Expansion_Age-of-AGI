using AgeOfAgi.Items.Logic.Gpu;
namespace AgeOfAgi.Items.Helpers
{
    internal static class GpuInspectionHelper
    {
        internal static void Show(InspectionUIManager manager, GameItem item) => GpuInspectionRenderer.Show(manager, item);
        internal static void Reset() => GpuInspectionRenderer.Reset();
        internal static void Shutdown() => GpuInspectionRenderer.Shutdown();
    }
}
