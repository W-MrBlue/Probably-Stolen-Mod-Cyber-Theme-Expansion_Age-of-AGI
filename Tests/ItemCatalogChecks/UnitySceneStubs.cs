// Shared Unity and native machinery substitutes, used by item and inspection checks.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal interface MachineryWidget { }

internal sealed class TreeNodeRender
{
    internal readonly RectTransform rectTransform = new RectTransform();
    internal readonly GameObject gameObject = new GameObject();
    // Match a render-node prefab whose Image needs explicit enabling.
    internal readonly UnityEngine.UI.Image image = new UnityEngine.UI.Image();
    internal readonly List<TreeNodeRender> Children = new List<TreeNodeRender>();
    internal T[] GetComponentsInChildren<T>(bool includeInactive) => new[] { image }.OfType<T>()
        .Concat(Children.SelectMany(child => child.GetComponentsInChildren<T>(includeInactive))).ToArray();
    internal static TreeNodeRender Instantiate(PixelElement element, string name) => new TreeNodeRender();
    internal static TreeNodeRender Instantiate() => new TreeNodeRender();
    internal TreeNodeRender SetParent(TreeNodeRender parent) { parent.Children.Add(this); return this; }
}

internal static class MachineryStyle
{
    internal static void Place(TreeNodeRender node, int x, int y, int width, int height)
    {
        node.rectTransform.anchoredPosition = new Vector2(x, -y);
        node.rectTransform.sizeDelta = new Vector2(width, height);
    }
    internal static void SetSimple(TreeNodeRender node, Sprite sprite, Color color)
    {
        node.image.sprite = sprite;
        node.image.type = UnityEngine.UI.Image.Type.Simple;
        node.image.color = color;
        node.image.enabled = true;
        node.rectTransform.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);
    }
}

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal static void Destroy(Object target) { if (target != null) target.Destroyed = true; }
        internal static GameObject Instantiate(GameObject source, Transform parent, bool worldPositionStays)
        {
            var map = new Dictionary<object, object>();
            GameObject CopyNode(GameObject original, Transform host)
            {
                var copy = new GameObject(original.name) { activeSelf = original.activeSelf, layer = original.layer, CloneSource = original };
                copy.transform.SetParent(host, false);
                copy.transform.localPosition = original.transform.localPosition;
                copy.transform.localScale = original.transform.localScale;
                copy.transform.sizeDelta = original.transform.sizeDelta;
                copy.transform.anchoredPosition = original.transform.anchoredPosition;
                copy.transform.anchorMin = original.transform.anchorMin; copy.transform.anchorMax = original.transform.anchorMax;
                copy.transform.pivot = original.transform.pivot;
                map.Add(original, copy); map.Add(original.transform, copy.transform);
                foreach (var component in original.Components)
                {
                    var duplicate = (MonoBehaviour)Activator.CreateInstance(component.GetType());
                    duplicate.gameObject = copy; copy.Components.Add(duplicate); map.Add(component, duplicate);
                }
                foreach (var child in original.transform.Children) CopyNode(child.gameObject, copy.transform);
                return copy;
            }
            var result = CopyNode(source, parent);
            foreach (var pair in map.Where(pair => pair.Key is MonoBehaviour))
            {
                foreach (var field in pair.Key.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    if (field.IsInitOnly) continue;
                    object value = field.GetValue(pair.Key);
                    field.SetValue(pair.Value, value != null && map.TryGetValue(value, out var mapped) ? mapped : value);
                }
            }
            return result;
        }
    }
    internal class Component : Object
    {
        internal GameObject gameObject;
        internal Transform transform => gameObject.transform;
        internal T[] GetComponentsInChildren<T>(bool includeInactive) => gameObject.GetComponentsInChildren<T>(includeInactive);
        internal T GetComponentInParent<T>() where T : class => gameObject.GetComponentInParent<T>();
    }
    internal class Behaviour : Component
    {
        internal bool enabled = true;
    }
    internal class MonoBehaviour : Behaviour
    {
    }
    internal sealed class GameObject : Object
    {
        internal string name;
        internal int layer;
        internal GameObject CloneSource;
        internal bool activeSelf = true;
        internal readonly RectTransform transform;
        internal bool activeInHierarchy => activeSelf && !Destroyed && (transform.parent?.gameObject.activeInHierarchy ?? true);
        internal readonly List<MonoBehaviour> Components = new List<MonoBehaviour>();
        internal GameObject(string name = "", params Type[] types)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this };
            foreach (Type type in types)
                if (typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    var component = (MonoBehaviour)Activator.CreateInstance(type);
                    component.gameObject = this;
                    Components.Add(component);
                }
        }
        internal void SetActive(bool active) => activeSelf = active;
        internal T GetComponent<T>() where T : class => transform as T ?? Components.OfType<T>().FirstOrDefault();
        internal T GetComponentInParent<T>() where T : class => GetComponent<T>() ?? transform.parent?.gameObject.GetComponentInParent<T>();
        internal T[] GetComponentsInChildren<T>(bool includeInactive) => (includeInactive || activeInHierarchy ? Components.OfType<T>() : Enumerable.Empty<T>())
            .Concat(transform.Children.SelectMany(child => child.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        internal T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var component = new T();
            component.gameObject = this;
            Components.Add(component);
            return component;
        }
    }
    internal class Transform
    {
        internal GameObject gameObject;
        internal Transform parent;
        internal readonly List<Transform> Children = new List<Transform>();
        internal Vector3 localPosition;
        internal Vector3 localScale = Vector3.one;
        internal Vector3 lossyScale => parent == null ? localScale : new Vector3(localScale.x * parent.lossyScale.x,
            localScale.y * parent.lossyScale.y, localScale.z * parent.lossyScale.z);
        internal bool IsChildOf(Transform host) => this == host || parent?.IsChildOf(host) == true;
        internal int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        internal void SetAsLastSibling() => SetSiblingIndex(parent?.Children.Count ?? 0);
        internal void SetSiblingIndex(int index)
        {
            if (parent == null) return;
            parent.Children.Remove(this);
            parent.Children.Insert(Math.Max(0, Math.Min(index, parent.Children.Count)), this);
        }
        internal Vector3 position
        {
            get => TransformPoint(new Vector3(0, 0, 0));
            set => localPosition = parent == null ? value : parent.InverseTransformPoint(value);
        }
        internal Vector3 TransformPoint(Vector3 local)
        {
            var value = new Vector3(localPosition.x + local.x * localScale.x, localPosition.y + local.y * localScale.y,
                localPosition.z + local.z * localScale.z);
            return parent == null ? value : parent.TransformPoint(value);
        }
        internal Vector3 InverseTransformPoint(Vector3 world)
        {
            var value = parent == null ? world : parent.InverseTransformPoint(world);
            return new Vector3((value.x - localPosition.x) / localScale.x, (value.y - localPosition.y) / localScale.y,
                (value.z - localPosition.z) / localScale.z);
        }
        internal void SetParent(Transform host, bool worldPositionStays)
        {
            parent?.Children.Remove(this);
            parent = host;
            host?.Children.Add(this);
        }
    }
    internal sealed class RectTransform : Transform
    {
        internal Vector2 sizeDelta;
        internal Vector2 anchoredPosition;
        internal Vector2 anchorMin, anchorMax, pivot = new Vector2(0.5f, 0.5f);
        internal Rect rect => new Rect(-sizeDelta.x * pivot.x, -sizeDelta.y * pivot.y, sizeDelta.x, sizeDelta.y);
        internal void GetWorldCorners(Vector3[] corners)
        {
            corners[0] = TransformPoint(new Vector3(rect.xMin, rect.yMin, 0));
            corners[1] = TransformPoint(new Vector3(rect.xMin, rect.yMax, 0));
            corners[2] = TransformPoint(new Vector3(rect.xMax, rect.yMax, 0));
            corners[3] = TransformPoint(new Vector3(rect.xMax, rect.yMin, 0));
        }
    }
    internal readonly struct Vector3
    {
        internal static Vector3 one => new Vector3(1, 1, 1);
        internal readonly float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static implicit operator Vector2(Vector3 value) => new Vector2(value.x, value.y);
    }
    internal sealed class CanvasRenderer { }
    internal sealed class Canvas : MonoBehaviour
    {
        internal bool overrideSorting;
        internal int sortingOrder, sortingLayerID;
        internal Canvas rootCanvas => this;
        internal static void ForceUpdateCanvases() { }
    }
    internal sealed class Material : Object { }
    internal readonly struct Vector4
    {
        internal static Vector4 zero => default;
        internal readonly float x, y, z, w;
        internal Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
    internal readonly struct Bounds
    {
        internal readonly Vector3 center, size;
        internal Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }
    internal static class RectTransformUtility
    {
        internal static Bounds CalculateRelativeRectTransformBounds(Transform root)
        {
            Vector2 size = ((RectTransform)root).sizeDelta;
            return new Bounds(new Vector3(0, 0, 0), new Vector3(size.x, size.y, 0));
        }
    }
    internal enum TextureFormat { RGBA32 }
    internal enum FilterMode { Point, Bilinear }
    internal enum TextureWrapMode { Clamp }
    internal readonly struct Color32
    {
        internal readonly byte r, g, b, a;
        internal Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    internal readonly struct Color
    {
        internal readonly float r, g, b, a;
        internal Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        internal static Color white => default;
    }
    internal readonly struct Rect
    {
        internal readonly float x, y, width, height;
        internal float xMin => x;
        internal float xMax => x + width;
        internal float yMin => y;
        internal float yMax => y + height;
        internal Vector2 center => new Vector2(x + width / 2, y + height / 2);
        internal Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    internal sealed class Texture2D : Object
    {
        internal readonly int width, height;
        internal FilterMode filterMode;
        internal TextureWrapMode wrapMode;
        internal Color32[] Pixels;
        internal Color32[] GetPixels32() => Pixels != null ? (Color32[])Pixels.Clone() :
            Enumerable.Repeat(new Color32(255, 255, 255, 255), width * height).ToArray();
        internal Texture2D(int width, int height, TextureFormat format, bool mipChain) { this.width = width; this.height = height; }
        internal void SetPixels32(Color32[] pixels)
        {
            if (pixels.Length != width * height) throw new ArgumentException("Texture dimensions differ.");
            Pixels = (Color32[])pixels.Clone();
        }
        internal void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }
    internal sealed class Sprite : Object
    {
        internal TestSprite BackingTestSprite;
        internal float pixelsPerUnit = 100;
        internal string AssetKey;
        internal Texture2D texture;
        internal Rect rect;
        internal static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) =>
            new Sprite { texture = texture, rect = rect, pixelsPerUnit = pixelsPerUnit };
    }
}

namespace UnityEngine.UI
{
    internal sealed class GraphicRaycaster : UnityEngine.MonoBehaviour { }
    internal static class Selectable
    {
        internal enum Transition { None, ColorTint, SpriteSwap }
    }
    internal struct SpriteState
    {
        internal UnityEngine.Sprite highlightedSprite, selectedSprite, pressedSprite, disabledSprite;
    }
    internal static class LayoutRebuilder
    {
        internal static void ForceRebuildLayoutImmediate(UnityEngine.RectTransform rect) { }
    }
    internal class LayoutGroup : UnityEngine.MonoBehaviour { }
    internal sealed class ContentSizeFitter : UnityEngine.MonoBehaviour { }
    internal sealed class Button : UnityEngine.MonoBehaviour
    {
        internal sealed class ButtonClickedEvent : UnityEngine.Events.UnityEventBase
        {
            private Action listeners;
            internal void AddListener(Action listener) => listeners += listener;
            internal void Invoke() { InvokePersistent(); listeners?.Invoke(); }
        }
        internal ButtonClickedEvent onClick = new ButtonClickedEvent();
        internal bool interactable = true;
        internal Graphic targetGraphic;
        internal Selectable.Transition transition;
        internal SpriteState spriteState;
    }
    internal sealed class ToggleGroup : UnityEngine.MonoBehaviour
    {
        internal bool allowSwitchOff;
        internal readonly List<Toggle> Toggles = new List<Toggle>();
        internal void Activate(Toggle selected)
        {
            foreach (var toggle in Toggles) if (toggle != selected) toggle.isOn = false;
        }
    }
    internal sealed class Toggle : UnityEngine.MonoBehaviour
    {
        private bool selected;
        private ToggleGroup currentGroup;
        internal ToggleGroup group
        {
            get => currentGroup;
            set { currentGroup?.Toggles.Remove(this); currentGroup = value; currentGroup?.Toggles.Add(this); }
        }
        internal bool isOn
        {
            get => selected;
            set { selected = value; if (value) currentGroup?.Activate(this); }
        }
    }
    internal class Graphic : UnityEngine.MonoBehaviour
    {
        internal Color color;
        internal bool raycastTarget;
    }
    internal sealed class Image : Graphic
    {
        internal enum Type { Simple }
        internal Sprite sprite;
        internal Type type;
        internal bool preserveAspect;
        internal bool maskable;
    }
}

namespace TMPro
{
    internal sealed class TMP_Dropdown
    {
        internal sealed class OptionData
        {
            internal string text;
            internal OptionData(string value) { text = value; }
        }
        internal readonly List<OptionData> options = new List<OptionData>();
        internal int Refreshes;
        internal void RefreshShownValue() => Refreshes++;
    }
    internal enum TextAlignmentOptions { MidlineLeft, Center, MidlineRight }
    internal enum TextOverflowModes { Overflow }
    internal sealed class TMP_FontAsset
    {
        internal readonly UnityEngine.Material material = new UnityEngine.Material();
    }
    internal sealed class TextMeshProUGUI : UnityEngine.UI.Graphic
    {
        internal string text;
        internal TextAlignmentOptions alignment;
        internal TMP_FontAsset font;
        internal UnityEngine.Material fontSharedMaterial;
        internal bool enableAutoSizing, enableWordWrapping;
        internal float fontSize;
        internal UnityEngine.Vector4 margin;
        internal TextOverflowModes overflowMode;
    }
    internal sealed class TMP_InputField : UnityEngine.MonoBehaviour
    {
        internal enum ContentType { Standard, IntegerNumber }
        internal ContentType contentType;
        internal string text;
        internal TextMeshProUGUI textComponent;
        internal sealed class OnChangeEvent : UnityEngine.Events.StringEvent { }
        internal sealed class SubmitEvent : UnityEngine.Events.StringEvent { }
        internal sealed class SelectionEvent : UnityEngine.Events.StringEvent { }
        internal OnChangeEvent onValueChanged = new OnChangeEvent();
        internal SubmitEvent onEndEdit = new SubmitEvent(), onSubmit = new SubmitEvent();
        internal SelectionEvent onSelect = new SelectionEvent(), onDeselect = new SelectionEvent();
        internal void SetTextWithoutNotify(string value) { text = value; if (textComponent != null) textComponent.text = value; }
    }
}

namespace UnityEngine.Events
{
    internal class UnityEventBase
    {
        private sealed class ArgumentCache { internal int m_IntArgument; }
        private sealed class PersistentCall
        {
            internal string m_MethodName;
            internal ArgumentCache m_Arguments;
            internal Action Callback;
        }
        private sealed class PersistentCallGroup { internal List<PersistentCall> m_Calls = new List<PersistentCall>(); }
        private readonly PersistentCallGroup m_PersistentCalls = new PersistentCallGroup();
        internal int GetPersistentEventCount() => m_PersistentCalls.m_Calls.Count;
        internal string GetPersistentMethodName(int index) => m_PersistentCalls.m_Calls[index].m_MethodName;
        internal void AddPersistent(string method, int argument, Action callback) => m_PersistentCalls.m_Calls.Add(new PersistentCall
            { m_MethodName = method, m_Arguments = new ArgumentCache { m_IntArgument = argument }, Callback = callback });
        protected void InvokePersistent() { foreach (var call in m_PersistentCalls.m_Calls) call.Callback(); }
    }
    internal class StringEvent
    {
        private Action<string> listeners;
        internal void AddListener(Action<string> listener) => listeners += listener;
        internal void Invoke(string value) => listeners?.Invoke(value);
    }
}
