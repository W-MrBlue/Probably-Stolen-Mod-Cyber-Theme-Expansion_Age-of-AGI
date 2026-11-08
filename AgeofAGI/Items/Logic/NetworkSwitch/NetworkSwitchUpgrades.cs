using System;

namespace AgeOfAgi.Items.Logic.NetworkSwitch
{
    // The switch has no window of its own. Dedicated modules are applied through
    // the native item-on-item interaction and recorded as a tag bitmask, so
    // server recipes can query which upgrades an installed switch has.
    internal static class NetworkSwitchUpgrades
    {
        internal const string SwitchId = ModConstants.ModId + ".net_switch";
        internal const string ModuleIdPrefix = ModConstants.ModId + ".ns_upgrade_module";
        internal const string SwitchTag = "AGE_OF_AGI_NET_SWITCH";
        internal const string UpgradesTag = "AGE_OF_AGI_NS_UPGRADES";
        internal const string ModuleIndexTag = "AGE_OF_AGI_NS_MODULE_INDEX";
        internal const int ModuleCount = 4;
        internal const int FirewallUpgrade = 0;
        internal const int HighSpeedUpgrade = 1;
        internal const int NetworkAccessUpgrade = 2;
        internal const int PacketDisguiseUpgrade = 3;
        private const string UpgradeTextKey = "net_switch_stat_upgrade";

        private static bool applying;

        internal static int ModuleIndex(string itemId)
        {
            if (itemId == null || !itemId.StartsWith(ModuleIdPrefix, StringComparison.Ordinal) ||
                !int.TryParse(itemId.Substring(ModuleIdPrefix.Length), out int index) || index < 0 || index >= ModuleCount)
            {
                throw new InvalidOperationException("Upgrade module IDs must be " + ModuleIdPrefix + "0.." + (ModuleCount - 1) + ": " + itemId);
            }
            return index;
        }

        internal static bool IsNetworkSwitch(GameItem item)
        {
            return item != null && item.IsTag(SwitchTag);
        }

        // Query interface for server recipes: does this switch have the upgrade
        // installed by ns_upgrade_module{moduleIndex}? Safe on null or foreign items.
        internal static bool HasUpgrade(GameItem item, int moduleIndex)
        {
            return IsNetworkSwitch(item) && moduleIndex >= 0 && moduleIndex < ModuleCount &&
                (GetUpgradeMask(item) & (1 << moduleIndex)) != 0;
        }

        internal static int GetUpgradeMask(GameItem item)
        {
            return item == null ? 0 : Math.Max(0, item.GetTagReadonly(UpgradesTag).GetInt());
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !IsNetworkSwitch(item))
            {
                return;
            }
            int mask = GetUpgradeMask(item);
            for (int index = 0; index < ModuleCount; index++)
            {
                if ((mask & (1 << index)) != 0)
                {
                    string name = ModHelper.GetLocalized(ModConstants.ModId, "item_ns_upgrade_module" + index + "_name");
                    builder.AddLine(ModHelper.GetLocalized(ModConstants.ModId, UpgradeTextKey, name),
                        color: RenderHandler.ColorPalette.LightCyan);
                }
            }
            NetworkSwitchLinks.CreateTooltip(builder, item);
        }

        internal static bool CanApply(GameItem source, GameItem target)
        {
            if (source == null || target == null || !IsNetworkSwitch(target) || !source.IsTag(ModuleIndexTag))
            {
                return false;
            }
            int index = source.GetTagReadonly(ModuleIndexTag).GetInt();
            if (index < 0 || index >= ModuleCount || HasUpgrade(target, index) || source.unitCount <= 0)
            {
                return false;
            }
            return GeneralHelper.IsItemOwned(source) && GeneralHelper.IsItemOwned(target);
        }

        internal static void Apply(GameItem source, GameItem target)
        {
            if (applying || !CanApply(source, target))
            {
                return;
            }
            applying = true;
            try
            {
                int index = source.GetTagReadonly(ModuleIndexTag).GetInt();
                target.ModifyTag(UpgradesTag, tag => tag.Enable().SetInt(GetUpgradeMask(target) | (1 << index)));
                source.SetAmount(source.unitCount - 1);
                if (source.unitCount == 0) source.Destroy();
                EmporiumAudioPlayer.Instance.ratchet.Play();
            }
            finally
            {
                applying = false;
            }
        }
    }
}
