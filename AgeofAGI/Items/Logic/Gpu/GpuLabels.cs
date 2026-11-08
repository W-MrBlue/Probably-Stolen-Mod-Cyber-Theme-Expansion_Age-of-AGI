using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Random = System.Random;

namespace AgeOfAgi.Items.Logic.Gpu
{
    // Native label machines edit fakeCondition. Actual hardware is kept in the
    // assembly snapshot and never read from either label condition by a server.
    internal static class GpuLabels
    {
        internal const string ComputeFeature = "age_of_agi.gpu_compute_label";
        internal const string PowerFeature = "age_of_agi.gpu_power_label";
        internal const string SupplyFeature = "age_of_agi.gpu_supply_label";
        internal const string GoldfingerFeature = "age_of_agi.gpu_goldfinger_label";
        internal const string MissingSupplyTextKey = "gpu_power_supply_missing";
        internal const string NormalSupplyTextKey = "gpu_power_supply_normal";
        internal const string GoldfingerExposedTag = "AGE_OF_AGI_GPU_GOLDFINGER_EXPOSED";
        internal const string ConfrontDiscountFeature = "age_of_agi.gpu_confront_discount";
        private const string CategoryPrefix = "age_of_agi.gpu_label.";
        private static readonly Dictionary<string, List<ItemCondition>> owned = new Dictionary<string, List<ItemCondition>>();
        private static readonly Dictionary<string, string> names = new Dictionary<string, string>();
        private static LabelerUIManager editor;
        private static RectTransform editorPanel, editorList;
        private static Vector2 editorPanelSize, editorPanelPosition, editorListPosition;
        private const string DishonestClaimsTag = "AGE_OF_AGI_GPU_DISHONEST_CLAIMS";

        internal static bool IsFeature(ItemFeature feature) => feature != null &&
            (feature.identifier == ComputeFeature || feature.identifier == PowerFeature || feature.identifier == SupplyFeature ||
             feature.identifier == GoldfingerFeature);

        internal static void Ensure(GameItem item)
        {
            if (!GpuRuntime.IsGpu(item)) return;
            GpuInspectionState state = GpuInspectionState.Read(item);
            if (state == null || !state.TryGetPerformance(out var actual) || !state.CoreComputePower.HasValue || !state.CorePowerPerCycle.HasValue) return;
            int maximumCompute = state.Parts.Any(part => part.Slot.StartsWith("memory_", StringComparison.Ordinal)) ? state.CoreComputePower.Value : 0;
            EnsureFeature(item, ComputeFeature, "compute." + maximumCompute, "gpu_label_compute",
                0, maximumCompute, actual.ComputePower, false);
            int maximumPower = GpuPerformanceCalculator.PowerMaximum(state);
            int minimumPower = Math.Min(state.CorePowerPerCycle.Value, maximumPower);
            EnsureFeature(item, PowerFeature, "power." + minimumPower + "." + maximumPower, "gpu_label_power",
                minimumPower, maximumPower, actual.PowerPerCycle, true);
            EnsureFeature(item, SupplyFeature, "supply", "gpu_label_supply", 0, 1, GpuPerformanceCalculator.HasPowerInterface(state) ? 1 : 0, false);
            EnsureFeature(item, GoldfingerFeature, "goldfinger", "gpu_label_goldfinger", 0, 1, GpuPerformanceCalculator.HasMissingGoldfinger(state) ? 0 : 1, false);
        }

