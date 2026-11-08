using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Logic.Gpu;
using Newtonsoft.Json;

public sealed class ItemCondition
{
    internal string identifier, category, display;
    internal int modValue = 0, modValueDisplay = 0, compareValue = 0;
}

public sealed class ItemFeature
{
    internal enum FeatureType { Normale, Event, TemporaryBuying }
    internal enum ValueStage { Innate, Market, Final }
    internal ValueStage valueStage;
    internal FeatureType featureType = FeatureType.Normale;
    internal int customIntValue1, valueModifier;
    internal ItemFeature() { }
    internal ItemFeature(FeatureType type) { featureType = type; }
    internal ItemFeature SetValueStage(ValueStage stage) { valueStage = stage; return this; }
    internal ItemFeature SetPublicDisplay(string text) { return this; }
    internal ItemFeature SetActualDisplay(string text) { return this; }
    internal ItemFeature SetPreExposedValueMod(int value) { valueModifier = value; return this; }
    internal ItemFeature SetValueModifier(int value) { valueModifier = value; return this; }
    internal int GetCurrentValueMod() => useCondition ? GetCurrentItemCondition().modValue : valueModifier;
    internal string identifier, category;
    internal bool isExposable = true, useCondition;
    internal ItemCondition fakeCondition, realCondition;
    private bool exposed, discovered;
    internal bool IsExposed() => exposed;
    internal void SetExposed(bool value) => exposed = value;
    internal void ExposeFeature(bool lowkey = false, bool playBad = false) => exposed = true;
    internal bool IsFeatureDiscovered() => discovered;
    internal void SetFeatureDiscovered(bool value) => discovered = value;
    internal bool IsFeatureMatch() => fakeCondition.identifier == realCondition.identifier;
    internal bool GetPublicHidden() => false;
    internal ItemCondition GetCurrentItemCondition() => exposed ? realCondition : fakeCondition;
    internal string GetBaseCurrentDisplay() => GetCurrentItemCondition().display;
    internal void SetConditions(ItemCondition fake, ItemCondition real) { useCondition = true; fakeCondition = fake; realCondition = real; }
}

internal static class ItemConditionList
{
    internal static readonly Dictionary<string, List<ItemCondition>> conditionLookup = new Dictionary<string, List<ItemCondition>>();
}

internal static class ItemFeatureList
{
    internal static readonly Dictionary<string, string> CategoryNames = new Dictionary<string, string>();
}

internal sealed class LabelerUIManager
{
    internal static readonly LabelerUIManager Instance = new LabelerUIManager();
    internal UnityEngine.GameObject panel = null, verticalLayoutGroup = null, repeatButton = null;
    internal bool Closed;
    internal void OpenUI(GameItem gameItem) { Closed = false; GpuLabelerOpenPatch.Prefix(this, gameItem); }
    internal void CloseUI() { Closed = true; GpuLabelerClosePatch.Postfix(this); }
}
internal sealed class LabelerElement : UnityEngine.MonoBehaviour
{
    internal readonly TMPro.TextMeshProUGUI currentFeatureDisplay = new TMPro.TextMeshProUGUI();
    internal readonly TMPro.TMP_Dropdown dropdown = new TMPro.TMP_Dropdown();
    internal void InitElement(ItemFeature itemFeature)
    {
        currentFeatureDisplay.text = itemFeature.GetCurrentItemCondition().display + "(价值：0%)";
        foreach (var condition in ItemConditionList.conditionLookup[itemFeature.category])
            dropdown.options.Add(new TMPro.TMP_Dropdown.OptionData(condition.display + "(价值：0%)"));
        GpuLabelElementPatch.Postfix(this, itemFeature);
    }
}
internal static class WaterHelper
{
    internal static bool IsEmpty(GameItem item) => false;
    internal static int GetCurrentCapacityML(GameItem item) => item.GetTagReadonly("TEST_WATER_ML").GetInt();
    internal static int GetWaterPurity(GameItem item) => item.GetTagReadonly("TEST_REAL_PURITY").GetInt();
    internal static void RemoveMl(GameItem item, int amount) => item.ModifyTag("TEST_WATER_ML",
        tag => tag.Enable().SetInt(GetCurrentCapacityML(item) - amount));
}

internal static class WaterFeatureHelper
{
    internal const int HIGH_QUALITY_WATER_PURITY = 9900;
}

internal static class GpuTestState
{
    internal static void SetReal(GameItem item, double? compute = null, int? power = null, bool? supply = null)
    {
        var state = GpuInspectionState.Read(item);
        if (compute.HasValue) state.RealComputePower = compute.Value;
        if (power.HasValue) state.RealPowerPerCycle = power.Value;
        if (supply.HasValue) state.PowerSupplyWorking = supply.Value;
        item.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
    }
}

internal static class SaveManager
{
    internal static bool IsDecoding;
}
