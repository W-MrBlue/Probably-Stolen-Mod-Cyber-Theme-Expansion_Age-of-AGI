global using TagState = TestTagState;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

// Production mod sources are linked into this check project; only the game APIs are substituted.
public interface IMod
{
    void Init(ModManifest manifest);
    void OnEnable();
    void OnDisable();
}

public sealed class ModManifest
{
    public string ID = "age_of_agi";
    public string Name = "Age of AGI";
}

internal sealed class LoadedMod
{
    internal ModManifest Manifest;
    internal bool IsEnabled;
    internal Dictionary<string, Dictionary<string, string>> Localization;
    internal readonly Dictionary<string, TestSprite> Sprites = new Dictionary<string, TestSprite>();
    internal readonly Dictionary<string, UnityEngine.Sprite> PaperSprites = new Dictionary<string, UnityEngine.Sprite>();
    internal readonly Dictionary<string, UnityEngine.Texture2D> Textures = new Dictionary<string, UnityEngine.Texture2D>();
}

internal static class ModLoader
{
    internal static readonly List<LoadedMod> Mods = new List<LoadedMod>();
}

internal sealed class UISettings
{
    internal static UISettings current;
    internal int gridSize = 16;
}

internal sealed class TestSprite
{
    internal TestRect rect;
    internal GridShape Shape;
    private UnityEngine.Sprite renderSprite;
    public static implicit operator UnityEngine.Sprite(TestSprite source) => source == null ? null :
        source.renderSprite ??= new UnityEngine.Sprite { rect = new UnityEngine.Rect(0, 0, source.rect.width, source.rect.height), BackingTestSprite = source };
    public static implicit operator TestSprite(UnityEngine.Sprite source)
    {
        if (source == null) return null;
        if (source.BackingTestSprite != null) return source.BackingTestSprite;
        int width = (int)source.rect.width / 16, height = (int)source.rect.height / 16;
        var cells = new byte[width * height];
        var pixels = source.texture.GetPixels32();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                cells[y * width + x] = pixels[((height - 1 - y) * 16 + 8) * source.texture.width + x * 16 + 8].a > 0 ? (byte)1 : (byte)0;
        return new TestSprite { renderSprite = source, rect = new TestRect { width = source.rect.width, height = source.rect.height },
            Shape = new GridShapeBuilder().SetData(cells, width) };
    }
}

internal struct TestRect
{
    internal float width;
    internal float height;
}

internal sealed class ModLog
{
    internal static readonly List<string> Entries = new List<string>();
    internal ModLog(ModManifest manifest) { }
    internal void Log(string message) => Entries.Add("INFO: " + message);
    internal void Warning(string message) => Entries.Add("WARN: " + message);
    internal void Error(string message) => Entries.Add("ERROR: " + message);
}

internal static partial class ModHook
{
    internal static event Action<ModItemDirectory> OnModItemDirectoryInit;
    internal static event Action<RichTextBuilder, GameItem> OnCreateTooltipEarly;
    internal static event Action<RichTextBuilder, GameItem> OnCreateTooltipLate;

    internal static void RaiseItemDirectoryInit(ModItemDirectory directory)
    {
        OnModItemDirectoryInit?.Invoke(directory);
    }

    internal static void RaiseCreateTooltip(RichTextBuilder builder, GameItem item)
    {
        OnCreateTooltipLate?.Invoke(builder, item);
    }

    internal static void RaiseCreateTooltipEarly(RichTextBuilder builder, GameItem item)
    {
        OnCreateTooltipEarly?.Invoke(builder, item);
    }
}

internal static class RenderHandler
{
    internal static UnityEngine.Sprite LoadFromAtlas(string atlas, string key) => new UnityEngine.Sprite
        { rect = new UnityEngine.Rect(0, 0, 32, 32), pixelsPerUnit = 100,
            texture = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBA32, false) };
    internal enum ColorPalette
    {
        None,
        White,
        LightCyan,
        LightGreen,
        VeryDarkGreen,
        Yellow,
        DarkYellow,
        LightRed,
        LightBlue,
        LightOrange,
        LightTurquoise,
        Turquoise
    }
}

public sealed class RichTextBuilder
{
    internal readonly List<(string Text, RenderHandler.ColorPalette Color)> Lines = new List<(string, RenderHandler.ColorPalette)>();
    internal readonly List<(float Ratio, int Length, RenderHandler.ColorPalette Color, RenderHandler.ColorPalette Background)> ProgressBars =
        new List<(float, int, RenderHandler.ColorPalette, RenderHandler.ColorPalette)>();
    internal string Text = "";
    private string nextBegin = "";