        private static void EnsureFeature(GameItem item, string id, string suffix, string nameKey, int minimum, int maximum, double actual, bool power)
        {
            string category = CategoryPrefix + suffix;
            if (!owned.TryGetValue(category, out var options))
            {
                options = new List<ItemCondition>();
                owned.Add(category, options);
                if (id == SupplyFeature || id == GoldfingerFeature)
                {
                    Condition(options, category, 0, true); Condition(options, category, 1, true);
                }
                else
                {
                    // Eight evenly spaced endpoints; integer energy labels collapse
                    // duplicate points when fewer than eight distinct charges exist.
                    for (int index = 0; index < 8; index++)
                    {
                        double value = minimum + (maximum - (double)minimum) * index / 7;
                        if (power) value = Math.Round(value, MidpointRounding.AwayFromZero);
                        Condition(options, category, value, true);
                    }
                }
            }
            ItemConditionList.conditionLookup[category] = options;
            names[category] = Text(nameKey);
            ItemFeatureList.CategoryNames[category] = names[category];
            ItemCondition real = Condition(options, category, actual);
            real.display = Display(id, actual);
            ItemFeature feature = item.FindItemFeatureByID(id);
            if (feature == null)
            {
                feature = new ItemFeature { identifier = id, category = category, isExposable = false };
                feature.SetConditions(real, real);
                item.AddItemFeature(feature);
            }
            else
            {
                // Saved public claims and exposure state survive reload; rebuild only
                // the actual label from the authoritative snapshot and localize options.
                double claim = ReadValue(feature.fakeCondition, actual);
                feature.category = category;
                feature.useCondition = true;
                feature.realCondition = real;
                feature.fakeCondition = Condition(options, category, claim);
                feature.fakeCondition.display = Display(id, claim);
                feature.isExposable = false;
            }
            options.Sort((left, right) => ReadValue(left, 0).CompareTo(ReadValue(right, 0)));
            foreach (ItemCondition condition in options) condition.display = Display(id, ReadValue(condition, actual));
        }

        private static ItemCondition Condition(List<ItemCondition> options, string category, double value, bool addOption = false)
        {
            string identifier = category + ":" + value.ToString("R", CultureInfo.InvariantCulture);
            ItemCondition condition = options.FirstOrDefault(candidate => candidate.identifier == identifier);
            if (condition != null) return condition;
            condition = new ItemCondition { identifier = identifier, category = category };
            // Exact real/saved/calculated claims stay attached to the item without
            // adding arbitrary values to every card's shared printer choices.
            if (addOption) options.Add(condition);
            return condition;
        }

        internal static double ReadValue(ItemCondition condition, double fallback)
        {
            if (condition?.identifier == null) return fallback;
            int separator = condition.identifier.LastIndexOf(':');
            return separator >= 0 && double.TryParse(condition.identifier.Substring(separator + 1), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) && value >= 0 && !double.IsInfinity(value) && !double.IsNaN(value) ? value : fallback;
        }

        internal static void SetClaim(GameItem item, string id, double value)
        {
            Ensure(item);
            ItemFeature feature = item.FindItemFeatureByID(id);
            if (feature == null || !owned.TryGetValue(feature.category, out var options)) return;
            feature.fakeCondition = Condition(options, feature.category, value);
            feature.fakeCondition.display = Display(id, value);
            feature.SetExposed(false);
            feature.SetFeatureDiscovered(false);
        }

        internal static double RollClaim(double actual, bool higherIsBetter, Random random)
        {
            int roll = random.Next(100);
            if (roll >= 30 && roll < 90) return actual;
            bool increase = (roll < 30) == higherIsBetter;
            return actual * (increase ? 1.25 : 0.75);
        }

        internal static void ApplyDishonestClaims(GameItem item, Random random)
        {
            if (!GpuRuntime.IsGpu(item) || item.IsTag(DishonestClaimsTag) ||
                !GpuRuntime.TryGetRealPerformance(item, out var actual)) return;
            SetClaim(item, ComputeFeature, RollClaim(actual.ComputePower, true, random));
            SetClaim(item, PowerFeature, RollClaim(actual.PowerPerCycle, false, random));
            if (!GpuPerformanceCalculator.HasPowerInterface(GpuInspectionState.Read(item)) && random.Next(100) < 80)
                SetClaim(item, SupplyFeature, 1);
            if (GpuPerformanceCalculator.HasMissingGoldfinger(GpuInspectionState.Read(item)))
                SetClaim(item, GoldfingerFeature, 1);
            item.EnableTag(DishonestClaimsTag);
        }

        internal static void MatchActualLabels(GameItem item)
        {
            Ensure(item);
            foreach (ItemFeature feature in item.itemFeatures.Where(IsFeature))
            {
                feature.fakeCondition = feature.realCondition;
                feature.SetExposed(false);
                feature.SetFeatureDiscovered(false);
            }
        }

