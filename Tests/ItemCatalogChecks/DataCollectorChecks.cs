using AgeOfAgi.Items.Logic.DataCollector;
using AgeOfAgi.Items.Logic.NetworkSwitch;
using AgeOfAgi.Items.Logic.DataContent;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;

internal static class DataCollectorChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        LoadedMod loaded = Program.PrepareLoadedMod(items);
        var registration = new ItemRegistration(loaded.Manifest);
        var directory = new ModItemDirectory();
        registration.Register(directory, items);
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        try
        {
            CheckStorageAndSprites();
            CheckUpgrade();
            CheckDailyPower();
        }
        finally
        {
            registration.Unregister();
            ModHelper.Translations.Clear();
            ModHelper.Locale = "en";
        }
    }

    private static void CheckStorageAndSprites()
    {
        ModHelper.Locale = "zh";
        var rig = new Rig();
        Assert(rig.Row.GetPanel("network").Label == "交换机" && rig.Row.GetPanel("containers").Label == "数据容器",
            "Reuse the existing switch and data-container localization.");
        Assert(rig.Battery.Width == 1 && rig.Battery.Height == 2 && rig.Network.Width == 2 && rig.Network.Height == 2 &&
            rig.Containers.Width == DataCollectorState.InitialWidth && rig.Containers.Height == DataCollectorState.ContainerHeight &&
            rig.Row.GetStorage(3) == null, "Storage order must be power/network/container grid.");
        Assert(rig.Machine.SpriteKey == DataCollectorState.EmptySpriteKey && rig.Machine.IsTag("machinery") &&
            rig.Machine.GetTagReadonly("machinery_drawn_power").GetInt() == 2,
            "An empty collector uses the empty sprite and native daily power metadata.");

        GameItem normal = DirectoryMaster.Item("age_of_agi.data_container");
        GameItem highCapacity = DirectoryMaster.Item("age_of_agi.hypo_data_container");
        GameItem carrier = DirectoryMaster.Item("age_of_agi.dc_common_data_disk");
        GameItem disk = DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId);
        Assert(DataContentState.IsContainer(normal) && DataContentState.IsContainer(highCapacity) &&
            GraphUtils.CanAccept(rig.Containers, normal) && GraphUtils.CanAccept(rig.Containers, highCapacity) &&
            !GraphUtils.CanAccept(rig.Containers, carrier) && !GraphUtils.CanAccept(rig.Containers, disk),
            "Accept both existing data-container IDs and reject carriers, disks and other items.");
        Assert(GraphUtils.CanAccept(rig.Network, DirectoryMaster.Item(NetworkSwitchUpgrades.SwitchId)) &&
            !GraphUtils.CanAccept(rig.Network, DirectoryMaster.Item("age_of_agi.cpu0")), "The network bay accepts only the existing switch.");
        normal.Owned = false;
        Assert(!GraphUtils.CanAccept(rig.Containers, normal), "Container storage retains native ownership restrictions.");
        normal.Owned = true;

        Put(rig.Machine, normal);
        Assert(ReferenceEquals(normal.parentInventory, rig.Containers) && rig.Machine.SpriteKey == DataCollectorState.LoadedSpriteKey,
            "Dropping a container on the machine routes to storage and shows the loaded sprite.");
        Put(rig.Containers, highCapacity);
        rig.Containers.Remove(normal);
        Assert(rig.Machine.SpriteKey == DataCollectorState.LoadedSpriteKey, "Keep the loaded sprite while another container remains.");
        rig.Containers.Remove(highCapacity);
        Assert(rig.Machine.SpriteKey == DataCollectorState.EmptySpriteKey, "Removing the last container restores the empty sprite.");

        rig.Containers.UncheckedAccept(normal);
        rig.Machine.onLoaded(rig.Machine);
        Assert(rig.Machine.SpriteKey == DataCollectorState.LoadedSpriteKey, "Load derives the sprite from restored container contents.");
        rig.Containers.RawRemove(normal);
        rig.Machine.onLoaded(rig.Machine);
        Assert(rig.Machine.SpriteKey == DataCollectorState.EmptySpriteKey, "Load restores the empty sprite when no containers remain.");
        ModHelper.Locale = "en";
        Assert(new Rig().Row.GetPanel("containers").Label == "Data Container", "English container panel text is missing.");
    }

    private static void CheckUpgrade()
    {
        var rig = new Rig();
        GameItem container = DirectoryMaster.Item("age_of_agi.data_container");
        var before = new RichTextBuilder();
        DataCollectorHelper.CreateTooltip(before, rig.Machine);
        Assert(before.Lines.Single().Text == ModHelper.GetLocalized(AgeOfAgi.ModConstants.ModId, "data_collector_expansion",
            0, DataCollectorState.UpgradeDiskCost),
            "An unexpanded collector displays zero contributed disks and the full upgrade cost.");
        Put(rig.Containers, container);
        GameItem disks = DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId).SetAmount(DataCollectorState.UpgradeDiskCost - 1);
        Assert(!rig.Machine.mayItemTargetThisFunc(disks, rig.Machine) && !rig.Machine.canItemTargetThisFunc(disks, rig.Machine),
            "Fewer than six blank disks cannot expand the collector.");
        rig.Machine.onItemTargetThisFunc(disks, rig.Machine);
        Assert(disks.unitCount == DataCollectorState.UpgradeDiskCost - 1 && rig.Containers.Width == DataCollectorState.InitialWidth,
            "Rejected upgrades conserve all disks and storage capacity.");

        disks.SetAmount(DataCollectorState.UpgradeDiskCost);
        int sounds = EmporiumAudioPlayer.Instance.ratchet.Plays;
        Assert(rig.Machine.mayItemTargetThisFunc(disks, rig.Machine) && rig.Machine.canItemTargetThisFunc(disks, rig.Machine),
            "Six blank disks are a valid upgrade interaction.");
        rig.Machine.onItemTargetThisFunc(disks, rig.Machine);
        Assert(disks.Destroyed && rig.Containers.Width == DataCollectorState.ExpandedWidth &&
            rig.Containers.Height == DataCollectorState.ContainerHeight &&
            rig.Machine.GetTagReadonly(DataCollectorState.ExpandedTag).GetInt() == 1 &&
            ReferenceEquals(container.parentInventory, rig.Containers) && rig.Machine.SpriteKey == DataCollectorState.LoadedSpriteKey &&
            EmporiumAudioPlayer.Instance.ratchet.Plays == sounds + 1,
            "Expansion consumes six disks, preserves containers and leaves appearance tied to contents.");
        var after = new RichTextBuilder();
        DataCollectorHelper.CreateTooltip(after, rig.Machine);
        Assert(after.Lines.Count == 0, "The expansion requirement disappears after a successful upgrade.");

        var restored = new Rig();
        restored.Machine.ModifyTag(DataCollectorState.ExpandedTag, tag => tag.SetInt(1));
        restored.Machine.onLoaded(restored.Machine);
        Assert(restored.Containers.Width == DataCollectorState.ExpandedWidth &&
            restored.Containers.Height == DataCollectorState.ContainerHeight,
            "A newly constructed inventory restores the expanded shape from current saved state.");

        GameItem extra = DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId).SetAmount(DataCollectorState.UpgradeDiskCost);
        Assert(!rig.Machine.mayItemTargetThisFunc(extra, rig.Machine), "Expansion is applied only once.");
        rig.Machine.onItemTargetThisFunc(extra, rig.Machine);
        Assert(extra.unitCount == DataCollectorState.UpgradeDiskCost, "A repeated interaction cannot consume more disks.");
        rig.Containers.RawRemove(container);
        rig.Containers.UncheckedAccept(container);
        rig.Machine.onLoaded(rig.Machine);
        Assert(rig.Containers.Width == DataCollectorState.ExpandedWidth && rig.Machine.SpriteKey == DataCollectorState.LoadedSpriteKey,
            "Load restores expanded capacity and the content-driven sprite.");

        var empty = new Rig();
        GameItem stack = DirectoryMaster.Item(DiskWriterCatalog.BlankDiskId).SetAmount(DataCollectorState.UpgradeDiskCost + 2);
        empty.Machine.onItemTargetThisFunc(stack, empty.Machine);
        Assert(stack.unitCount == 2 && !stack.Destroyed && empty.Machine.SpriteKey == DataCollectorState.EmptySpriteKey &&
            empty.Containers.Width == DataCollectorState.ExpandedWidth,
            "Expansion retains excess disks and does not make an empty collector look loaded.");
        stack.Owned = false;
        var target = new Rig();
        Assert(!target.Machine.mayItemTargetThisFunc(stack, target.Machine), "Unowned upgrade materials are rejected.");
    }

    private static void CheckDailyPower()
    {
        var rig = new Rig();
        rig.Machine.onCycleEndSlotItemFunc(rig.Machine, null, null);
        GameItem battery = new GameItem { Id = "test_battery" };
        battery.EnableTag("power_source_item");
        PowerHelper.SetPowerSourceAt(battery, 5);
        Put(rig.Battery, battery);
        rig.Machine.onCycleEndSlotItemFunc(rig.Machine, null, null);
        Assert(PowerHelper.GetAvailableEnergyFromItem(battery) == 3, "The first daily cycle draws two energy.");
        rig.Machine.onCycleEndSlotItemFunc(rig.Machine, null, null);
        Assert(PowerHelper.GetAvailableEnergyFromItem(battery) == 1, "The second daily cycle draws two energy.");
        rig.Machine.onCycleEndSlotItemFunc(rig.Machine, null, null);
        Assert(PowerHelper.GetAvailableEnergyFromItem(battery) == 1, "Insufficient power is never overdrawn.");
    }

    private static void Put(GraphNodeStorage target, GameItem item)
    {
        Assert(GraphUtils.TryAcceptAll(target, item) == 0, "Test item did not fit its target inventory.");
    }

    private sealed class Rig
    {
        internal Rig()
        {
            Machine = DirectoryMaster.Item(DataCollectorState.CollectorId);
            Row = Machine.contentWindow.childElement;
            Battery = (GameSlotInventory)Row.GetStorage(DataCollectorState.BatteryIndex);
            Network = (GameSlotInventory)Row.GetStorage(DataCollectorState.NetworkIndex);
            Containers = (GameGridInventory)Row.GetStorage(DataCollectorState.ContainerIndex);
        }

        internal GameItem Machine { get; }
        internal MachineryPanelRow Row { get; }
        internal GameSlotInventory Battery { get; }
        internal GameSlotInventory Network { get; }
        internal GameGridInventory Containers { get; }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