    internal RichTextBuilder AddLine(string text = null, bool ignoreEmpty = true, RenderHandler.ColorPalette color = RenderHandler.ColorPalette.None,
        bool bold = false, bool italic = false, bool underline = false)
    {
        if (!ignoreEmpty || !string.IsNullOrEmpty(text))
        {
            Lines.Add((text, color));
            Text += nextBegin + text;
        }

        nextBegin = "\n";

        return this;
    }

    internal RichTextBuilder Add(object text, RenderHandler.ColorPalette color = RenderHandler.ColorPalette.None)
    {
        Text += nextBegin + text;
        nextBegin = "";
        return this;
    }

    internal RichTextBuilder AddEmptyLine(int heightPixels)
    {
        NextLine();
        return Add($"<size={heightPixels}px> </size>");
    }

    internal RichTextBuilder NextLine()
    {
        nextBegin = "";
        Text += "\n";
        return this;
    }

    internal RichTextBuilder AddProgressBar(float ratio, int length, RenderHandler.ColorPalette color = RenderHandler.ColorPalette.None,
        RenderHandler.ColorPalette backgroundColor = RenderHandler.ColorPalette.None, char[] style = null,
        string end = "", bool addFullLength = true, bool bold = false)
    {
        ProgressBars.Add((ratio, length, color, backgroundColor));
        return this;
    }
}

internal sealed class ModItemDirectory
{
    internal readonly Dictionary<string, Func<GameItem>> Factories = new Dictionary<string, Func<GameItem>>();
    internal Dictionary<string, Func<GameItem>> factoryDictionary => Factories;
    internal int AddCalls;
    internal string RejectAddFor;

    internal ModItemDirectory()
    {
        DirectoryMaster.Directories.Add(this);
    }

    internal bool Add(string itemId, Func<GameItem> factory)
    {
        AddCalls++;
        // Match the game's destructive Add behavior: remove a global match first.
        DirectoryMaster.Remove<GameItem>(itemId);
        return itemId != RejectAddFor && Factories.TryAdd(itemId, factory);
    }

    internal bool Remove(string itemId)
    {
        return Factories.Remove(itemId);
    }
}

internal static class DirectoryMaster
{
    internal static GameItem Item(string id, bool isOwned = true)
    {
        return GetFactory<GameItem>(id)?.Invoke() ?? throw new InvalidOperationException("Missing item factory: " + id);
    }

    // Native DirectoryMaster also has this overload; name-only Harmony lookup is ambiguous.
    internal static GameItem Item<K>(K identifier) where K : Enum => Item(identifier.ToString());

    internal static bool Has<T>(string id)
    {
        return GetFactory<T>(id) != null;
    }
    internal static readonly List<ModItemDirectory> Directories = new List<ModItemDirectory>();

    internal static Func<T> GetFactory<T>(string itemId)
    {
        foreach (ModItemDirectory directory in Directories)
        {
            if (directory.Factories.TryGetValue(itemId, out var factory))
            {
                return (Func<T>)(object)factory;
            }
        }

        return null;
    }

    internal static bool Remove<T>(string itemId)
    {
        foreach (ModItemDirectory directory in Directories)
        {
            if (directory.Remove(itemId))
            {
                return true;
            }
        }

        return false;
    }
}