        internal static long Price(long basisValue, double initialCompute, double compute, bool interfaceMissing, bool goldfingerMissing = false)
        {
            if (basisValue <= 0 || initialCompute <= 0 || compute < 0 || double.IsNaN(compute) || double.IsInfinity(compute)) return Math.Max(0, basisValue);
            double value = basisValue * (0.6 + 0.4 * compute / initialCompute) * (interfaceMissing ? 0.4 : 1) * (goldfingerMissing ? 0.2 : 1);
            return value >= long.MaxValue ? long.MaxValue : (long)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        internal static long QuotedPrice(GameItem item)
        {
            var state = GpuInspectionState.Read(item);
            if (state?.CoreComputePower == null || !state.TryGetPerformance(out var actual)) return item.unitValue;
            var compute = item.FindItemFeatureByID(ComputeFeature);
            var supply = item.FindItemFeatureByID(SupplyFeature);
            var goldfinger = item.FindItemFeatureByID(GoldfingerFeature);
            double visibleCompute = ReadValue(compute?.GetCurrentItemCondition(), actual.ComputePower);
            bool missing = ReadValue(supply?.GetCurrentItemCondition(), GpuPerformanceCalculator.HasPowerInterface(state) ? 1 : 0) < 0.5;
            return Price(item.unitValue, state.CoreComputePower.Value, visibleCompute, missing,
                ReadValue(goldfinger?.GetCurrentItemCondition(), GpuPerformanceCalculator.HasMissingGoldfinger(state) ? 0 : 1) < 0.5);
        }

        internal static void AddConfrontDiscount(GameItem item, int choice)
        {
            ItemFeature feature = item.FindItemFeatureByID(ConfrontDiscountFeature);
            if (feature == null)
            {
                // The native purchase path removes TemporaryBuying features after
                // payment, so a seller's concession cannot lower later resale value.
                feature = new ItemFeature(ItemFeature.FeatureType.TemporaryBuying)
                {
                    identifier = ConfrontDiscountFeature,
                    category = ConfrontDiscountFeature,
                    isExposable = false
                };
                feature.SetValueStage(ItemFeature.ValueStage.Final);
                feature.SetPublicDisplay(Text("gpu_confront_discount"));
                feature.SetActualDisplay(Text("gpu_confront_discount"));
                item.AddItemFeature(feature);
            }
            feature.customIntValue1 |= choice & 15;
            int percent = 0;
            for (int bits = feature.customIntValue1 & 15; bits != 0; bits >>= 1) percent += (bits & 1) * 10;
            feature.SetPreExposedValueMod(-percent);
            feature.SetValueModifier(-percent);
        }

        private static string Display(string id, double value)
        {
            if (id == SupplyFeature) return Text(value > 0 ? NormalSupplyTextKey : MissingSupplyTextKey);
            if (id == GoldfingerFeature) return Text(value > 0 ? "gpu_goldfinger_normal" : "gpu_goldfinger_missing");
            return Text(id == ComputeFeature ? GpuRuntime.ComputePowerTextKey : GpuRuntime.PowerPerCycleTextKey,
                value.ToString("0.##", CultureInfo.CurrentCulture));
        }

        internal static string NegotiationDisplay(ItemFeature feature) => ValueDisplay(feature.identifier, feature.GetCurrentItemCondition());

        internal static string ValueDisplay(string id, ItemCondition condition)
        {
            if (ReadValue(condition, 1) < 0.5)
            {
                if (id == SupplyFeature) return Text("gpu_power_supply_missing_value", 60);
                if (id == GoldfingerFeature) return Text("gpu_goldfinger_missing_value", 80);
            }
            return condition.display;
        }

        internal static bool CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            Ensure(item);
            GpuInspectionState state = GpuInspectionState.Read(item);
            if (state == null || !state.TryGetPerformance(out var actual)) return false;
            ItemFeature compute = item.FindItemFeatureByID(ComputeFeature);
            ItemFeature power = item.FindItemFeatureByID(PowerFeature);
            ItemFeature supply = item.FindItemFeatureByID(SupplyFeature);
            ItemFeature goldfinger = item.FindItemFeatureByID(GoldfingerFeature);
            if (compute == null || power == null || supply == null || goldfinger == null) return false;
            double claimedCompute = ReadValue(compute.GetCurrentItemCondition(), 0);
            double claimedPower = ReadValue(power.GetCurrentItemCondition(), 0);
            bool claimedSupply = ReadValue(supply.GetCurrentItemCondition(), 0) > 0;
            // Memory effectiveness ranges from 0 (all missing/mismatched) to 1
            // (all normal). The frozen core budget is the maximum, not the midpoint.
            builder.AddLine(Display(ComputeFeature, claimedCompute), color: StatColor(claimedCompute, state.CoreComputePower.Value / 2d, true));
            builder.AddLine(Display(PowerFeature, claimedPower), color: StatColor(claimedPower, GpuPerformanceCalculator.PowerMidpoint(state), false));
            if (!claimedSupply)
                builder.AddLine(Display(SupplyFeature, 0), color: RenderHandler.ColorPalette.LightRed);
            if (!GpuPerformanceCalculator.HasWorkingPowerModule(state))
                builder.AddLine(Text("gpu_power_modules_missing"), color: RenderHandler.ColorPalette.LightRed);
            if (ReadValue(goldfinger.GetCurrentItemCondition(), 1) < 0.5)
                builder.AddLine(Display(GoldfingerFeature, 0), color: RenderHandler.ColorPalette.LightRed);
            return true;
        }

