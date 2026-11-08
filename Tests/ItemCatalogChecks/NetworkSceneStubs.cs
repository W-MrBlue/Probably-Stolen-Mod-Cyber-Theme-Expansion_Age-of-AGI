using System.Collections.Generic;
using System.Linq;

internal sealed class ItemContextHandler { }
internal sealed class RichTextElement
{
    internal string Text;
    internal void SetText(string text, int width, RenderHandler.ColorPalette color) { Text = text; }
}

internal sealed partial class PlayerStore
{
    internal List<GameItem> FindAllItem(bool isOwn = true)
    {
        var items = new List<GameItem>();
        void Add(GameItem item)
        {
            if (item.Destroyed || items.Contains(item)) return;
            items.Add(item);
            if (item.contentWindow?.childElement is MachineryPanelRow row)
                foreach (GameItem child in row.children.OfType<GameInventory>().SelectMany(inventory => inventory.childItems)) Add(child);
        }
        if (EmporiumEntry.Instance != null)
            foreach (var item in EmporiumEntry.Instance.GetAllItems()) Add(item);
        return items.Where(item => !isOwn || GeneralHelper.IsItemOwned(item)).ToList();
    }
}