public class GameItem : GraphNodeStorage
{
    internal void EnableManufacturer(string manufacturer) => ModifyTag("manufacturer", state => state.Enable().SetString(manufacturer));
    internal void DisplayManufacturer(RichTextBuilder builder, string manufacturer)
    {
        if (AgeOfAgi.Items.Logic.Manufacturers.ManufacturerTooltipPatch.Prefix(builder, manufacturer))
            builder.AddLine("native-manufacturer:" + manufacturer);
    }
    internal readonly List<ItemFeature> itemFeatures = new List<ItemFeature>();
    internal ItemFeature FindItemFeatureByID(string id) => itemFeatures.Find(feature => feature.identifier == id);
    internal void AddItemFeature(ItemFeature feature)
    {
        if (FindItemFeatureByID(feature.identifier) == null) itemFeatures.Add(feature);
    }
    private static int nextUniqueId;
    internal int uniqueId;
    internal int GetUniqueID() => uniqueId == 0 ? uniqueId = ++nextUniqueId : uniqueId;
    internal Func<GameItem, GameItem, bool> mayThisTargetItemFunc;
    internal Func<GameItem, GameItem, bool> canThisTargetItemFunc;
    internal Action<GameItem, GameItem> onThisTargetItemFunc;
    internal Func<GameItem, GameItem, bool> mayItemTargetThisFunc;
    internal Func<GameItem, GameItem, bool> canItemTargetThisFunc;
    internal Action<GameItem, GameItem> onItemTargetThisFunc;
    internal Func<GameItem, GameInventory, SlotMarker, bool> maySelectSlotItemFunc, canSelectSlotItemFunc;
    internal Func<GameItem, GameInventory, SlotMarker, bool> mayActivateSlotItemFunc, canActivateSlotItemFunc;
    internal Action<GameItem, GameInventory, SlotMarker> onActivateSlotItemFunc;
    internal string soundUse;
    internal GridShape shape = new GridShapeBuilder().SetData(new byte[] { 1 }, 1);
    internal GridShape modifiedShape => shape;
    internal GameInventory parentInventory;
    internal PixelWindow contentWindow;
    internal Action<GameItem> onLoaded;
    internal Action<GameItem, GameInventory, SlotMarker> onCycleEndSlotItemFunc = null;
    internal Action<GameItem, GameInventory, SlotMarker> onCycleEndEarlySlotItemFunc = null;
    internal Func<GameItem, GameInventory, bool> mayInventoryRemoveItemFunc;
    internal bool Destroyed;
    internal bool Owned = true;
    internal int unitCount = 1;
    internal TestTagSystem modifiedState => new TestTagSystem { dict = tags };

    internal GameItem SetContentWindow(PixelWindow window)
    {
        contentWindow = window;
        foreach (GameInventory inventory in window.childElement.children.OfType<GameInventory>()) inventory.ParentItem = this;
        return this;
    }

    internal GameItem SetShape(GridShape nextShape)
    {
        shape = nextShape.Clone();
        return this;
    }

    internal string GetDisplayName() => name;

    internal GameItem SetAmount(int count)
    {
        unitCount = count;
        return this;
    }

    internal bool MayStack(GameItem otherItem) => otherItem != null && otherItem.identifier == identifier;

    internal void Destroy()
    {
        Destroyed = true;
        parentInventory?.Remove(this);
    }
    private readonly List<string> itemTypes = new List<string>();

    internal void SetGameItemType(string type)
    {
        EnableTag(type);
        if (!itemTypes.Contains(type))
        {
            itemTypes.Add(type);
        }
    }

    internal bool isCombatBackpack = false;
    internal void SetType(string type) => EnableTag(type);
    internal bool IsOneOfTags(List<string> values) => values.Any(IsTag);
    internal bool IsGameItemType(string type) => itemTypes.Contains(type);

    internal void RemoveGameItemType(string type)
    {
        DisableTag(type);
        itemTypes.Remove(type);
    }

    internal List<string> GetGameItemType()
    {
        return itemTypes;
    }

    internal string soundDragStart;
    internal string soundDragEnd;
    internal string soundContainerOpen;
    internal string soundContainerClose;
    internal string soundActivate;
    internal string Id;
    internal string SpriteKey;
    internal string spritePath => SpriteKey;
    internal string name;
    internal string shortDescription;
    internal string flavorText;
    internal string SourceId;
    internal string SourceName;
    internal string identifier => Id;
    internal long unitValue;
    internal long lateUnitValue;
    internal int SetValueCalls;
    internal long? CurrentValueOverride;
    internal long? BaseValueOverride;
    private readonly Dictionary<string, TestTagState> tags = new Dictionary<string, TestTagState>();

    internal GameItem SetValue(long value)
    {
        SetValueCalls++;
        unitValue = value;
        return this;
    }

    internal long GetCurrentValue(bool useRetailMarkup = false, bool withChild = true,
        bool includeEvents = true, bool forceMarkup = false, long extraMarketMod = 0L)
    {
        if (useRetailMarkup || !withChild || !includeEvents || forceMarkup || extraMarketMod != 0)
        {
            throw new Exception("Tooltip must use the same current-value query as the native price block.");
        }

        double finalFactor = 1;
        foreach (var feature in itemFeatures)
            if (feature.valueStage == ItemFeature.ValueStage.Final)
                finalFactor *= Math.Max(0, 1 + feature.GetCurrentValueMod() / 100d);
        return CurrentValueOverride ?? (long)Math.Round((unitValue + GetComplexeItemValue() + lateUnitValue + GetChildValue()) * finalFactor);
    }

