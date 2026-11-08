using UnityEngine;
using UnityEngine.UI;

internal sealed class StoreUI : MonoBehaviour
{
    internal string identifier;
    internal bool isPaperUI, zoomable;
    internal void ToggleZoom() { }
}

internal sealed class PaperUIManager
{
    internal static PaperUIManager Instance;
    internal GameObject JoeWildBusinessCard;
    internal readonly TestAudioSource audioSourcePickup = new(), Drop = new();
    internal void OnJoeCardClosed() => Drop.Play();

    internal static void PrepareFixture()
    {
        var parent = new GameObject("fixture_paper_layer");
        var template = new GameObject("fixture_joe_card");
        template.transform.SetParent(parent.transform, false);
        template.transform.sizeDelta = new Vector2(420, 240);
        template.transform.localScale = new Vector3(1.25f, 1.25f, 1);
        template.AddComponent<StoreUI>().identifier = "joe_card";
        var art = template.AddComponent<Image>();
        art.sprite = new Sprite { rect = new Rect(0, 0, 80, 40) };
        var close = new GameObject("fixture_card_close");
        close.transform.SetParent(template.transform, false);
        close.AddComponent<TMPro.TextMeshProUGUI>().text = "X";
        close.AddComponent<Button>().onClick.AddPersistent("CloseOriginal", 0, () => template.SetActive(false));
        var zoom = new GameObject("fixture_card_zoom");
        zoom.transform.SetParent(template.transform, false);
        zoom.AddComponent<Button>().onClick.AddPersistent("ToggleZoom", 0, () => { });
        template.SetActive(false);
        Instance = new PaperUIManager { JoeWildBusinessCard = template };
    }
}