        internal static RenderHandler.ColorPalette StatColor(double value, double basis, bool higherIsBetter)
        {
            if (Math.Abs(value - basis) < 1e-9) return RenderHandler.ColorPalette.LightCyan;
            return (value > basis) == higherIsBetter ? RenderHandler.ColorPalette.LightGreen : RenderHandler.ColorPalette.LightRed;
        }

        internal static void Shutdown()
        {
            if (editor != null) editor.CloseUI();
            editor = null;
            foreach (var entry in owned)
            {
                if (ItemConditionList.conditionLookup.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry.Value))
                {
                    ItemConditionList.conditionLookup.Remove(entry.Key);
                    if (ItemFeatureList.CategoryNames.TryGetValue(entry.Key, out string currentName) && currentName == names[entry.Key])
                        ItemFeatureList.CategoryNames.Remove(entry.Key);
                }
            }
            owned.Clear(); names.Clear();
        }

        internal static void TrackEditor(LabelerUIManager manager, GameItem item)
        {
            RestoreEditorLayout();
            editor = GpuRuntime.IsGpu(item) ? manager : null;
            if (editor?.verticalLayoutGroup == null) return;
            // Native OpenUI defers destruction until the frame ends. Exclude old
            // rows from layout while it creates the new card's labels.
            foreach (var row in editor.verticalLayoutGroup.GetComponentsInChildren<LabelerElement>(true))
                row.gameObject.SetActive(false);
        }

