using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AgeOfAgi.Items.Logic.StoryDocuments
{
    internal static class QuorraPhoneCard
    {
        private static GameObject panel;

        internal static void Toggle(Sprite detail, Sprite closeIcon)
        {
            var native = PaperUIManager.Instance;
            if (native == null || native.JoeWildBusinessCard == null) return;
            if (panel == null)
            {
                // Preserve the authored card dimensions, scale, drag behaviour and
                // close-button layout instead of treating source pixels as UI units.
                panel = Object.Instantiate(native.JoeWildBusinessCard, native.JoeWildBusinessCard.transform.parent, false);
                panel.SetActive(false);
                panel.name = ModConstants.ModId + ":quorra_phone_card";
                foreach (StoreUI ui in panel.GetComponentsInChildren<StoreUI>(true))
                {
                    ui.identifier = panel.name;
                    ui.isPaperUI = true;
                    ui.zoomable = false;
                }
                Image artwork = panel.GetComponentsInChildren<Image>(true)
                    .Where(image => image.sprite != null)
                    .OrderByDescending(image => image.sprite.rect.width * image.sprite.rect.height).FirstOrDefault();
                if (artwork == null)
                {
                    Object.Destroy(panel);
                    panel = null;
                    throw new InvalidOperationException("Joe Wild business-card template has no artwork.");
                }
                foreach (var text in panel.GetComponentsInChildren<TextMeshProUGUI>(true)) text.enabled = false;
                foreach (var image in panel.GetComponentsInChildren<Image>(true))
                    if (image != artwork && image.GetComponentInParent<Button>() == null) image.enabled = false;
                artwork.enabled = true;
                artwork.sprite = detail;
                artwork.type = Image.Type.Simple;
                artwork.preserveAspect = true;
                foreach (Button button in panel.GetComponentsInChildren<Button>(true))
                {
                    bool zoom = Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                        .Any(index => button.onClick.GetPersistentMethodName(index) == nameof(StoreUI.ToggleZoom));
                    button.onClick = new Button.ButtonClickedEvent();
                    if (zoom) button.gameObject.SetActive(false);
                    else
                    {
                        // The native card may use a text glyph for X; that text was
                        // removed with Joe's lettering. Give our close control its own image.
                        Image image = button.targetGraphic as Image ?? button.gameObject.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
                        foreach (Image child in button.GetComponentsInChildren<Image>(true)) child.enabled = child == image;
                        image.gameObject.SetActive(true);
                        image.enabled = true;
                        image.sprite = closeIcon;
                        image.color = Color.white;
                        image.type = Image.Type.Simple;
                        image.preserveAspect = true;
                        image.raycastTarget = true;
                        button.targetGraphic = image;
                        button.transition = Selectable.Transition.None;
                        button.spriteState = new SpriteState();
                        button.interactable = true;
                        button.gameObject.SetActive(true);
                        button.onClick.AddListener(Close);
                    }
                }
            }
            if (panel.activeSelf) Close();
            else
            {
                panel.SetActive(true);
                panel.transform.SetAsLastSibling();
                native.audioSourcePickup?.Play();
            }
        }

        private static void Close()
        {
            if (panel == null || !panel.activeSelf) return;
            panel.SetActive(false);
            PaperUIManager.Instance?.OnJoeCardClosed();
        }

        internal static void Shutdown()
        {
            Close();
            if (panel != null) Object.Destroy(panel);
            panel = null;
        }
    }
}
