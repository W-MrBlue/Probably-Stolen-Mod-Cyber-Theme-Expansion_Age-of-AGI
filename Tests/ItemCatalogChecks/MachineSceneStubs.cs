using System;
using System.Collections.Generic;
using System.Linq;

// Scene substitutes only. Recipe consumption/filtering uses the linked native sources.
public interface GraphNodeStorage { }
internal interface PixelElement { }

internal sealed class SlotMarker
{
    internal GameItem item;
    internal GameGridInventory inventory;
    internal GridShape itemGridShape;
    internal int numTransfer;
    internal bool IsValid() => numTransfer > 0;
    internal int TryAcceptOnce(int amount = -1)
    {
        if (!IsValid() || inventory.RejectTransfer) return 0;
        inventory.AcceptAt(item, itemGridShape);
        return numTransfer;
    }
}

internal sealed class TestTagSystem
{
    internal Dictionary<string, TestTagState> dict;
}

internal static class Debug
{
    internal static void LogWarning(string message) { }
}

public abstract class GameInventory : GraphNodeStorage, PixelElement
{
    internal GameItem ParentItem;
    internal GameItem GetParentItem() => ParentItem;
    internal void EnableTag(string tag) { }
    internal readonly List<GameItem> Items = new List<GameItem>();
    internal Func<GameItem, GameInventory, bool> mayInventoryAddItemFunc;
    internal Func<GameItem, GameInventory, SlotMarker, int> slotPriorityFunc;
    internal Func<GameItem, GameInventory, bool> mayInventoryRemoveItemFunc = null;
    internal bool overrideLockInsert;
    internal bool overrideLockRemove;
    internal void LockInv() { overrideLockInsert = true; overrideLockRemove = true; }
    internal void UnlockInv() { overrideLockInsert = false; overrideLockRemove = false; }
    internal bool IsInsertLocked() => overrideLockInsert;
    internal bool IsRemoveLocked() => overrideLockRemove;
    internal Action<GameItem, GameInventory, SlotMarker> onSlotAddItemEndFunc;
    internal Action<GameItem, GameInventory, SlotMarker> onSlotRemoveItemEndFunc;
    internal Action<GameItem> AfterAccept;
    internal List<GameItem> childItems => Items.ToList();
    internal virtual bool CanAccept(GameItem item) => !overrideLockInsert && item != null && !item.Destroyed &&
        (mayInventoryAddItemFunc?.Invoke(item, this) ?? true);
    internal virtual void Accept(GameItem item)
    {
        item.parentInventory?.Remove(item);
        Items.Add(item);
        item.parentInventory = this;
        AfterAccept?.Invoke(item);
        onSlotAddItemEndFunc?.Invoke(item, this, null);
    }
    // Native save restore: place without consulting filters, shape search or events.
    internal bool UncheckedAccept(GameItem item)
    {
        item.parentInventory?.RawRemove(item);
        Items.Add(item);
        item.parentInventory = this;
        return true;
    }
    internal void RawRemove(GameItem item)
    {
        Items.Remove(item);
        item.parentInventory = null;
    }
    internal void Remove(GameItem item)
    {
        bool removed = Items.Remove(item);
        item.parentInventory = null;
        if (removed)
        {
            onSlotRemoveItemEndFunc?.Invoke(item, this, null);
        }
    }
    internal virtual void Validate() { }
}

internal sealed class GameSlotInventory : GameInventory
{
    internal void SetBackgroundFadeSprite(string atlas, string sprite) { }
    internal readonly int Width;
    internal readonly int Height;
    internal GameItem childItem => Items.FirstOrDefault();
    internal GameSlotInventory(int width = 1, int height = 1) { Width = width; Height = height; }
    internal override bool CanAccept(GameItem item) => Items.Count == 0 && base.CanAccept(item);
}

