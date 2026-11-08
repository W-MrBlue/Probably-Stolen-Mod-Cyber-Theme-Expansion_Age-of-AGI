using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Gpu;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

internal sealed class FuelUI : MonoBehaviour { }

internal sealed class WaterScannerUIManager
{
    internal static WaterScannerUIManager Instance;
    internal readonly GameObject panel = new GameObject("NativeWaterScanner");
    internal readonly TextMeshProUGUI testedTMP;
    internal readonly TextMeshProUGUI labelTMP, contaminantTMP;
    internal readonly Button matchButton;
    internal int NativeMatches;
    internal WaterScannerUIManager()
    {
        var host = new GameObject("NativeWaterScannerHost");
        host.transform.sizeDelta = new Vector2(1280, 720);
        host.AddComponent<Canvas>();
        panel.transform.SetParent(host.transform, false);
        panel.transform.sizeDelta = new Vector2(211, 307);
        panel.transform.localScale = new Vector3(2, 2, 1);
        testedTMP = NativeText("NativeWaterReading", new Vector3(0, 48, 0), 7.5f, new Color(0.2f, 0.6f, 0.7f), TextAlignmentOptions.Center);
        labelTMP = NativeText("NativeWaterLabel", new Vector3(0, 16, 0), 6.2f, new Color(0.3f, 0.7f, 0.5f), TextAlignmentOptions.Center);
        contaminantTMP = NativeText("NativeWaterContents", new Vector3(14, -24, 0), 5.8f, new Color(0.5f, 0.47f, 0.44f), TextAlignmentOptions.MidlineRight);
        var button = new GameObject("NativeWaterMatch", typeof(Image), typeof(Button));
        button.transform.SetParent(panel.transform, false);
        button.transform.localPosition = new Vector3(0, -112, 0);
        button.transform.sizeDelta = new Vector2(73, 19);
        matchButton = button.GetComponent<Button>();
        matchButton.targetGraphic = button.GetComponent<Image>();
        matchButton.onClick.AddPersistent("OnMatch", 0, () => NativeMatches++);
        var caption = NativeText("NativeWaterMatchCaption", new Vector3(0, 0, 0), 7, new Color(0.18f, 0.12f, 0.24f), TextAlignmentOptions.Center);
        caption.transform.SetParent(button.transform, false);
        panel.SetActive(false);
    }

    private TextMeshProUGUI NativeText(string name, Vector3 position, float size, Color color, TextAlignmentOptions alignment)
    {
        var label = new GameObject(name);
        label.transform.SetParent(panel.transform, false);
        label.transform.localPosition = position;
        label.transform.sizeDelta = new Vector2(135, 38);
        var text = label.AddComponent<TextMeshProUGUI>();
        text.font = new TMP_FontAsset();
        text.fontSize = size; text.color = color; text.alignment = alignment;
        label.AddComponent<TMPLocalizer>();
        label.AddComponent<LocalizeStringEvent>();
        return text;
    }
}

internal sealed class UIDragHandler : MonoBehaviour { internal string id; }
internal sealed class PaperPanelFocusStamp : MonoBehaviour
{
    internal static void BumpPanel(GameObject root) { }
}
internal static class UIFamilyLayer { internal static Canvas RaisedCanvas; }

