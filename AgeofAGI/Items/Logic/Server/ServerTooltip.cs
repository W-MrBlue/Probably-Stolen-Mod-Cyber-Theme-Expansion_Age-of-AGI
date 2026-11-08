using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.Memory;
using AgeOfAgi.Items.Logic.Cpu;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.DistributedTerminal;
using AgeOfAgi.Items.Logic.BrainServer;
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
    internal static class ServerTooltip
    {
        internal static void CreateTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || item == null) return;
            if (!item.IsTag(ServerTag))
            {
                if (item.IsTag(ProgressTextTag)) AppendProgressTooltip(builder, item, item.GetTagReadonly(ProgressTextTag).GetString());
                return;
            }
            bool brain = item.identifier == BrainServerLifeSupport.ItemId;
            bool terminal = item.IsTag(DistributedTerminalState.TerminalTag);
            if (brain || terminal) DistributedNetwork.Refresh(item);
            var row = item.contentWindow?.childElement as MachineryPanelRow;
            GameItem battery = (row?.GetStorage(DistributedTerminalState.BatteryIndex) as GameSlotInventory)?.childItem;
            bool insufficientPower = terminal && (battery == null || !GeneralHelper.IsItemOwned(battery) ||
                !PowerHelper.CanDrawPowerSource(battery, MachineParameters.GetPowerPerCycle(item)));
            MachineParameters.AppendPowerTooltip(builder, item, "server_stat_power");
            builder.AddLine("", ignoreEmpty: false);
            builder.AddLine(Text("server_stat_work", item.GetTagReadonly(WorkPerNightTag).GetDouble()), color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine(Text("server_stat_memory", item.GetTagReadonly(MemoryCapacityTag).GetLong()), color: RenderHandler.ColorPalette.LightCyan);
            builder.AddLine("", ignoreEmpty: false);
            int trainingData = item.GetTagReadonly(TrainingDataTag).GetInt();
            bool hasDetails = trainingData > 0;
            if (trainingData > 0) builder.AddLine(Text("server_training_data_remaining", trainingData), color: RenderHandler.ColorPalette.LightCyan);
            if (terminal)
            {
                hasDetails = true;
                builder.AddLine(Text("terminal_number", item.GetUniqueID()), color: RenderHandler.ColorPalette.LightCyan);
                builder.AddLine(Text("terminal_servers", item.GetTagReadonly("AGE_OF_AGI_TERMINAL_SERVER_COUNT").GetInt()), color: RenderHandler.ColorPalette.LightCyan);
                builder.AddLine(Text("terminal_batch_limit", item.GetTagReadonly("AGE_OF_AGI_TERMINAL_BATCH_LIMIT").GetLong()), color: RenderHandler.ColorPalette.LightCyan);
                builder.AddLine(Text("terminal_data", item.GetTagReadonly("AGE_OF_AGI_TERMINAL_DATA_AMOUNT").GetLong(),
                    item.GetTagReadonly("AGE_OF_AGI_TERMINAL_DATA_FREE").GetLong()), color: RenderHandler.ColorPalette.LightCyan);
            }
            else if (brain)
            {
                string deathReason = item.GetTagReadonly(BrainServerLifeSupport.DeathReasonTag).GetString();
                if (item.IsTag(BrainServerLifeSupport.DeadTag) && !string.IsNullOrEmpty(deathReason))
                {
                    builder.AddLine(Text(deathReason), color: RenderHandler.ColorPalette.LightRed);
                    hasDetails = true;
                }
            }
            else
            {
                int cpuTarget = item.GetTagReadonly(CpuUpgradeTargetTag).GetInt();
                int gpuTarget = item.GetTagReadonly(GpuUpgradeTargetTag).GetInt();
                int memoryTarget = item.GetTagReadonly(MemoryUpgradeTargetTag).GetInt();
                hasDetails |= AppendExpansion(builder, "server_stat_cpu_expansion", Count(item, CpuUnitsTag),
                    item.GetTagReadonly(CpuUpgradeStep1TargetTag).GetInt(), cpuTarget);
                hasDetails |= AppendExpansion(builder, "server_stat_gpu_expansion", Count(item, GpuUnitsTag),
                    item.GetTagReadonly(GpuUpgradeStep1TargetTag).GetInt(), gpuTarget);
                hasDetails |= AppendExpansion(builder, "server_stat_memory_expansion", Count(item, MemoryUnitsTag), memoryTarget, memoryTarget);
            }
            if (hasDetails) builder.AddLine("", ignoreEmpty: false);
            string statusKey = item.GetTagReadonly(StatusTag).GetString();
            if (!string.IsNullOrEmpty(statusKey) && !(terminal && statusKey == "server_insufficient_power"))
                builder.AddLine(Text(statusKey), color: statusKey.StartsWith("brain_") ? RenderHandler.ColorPalette.LightRed :
                    statusKey == "server_insufficient_power" ? RenderHandler.ColorPalette.Yellow :
                    statusKey == "server_insufficient_extraction_data" ? RenderHandler.ColorPalette.DarkYellow :
                    statusKey == "server_insufficient_memory" ? RenderHandler.ColorPalette.LightRed :
                    statusKey == "server_running" ? RenderHandler.ColorPalette.LightGreen : RenderHandler.ColorPalette.LightCyan);
            AppendProgressTooltip(builder, item, "server_progress_tooltip");
            if (insufficientPower)
                builder.AddLine(LocHelper.GetLocalizedMechanic("mech_machinery_insufficient_power"), color: RenderHandler.ColorPalette.LightRed);
        }

        private static bool AppendExpansion(RichTextBuilder builder, string key, int count, int firstTarget, int totalTarget)
        {
            if (firstTarget <= 0 || totalTarget < firstTarget || count >= totalTarget) return false;
            bool secondStage = count >= firstTarget;
            builder.AddLine(Text(key, secondStage ? count - firstTarget : count, secondStage ? totalTarget - firstTarget : firstTarget),
                color: RenderHandler.ColorPalette.LightCyan);
            return true;
        }

        private static void AppendProgressTooltip(RichTextBuilder builder, GameItem item, string textKey)
        {
            if (!item.IsTag(ProgressVisibleTag)) return;
            float current = item.GetTagReadonly(ProgressCurrentTag).GetFloat();
            float total = item.GetTagReadonly(ProgressTargetTag).GetFloat();
            if (total <= 0 || float.IsNaN(total) || float.IsInfinity(total) || current < 0 || current > total || float.IsNaN(current)) return;
            // Same native rich-text bar used by MachineHydroponic.CreateMachineTooltip.
            // Custom float progress tags preserve fractional work without adopting
            // the native integer progress machine's execution callbacks.
            builder.AddProgressBar(current / total, 10, RenderHandler.ColorPalette.LightGreen,
                RenderHandler.ColorPalette.VeryDarkGreen, null, " ");
            builder.AddLine(Text(textKey, current, total, 100 * (current / total)), color: RenderHandler.ColorPalette.LightGreen);
        }

        private static string Text(string key, params object[] args) => ModHelper.GetLocalized(ModConstants.ModId, key, args);
    }
}
