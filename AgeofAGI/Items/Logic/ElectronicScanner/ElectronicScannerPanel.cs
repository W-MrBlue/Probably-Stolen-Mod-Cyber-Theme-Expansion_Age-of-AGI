using System;
using AgeOfAgi.Items.Logic.Gpu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfAgi.Items.Logic.ElectronicScanner
{
    internal sealed class ElectronicScannerPanel : MonoBehaviour
    {
        private static ElectronicScannerPanel current;
        private static Sprite face, buttonFace, closeFace, closePressed;
        private GameItem scanner, target;
        private TextMeshProUGUI heading, labelStatus, readings;
        private Button match;
        private Canvas layer, sourceCanvas;

        internal static bool Show(GameItem source, GameItem inspected)
        {
            if (!ElectronicScannerLogic.CanScan(source, inspected) || WaterScannerUIManager.Instance == null) return false;
            var template = WaterScannerUIManager.Instance;
            if (current != null) current.Close();
            LoadSprite("aqua_scan", ref face);
            LoadSprite("aqua_scan_button", ref buttonFace);
            LoadSprite("close_normal", ref closeFace);
            LoadSprite("close_pressed_down", ref closePressed);
            var root = new GameObject("AgeOfAgiElectronicScanner", typeof(RectTransform), typeof(Image));
            root.SetActive(false);
            root.layer = template.panel.layer;
            var nativeRect = (RectTransform)template.panel.transform;
            root.transform.SetParent(nativeRect.parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = nativeRect.pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(nativeRect.rect.width, nativeRect.rect.height);
            rect.localScale = nativeRect.localScale;
            root.GetComponent<Image>().sprite = face;
            current = root.AddComponent<ElectronicScannerPanel>();
            current.scanner = source;
            current.target = inspected;
            current.sourceCanvas = template.panel.GetComponentInParent<Canvas>();
            current.layer = root.AddComponent<Canvas>();
            current.layer.overrideSorting = true;
            if (current.sourceCanvas != null) current.layer.sortingLayerID = current.sourceCanvas.sortingLayerID;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<PaperPanelFocusStamp>();
            root.AddComponent<UIDragHandler>().id = ModConstants.ModId + ".electronic_scanner";
            current.heading = current.CopyControl(template.testedTMP, nativeRect, "heading");
            current.labelStatus = current.CopyControl(template.labelTMP, nativeRect, "labels");
            current.readings = current.CopyControl(template.contaminantTMP, nativeRect, "readings");
            current.match = current.CopyControl(template.matchButton, nativeRect, "match");
            current.match.gameObject.GetComponent<Image>().sprite = buttonFace;
            current.match.onClick = new Button.ButtonClickedEvent();
            current.match.onClick.AddListener(current.MatchLabels);
            foreach (var caption in current.match.GetComponentsInChildren<TextMeshProUGUI>(true))
                caption.text = Text("electronic_scanner_match");
            current.AddCloseButton(nativeRect);
            current.Refresh();
            root.SetActive(true);
            PaperPanelFocusStamp.BumpPanel(root);
            EmporiumAudioPlayer.Instance.audioButton2.Play();
            return true;
        }

        private static void LoadSprite(string key, ref Sprite sprite)
        {
            LoadedMod mod = ModLoader.Mods.Find(value => value.Manifest.ID == ModConstants.ModId && value.IsEnabled);
            if (mod == null || !mod.Textures.TryGetValue(key, out var texture) || texture == null)
                throw new InvalidOperationException("Electronic scanner texture is not loaded: " + key);
            if (sprite != null && sprite.texture == texture) return;
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100);
        }

        private T CopyControl<T>(T template, RectTransform nativePanel, string name) where T : Component
        {
            var node = UnityEngine.Object.Instantiate(template.gameObject, transform, false);
            node.name = name;
            var original = (RectTransform)template.transform;
            var rect = (RectTransform)node.transform;
            rect.anchorMin = rect.anchorMax = nativePanel.pivot;
            rect.sizeDelta = new Vector2(original.rect.width, original.rect.height);
            Vector3 position = nativePanel.InverseTransformPoint(original.position);
            rect.anchoredPosition = new Vector2(position.x, position.y);
            rect.localPosition = position;
            Vector3 scale = original.lossyScale, panelScale = nativePanel.lossyScale;
            rect.localScale = new Vector3(scale.x / panelScale.x, scale.y / panelScale.y, scale.z / panelScale.z);
            foreach (var behaviour in node.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour is TMPLocalizer localizer)
                {
                    localizer.ChangeFontOnly = true;
                    localizer.enabled = true;
                }
                else if (behaviour.GetType().Name == "LocalizeStringEvent") behaviour.enabled = false;
            }
            foreach (var text in node.GetComponentsInChildren<TextMeshProUGUI>(true)) text.raycastTarget = false;
            node.SetActive(true);
            return node.GetComponent<T>();
        }

        private void AddCloseButton(RectTransform nativePanel)
        {
            var node = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            node.layer = gameObject.layer;
            node.transform.SetParent(transform, false);
            float scaleX = nativePanel.rect.width / 211f, scaleY = nativePanel.rect.height / 307f;
            Place((RectTransform)node.transform, 180 * scaleX, 46 * scaleY, 16 * scaleX, 16 * scaleY);
            var image = node.GetComponent<Image>();
            image.sprite = closeFace;
            var button = node.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { highlightedSprite = closeFace, selectedSprite = closeFace,
                pressedSprite = closePressed, disabledSprite = closeFace };
            button.onClick.AddListener(CloseByUser);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.localScale = Vector3.one;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private void Update() => Refresh();

        private void MatchLabels()
        {
            if (current != this || !gameObject.activeSelf) return;
            if (ElectronicScannerLogic.Match(scanner, target)) EmporiumAudioPlayer.Instance.audioLabeler.Play();
            Refresh();
        }

        private void Refresh()
        {
            if (!ElectronicScannerLogic.CanScan(scanner, target)) { Close(); return; }
            heading.text = "[" + Text("electronic_scanner_result") + "]\n" + target.GetDisplayName();
            readings.text = string.Join("\n", ElectronicScannerLogic.Readings(target));
            labelStatus.text = Text("electronic_scanner_labels") + "\n" +
                Text(target.itemFeatures.TrueForAll(feature => !GpuLabels.IsFeature(feature) || feature.IsFeatureMatch())
                    ? "electronic_scanner_matched" : "electronic_scanner_mismatched");
            bool canMatch = ElectronicScannerLogic.CanMatch(scanner, target);
            match.gameObject.SetActive(canMatch);
            match.interactable = canMatch;
            layer.sortingOrder = Math.Max(3, Math.Max(sourceCanvas != null ? sourceCanvas.sortingOrder : 0,
                UIFamilyLayer.RaisedCanvas != null ? UIFamilyLayer.RaisedCanvas.sortingOrder : 0)) + 1;
        }

        private void CloseByUser()
        {
            if (current != this || !gameObject.activeSelf) return;
            EmporiumAudioPlayer.Instance.audioButton4.Play();
            Close();
        }

        private void Close()
        {
            scanner = target = null;
            gameObject.SetActive(false);
            UnityEngine.Object.Destroy(gameObject);
            if (current == this) current = null;
        }

        internal static void Shutdown()
        {
            if (current != null) current.Close();
            current = null;
            if (face != null) UnityEngine.Object.Destroy(face);
            if (buttonFace != null) UnityEngine.Object.Destroy(buttonFace);
            if (closeFace != null) UnityEngine.Object.Destroy(closeFace);
            if (closePressed != null) UnityEngine.Object.Destroy(closePressed);
            face = buttonFace = closeFace = closePressed = null;
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }
}
