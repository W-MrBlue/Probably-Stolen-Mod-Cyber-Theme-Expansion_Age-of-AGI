using System;
using System.Linq;
using AgeOfAgi.Items.Logic.Gpu;

namespace AgeOfAgi.Items.Logic.ElectronicScanner
{
    internal static class ElectronicScannerLogic
    {
        internal const string ItemId = ModConstants.ModId + ".electronic_scanner";
        internal const string ScannerTag = "AGE_OF_AGI_ELECTRONIC_SCANNER";
        private static readonly string[] features = { GpuLabels.ComputeFeature, GpuLabels.PowerFeature,
            GpuLabels.SupplyFeature, GpuLabels.GoldfingerFeature };

        internal static void Scan(GameItem source, GameItem target)
        {
            if (CanScan(source, target) && ElectronicScannerPanel.Show(source, target))
                AudioManager.Instance.Play3(source.soundActivate);
        }

        private static bool IsAlive(GameItem item) => item != null && item.unitCount > 0 &&
            (!(item is GameItemElement element) || !element.IsDestroyed());

        internal static bool CanUse(GameItem source) => IsAlive(source) && source.IsTag(ScannerTag) &&
            GeneralHelper.IsItemOwned(source) && PlayerStore.HasInstance &&
            ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled);

        internal static bool CanScan(GameItem source, GameItem target)
        {
            if (!CanUse(source) || !IsAlive(target) || ReferenceEquals(source, target) ||
                !GpuRuntime.TryGetRealPerformance(target, out _)) return false;
            var state = GpuInspectionState.Read(target);
            return state?.CoreComputePower != null && state.CorePowerPerCycle != null;
        }

        internal static string[] Readings(GameItem target)
        {
            // Ensure rebuilds realCondition from the authoritative assembly, never
            // from claims or editable numeric Tags.
            GpuLabels.Ensure(target);
            return features.Select(id => target.FindItemFeatureByID(id).realCondition.display).ToArray();
        }

        internal static bool CanMatch(GameItem source, GameItem target)
        {
            if (!CanScan(source, target) || !GeneralHelper.IsItemOwned(target)) return false;
            GpuLabels.Ensure(target);
            return target.itemFeatures.Any(feature => GpuLabels.IsFeature(feature) && !feature.IsFeatureMatch());
        }

        internal static bool Match(GameItem source, GameItem target)
        {
            if (!CanMatch(source, target)) return false;
            GpuLabels.MatchActualLabels(target);
            NegociationUIManager.Instance?.RefreshUI();
            return true;
        }

        internal static void Shutdown() => ElectronicScannerPanel.Shutdown();
    }
}
