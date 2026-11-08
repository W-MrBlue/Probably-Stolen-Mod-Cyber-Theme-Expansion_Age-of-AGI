using System;
using System.Collections.Generic;

namespace AgeOfAgi.Debugging
{
    internal sealed class ModDebugMenu
    {
        internal static readonly ModDebugMenu Items = new ModDebugMenu("items", "物品生成");
        internal static readonly ModDebugMenu Events = new ModDebugMenu("events", "事件生成");
        internal static readonly ModDebugMenu Characters = new ModDebugMenu("characters", "人物生成");
        internal static readonly ModDebugMenu Stories = new ModDebugMenu("stories", "剧情生成");
        internal static readonly IReadOnlyList<ModDebugMenu> All = new[] { Items, Events, Characters, Stories };

        internal readonly string Title;
        private readonly string windowId;
        private readonly Dictionary<string, Action> actions = new Dictionary<string, Action>(StringComparer.Ordinal);
        internal IReadOnlyDictionary<string, Action> Actions => actions;

        private ModDebugMenu(string id, string title)
        {
            windowId = ModConstants.ModId + ".debug_" + id;
            Title = "Age of AGI · " + title;
        }

        internal void Add(string name, Action action) => actions.Add(name, action);

        internal void Clear()
        {
            CustomUIManager.Instance?.CloseWindow(windowId);
            actions.Clear();
        }

        internal void Open()
        {
            CustomUIManager manager = CustomUIManager.Instance;
            if (manager == null) return;
            var buttons = new List<KeyValuePair<string, CustomUIElement>>();
            CustomUIBuilder builder = manager.CreateWindow(windowId, Title, "rear")
                .SetSize(440, 570).SetDraggable(true).SetCloseOnEscape();
            builder.AddInput("搜索名称 / ID", "", query =>
            {
                string filter = (query ?? "").Trim();
                foreach (var button in buttons)
                    button.Value.SetVisible(button.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }, "search");
            builder.BeginScroll(460);
            foreach (var entry in actions)
            {
                string name = entry.Key;
                Action action = entry.Value;
                builder.AddButton(name, () => Run(name, action), name);
                builder.Last.SetPreferredSize(400, 30);
                buttons.Add(new KeyValuePair<string, CustomUIElement>(name, builder.Last));
            }
            builder.End().Center().Show();
        }

        private static void Run(string name, Action action)
        {
            LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId && candidate.IsEnabled);
            if (mod == null) return;
            var log = new ModLog(mod.Manifest);
            if (!PlayerStore.HasInstance)
            {
                log.Warning("调试操作需要先进入游戏：" + name);
                return;
            }
            try { action(); }
            catch (Exception exception) { log.Error("调试操作失败：" + name + "\n" + exception); }
        }
    }
}
