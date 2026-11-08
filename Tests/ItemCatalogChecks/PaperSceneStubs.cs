using System.Collections.Generic;

// Only scene controls are substituted. The item uses the native PaperUI API.
internal sealed class PaperUI
{
    internal sealed class ChromeElement { internal string action, sprite, hoverSprite; }
    internal sealed class BookData { internal readonly List<ChromeElement> chrome = new() { new ChromeElement { action = "close" } }; }
    internal readonly BookData Data = new();
    internal static readonly List<PaperUI> Created = new List<PaperUI>();
    internal string Id, Background, ModId;
    internal float Width, Height;
    internal bool Visible, Closed;
    internal int Shows;
    internal readonly List<string> Texts = new();
    internal PaperUI AddText(float x, float y, float w, float h, string text, string style = "body", string id = null, string align = "left", float fontSize = 0)
    { Texts.Add(text); return this; }
    internal PaperUI AddImage(float x, float y, float w, float h, string spriteKey, string fit = "stretch", string id = null) => this;
    internal static PaperUI Create(string id, string background, float width = 0, float height = 0, string modId = null)
    {
        var panel = new PaperUI { Id = id, Background = background, Width = width, Height = height, ModId = modId };
        Created.Add(panel);
        return panel;
    }
    internal PaperUI Show() { Visible = true; Closed = false; Shows++; return this; }
    internal void Close() { Visible = false; Closed = true; }
}

internal static class HealthData
{
    internal static bool ReadingAllowed = true;
    internal static bool CanRead() => ReadingAllowed;
}

internal static partial class ModHelper
{
    private static readonly Dictionary<string, PaperUI> books = new Dictionary<string, PaperUI>();

    internal static PaperUI OpenPaper(string modId, string bookId)
    {
        string id = modId + ":" + bookId;
        if (!books.TryGetValue(id, out var panel))
        {
            panel = PaperUI.Create(id, "fixture_book_background", modId: modId);
            books.Add(id, panel);
        }
        return panel.Show();
    }

    internal static void ClosePaper(string modId, string bookId)
    {
        if (books.TryGetValue(modId + ":" + bookId, out var panel)) panel.Close();
    }
}
