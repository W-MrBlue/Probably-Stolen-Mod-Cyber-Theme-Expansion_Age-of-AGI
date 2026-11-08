using AgeOfAgi.Items.Logic.BrainServer;
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

namespace AgeOfAgi.Items.Logic.Server
{
    internal static class ServerState
    {
        internal const string ServerTag = "AGE_OF_AGI_SERVER";
        internal const string ActiveTaskTag = "AGE_OF_AGI_SERVER_TRAINING_ACTIVE";
        internal const string TrainingStateTag = "AGE_OF_AGI_SERVER_TRAINING_STATE";
        internal const string LastNightTag = "AGE_OF_AGI_SERVER_LAST_NIGHT";
        internal const string ProgressVisibleTag = "AGE_OF_AGI_SERVER_PROGRESS_VISIBLE";
        internal const string ProgressCurrentTag = "AGE_OF_AGI_SERVER_PROGRESS_CURRENT";
        internal const string ProgressTargetTag = "AGE_OF_AGI_SERVER_PROGRESS_TARGET";
        internal const string WorkPerNightTag = "AGE_OF_AGI_SERVER_WORK_PER_NIGHT";
        internal const string MemoryCapacityTag = "AGE_OF_AGI_SERVER_MEMORY_CAPACITY_GB";
        internal const string StatusTag = "AGE_OF_AGI_SERVER_STATUS_KEY";
        internal const string CpuUpgradeTargetTag = "AGE_OF_AGI_SERVER_CPU_UPGRADE_TARGET";
        internal const string CpuUpgradeStep1TargetTag = "AGE_OF_AGI_SERVER_CPU_UPGRADE_STEP1_TARGET";
        internal const string GpuUpgradeTargetTag = "AGE_OF_AGI_SERVER_GPU_UPGRADE_TARGET";
        internal const string GpuUpgradeStep1TargetTag = "AGE_OF_AGI_SERVER_GPU_UPGRADE_STEP1_TARGET";
        internal const string MemoryUpgradeTargetTag = "AGE_OF_AGI_SERVER_MEMORY_UPGRADE_TARGET";
        internal const string ProcessingLockTag = "AGE_OF_AGI_SERVER_PROCESSING_LOCK";
        internal const string ProgressTextTag = "AGE_OF_AGI_SERVER_PROGRESS_TEXT";
        internal const int BatteryIndex = 0;
        internal const int CpuIndex = 1;
        internal const int GpuIndex = 2;
        internal const int NetworkIndex = 3;
        internal const int MemoryIndex = 4;
        internal const int ModelIndex = 5;
        internal const int InputIndex = 6;
        internal const int OutputIndex = 7;
        internal const int DataContainerIndex = 8;
        internal const string TrainingDataTag = "AGE_OF_AGI_SERVER_TRAINING_DATA_REMAINING";
        internal const string CpuUnitsTag = "AGE_OF_AGI_SERVER_CPU_UPGRADE_UNITS";
        internal const string GpuUnitsTag = "AGE_OF_AGI_SERVER_GPU_UPGRADE_UNITS";
        internal const string MemoryUnitsTag = "AGE_OF_AGI_SERVER_MEMORY_UPGRADE_UNITS";
        internal const string ElectronicsId = "common_electronic";
        internal const string IngotId = "metal_ingot";
        internal const string GoldFingerId = ModConstants.ModId + ".gold_finger";

        internal static void RestoreStructure(GameItem machine)
        {
            machine.EnableTag(ServerTag);
            machine.EnableTag("CONTAINER_TAG");
            machine.EnableTag("IMPORTANT_TAG");
            machine.EnableTag("MACHINE_RECHARGABLE_TAG");
            machine.SetGameItemType("MACHINE");
            machine.EnableTag(MachineParameters.MachineTag);
        }

        internal static GameItem GetInstalledSwitch(GameItem machine)
        {
            if (machine == null || !machine.IsTag(ServerTag)) return null;
            MachineryPanelRow row = machine.contentWindow?.childElement as MachineryPanelRow;
            return (row?.GetStorage(machine.identifier == BrainServerLifeSupport.ItemId ? BrainServerLifeSupport.NetworkIndex :
                machine.IsTag(DistributedTerminalState.TerminalTag) ? DistributedTerminalState.NetworkIndex : NetworkIndex) as GameSlotInventory)?.childItem;
        }

        internal static bool HasSwitchUpgrade(GameItem machine, int moduleIndex)
        {
            return NetworkSwitchUpgrades.HasUpgrade(GetInstalledSwitch(machine), moduleIndex);
        }

        internal static GameItem GetInstalledModel(GameItem machine)
        {
            if (machine == null || !machine.IsTag(ServerTag) || machine.identifier == BrainServerLifeSupport.ItemId) return null;
            var row = machine.contentWindow?.childElement as MachineryPanelRow;
            return (row?.GetStorage(machine.IsTag(DistributedTerminalState.TerminalTag) ? DistributedTerminalState.ModelIndex : ModelIndex)
                as GameSlotInventory)?.childItem;
        }

        internal static int Count(GameItem machine, string tag) => Math.Max(0, machine.GetTagReadonly(tag).GetInt());
    }
}