internal sealed class InspectionUIManager
{
    internal static bool TradeLocked;
    internal static bool IsConfrontLocked() => TradeLocked;
    private string pendingConfrontItemId = "", pendingConfrontType = "";
    internal string ConfrontItem, ConfrontType;
    internal int Successes, Failures;
    internal GameObject confrontPanel = new GameObject("Confront");
    internal Toggle toggle1 = NewToggle(), toggle2 = NewToggle(), toggle3 = NewToggle(), toggle4 = NewToggle(), toggle5 = NewToggle();
    internal TextMeshProUGUI confrontTitleTMP = new TextMeshProUGUI();
    internal GameObject appraisalPanel;
    internal GameObject magnifyingGlass;
    internal TMP_InputField apprisalServiceInputField;
    internal int NativePriceWrites, NativeEstimates;
    internal InspectionUIManager()
    {
        var canvas = new GameObject("InspectionCanvas");
        canvas.transform.sizeDelta = new Vector2(1280, 720);
        canvas.AddComponent<Canvas>();
        magnifyingGlass = new GameObject("MagnifyingGlass");
        magnifyingGlass.transform.SetParent(canvas.transform, false);
        canvas.transform.localPosition = new Vector3(43, 29, 0);
        canvas.transform.localScale = new Vector3(1.25f, 1.25f, 1);
        confrontPanel.transform.SetParent(canvas.transform, false);
        confrontPanel.transform.sizeDelta = new Vector2(250, 330);
        confrontPanel.transform.localPosition = new Vector3(400, -90, 0);
        var confrontTitle = new GameObject("ConfrontTitle");
        confrontTitle.transform.SetParent(confrontPanel.transform, false);
        confrontTitleTMP = confrontTitle.AddComponent<TextMeshProUGUI>();
        confrontTitleTMP.font = new TMP_FontAsset();
        confrontTitle.AddComponent<TMPLocalizer>();
        confrontTitle.AddComponent<LocalizeStringEvent>();
        appraisalPanel = new GameObject("NativeAppraisalPanel");
        var appraisalHost = new GameObject("InactiveAppraisalHost");
        appraisalHost.transform.SetParent(canvas.transform, false);
        appraisalHost.transform.sizeDelta = new Vector2(540, 200);
        appraisalHost.transform.localPosition = new Vector3(-280, 180, 0);
        appraisalPanel.transform.SetParent(appraisalHost.transform, false);
        appraisalPanel.transform.sizeDelta = new Vector2(260, 140);
        appraisalPanel.transform.localPosition = new Vector3(0, 210, 0);
        appraisalPanel.AddComponent<Image>();
        appraisalPanel.AddComponent<Canvas>().overrideSorting = true;
        var title = new GameObject("NativeTitle");
        title.transform.SetParent(appraisalPanel.transform, false);
        var amountLabel = title.AddComponent<TextMeshProUGUI>();
        amountLabel.text = "Native price";
        amountLabel.font = new TMP_FontAsset();
        amountLabel.margin = new Vector4(32, 24, 32, 24);
        var input = new GameObject("NativePriceInput");
        input.transform.SetParent(appraisalPanel.transform, false);
        input.AddComponent<Image>();
        apprisalServiceInputField = input.AddComponent<TMP_InputField>();
        var inputText = new GameObject("Value");
        inputText.transform.SetParent(input.transform, false);
        apprisalServiceInputField.textComponent = inputText.AddComponent<TextMeshProUGUI>();
        apprisalServiceInputField.onValueChanged.AddListener(_ => NativePriceWrites++);
        apprisalServiceInputField.onEndEdit.AddListener(_ => NativePriceWrites++);
        NativeButton("Minus", nameof(ChangeAppraisalServicePrice), -25, () => ChangeAppraisalServicePrice(-25));
        NativeButton("Plus", nameof(ChangeAppraisalServicePrice), 25, () => ChangeAppraisalServicePrice(25));
        NativeButton("Estimate", nameof(OnAppraisalServiceEstimated), 0, OnAppraisalServiceEstimated);
        appraisalPanel.SetActive(false);
    }
    private void NativeButton(string name, string method, int step, Action callback)
    {
        var node = new GameObject(name);
        node.transform.SetParent(appraisalPanel.transform, false);
        node.AddComponent<Image>();
        node.AddComponent<TextMeshProUGUI>().text = name;
        node.AddComponent<Button>().onClick.AddPersistent(method, step, callback);
    }
    internal void ChangeAppraisalServicePrice(int amount) => NativePriceWrites++;
    internal void OnAppraisalServiceEstimated() => NativeEstimates++;
    private static Toggle NewToggle()
    {
        var node = new GameObject("Toggle");
        node.AddComponent<TextMeshProUGUI>();
        return node.AddComponent<Toggle>();
    }
    internal void OnConfront() { GpuInspectionConfrontPatch.Prefix(this); }
    internal void OnConfrontSuccess() { Successes++; ConfrontItem = pendingConfrontItemId; ConfrontType = pendingConfrontType; }
    internal void OnConfrontFailed() { Failures++; ConfrontItem = pendingConfrontItemId; ConfrontType = pendingConfrontType; EmporiumAudioPlayer.Instance.audioFail.Play(); }
    internal FuelUI fuelUISmall = new GameObject("Small").AddComponent<FuelUI>();
    internal FuelUI fuelUILarge = new GameObject("Large").AddComponent<FuelUI>();
    internal GameItem currentInspectingItem;
    internal bool Closed;
    internal void OnInspectItem(GameItem gameItem)
    {
        ResetUI();
        currentInspectingItem = gameItem;
        GpuInspectionOpenPatch.Postfix(this, gameItem);
    }
    internal void ResetUI()
    {
        fuelUISmall.gameObject.SetActive(false);
        fuelUILarge.gameObject.SetActive(false);
        confrontPanel.SetActive(false);
        GpuInspectionResetPatch.Postfix();
    }
    internal void CloseUI() { Closed = true; currentInspectingItem = null; confrontPanel.SetActive(false); GpuInspectionClosePatch.Postfix(); }
}

