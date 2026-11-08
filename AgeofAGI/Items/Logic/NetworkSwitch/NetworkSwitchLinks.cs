using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AgeOfAgi.Items.Logic.NetworkSwitch
{
    internal static class NetworkSwitchLinks
    {
        internal const string NumberTag = "AGE_OF_AGI_NETWORK_NUMBER";
        internal const string PartnersTag = "AGE_OF_AGI_NETWORK_PARTNERS";
        internal static bool Enabled { get; private set; }

        internal static void Enable() => Enabled = true;
        internal static void Shutdown() => Enabled = false;
        internal static long Number(GameItem item) => item == null ? 0 : Math.Max(0, item.GetTagReadonly(NumberTag).GetLong());

        internal static List<long> Partners(GameItem item)
        {
            string saved = item?.GetTagReadonly(PartnersTag).GetString();
            if (string.IsNullOrEmpty(saved)) return new List<long>();
            try
            {
                return (JsonConvert.DeserializeObject<List<long>>(saved) ?? new List<long>())
                    .Where(id => id > 0 && id != Number(item)).Distinct().OrderBy(id => id).ToList();
            }
            catch (JsonException) { return new List<long>(); }
        }

        internal static bool CanConnect(GameItem item) => Enabled && NetworkSwitchUpgrades.IsNetworkSwitch(item) &&
            item.unitCount == 1 && GeneralHelper.IsItemOwned(item) &&
            NetworkSwitchUpgrades.HasUpgrade(item, NetworkSwitchUpgrades.HighSpeedUpgrade);

        internal static bool CanPair(GameItem first, GameItem second) => first != second &&
            CanConnect(first) && CanConnect(second);

        internal static void Pair(GameItem first, GameItem second, IEnumerable<GameItem> allItems)
        {
            if (!CanPair(first, second)) return;
            var switches = allItems.Where(NetworkSwitchUpgrades.IsNetworkSwitch).Concat(new[] { first, second }).Distinct().ToArray();
            EnsureNumber(first, switches);
            EnsureNumber(second, switches);
            SavePartners(first, Partners(first).Concat(new[] { Number(second) }));
            SavePartners(second, Partners(second).Concat(new[] { Number(first) }));
        }

        private static void EnsureNumber(GameItem item, GameItem[] switches)
        {
            long number = Number(item);
            if (number > 0 && !switches.Any(other => other != item && Number(other) == number)) return;
            long maximum = switches.Max(Number);
            long next = checked(Math.Max(maximum + 1, (long)item.GetUniqueID()));
            item.ModifyTag(NumberTag, tag => tag.Enable().SetLong(next));
        }

        internal static void Clear(GameItem item, IEnumerable<GameItem> allItems)
        {
            if (!Enabled || !NetworkSwitchUpgrades.IsNetworkSwitch(item) || !GeneralHelper.IsItemOwned(item)) return;
            long number = Number(item);
            foreach (var peer in allItems.Where(NetworkSwitchUpgrades.IsNetworkSwitch).Where(peer => GeneralHelper.IsItemOwned(peer)))
                if (peer != item && Partners(peer).Contains(number))
                    SavePartners(peer, Partners(peer).Where(id => id != number));
            SavePartners(item, Array.Empty<long>());
        }

        internal static List<GameItem> Connected(GameItem start, IEnumerable<GameItem> allItems)
        {
            if (!CanConnect(start)) return new List<GameItem>();
            // Ignore ambiguous copied numbers and one-sided records; neither may create phantom capacity.
            var switches = allItems.Where(CanConnect).Concat(new[] { start }).Distinct()
                .Where(item => Number(item) > 0).GroupBy(Number).Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());
            if (!switches.ContainsKey(Number(start))) return new List<GameItem>();
            var result = new List<GameItem>();
            var visited = new HashSet<long>();
            var queue = new Queue<GameItem>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(Number(current))) continue;
                result.Add(current);
                foreach (long id in Partners(current))
                    if (switches.TryGetValue(id, out var peer) && Partners(peer).Contains(Number(current))) queue.Enqueue(peer);
            }
            return result;
        }

        private static void SavePartners(GameItem item, IEnumerable<long> ids)
        {
            long number = Number(item);
            string json = JsonConvert.SerializeObject(ids.Where(id => id > 0 && id != number).Distinct().OrderBy(id => id).ToArray());
            item.ModifyTag(PartnersTag, tag => tag.Enable().SetString(json));
        }

        internal static IEnumerable<GameItem> OwnedItems() => PlayerStore.HasInstance ?
            PlayerStore.Instance.FindAllItem() : Enumerable.Empty<GameItem>();

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (!Enabled) return;
            long number = Number(item);
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, "network_switch_number",
                number > 0 ? number.ToString() : ModHelper.GetLocalized(ModConstants.ModId, "network_switch_unnumbered")),
                color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, "network_switch_partners",
                string.Join(", ", Partners(item))), color: RenderHandler.ColorPalette.LightCyan);
        }
    }
}
