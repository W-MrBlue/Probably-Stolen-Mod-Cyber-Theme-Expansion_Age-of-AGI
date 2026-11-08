using HarmonyLib;

namespace AgeOfAgi.Items.Logic.NetworkSwitch
{
    [HarmonyPatch(typeof(ItemContextHandler), "SetButtonColor")]
    internal static class NetworkSwitchContextMenu
    {
        [HarmonyPostfix]
        private static void Postfix(RichTextElement button, RichTextElement selectedButton, bool isEnabled,
            GameItem ___currentItem, RichTextElement ___buttonActivate,
            RenderHandler.ColorPalette ___textColor, RenderHandler.ColorPalette ___textDisabledColor,
            RenderHandler.ColorPalette ___textSelectColor, RenderHandler.ColorPalette ___textSelectDisabledColor)
        {
            if (!NetworkSwitchLinks.Enabled || !NetworkSwitchUpgrades.IsNetworkSwitch(___currentItem) || button != ___buttonActivate) return;
            bool selected = button == selectedButton;
            var color = isEnabled ? (selected ? ___textSelectColor : ___textColor) :
                (selected ? ___textSelectDisabledColor : ___textDisabledColor);
            button.SetText(ModHelper.GetLocalized(ModConstants.ModId, "network_switch_clear"), 10000, color);
        }
    }
}