    internal long GetBaseValue() => BaseValueOverride ?? (unitValue + GetComplexeItemValue() + lateUnitValue + GetChildValue());
    private long GetChildValue() => IsTag("IGNORE_CHILD_VALUE") ? 0 :
        contentWindow?.childElement.children.OfType<GameInventory>().SelectMany(inventory => inventory.childItems)
            .Where(item => !item.IsTag("ITEM_BEHIND_LOCK_TAG")).Sum(item => item.GetCurrentValue()) ?? 0;
    internal long GetComplexeItemValue()
    {
        long result = 0;
        AgeOfAgi.Items.Logic.Gpu.GpuPricePatch.Postfix(this, ref result);
        return result;
    }

    internal bool EnableTag(string key)
    {
        ModifyTag(key, state => state.Enable());
        return true;
    }

    internal void DisableTag(string key)
    {
        ModifyTag(key, state => state.Enabled = false);
    }

    internal virtual void Validate() { }

    internal bool IsTag(string key)
    {
        return tags.TryGetValue(key, out var state) && state.Enabled;
    }

    internal void ModifyTag(string key, Action<TestTagState> action)
    {
        if (!tags.TryGetValue(key, out var state))
        {
            state = new TestTagState();
            tags.Add(key, state);
        }

        action(state);
    }

    internal TestTagState GetTagReadonly(string key)
    {
        return tags.TryGetValue(key, out var state)
            ? new TestTagState { Enabled = state.Enabled, Value = state.Value, FloatValue = state.FloatValue,
                LongValue = state.LongValue, DoubleValue = state.DoubleValue, StringValue = state.StringValue }
            : new TestTagState();
    }

    internal void SetSprite(string modId, string spriteKey)
    {
        if (!ModHelper.AssetsReady || !ModLoader.Mods.Find(candidate => candidate.Manifest.ID == modId).Sprites.ContainsKey(spriteKey))
            throw new InvalidOperationException("Sprite is not loaded: " + spriteKey);
        SpriteKey = spriteKey;
    }

    internal void SetSpriteAndShapeFromMod(string modId, string spriteKey)
    {
        if (!ModHelper.AssetsReady)
        {
            throw new InvalidOperationException("Sprites are not loaded yet.");
        }

        SpriteKey = spriteKey;
        LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == modId);
        SetShape(mod.Sprites[spriteKey].Shape);
    }
}

internal sealed class TestTagState
{
    internal bool Enabled;
    internal int Value;
    internal float FloatValue;
    internal long LongValue;
    internal double DoubleValue;
    internal string StringValue;

    internal TestTagState SetString(string value)
    {
        StringValue = value;
        return this;
    }

    internal string GetString() => StringValue;
    internal TestTagState SetLong(long value) { LongValue = value; return this; }
    internal long GetLong() => LongValue;
    internal TestTagState SetDouble(double value) { DoubleValue = value; return this; }
    internal double GetDouble() => DoubleValue;

    internal TestTagState SetFloat(float value)
    {
        FloatValue = value;
        return this;
    }

    internal float GetFloat()
    {
        return FloatValue;
    }

    internal TestTagState Enable()
    {
        Enabled = true;
        return this;
    }

    internal TestTagState SetInt(int value)
    {
        Value = value;
        return this;
    }

    internal int GetInt()
    {
        return Value;
    }
}

public sealed class GameItemElement : GameItem
{
    internal bool IsDestroyed() => Destroyed || handler == null;
    internal TreeNodeRender handler = new TreeNodeRender();
    internal TestItemBackground background = new TestItemBackground();
    internal TestItemBackground fakeBackground = new TestItemBackground();
    private RichTextBuilder GetTooltipBasic() => throw new NotSupportedException("Native UI is substituted in checks.");
}

internal sealed class TestItemBackground
{
    internal TestItemRect rectTransform = new TestItemRect();
}
internal sealed class TestItemRect
{
    internal UnityEngine.Vector2 sizeDelta;
}
namespace UnityEngine
{
    internal readonly struct Vector2
    {
        internal static Vector2 zero => default;
        internal readonly float x, y;
        internal Vector2(float x, float y) { this.x = x; this.y = y; }
    }
}