internal sealed class NegociationUIManager
{
    internal static NegociationUIManager Instance = new NegociationUIManager();
    internal readonly List<ItemFeatureUI> Rows = new List<ItemFeatureUI>();
    internal T[] GetComponentsInChildren<T>(bool includeInactive) => Rows.OfType<T>().ToArray();
    internal void RefreshUI() { }
}

internal sealed class ItemFeatureUI
{
    internal TMPro.TextMeshProUGUI TMP = new TMPro.TextMeshProUGUI();
    internal ItemFeature itemFeature;
}

internal sealed partial class StoreUIManager
{
    internal static StoreUIManager Instance = new StoreUIManager();
    internal bool Typing;
    internal void StartTyping() => Typing = true;
    internal void StopTyping() => Typing = false;
}

internal sealed class CustomUIElement
{
    internal string Text;
    internal bool Interactable = true;
    internal bool Visible = true;
    internal Action Click;
    internal Action<string> Input;
    internal Action<bool> Toggle;
    internal CustomUIElement SetText(string text) { Text = text; return this; }
    internal CustomUIElement SetString(string text, bool notify = false) { Text = text; if (notify) Input?.Invoke(text); return this; }
    internal CustomUIElement SetPreferredSize(float width, float height) => this;
    internal CustomUIElement SetInteractable(bool value) { Interactable = value; return this; }
    internal CustomUIElement SetVisible(bool value) { Visible = value; return this; }
}

internal sealed class CustomUIWindow
{
    internal static CustomUIWindow Last;
    internal readonly Dictionary<string, CustomUIElement> Elements = new Dictionary<string, CustomUIElement>();
    internal Action onClose;
    internal bool Closed;
    internal CustomUIElement Get(string id) => Elements[id];
    internal void Close() { Closed = true; onClose?.Invoke(); }
}

internal sealed class CustomUIBuilder
{
    private readonly CustomUIWindow window;
    internal CustomUIBuilder(CustomUIWindow window = null) { this.window = window ?? new CustomUIWindow(); }
    private int automaticId;
    internal CustomUIElement Last { get; private set; }
    private CustomUIElement Add(string text, string id)
    {
        var element = new CustomUIElement { Text = text };
        window.Elements[id ?? "auto_" + automaticId++] = element;
        return Last = element;
    }
    internal CustomUIBuilder SetSize(float width, float height) => this;
    internal CustomUIBuilder Center() => this;
    internal CustomUIBuilder SetPosition(Vector2 position) => this;
    internal CustomUIBuilder SetDraggable(bool value) => this;
    internal CustomUIBuilder SetCloseOnEscape(bool value = true) => this;
    internal CustomUIBuilder OnClose(Action action) { window.onClose = action; return this; }
    internal CustomUIBuilder AddLabel(string text, string id = null) { Add(text, id); return this; }
    internal CustomUIBuilder AddInput(string placeholder, string initial, Action<string> onChanged, string id = null)
    { Add(initial, id).Input = onChanged; return this; }
    internal CustomUIBuilder AddToggle(string label, bool initial, Action<bool> onChanged = null, string id = null)
    { Add(label, id).Toggle = onChanged; return this; }
    internal CustomUIBuilder AddButton(string text, Action action, string id = null)
    { Add(text, id).Click = action; return this; }
    internal CustomUIBuilder BeginRow() => this;
    internal CustomUIBuilder BeginScroll(float height) => this;
    internal CustomUIBuilder AddFlexibleSpace() => this;
    internal CustomUIBuilder End() => this;
    internal CustomUIWindow Show() { CustomUIWindow.Last = window; return window; }
}

internal sealed class FontLoader
{
    internal enum FontRole { Noto_Sans }
}
internal sealed class TMPLocalizer : MonoBehaviour
{
    internal bool ChangeFontOnly;
    internal FontLoader.FontRole fontRole;
}
internal sealed class LocalizeStringEvent : MonoBehaviour { }
