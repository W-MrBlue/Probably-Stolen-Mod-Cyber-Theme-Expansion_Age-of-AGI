using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.Memory;
using AgeOfAgi.Items.Logic.Cpu;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.Server
{
    internal sealed class ServerHardware
    {
        internal int Power { get; private set; }
        internal long MemoryGb { get; private set; }
        internal double ComputePerNight { get; private set; }
        internal double CpuMultiplier { get; private set; }
        private GameItem[] items;

        // Intrinsic hardware has no per-task power cost; life support pays every day.
        internal static ServerHardware Fixed(GameItem owner, double compute, long memory) => new ServerHardware
        { Power = 0, MemoryGb = memory, ComputePerNight = compute, CpuMultiplier = 1, items = new[] { owner } };

        internal static ServerHardware Aggregate(IEnumerable<ServerHardware> hardware)
        {
            var sources = hardware.ToArray();
            if (sources.Length == 0 || sources.Any(source => source == null)) return null;
            double compute = sources.Sum(source => source.ComputePerNight);
            if (!Positive(compute)) return null;
            return new ServerHardware
            {
                Power = 0, CpuMultiplier = 1, ComputePerNight = compute,
                MemoryGb = sources.Sum(source => source.MemoryGb),
                items = sources.SelectMany(source => source.items).ToArray()
            };
        }

        internal static int PowerCost(params GameInventory[] inventories)
        {
            long total = 0;
            foreach (GameItem item in inventories.SelectMany(inventory => inventory.childItems))
            {
                if (item == null) return -1;
                int power;
                if (GpuRuntime.IsGpu(item))
                {
                    if (!GpuRuntime.TryGetRealPerformance(item, out var gpu) || !gpu.PowerSupplyWorking) return -1;
                    power = gpu.PowerPerCycle;
                }
                else power = CpuState.IsCpu(item) ? CpuState.GetPowerPerCycle(item) : MachineParameters.GetPowerPerCycle(item);
                if (power < 0 || item.unitCount != 1) return -1;
                total += power;
                if (total > int.MaxValue) return -1;
            }
            return (int)total;
        }

        internal static bool TryRead(GameGridInventory cpu, GameGridInventory gpu, GameGridInventory memory,
            GameSlotInventory network, ServerDefinition definition, out ServerHardware result)
        {
            result = null;
            var cpus = cpu.childItems;
            var gpus = gpu.childItems;
            var memories = memory.childItems;
            GameItem switchItem = network.childItem;
            if (cpus.Count == 0 || gpus.Count == 0 || memories.Count == 0)
                return false;
            if (switchItem != null && (!NetworkSwitchUpgrades.IsNetworkSwitch(switchItem) || !OwnedUnit(switchItem)))
                return false;
            double bonus = 0;
            double compute = 0;
            long capacity = MemoryCapacity(memory);
            if (capacity <= 0) return false;
            foreach (GameItem item in cpus)
            {
                if (!OwnedUnit(item) || !CpuState.IsCpu(item)) return false;
                float rate = CpuState.GetPowerBoostRate(item);
                if (rate < 0 || float.IsNaN(rate) || float.IsInfinity(rate)) return false;
                bonus += rate;
            }
            foreach (GameItem item in gpus)
            {
                if (!OwnedUnit(item) || !GpuRuntime.IsGpu(item)) return false;
                if (!GpuRuntime.TryGetRealPerformance(item, out var actual) || !actual.PowerSupplyWorking) return false;
                double power = actual.ComputePower;
                if (power < 0 || double.IsNaN(power) || double.IsInfinity(power)) return false;
                compute += power;
            }
            int cost = PowerCost(cpu, gpu, network);
            double multiplier = 1 + bonus / 100;
            double progress = compute * multiplier * definition.CoolingMultiplier;
            if (cost < 0 || !Positive(progress)) return false;
            result = new ServerHardware
            {
                Power = cost,
                MemoryGb = capacity,
                CpuMultiplier = multiplier,
                ComputePerNight = progress,
                items = cpus.Concat(gpus).Concat(memories).ToArray()
            };
            return true;
        }

        internal static long MemoryCapacity(GameGridInventory memory)
        {
            long capacity = 0;
            foreach (GameItem item in memory.childItems)
            {
                if (!OwnedUnit(item) || !MemoryState.IsMemory(item)) return -1;
                int amount = MemoryState.GetMemory(item);
                if (amount <= 0) return -1;
                capacity += amount;
            }
            return capacity;
        }

        internal bool Matches(ServerHardware other)
        {
            return other != null && Power == other.Power && MemoryGb == other.MemoryGb &&
                ComputePerNight == other.ComputePerNight && CpuMultiplier == other.CpuMultiplier &&
                items.Length == other.items.Length && items.All(item => other.items.Contains(item));
        }

        private static bool OwnedUnit(GameItem item) => item != null && item.unitCount == 1 && GeneralHelper.IsItemOwned(item);
        private static bool Positive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
