using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.Memory;
using AgeOfAgi.Items.Logic.Cpu;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.DistributedTerminal;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using HarmonyLib;
using Newtonsoft.Json;

using static AgeOfAgi.Items.Logic.Server.ServerState;

namespace AgeOfAgi.Items.Logic.Server
{
    internal static class ServerUpgrades
    {
        internal static void Bind(GameItem machine, ServerDefinition definition)
        {
            bool applying = false;
            machine.mayItemTargetThisFunc = (source, target) => CanUpgrade(source, target, definition);
            machine.canItemTargetThisFunc = (source, target) => CanUpgrade(source, target, definition);
            machine.onItemTargetThisFunc = (source, target) =>
            {
                if (applying || !CanUpgrade(source, target, definition)) return;
                applying = true;
                try
                {
                    string tag = source.identifier == ElectronicsId ? CpuUnitsTag :
                        source.identifier == GoldFingerId ? MemoryUnitsTag : GpuUnitsTag;
                    int count = Count(target, tag);
                    target.ModifyTag(tag, value => value.Enable().SetInt(count + 1));
                    source.SetAmount(source.unitCount - 1);
                    if (source.unitCount == 0) source.Destroy();
                    target.contentWindow?.stateChangeActionFunc?.Invoke();
                    EmporiumAudioPlayer.Instance.ratchet.Play();
                }
                finally
                {
                    applying = false;
                }
            };
        }

        internal static bool CanUpgrade(GameItem source, GameItem target, ServerDefinition definition)
        {
            if (source == null || target == null || source.unitCount <= 0 || target.identifier != ServerCatalog.ServerId ||
                !GeneralHelper.IsItemOwned(source) || !GeneralHelper.IsItemOwned(target) || target.IsTag(ActiveTaskTag)) return false;
            if (source.identifier == ElectronicsId) return Count(target, CpuUnitsTag) < definition.CpuUpgradeUnitsTotal;
            if (source.identifier == IngotId) return Count(target, GpuUnitsTag) < 2L * definition.GpuUpgradeUnitsPerStep;
            if (source.identifier == GoldFingerId) return Count(target, MemoryUnitsTag) < definition.MemoryUpgradeUnits;
            return false;
        }

        internal static bool IsEnabledServer(GameItem target)
        {
            return target != null && target.identifier == ServerCatalog.ServerId && target.IsTag(ServerTag) &&
                ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled);
        }

        internal static void BindNativeMaterial(GameItem item)
        {
            var may = item.mayThisTargetItemFunc;
            var can = item.canThisTargetItemFunc;
            var execute = item.onThisTargetItemFunc;
            item.mayThisTargetItemFunc = (source, target) => IsEnabledServer(target)
                ? target.mayItemTargetThisFunc?.Invoke(source, target) == true : may?.Invoke(source, target) == true;
            item.canThisTargetItemFunc = (source, target) => IsEnabledServer(target)
                ? target.canItemTargetThisFunc?.Invoke(source, target) == true : can?.Invoke(source, target) == true;
            item.onThisTargetItemFunc = (source, target) =>
            {
                if (!IsEnabledServer(target)) execute?.Invoke(source, target);
            };
        }
    }
    [HarmonyPatch(typeof(MaterialDirectory), "Electronic")]
    internal static class ServerElectronicUpgradePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(GameItem __result) => ServerUpgrades.BindNativeMaterial(__result);
    }

    [HarmonyPatch(typeof(MaterialDirectory), "MetalIngot")]
    internal static class ServerIngotUpgradePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(GameItem __result) => ServerUpgrades.BindNativeMaterial(__result);
    }

}