internal sealed class GameGridInventory : GameInventory
{
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal bool RejectTransfer;
    internal GridShape inventoryShape => new GridShapeBuilder().SetData(new byte[Width * Height], Width);
    internal SlotMarker TryInventorySlot(GameItem item, GridShape shape)
    {
        bool fits = base.CanAccept(item) && shape.minX >= 0 && shape.minY >= 0 && shape.minX + shape.globalWidth <= Width && shape.minY + shape.globalHeight <= Height &&
            !Items.Any(existing => Overlaps(shape, existing.modifiedShape));
        return new SlotMarker { item = item, inventory = this, itemGridShape = shape, numTransfer = fits ? item.unitCount : 0 };
    }
    internal void AcceptAt(GameItem item, GridShape shape) { item.SetShape(shape); base.Accept(item); }
    internal GameGridInventory(int width, int height) { Width = width; Height = height; }
    internal GameGridInventory SetShape(int width, int height)
    {
        Width = width;
        Height = height;
        Validate();
        return this;
    }
    internal GridShape FindSpace(GameItem item)
    {
        for (int y = 0; y <= Height - item.modifiedShape.globalHeight; y++)
        {
            for (int x = 0; x <= Width - item.modifiedShape.globalWidth; x++)
            {
                GridShape candidate = new GridShapeBuilder(item.modifiedShape).SetPosition(x, y);
                bool overlap = Items.Any(existing => Overlaps(candidate, existing.modifiedShape));
                if (!overlap) return candidate;
            }
        }
        return null;
    }
    private bool Overlaps(GridShape a, GridShape b)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (a.Get(x, y) != 0 && b.Get(x, y) != 0) return true;
        return false;
    }
    internal override bool CanAccept(GameItem item) => base.CanAccept(item) && FindSpace(item) != null;
    internal override void Accept(GameItem item)
    {
        item.SetShape(FindSpace(item));
        base.Accept(item);
    }
}

internal sealed class PixelWindow : GraphNodeStorage
{
    internal static readonly List<PixelWindow> CreatedWindows = new List<PixelWindow>();
    internal MachineryPanelRow childElement;
    internal Action stateChangeActionFunc;
    internal Action closeCallback;
    internal int ValidateCount;
    internal bool Visible;
    internal UnityEngine.Vector2 ShowPosition;
    internal readonly TreeNodeRender handler = new TreeNodeRender();
    internal PixelWindow() { CreatedWindows.Add(this); GraphHandler.windowGraphNodeRoots.Add(this); }
    internal PixelWindow Show(UnityEngine.Vector2 position) { ShowPosition = position; Visible = true; return this; }
    internal PixelWindow Hide() { Visible = false; return this; }
    internal bool IsVisible() => Visible;
    internal PixelWindow AddInventoryStateChangeAction(Action action) { stateChangeActionFunc += action; return this; }
    internal void Validate() { ValidateCount++; }
}

internal sealed class MachineryPanel : PixelElement
{
    internal readonly TreeNodeRender handler = new TreeNodeRender();
    internal readonly string role;
    internal readonly PixelElement content;
    internal readonly string Label;
    internal bool isHidden;
    internal MachineryPanel(string label, PixelElement content, string role)
    {
        Label = label; this.content = content; this.role = role;
        var plate = new TreeNodeRender(); plate.image.enabled = true;
        handler.Children.Add(plate);
    }
    internal MachineryPanel SetHidden(bool hidden)
    {
        isHidden = hidden;
        return this;
    }
}

internal sealed class MachineryPanelRow
{
    internal readonly MachineryPanel[] Panels;
    internal int[] DisplayOrder;
    internal MachineryPanel[] DisplayPanels => DisplayOrder == null ? Panels : DisplayOrder.Select(index => Panels[index]).ToArray();
    internal MachineryPanelRow SetDisplayOrder(int[] order) { DisplayOrder = order; return this; }
    internal MachineryPanelRow(params MachineryPanel[] panels) { Panels = panels; }
    internal MachineryPanel GetPanel(string role) => Panels.FirstOrDefault(panel => panel.role == role);
    private static IEnumerable<MachineryPanel> FindPanels(PixelElement element)
    {
        if (element is MachineryPanel panel)
        {
            yield return panel;
            foreach (var child in FindPanels(panel.content)) yield return child;
        }
        else if (element is MachineryStack stack)
            foreach (var child in stack.Elements.SelectMany(FindPanels)) yield return child;
    }
    internal GraphNodeStorage GetStorage(int index) => Panels.SelectMany(FindStorage).ElementAtOrDefault(index);
    internal List<GraphNodeStorage> children => Panels.SelectMany(FindStorage).ToList();
    private static IEnumerable<GraphNodeStorage> FindStorage(PixelElement element)
    {
        if (element is GraphNodeStorage storage) yield return storage;
        else if (element is MachineryPanel panel)
            foreach (var child in FindStorage(panel.content)) yield return child;
        else if (element is MachineryStack stack)
            foreach (var child in stack.Elements.SelectMany(FindStorage)) yield return child;
    }
}

