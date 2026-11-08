using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Helpers;
using Newtonsoft.Json;

internal static class GpuPerformanceChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        var complete = Assembly();
        Check(complete, 100, 10, "An intact card retains the core budgets.");
        foreach (var part in Memory(complete)) part.Condition = "lowfrequency";
        Check(complete, 60, 10, "All low-frequency memory supplies 60 percent compute.");
        foreach (var part in Memory(complete)) part.Condition = "mismatch";
        Check(complete, 0, 10, "Wrong-model memory supplies no compute.");
        foreach (var part in Memory(complete)) { part.Condition = "empty"; part.Sprite = ""; }
        Check(complete, 0, 10, "Empty memory sockets remain in the denominator.");

        var mixed = Assembly();
        Memory(mixed)[0].Condition = "lowfrequency";
        Memory(mixed)[1].Condition = "mismatch";
        Memory(mixed)[2].Sprite = "";
        Check(mixed, 84, 10, "Mixed normal, low-frequency, wrong-model and absent memory uses all 15 sockets.");
        foreach (string missing in new[] { "goldfinger_up", "goldfinger_down" })
        {
            var state = Assembly();
            Part(state, missing).Sprite = "";
            Check(state, 100, 10, "Neither contact changes compute or power.");
            Assert(GpuPerformanceCalculator.HasMissingGoldfinger(state), "Either absent contact is a physical defect.");
            Assert(!GpuPerformanceCalculator.Calculate(state, 100, 10).PowerSupplyWorking,
                "Either missing contact prevents use while retaining compute and energy requirements.");
        }
        var noContacts = Assembly();
        Part(noContacts, "goldfinger_up").Sprite = "";
        Part(noContacts, "goldfinger_down").Sprite = "";
        Check(noContacts, 100, 10, "Missing both contacts also leaves compute and power unchanged.");
        foreach (var sample in new[] { (Working: 8, Power: 10), (Working: 7, Power: 12), (Working: 4, Power: 20),
            (Working: 1, Power: 80), (Working: 0, Power: 0) })
        {
            var state = Assembly();
            foreach (var part in state.Parts.Where(part => part.Slot.StartsWith("power_module_")).Skip(sample.Working)) part.Sprite = "";
            Check(state, 100, sample.Power, "Module efficiency applies reciprocal power and rounds charges up.");
            Assert(GpuPerformanceCalculator.Calculate(state, 100, 10).PowerSupplyWorking == (sample.Working > 0),
                "Zero working modules records an independent supply fault.");
        }
        var disconnected = Assembly();
        Part(disconnected, "power_interface").Sprite = "";
        Check(disconnected, 100, 10, "A missing power connector retains compute and required energy while preventing use.");
        disconnected.CoreComputePower = 100;
        disconnected.CorePowerPerCycle = 10;
        Assert(GpuPerformanceCalculator.TryCalculateInspection(disconnected, 4, 0, 0, 0, out var required) &&
            required.PowerPerCycle == 20 && !required.PowerSupplyWorking,
            "Manual module counts still calculate required energy when the physical connector is missing.");
        Assert(!GpuPerformanceCalculator.Calculate(disconnected, 100, 0).PowerSupplyWorking,
            "Missing power connector prevents work even with zero base power.");
        Assert(GpuPerformanceCalculator.Calculate(Assembly(), 100, 0).PowerPerCycle == 0,
            "Configured zero base power remains valid on functioning hardware.");
        var fractional = Assembly();
        Memory(fractional)[0].Condition = "lowfrequency";
        Near(GpuPerformanceCalculator.Calculate(fractional, 10, 2).ComputePower, 146d / 15,
            "Fractional compute is not truncated to an integer.");
        CheckSavedConversion(definitions);
        GpuLabelChecks.Run();
        GpuSaveLoadChecks.Run();
        Console.WriteLine("PASS: GPU memory performance independent of contacts, independent power faults, fractional precision, core binding and saved-state conversion.");
    }

    private static void CheckSavedConversion(IReadOnlyList<ItemDefinition> definitions)
    {
        var cores = GpuCatalog.Load();
        // The old item identity is gpu0; its frozen core is vector, whose CSV budget is gpu2.
        var oldAssembly = Assembly();
        oldAssembly.Parts[1].Sprite = "gpu_core_vector";
        Memory(oldAssembly)[0].Condition = "lowfrequency";
        // Exercise pre-feature snapshots without Condition or core budgets.
        foreach (var part in oldAssembly.Parts)
        {
            if (part.Slot.StartsWith("memory_"))
                part.Sprite = part.Condition == "lowfrequency" ? "gpu_mem_lowfrequency_rec" : "gpu_mem_normal_rec";
            part.Condition = null;
        }
        GameItem source = DirectoryMaster.Item("age_of_agi.gpu0");
        source.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(oldAssembly)));
        source.DisableTag(GpuRuntime.ComponentStatsTag);
        source.onLoaded(source);
        var converted = GpuInspectionState.Read(source);
        var core = cores.GetByCore("gpu_core_vector");
        Near(GpuRuntime.GetComputePower(source), core.ComputePower * 14.6 / 15, "Saved core identity supplies initial compute.");
        Assert(GpuRuntime.GetPowerPerCycle(source) == core.PowerPerCycle && converted.CoreComputePower == core.ComputePower &&
            converted.CorePowerPerCycle == core.PowerPerCycle && converted.Parts.Count == oldAssembly.Parts.Count,
            "Conversion preserves all saved components and freezes the matching core budgets.");
        var tunedRows = CsvFixture.Parse(ModHelper.ReadModFileText("age_of_agi", GpuCatalog.CatalogPath));
        foreach (var row in tunedRows.Rows) { row["computePower"] = "1234"; row["powerPerCycle"] = "123"; }
        var tunedCores = GpuCatalog.Parse(tunedRows.ToString());
        GameItem frozen = new GameItem { Id = source.identifier };
        GpuHelper.InitGpu(frozen, tunedCores.Get(frozen.identifier), _ => { }, GpuInspectionCatalog.Load(definitions), tunedCores);
        frozen.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(source.GetTagReadonly(GpuInspectionState.StateTag).GetString()));
        frozen.DisableTag(GpuRuntime.ComponentStatsTag);
        frozen.onLoaded(frozen);
        Near(GpuRuntime.GetComputePower(frozen), core.ComputePower * 14.6 / 15,
            "Frozen core budgets survive later CSV changes even when component stats are rebuilt.");
        Assert(GpuRuntime.GetPowerPerCycle(frozen) == core.PowerPerCycle, "Saved initial power is not replaced by a new configuration.");
        string saved = source.GetTagReadonly(GpuInspectionState.StateTag).GetString();
        source.ModifyTag(GpuRuntime.ComputePowerTag, tag => tag.SetDouble(7.125));
        source.ModifyTag(GpuRuntime.PowerPerCycleTag, tag => tag.SetInt(19));
        source.onLoaded(source);
        Assert(saved == source.GetTagReadonly(GpuInspectionState.StateTag).GetString() &&
            Math.Abs(GpuRuntime.GetComputePower(source) - core.ComputePower * 14.6 / 15) < 1e-9 &&
            GpuRuntime.GetPowerPerCycle(source) == core.PowerPerCycle, "Legacy numeric Tag changes must not replace actual hardware fields.");

        GameItem rebuilt = DirectoryMaster.Item("age_of_agi.gpu0");
        rebuilt.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(saved));
        rebuilt.DisableTag(GpuRuntime.ComponentStatsTag);
        rebuilt.onLoaded(rebuilt);
        Near(GpuRuntime.GetComputePower(rebuilt), core.ComputePower * 14.6 / 15, "A fresh item restores frozen assembly calculations.");
        rebuilt.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString("damaged"));
        rebuilt.DisableTag(GpuRuntime.ComponentStatsTag);
        rebuilt.ModifyTag(GpuRuntime.ComputePowerTag, tag => tag.SetInt(73));
        rebuilt.onLoaded(rebuilt);
        Assert(GpuRuntime.GetComputePower(rebuilt) == 73 && rebuilt.GetTagReadonly(GpuInspectionState.StateTag).GetString() == "damaged",
            "Damaged or unknown old state retains legacy numeric fields.");
    }

    private static GpuInspectionState Assembly()
    {
        var state = new GpuInspectionState();
        Add("board"); Add("core");
        for (int index = 0; index < 15; index++) Add("memory_" + index);
        Add("goldfinger_up"); Add("goldfinger_down"); Add("power_interface");
        for (int index = 0; index < 8; index++) Add("power_module_" + index);
        return state;

        void Add(string slot) => state.Parts.Add(new GpuInspectionPart { Slot = slot, Sprite = "test_sprite", Condition = "normal" });
    }

    private static GpuInspectionPart[] Memory(GpuInspectionState state) => state.Parts.Where(part => part.Slot.StartsWith("memory_")).ToArray();
    private static GpuInspectionPart Part(GpuInspectionState state, string slot) => state.Parts.Single(part => part.Slot == slot);
    private static void Check(GpuInspectionState state, double compute, int power, string message)
    {
        var result = GpuPerformanceCalculator.Calculate(state, 100, 10);
        Near(result.ComputePower, compute, message);
        Assert(result.PowerPerCycle == power, message);
    }
    private static void Near(double actual, double expected, string message) => Assert(Math.Abs(actual - expected) < 1e-9, message + " " + actual + " != " + expected);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
