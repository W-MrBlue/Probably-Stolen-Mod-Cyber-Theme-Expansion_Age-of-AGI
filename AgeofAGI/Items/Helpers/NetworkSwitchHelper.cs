using System;
using AgeOfAgi.Items.Logic.NetworkSwitch;

using static AgeOfAgi.Items.Logic.NetworkSwitch.NetworkSwitchUpgrades;

namespace AgeOfAgi.Items.Helpers
{
    internal static class NetworkSwitchHelper
    {
        internal static void Init(GameItem item, Action<GameItem> initializeAudio)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            if (item.identifier != SwitchId)
            {
                throw new ArgumentException("Network switch ID does not match item '" + item.identifier + "'.", nameof(item));
            }
            item.EnableTag(SwitchTag);
            initializeAudio(item);
            item.mayItemTargetThisFunc = (source, target) => CanApply(source, target) || NetworkSwitchLinks.CanPair(source, target);
            item.canItemTargetThisFunc = (source, target) => CanApply(source, target) || NetworkSwitchLinks.CanPair(source, target);
            item.maySelectSlotItemFunc = (self, _, __) => NetworkSwitchLinks.CanConnect(self);
            item.canSelectSlotItemFunc = (self, _, __) => NetworkSwitchLinks.CanConnect(self);
            item.mayThisTargetItemFunc = NetworkSwitchLinks.CanPair;
            item.canThisTargetItemFunc = NetworkSwitchLinks.CanPair;
            item.onThisTargetItemFunc = (source, target) => NetworkSwitchLinks.Pair(source, target, NetworkSwitchLinks.OwnedItems());
            item.mayActivateSlotItemFunc = (self, _, __) => NetworkSwitchLinks.Enabled;
            item.canActivateSlotItemFunc = (self, _, __) => NetworkSwitchLinks.Enabled && GeneralHelper.IsItemOwned(self);
            item.onActivateSlotItemFunc = (self, _, __) => NetworkSwitchLinks.Clear(self, NetworkSwitchLinks.OwnedItems());
        }

        internal static void InitModule(GameItem item, int index, Action<GameItem> initializeAudio)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            if (ModuleIndex(item.identifier) != index)
            {
                throw new ArgumentException("Upgrade module index does not match item '" + item.identifier + "'.", nameof(index));
            }
            item.ModifyTag(ModuleIndexTag, tag => tag.Enable().SetInt(index));
            initializeAudio(item);
            item.mayThisTargetItemFunc = (source, target) => CanApply(source, target);
            item.canThisTargetItemFunc = (source, target) => CanApply(source, target);
            // The module's own callback executes the upgrade; the switch only
            // declares validity, so one application pays exactly one unit.
            item.onThisTargetItemFunc = (source, target) => Apply(source, target);
        }

        internal static void CreateTooltip(RichTextBuilder builder, GameItem item) => NetworkSwitchUpgrades.CreateTooltip(builder, item);
    }
}