internal sealed class MachineryButton : PixelElement
{
    internal string Text;
    internal string text => Text;
    internal int widthPixels = 80;
    internal readonly UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
    internal UnityEngine.Sprite Icon;
    internal string Description;
    internal bool Enabled = true;
    private Action callback;
    internal MachineryButton(string text = null) { Text = text; }
    internal MachineryButton SetText(string text) { Text = text; return this; }
    internal MachineryButton SetIcon(UnityEngine.Sprite icon) { Icon = icon; return this; }
    internal MachineryButton SetSize(int width, int height) { widthPixels = width; rectTransform.sizeDelta = new UnityEngine.Vector2(width, height); return this; }
    internal MachineryButton SetDescription(string description) { Description = description; return this; }
    internal void SetCallback(Action next) { callback = next; }
    internal void Enable() { Enabled = true; }
    internal void Disable() { Enabled = false; }
    internal void Click() { if (Enabled) callback?.Invoke(); }
}

internal static class GraphHandler
{
    internal static readonly List<GraphNodeStorage> windowGraphNodeRoots = new List<GraphNodeStorage>();
}

internal sealed class CustomUIManager
{
    internal enum Style { Machinery }
    private readonly Dictionary<string, CustomUIWindow> windows = new Dictionary<string, CustomUIWindow>();
    internal CustomUIBuilder CreateWindow(string id, string title, string layerId, object windowPrefabOverride = null, Style style = Style.Machinery)
    {
        CloseWindow(id);
        var window = new CustomUIWindow();
        windows[id] = window;
        return new CustomUIBuilder(window);
    }
    internal static readonly CustomUIManager Instance = new CustomUIManager();
    internal void CloseWindow(string id)
    {
        if (windows.TryGetValue(id, out var window)) { windows.Remove(id); window.Close(); }
    }
    internal UnityEngine.Sprite GetModSprite(string modId, string key)
    {
        if (!ModLoader.Mods.Single(mod => mod.Manifest.ID == modId).Sprites.TryGetValue(key, out var sprite)) return null;
        return new UnityEngine.Sprite { AssetKey = key, rect = new UnityEngine.Rect(0, 0, sprite.rect.width, sprite.rect.height) };
    }
}

internal sealed class MachineryStack : PixelElement
{
    internal readonly bool vertical;
    internal readonly MachineryButton[] Buttons;
    internal readonly PixelElement[] Elements;
    private MachineryStack(PixelElement[] elements, bool vertical) { this.vertical = vertical; Elements = elements; Buttons = elements.OfType<MachineryButton>().ToArray(); }
    internal static MachineryStack Vertical(params PixelElement[] elements) => new MachineryStack(elements, true);
    internal static MachineryStack Horizontal(params PixelElement[] elements) => new MachineryStack(elements, false);
}

internal sealed class MachineryProgressBar : PixelElement
{
    internal float value;
    internal MachineryProgressBar(float value = 0, int width = -1) { this.value = value; }
    internal void SetValue(float next) { value = next; }
}

internal sealed class MachineryText : PixelElement
{
    internal readonly TreeNodeRender handler = new TreeNodeRender();
    internal string Text;
    internal MachineryText(string text = null, bool uppercase = true) { Text = text; }
    internal void SetText(string text) { Text = text; }
}

internal static class MachineHelper
{
    internal static PixelWindow CreateMachineryWindow(bool canMove, int[] displayOrder, params MachineryPanel[] panels) =>
        new PixelWindow { childElement = new MachineryPanelRow(panels).SetDisplayOrder(displayOrder) };
    internal static PixelWindow CreateMachineryWindow(bool canMove, params MachineryPanel[] panels) =>
        new PixelWindow { childElement = new MachineryPanelRow(panels) };
    internal static MachineryPanel Panel(string label, PixelElement content, string role) => new MachineryPanel(label, content, role);
    internal static MachineryPanel PowerPanel(GameSlotInventory battery) => new MachineryPanel("Power", battery, "power");
    internal static void SetupBatterySlot(GameSlotInventory battery, GameItem machine)
    {
        machine.EnableTag("MACHINE_RECHARGABLE_TAG");
        battery.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && item.IsTag("power_source_item");
    }