internal static class LocHelper
{
    internal static string GetCurrentLocaleCode() => ModHelper.Locale;

    internal static string GetLocalizedDialogue(string key, params object[] arguments)
    {
        if (!new[] { "dialog_scav_greet_1", "dialog_petty_thief_1", "dialog_thief_1" }.Contains(key))
            throw new Exception("Unexpected native seller greeting key: " + key);
        return "native-dialogue:" + key;
    }

    internal static string GetLocalizedMechanic(string key, params object[] arguments)
    {
        if (key == "mech_machinery_insufficient_power")
            return ModHelper.Locale == "zh" ? "能量不足" : "Not enough energy";
        if (key != "mech_tooltip_est_value")
        {
            throw new Exception("Unexpected native mechanic localization key: " + key);
        }

        return ModHelper.Locale == "zh"
            ? $"预估价值：{arguments[0]}，基础价值：{arguments[1]}"
            : $"Estimated value: {arguments[0]}, base value: {arguments[1]}";
    }
}

internal static class TypeHelper
{
    internal static readonly List<string> MAJOR_TYPES_LIST = new List<string> { "MATERIAL", "MODULE", "TOOL" };
    internal static readonly List<string> ALL_TYPES_LIST = new List<string> { "MACHINE", "MODULE", "TOOL", "CONTRABAND", "MATERIAL", "STORAGE", "DOCUMENT" };

    internal static string GetTypeDisplayName(string typeConst)
    {
        return "native:" + typeConst;
    }
}

internal static class AudioHelper
{
    internal static void SetToDefault(GameItem item)
    {
        item.soundDragStart = "move_item_default";
        item.soundDragEnd = "move_item_default";
    }

    internal static void InitBattery(GameItem item)
    {
        item.soundDragStart = "battery_drag";
        item.soundDragEnd = "battery_drop";
    }

    internal static void InitModule(GameItem item)
    {
        item.soundDragStart = "module_drag";
        item.soundDragEnd = "module_drop";
    }

    internal static void InitSmallModule(GameItem item)
    {
        item.soundDragStart = "small_module_drag";
        item.soundDragEnd = "small_module_drop";
    }

    internal static void InitSmallMachine(GameItem item)
    {
        item.soundDragStart = "small_machine_drag";
        item.soundDragEnd = "small_machine_drop";
        item.soundContainerOpen = "open_machine";
        item.soundContainerClose = "close_machine";
    }

    internal static void InitMetalMachine(GameItem item)
    {
        item.soundDragStart = "metal_machine_drag";
        item.soundDragEnd = "metal_machine_drop";
        item.soundContainerOpen = "open_machine";
        item.soundContainerClose = "close_machine";
    }

    internal static void InitTinyItem(GameItem item)
    {
        item.soundDragStart = "tiny_item_drag";
        item.soundDragEnd = "tiny_item_drop";
    }

    internal static void InitTinyMetal(GameItem item)
    {
        item.soundDragStart = "tiny_metal_drag";
        item.soundDragEnd = "tiny_metal_drop";
    }

    internal static void InitPaperSheet(GameItem item)
    {
        item.soundDragStart = "paper_sheet_drag";
        item.soundDragEnd = "paper_sheet_drop";
    }

    internal static void InitPlasticCard(GameItem item)
    {
        item.soundDragStart = "plastic_card_drag";
        item.soundDragEnd = "plastic_card_drop";
    }

    internal static void InitPlasticContainer(GameItem item)
    {
        item.soundDragStart = "plastic_container_drag";
        item.soundDragEnd = "plastic_container_drop";
    }

    internal static void InitSolidMetal(GameItem item)
    {
        item.soundDragStart = "solid_metal_drag";
        item.soundDragEnd = "solid_metal_drop";
    }

    internal static void InitGlass(GameItem item)
    {
        item.soundDragStart = "glass_drag";
        item.soundDragEnd = "glass_drop";
    }

    internal static void InitBook(GameItem item)
    {
        item.soundDragStart = "book_drag";
        item.soundDragEnd = "book_drop";
    }

    internal static void InitCassette(GameItem item)
    {
        item.soundDragStart = "cassette_drag";
        item.soundDragEnd = "cassette_drop";
    }

