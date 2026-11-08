using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.ElectronicScanner;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class ElectronicScannerChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        var previousPlayer = PlayerStore.Instance;
        var previousWaterScanner = WaterScannerUIManager.Instance;
        var previousRaisedCanvas = UIFamilyLayer.RaisedCanvas;
        var previousTranslations = new Dictionary<string, Dictionary<string, string>>(ModHelper.Translations);
        string previousLocale = ModHelper.Locale;
        var loaded = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(loaded.Manifest);
        registration.Register(new ModItemDirectory(), definitions);
        PlayerStore.Instance = new PlayerStore();
        WaterScannerUIManager.Instance = new WaterScannerUIManager();
        UIFamilyLayer.RaisedCanvas = new GameObject("RaisedMachines").AddComponent<Canvas>();
        UIFamilyLayer.RaisedCanvas.sortingOrder = 6;
        try
        {
            foreach (string locale in new[] { "zh", "en" }) ModHelper.Translations[locale] = Program.ReadLocale(locale);
            ModHelper.Locale = "zh";
            GameItem scanner = DirectoryMaster.Item(ElectronicScannerLogic.ItemId);
            Assert(scanner.IsTag("TOOL") && scanner.IsTag("electronics") && scanner.IsTag("CAN_USE_ON_NON_OWNED_TAG") &&
                scanner.maySelectSlotItemFunc(scanner, null, null) && scanner.canSelectSlotItemFunc(scanner, null, null),
                "The tool registers native TOOL plus electronics categories and is selectable by its owner.");
            scanner.SetValue(29);
            scanner.onLoaded(scanner);
            Assert(scanner.unitValue == 29 && scanner.soundUse == null && scanner.maySelectSlotItemFunc(scanner, null, null),
                "Loading restores tool interactions without overwriting instance value.");
            GameItem card = GpuRuntime.CreateStandardItem("age_of_agi.gpu0");
            var state = GpuInspectionState.Read(card);
            state.RealComputePower = 7.125;
            state.RealPowerPerCycle = 19;
            state.PowerSupplyWorking = false;
            foreach (var part in state.Parts.Where(part => part.Slot == "power_interface" || part.Slot == "goldfinger_down"))
            { part.Sprite = ""; part.Condition = "empty"; }
            card.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(state)));
            foreach (string id in new[] { GpuLabels.ComputeFeature, GpuLabels.PowerFeature, GpuLabels.SupplyFeature, GpuLabels.GoldfingerFeature })
                GpuLabels.SetClaim(card, id, id == GpuLabels.ComputeFeature ? 30 : 1);
            card.ModifyTag(GpuRuntime.ComputePowerTag, tag => tag.SetDouble(999));
            card.ModifyTag(GpuRuntime.PowerPerCycleTag, tag => tag.SetInt(0));
            string hardware = card.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            string[] claims = Claims(card);
            card.Owned = false;
            Assert(scanner.mayThisTargetItemFunc(scanner, card) && scanner.canThisTargetItemFunc(scanner, card) &&
                !ElectronicScannerLogic.CanMatch(scanner, card) && !ElectronicScannerLogic.Match(scanner, card) &&
                !ElectronicScannerLogic.CanScan(scanner, scanner) && !ElectronicScannerLogic.CanScan(scanner, new GameItem()),
                "Owned scanners can inspect unpurchased GPUs, reject unrelated targets, and cannot rewrite unowned labels.");
            string[] readings = ElectronicScannerLogic.Readings(card);
            Assert(readings.Length == 4 && readings[2] == "供电接口缺失" && readings[3] == "金手指缺失" &&
                GpuLabels.ReadValue(card.FindItemFeatureByID(GpuLabels.ComputeFeature).realCondition, -1) == 7.125 &&
                GpuLabels.ReadValue(card.FindItemFeatureByID(GpuLabels.PowerFeature).realCondition, -1) == 19 &&
                Claims(card).SequenceEqual(claims), "Detection reads frozen truth rather than edited tags/labels and leaves claims untouched.");
            int scansBefore = AudioManager.Instance.Played3.Count;
            int opensBefore = EmporiumAudioPlayer.Instance.audioButton2.Plays;
            scanner.onThisTargetItemFunc(scanner, card);
            GameObject root = OpenPanel();
            Assert(!WaterScannerUIManager.Instance.panel.activeSelf && root.GetComponent<Image>().sprite.texture == loaded.Textures["aqua_scan"] &&
                root.GetComponentsInChildren<TextMeshProUGUI>(false).Single(text => text.gameObject.name == "readings").text == string.Join("\n", readings),
                "The standalone scanner uses its own artwork and truthful results without opening or modifying the native water scanner.");
            Assert(AudioManager.Instance.Played3.Count == scansBefore + 1 && AudioManager.Instance.Played3.Last() == scanner.soundActivate &&
                EmporiumAudioPlayer.Instance.audioButton2.Plays == opensBefore + 1,
                "Successful scanning explicitly plays its configured activation and native opening sounds once, including after load.");
            // Native ItemMouseDragHandler.EndDrag plays this after item.Target.
            AudioManager.Instance.TryPlayOne(scanner.soundDragEnd);
            Assert(AudioManager.Instance.PrimaryClip == scanner.soundDragEnd && AudioManager.Instance.ThirdClip == scanner.soundActivate,
                "The native drag-end drop sound cannot replace the scanner clip on its independent third channel.");
            Assert(root.GetComponent<Canvas>().overrideSorting && root.GetComponent<Canvas>().sortingOrder > UIFamilyLayer.RaisedCanvas.sortingOrder &&
                root.GetComponent<GraphicRaycaster>() != null && root.GetComponent<UIDragHandler>() != null &&
                root.GetComponent<PaperPanelFocusStamp>() != null,
                "The scanner has native dragging/focus and an interactive canvas above raised machine windows.");
            CheckNativeStyles(root);
            Button match = Button(root, "match");
            Assert(!match.interactable && !match.gameObject.activeSelf && match.gameObject.GetComponentInParent<Canvas>() != null &&
                match.gameObject.GetComponent<Image>().sprite.texture == loaded.Textures["aqua_scan_button"],
                "The provided button art is used and matching stays hidden until a mismatched GPU is purchased.");
            match.onClick.Invoke();
            Assert(Claims(card).SequenceEqual(claims), "A direct disabled-button callback cannot bypass target ownership.");
            card.Owned = true;
            Tick(root);
            Assert(match.interactable && match.gameObject.activeSelf, "Buying an inspected mismatched GPU immediately shows the match action.");
            int labelsBefore = EmporiumAudioPlayer.Instance.audioLabeler.Plays;
            match.onClick.Invoke();
            Assert(card.itemFeatures.Where(GpuLabels.IsFeature).All(feature => feature.IsFeatureMatch() && !feature.IsExposed()) &&
                card.GetTagReadonly(GpuInspectionState.StateTag).GetString() == hardware &&
                GpuRuntime.GetComputePower(card) == 7.125 && GpuRuntime.GetPowerPerCycle(card) == 19 &&
                card.GetTagReadonly(GpuInspectionCalculator.QuestionedTag).GetInt() == 0 &&
                card.FindItemFeatureByID(GpuLabels.ConfrontDiscountFeature) == null && scanner.unitCount == 1 &&
                EmporiumAudioPlayer.Instance.audioLabeler.Plays == labelsBefore + 1 && WaterScannerUIManager.Instance.NativeMatches == 0,
                "Matching corrects all four labels without consuming the tool, changing hardware, consuming questions or granting discounts.");
            Assert(!match.interactable && !match.gameObject.activeSelf, "A fully matched result immediately hides redundant matching.");
            GpuLabels.SetClaim(card, GpuLabels.ComputeFeature, 999);
            Tick(root);
            Assert(match.gameObject.activeSelf && match.interactable, "A new mismatch restores the match button while inspection remains open.");
            GameItem other = GpuRuntime.CreateStandardItem("age_of_agi.gpu1");
            GpuLabels.SetClaim(other, GpuLabels.ComputeFeature, 999);
            ElectronicScannerPanel.Show(scanner, other);
            GameObject second = OpenPanel();
            match.onClick.Invoke();
            Assert(!root.activeSelf && root.Destroyed && second != root &&
                GpuLabels.ReadValue(other.FindItemFeatureByID(GpuLabels.ComputeFeature).fakeCondition, -1) == 999,
                "Switching targets releases the old panel; stale match callbacks cannot act on a new target.");
            scanner.Owned = false;
            Tick(second);
            Assert(!second.activeSelf && !ElectronicScannerLogic.CanScan(scanner, other),
                "Losing tool ownership closes detection and prevents further use.");
            scanner.Owned = true;
            ModHelper.Locale = "en";
            ElectronicScannerPanel.Show(scanner, other);
            GameObject english = OpenPanel();
            Assert(english.GetComponentsInChildren<TextMeshProUGUI>(false).Single(text => text.gameObject.name == "heading").text.StartsWith("[Test results]\n"),
                "The panel localizes its result heading and retains the native language-aware font.");
            Button close = Button(english, "close");
            Assert(english.GetComponentsInChildren<Button>(false).Length == 2 && close.transition == Selectable.Transition.SpriteSwap &&
                close.gameObject.GetComponent<Image>().sprite.texture == loaded.Textures["close_normal"] &&
                close.spriteState.pressedSprite.texture == loaded.Textures["close_pressed_down"] &&
                close.gameObject.transform.anchoredPosition.x == 180 && close.gameObject.transform.anchoredPosition.y == -46,
                "The centered native match button is accompanied by a top-right X with normal and pressed artwork, without a footer close button.");
            int closesBefore = EmporiumAudioPlayer.Instance.audioButton4.Plays;
            close.onClick.Invoke();
            close.onClick.Invoke();
            Assert(!english.activeSelf && EmporiumAudioPlayer.Instance.audioButton4.Plays == closesBefore + 1,
                "Clicking X closes the owned panel and plays the native close sound once, even if a stale click is repeated.");
            ElectronicScannerPanel.Show(scanner, other);
            english = OpenPanel();
            Sprite ownedSprite = english.GetComponent<Image>().sprite;
            ElectronicScannerLogic.Shutdown();
            Assert(!english.activeSelf && ownedSprite.Destroyed, "Disabling the mod releases the owned panel and generated sprites.");
        }
        finally
        {
            ElectronicScannerLogic.Shutdown();
            registration.Unregister();
            PlayerStore.Instance = previousPlayer;
            WaterScannerUIManager.Instance = previousWaterScanner;
            UIFamilyLayer.RaisedCanvas = previousRaisedCanvas;
            ModHelper.Translations.Clear();
            foreach (var entry in previousTranslations) ModHelper.Translations.Add(entry.Key, entry.Value);
            ModHelper.Locale = previousLocale;
        }
        Console.WriteLine("PASS: electronic analyzer tool, authoritative truth, owned-label matching, supplied UI art, target isolation and cleanup.");
    }

    private static string[] Claims(GameItem card) => card.itemFeatures.Where(GpuLabels.IsFeature).Select(feature => feature.fakeCondition.identifier).ToArray();
    private static void CheckNativeStyles(GameObject root)
    {
        var native = WaterScannerUIManager.Instance;
        foreach (var pair in new[] { (Name: "heading", Source: native.testedTMP), (Name: "labels", Source: native.labelTMP),
            (Name: "readings", Source: native.contaminantTMP) })
        {
            var text = root.GetComponentsInChildren<TextMeshProUGUI>(true).Single(value => value.gameObject.name == pair.Name);
            Assert(ReferenceEquals(text.font, pair.Source.font) && text.fontSize == pair.Source.fontSize &&
                text.color.Equals(pair.Source.color) && text.alignment == pair.Source.alignment &&
                text.gameObject.transform.sizeDelta.x == pair.Source.gameObject.transform.sizeDelta.x &&
                text.gameObject.transform.sizeDelta.y == pair.Source.gameObject.transform.sizeDelta.y,
                "Each scanner region retains its corresponding water UI font, size, color, alignment and layout bounds.");
        }
        Assert(root.transform.sizeDelta.x == native.panel.transform.sizeDelta.x && root.transform.sizeDelta.y == native.panel.transform.sizeDelta.y &&
            root.transform.localScale.x == native.panel.transform.localScale.x &&
            Button(root, "match").transform.position.x == native.matchButton.transform.position.x,
            "The scanner frame uses native dimensions/scale and the native match-button placement.");
    }
    private static GameObject OpenPanel() => WaterScannerUIManager.Instance.panel.transform.parent.Children
        .Select(child => child.gameObject).Single(node => node.name == "AgeOfAgiElectronicScanner" && node.activeSelf && !node.Destroyed);
    private static Button Button(GameObject root, string name) => root.GetComponentsInChildren<Button>(true).Single(button => button.gameObject.name == name);
    private static void Tick(GameObject root) => typeof(ElectronicScannerPanel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(root.GetComponent<ElectronicScannerPanel>(), null);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