    internal static bool TryDrawCyclePower(GameItem machine, GameSlotInventory battery)
    {
        if (battery?.childItem == null) return false;
        int amount = machine.GetTagReadonly("machinery_drawn_power").GetInt();
        if (!PowerHelper.CanDrawPowerSource(battery.childItem, amount)) return false;
        return PowerHelper.TryRemoveEnergy(battery.childItem, amount) == amount;
    }
}

internal static class MachineryHelper
{
    internal static void InitMachinery(GameItem item, int drawnPower = 0, bool isTogglable = false)
    {
        item.SetGameItemType("MACHINE");
        item.EnableTag("machinery");
        item.EnableTag("IMPORTANT_TAG");
        item.ModifyTag("machinery_drawn_power", tag => tag.Enable().SetInt(drawnPower));
        item.ModifyTag("MACHINERY_DRAWN_POWER_BASE", tag => tag.Enable().SetInt(drawnPower));
        item.ModifyTag("is_machinery_powered", tag => tag.Enable().SetInt(1));
        item.ModifyTag("machinery_received_power", tag => tag.Enable().SetInt(0));
    }
}

internal static class ContrabandHelper
{
    internal static bool IsContraband(GameItem item) => item.IsTag("CONTRABAND_ITEM_TAG");
    internal static void InitContrabandItem(GameItem item, int level)
    {
        item.EnableTag("CONTRABAND_ITEM_TAG");
        item.SetGameItemType("CONTRABAND");
        string nativeLevel = level switch
        {
            1 => "CONTRABAND_LEVEL_LOW",
            2 => "CONTRABAND_LEVEL_MID",
            3 => "CONTRABAND_LEVEL_HIGH",
            4 => "CONTRABAND_LEVEL_CRITICAL",
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
        item.ModifyTag("CONTRABAND_LEVEL", tag => tag.Enable().SetString(nativeLevel).SetInt(level));
    }

    internal static void RemoveContrabandStatus(GameItem item)
    {
        item.ModifyTag("CONTRABAND_LEVEL", tag => tag.Enable().SetString("CONTRABAND_LEVEL_NONE"));
        item.DisableTag("CONTRABAND_ITEM_TAG");
        item.DisableTag("CONTRABAND_LEVEL");
        item.RemoveGameItemType("CONTRABAND");
    }
}

internal static class GeneralHelper
{
    // Native GeneralHelper.IsItemOwned dereferences item directly; null must throw.
    // Do not add a null guard here: it would hide unsafe mod callers during loading.
    internal static bool IsItemOwned(GameItem item) => item.Owned && !item.Destroyed;
    internal static void SetItemOwned(GameItem item, bool owned = true) => item.Owned = owned;
    internal static bool CanAddItemToInv(GameItem item, GameItem container) => container == null || IsItemOwned(item) == IsItemOwned(container);
    // Native LOCKED_TAG removal block, restored on load by the game's save decode.
    internal static void LockItem(GameItem item)
    {
        item.EnableTag("LOCKED_TAG");
        item.mayInventoryRemoveItemFunc = (_, _) => false;
    }
    internal static void UnlockItem(GameItem item)
    {
        item.DisableTag("LOCKED_TAG");
        item.mayInventoryRemoveItemFunc = (_, _) => true;
    }
    internal static bool IsItemLocked(GameItem item) => item != null && item.IsTag("LOCKED_TAG");
}

internal static class PowerHelper
{
    internal static int SpriteRefreshes;
    internal static bool IsAvailableEnergyFromItem(GameItem item) => item != null && item.IsTag("power_source_item");
    internal static int GetAvailableEnergyFromItem(GameItem item, bool useBandwidth = true) =>
        IsAvailableEnergyFromItem(item) ? item.GetTagReadonly("power_source_item_energy").GetInt() : 0;
    internal static bool CanDrawPowerSource(GameItem item, int amount) => IsAvailableEnergyFromItem(item) && GetAvailableEnergyFromItem(item) >= amount;
    internal static bool DrawPowerSource(GameItem item, int amount)
    {
        if (!CanDrawPowerSource(item, amount)) return false;
        item.ModifyTag("power_source_item_energy", state => state.SetInt(GetAvailableEnergyFromItem(item) - amount));
        UpdateEnergyCreditSprite(item);
        return true;
    }
    internal static int TryRemoveEnergy(GameItem item, int amount, bool useBandwidth = true)
    {
        int drawn = Math.Min(amount, GetAvailableEnergyFromItem(item));
        item.ModifyTag("power_source_item_energy", state => state.SetInt(GetAvailableEnergyFromItem(item) - drawn));
        return drawn;
    }
    internal static void UpdateEnergyCreditSprite(GameItem item) { SpriteRefreshes++; }
    internal static void SetPowerSourceAt(GameItem item, int energy)
    {
        item.ModifyTag("power_source_item_energy", state => state.Enable().SetInt(energy));
        UpdateEnergyCreditSprite(item);
    }
}

internal static class GraphUtils
{
    // Scene substitute for MachineryPanelRow's native highest-priority search;
    // equal priorities choose the later storage node, independent of display order.
    private static GameInventory FindInventory(GraphNodeStorage node, GameItem item)
    {
        if (node is GameItem machine) return FindInventory(machine.contentWindow, item);
        if (node is GameInventory inventory) return inventory.CanAccept(item) ? inventory : null;
        if (!(node is PixelWindow window)) return null;
        GameInventory best = null;
        int bestPriority = int.MinValue;
        foreach (GameInventory candidate in window.childElement.children.OfType<GameInventory>())
        {
            if (!candidate.CanAccept(item)) continue;
            int priority = candidate.slotPriorityFunc?.Invoke(item, candidate, null) ?? 0;
            if (priority < bestPriority) continue;
            best = candidate;
            bestPriority = priority;
        }
        return best;
    }
    internal static bool CanAccept(GraphNodeStorage node, GameItem item) => FindInventory(node, item) != null;
    // Native TryAcceptAll returns the UNTRANSFERRED count: zero means complete success.
    internal static int TryAcceptAll(GraphNodeStorage node, GameItem item)
    {
        GameInventory inventory = FindInventory(node, item);
        if (inventory == null ||
            inventory is GameGridInventory grid && grid.RejectTransfer) return item.unitCount;
        inventory.Accept(item);
        return 0;
    }
    internal static List<T> FindAllChildrenType<T>(GraphNodeStorage node) where T : class
    {
        return node is GameInventory inventory ? inventory.childItems.OfType<T>().ToList() : new List<T>();
    }
}

internal sealed class StoreStation
{
    internal static StoreStation Instance = new StoreStation();
    internal int dayCounter;
    internal StoreEventManager storeEventManager = new StoreEventManager();
}

internal sealed class EmporiumAudioPlayer
{
    internal static EmporiumAudioPlayer Instance = new EmporiumAudioPlayer();
    internal TestAudioSource ratchet = new TestAudioSource();
    internal TestAudioSource audioBeatVictory = new TestAudioSource(), audioFail = new TestAudioSource(), audioLabeler = new TestAudioSource();
    internal TestAudioSource audioButton2 = new TestAudioSource(), audioButton4 = new TestAudioSource();
    internal void PlayBeatVictory() => audioBeatVictory.Play();
}

internal sealed class TestAudioSource
{
    internal int Plays;
    internal void Play() { Plays++; }
}

internal sealed class AudioManager
{
    internal static readonly AudioManager Instance = new AudioManager();
    internal readonly List<string> Played = new List<string>();
    internal readonly List<string> Played3 = new List<string>();
    internal string PrimaryClip, ThirdClip;
    internal void TryPlayOne(string clip)
    {
        if (string.IsNullOrEmpty(clip)) return;
        PrimaryClip = clip;
        Played.Add(clip);
    }
    internal bool Play3(string clip)
    {
        if (string.IsNullOrEmpty(clip)) return false;
        ThirdClip = clip;
        Played3.Add(clip);
        return true;
    }
}

internal sealed class MaterialDirectory
{
    private GameItem Electronic() => new GameItem { Id = "common_electronic" };
    private GameItem MetalIngot() => new GameItem { Id = "metal_ingot" };
}