    internal static void InitScanner(GameItem item)
    {
        item.soundDragStart = "scanner_drag";
        item.soundDragEnd = "scanner_drop";
        item.soundActivate = "scanner_activate";
    }

    internal static void InitScrapMetal(GameItem item)
    {
        item.soundDragStart = "scrap_metal_drag";
        item.soundDragEnd = "scrap_metal_drop";
    }

    internal static void InitCartonBox(GameItem item)
    {
        item.soundDragStart = "carton_box_drag";
        item.soundDragEnd = "carton_box_drop";
    }

    internal static void InitCartonCard(GameItem item)
    {
        item.soundDragStart = "carton_card_drag";
        item.soundDragEnd = "carton_card_drop";
    }

    internal static void InitDossier(GameItem item)
    {
        item.soundContainerOpen = "open_dossier";
        item.soundContainerClose = "close_dossier";
    }
}

internal static class ItemDirectory
{
    internal static int CreatedItems;
    internal static readonly List<GameItem> CreatedInstances = new List<GameItem>();

    internal static GameItem CreateEmptyItem(string itemId)
    {
        CreatedItems++;
        var item = new GameItem { Id = itemId };
        CreatedInstances.Add(item);
        return item;
    }
}

internal static partial class ModHelper
{
    internal const string DEBUG_CATEGORY_SPAWN = "Spawn";
    internal static string Root;
    internal static bool IsRegistered = true;
    internal static bool AssetsReady;
    internal static int FileReads;
    internal static int LocalizationReads;
    internal static string LocalizationPrefix = "";
    internal static string Locale = "en";
    internal static readonly Dictionary<string, Dictionary<string, string>> Translations = new Dictionary<string, Dictionary<string, string>>();
    internal static readonly Dictionary<string, string> FileOverrides = new Dictionary<string, string>();
    internal static readonly Dictionary<string, Action> DebugActions = new Dictionary<string, Action>();
    internal static readonly List<GameItem> Inventory = new List<GameItem>();
    private static readonly Dictionary<string, string> debugOwners = new Dictionary<string, string>();

    internal static string ReadModFileText(string modId, string path)
    {
        RequireRegistered(modId);
        FileReads++;
        if (FileOverrides.TryGetValue(path, out var replacement))
        {
            return replacement;
        }

        string fullPath = Path.Combine(Root, path);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

    internal static bool ModFileExists(string modId, string path)
    {
        RequireRegistered(modId);
        return File.Exists(Path.Combine(Root, path));
    }

    internal static string GetLocalized(string modId, string key, params object[] args)
    {
        RequireRegistered(modId);
        if (!AssetsReady)
        {
            throw new InvalidOperationException("Localization is not loaded yet.");
        }

        LocalizationReads++;
        string text;
        if (!(Translations.TryGetValue(Locale, out var entries) && entries.TryGetValue(key, out text)) &&
            !(Translations.TryGetValue("en", out var english) && english.TryGetValue(key, out text)))
        {
            text = LocalizationPrefix + key;
        }

        return args != null && args.Length > 0 ? string.Format(text, args) : text;
    }

    internal static void SetSource(GameItem item, string modId, string modName)
    {
        item.SourceId = modId;
        item.SourceName = modName;
        item.ModifyTag("MOD_ID_TAG", state => state.Enable().SetString(modId));
        item.ModifyTag("MOD_NAME_TAG", state => state.Enable().SetString(modName));
    }

    internal static void SetClientSource(StoreClient client, string modId) => client.sourceModId = modId;

    internal static void AddDebugFunction(string modId, string category, string name, Action action)
    {
        RequireRegistered(modId);
        DebugActions.Add(name, action);
        debugOwners.Add(name, modId);
    }

    internal static void RemoveDebugFunctions(string modId)
    {
        foreach (string name in new List<string>(debugOwners.Keys))
        {
            if (debugOwners[name] == modId)
            {
                DebugActions.Remove(name);
                debugOwners.Remove(name);
            }
        }
    }

    internal static GameItem SpawnItem(string itemId)
    {
        return DirectoryMaster.GetFactory<GameItem>(itemId)?.Invoke() ?? new GameItem { Id = itemId };
    }

    internal static void MoveItemToInventory(GameItem item)
    {
        Inventory.Add(item);
    }

    private static void RequireRegistered(string modId)
    {
        if (!IsRegistered)
        {
            throw new ArgumentException("Unknown mod '" + modId + "'");
        }
    }
}