        internal static void FitEditor(LabelerUIManager manager)
        {
            if (editor != manager || manager.panel == null || manager.verticalLayoutGroup == null) return;
            var panel = manager.panel.GetComponent<RectTransform>();
            var list = manager.verticalLayoutGroup.GetComponent<RectTransform>();
            if (panel == null || list == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            var corners = new Vector3[4];
            float bottom = panel.rect.yMax;
            foreach (var row in manager.verticalLayoutGroup.GetComponentsInChildren<LabelerElement>(false))
            {
                if (!(row.transform is RectTransform rowRect)) continue;
                rowRect.GetWorldCorners(corners);
                bottom = Math.Min(bottom, panel.InverseTransformPoint(corners[0]).y);
            }
            float padding = 12;
            if (manager.repeatButton != null && manager.repeatButton.transform.IsChildOf(panel) &&
                manager.repeatButton.transform is RectTransform footer)
            {
                footer.GetWorldCorners(corners);
                float footerTop = panel.InverseTransformPoint(corners[1]).y;
                if (footerTop < panel.rect.center.y) padding = Math.Max(padding, footerTop - panel.rect.yMin + 12);
            }
            float extra = Math.Max(0, panel.rect.yMin + padding - bottom);
            if (extra <= 0) return;
            editorPanel = panel; editorList = list;
            editorPanelSize = panel.sizeDelta;
            editorPanelPosition = panel.anchoredPosition;
            editorListPosition = list.anchoredPosition;
            list.GetWorldCorners(corners);
            Vector3 listTop = corners[1];
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, panel.sizeDelta.y + extra);
            panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, panel.anchoredPosition.y - extra * (1 - panel.pivot.y));
            Canvas.ForceUpdateCanvases();
            // Preserve the list's top even when its anchors are centered in the
            // native frame; only the frame's bottom needs to move.
            list.GetWorldCorners(corners);
            Vector3 position = list.position;
            list.position = new Vector3(position.x + listTop.x - corners[1].x,
                position.y + listTop.y - corners[1].y, position.z + listTop.z - corners[1].z);
        }

        private static void RestoreEditorLayout()
        {
            if (editorPanel != null)
            {
                editorPanel.sizeDelta = editorPanelSize;
                editorPanel.anchoredPosition = editorPanelPosition;
            }
            if (editorList != null) editorList.anchoredPosition = editorListPosition;
            editorPanel = editorList = null;
        }

        internal static void EditorClosed(LabelerUIManager manager)
        {
            if (editor != manager) return;
            RestoreEditorLayout();
            editor = null;
        }

        private static string Text(string key, params object[] values) => ModHelper.GetLocalized(ModConstants.ModId, key, values);
    }

    [HarmonyPatch(typeof(LabelerElement), nameof(LabelerElement.InitElement))]
    internal static class GpuLabelElementPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(LabelerElement __instance, ItemFeature itemFeature)
        {
            if (!GpuLabels.IsFeature(itemFeature) ||
                !ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled)) return;
            // The native editor always appends a value percentage. GPU prices are
            // derived separately; only physical-defect choices show a penalty.
            __instance.currentFeatureDisplay.text = GpuLabels.NegotiationDisplay(itemFeature);
            var conditions = ItemConditionList.conditionLookup[itemFeature.category];
            for (int i = 0; i < conditions.Count; i++) __instance.dropdown.options[i].text = GpuLabels.ValueDisplay(itemFeature.identifier, conditions[i]);
            __instance.dropdown.RefreshShownValue();
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), "BuildExpandedRow")]
    internal static class GpuNegotiationLabelsPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(NegociationUIManager __instance)
        {
            if (!ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled)) return;
            foreach (var row in __instance.GetComponentsInChildren<ItemFeatureUI>(true))
                if (row.TMP != null && GpuLabels.IsFeature(row.itemFeature))
                    row.TMP.text = GpuLabels.NegotiationDisplay(row.itemFeature);
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetComplexeItemValue))]
    internal static class GpuPricePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(GameItem __instance, ref long __result)
        {
            if (GpuRuntime.IsGpu(__instance) && ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled))
                __result += GpuLabels.QuotedPrice(__instance) - __instance.unitValue;
        }
    }

    [HarmonyPatch(typeof(LabelerUIManager), nameof(LabelerUIManager.OpenUI))]
    internal static class GpuLabelerOpenPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(LabelerUIManager __instance, GameItem gameItem)
        {
            if (ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled))
            {
                GpuLabels.Ensure(gameItem);
                GpuLabels.TrackEditor(__instance, gameItem);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(LabelerUIManager __instance) => GpuLabels.FitEditor(__instance);
    }

    [HarmonyPatch(typeof(LabelerUIManager), nameof(LabelerUIManager.CloseUI))]
    internal static class GpuLabelerClosePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(LabelerUIManager __instance) => GpuLabels.EditorClosed(__instance);
    }

    [HarmonyPatch(typeof(LabelerHelper), nameof(LabelerHelper.HandleLine))]
    internal static class GpuLabelTooltipPatch
    {
        // The GPU's late tooltip renders these native labels once, with stat colors.
        [HarmonyPrefix]
        internal static bool Prefix(ItemFeature itemFeature) => !GpuLabels.IsFeature(itemFeature) ||
            !ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled);
    }
}
