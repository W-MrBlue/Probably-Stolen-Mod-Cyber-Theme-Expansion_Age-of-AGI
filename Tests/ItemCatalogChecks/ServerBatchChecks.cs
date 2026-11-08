using System;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.Server;

internal static partial class ServerProductionChecks
{
    private static void CheckBatchProduction()
    {
        Assert(EncryptedItemCatalog.Load().TryGet(ServerProduction.NormalInputId, out var recipe), "Fixture contains a normal recipe.");
        var rig = new Rig();
        AiModelState.ApplyTrained(rig.Model.childItem, AiTaskTypes.Computing, 2, 20, 10, AiModelTraits.Batch, 4);
        // Choose hardware against the isolated fixture's CPU multiplier, not the user's balance.
        var cpu = (GameGridInventory)rig.Row.GetStorage(ServerState.CpuIndex);
        double cpuMultiplier = 1 + AgeOfAgi.Items.Logic.Cpu.CpuState.GetPowerBoostRate(cpu.childItems.Single()) / 100.0;
        GpuTestState.SetReal(rig.Gpu.childItems.Single(), compute: recipe.Work * 2.5 / (cpuMultiplier * 1.2));
        GameItem input = DirectoryMaster.Item(ServerProduction.NormalInputId).SetAmount(8); Put(rig.Input, input);
        int power = rig.Gpu.childItems.Single().GetTagReadonly(GpuRuntime.PowerPerCycleTag).GetInt() +
            AgeOfAgi.Items.Logic.Cpu.CpuState.GetPowerPerCycle(cpu.childItems.Single());
        rig.Night(1);
        Assert(rig.Output.childItems.Count == 2 && input.unitCount == 6, "One nightly budget completes two recipes, not four full-compute attempts.");
        Near(rig.State().Progress, recipe.Work * .5, "Carry leftover work into the third recipe");
        Near(AiModelState.GetCurrentDurability(rig.Model.childItem), 8, "Only delivered batches wear AI");
        Assert(Energy(rig) == 1000 - power, "Hardware power is paid once for all successful batches and partial work.");
        var restored = Restore(rig); restored.Night(2);
        Assert(restored.Output.childItems.Count == 5 && input.unitCount == 3, "Current-format restore preserves partial recipe progress and batch traits.");
        Near(AiModelState.GetCurrentDurability(restored.Model.childItem), 5, "Restored production charges each delivered batch.");

        rig = new Rig(durability: 2);
        AiModelState.ApplyTrained(rig.Model.childItem, AiTaskTypes.Computing, 2, 20, 2, AiModelTraits.Batch, 4);
        GpuTestState.SetReal(rig.Gpu.childItems.Single(), compute: 10000);
        Put(rig.Input, DirectoryMaster.Item(ServerProduction.NormalInputId).SetAmount(5));
        rig.Night(1);
        Assert(rig.Output.childItems.Count == 2 && rig.Model.childItem == null, "Last use delivers and stops remaining batch attempts.");

        rig = new Rig();
        AiModelState.ApplyTrained(rig.Model.childItem, AiTaskTypes.Computing, 2, 20, 8, AiModelTraits.Batch, 3);
        GpuTestState.SetReal(rig.Gpu.childItems.Single(), compute: 10000);
        Put(rig.Input, DirectoryMaster.Item(ServerProduction.NormalInputId).SetAmount(5));
        rig.Output.AfterAccept = _ => rig.Output.RejectTransfer = true;
        rig.Night(1);
        Assert(rig.Output.childItems.Count == 1 && AiModelState.GetCurrentDurability(rig.Model.childItem) == 7 &&
            rig.Input.childItems.Single().unitCount == 4, "Later delivery failures retain earlier commits without extra materials or wear.");

        rig = new Rig();
        AiModelState.ApplyTrained(rig.Model.childItem, AiTaskTypes.Computing, 2, 20, 10,
            AiModelTraits.Batch | AiModelTraits.DeepFeedback, 3);
        GpuTestState.SetReal(rig.Gpu.childItems.Single(), compute: 10000);
        var container = DirectoryMaster.Item("age_of_agi.data_container"); Put(rig.DataContainer, container);
        var carrier = DirectoryMaster.Item("age_of_agi.dc_common_data_disk"); Put(rig.Input, carrier);
        var definition = ServerCatalog.Load().Definition;
        Assert(ServerHardware.TryRead((GameGridInventory)rig.Row.GetStorage(ServerState.CpuIndex), rig.Gpu,
            (GameGridInventory)rig.Row.GetStorage(ServerState.MemoryIndex), rig.Network, definition, out var hardware), "Independent extraction hardware.");
        int extracted = (int)(hardware.MemoryGb * definition.DataUnitsPer8Gb / 8) * 3;
        rig.Battery.childItem.ModifyTag("power_source_item_energy", tag => tag.SetInt(hardware.Power));
        int sourceBefore = DataContentState.Amount(carrier);
        rig.Night(1);
        Assert(DataContentState.Amount(container) == extracted && DataContentState.Amount(carrier) == sourceBefore - extracted &&
            AiModelState.GetCurrentDurability(rig.Model.childItem) == 7 && Energy(rig) == 0,
            "Extraction batches pay power once even when that payment empties the battery; deep feedback never halves production data.");

        rig = new Rig();
        rig.Model.Remove(rig.Model.childItem);
        var untrained = DirectoryMaster.Item(AiModelState.UntrainedId);
        AiModelState.Apply(untrained, new AiModelAttributes(AiTaskTypes.Computing, 1, 2, 20, 7, 1,
            AiModelTraits.Batch | AiModelTraits.UShape, 3));
        Put(rig.Model, untrained);
        var data = DirectoryMaster.Item("age_of_agi.data_container"); DataContentState.SetAmount(data, 1); Put(rig.DataContainer, data);
        rig.Night(1);
        var trained = rig.Output.childItems.Single();
        Assert(AiModelState.GetTraits(trained) == (AiModelTraits.Batch | AiModelTraits.UShape) && AiModelState.GetBatchSize(trained) == 3,
            "Training transfers frozen traits and batch size to the completed AI.");
        Console.WriteLine("PASS: batch work budgets, per-night power, recipe wear, partial progress restore, delivery rollback and trained trait transfer.");
    }
}
