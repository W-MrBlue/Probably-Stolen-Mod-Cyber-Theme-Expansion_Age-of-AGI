using System;
using System.Collections.Generic;

namespace AgeOfAgi.Items.Logic.ElectronicStorage
{
    internal sealed class ElectronicStorageRuntime
    {
        internal const string DiskBoxId = "age_of_agi.disk_box";
        internal const string CodeContainerId = "age_of_agi.code_container";
        internal const string EmptyDiskId = "age_of_agi.disk_empty";
        private readonly GameInventory inventory;

        internal ElectronicStorageRuntime(GameItem item, int width, int height,
            List<string> allowedTags, List<string> allowedIds = null)
        {
            var window = DirectoryUtils.CreateInventoryWindow(width, height);
            inventory = window.Item2;
            item.SetContentWindow(window.Item1);
            // Native child-value aggregation already adds the actual disk/model values.
            // No lateUnitValue copy is needed, which would count the contents twice.
            ContainerHelper.InitContainerItem(inventory, item, allowedTags, allowedIds);
        }

        internal void FillDisks()
        {
            var created = new List<GameItem>();
            try
            {
                for (int index = 0; index < 16; index++)
                {
                    GameItem disk = DirectoryMaster.Item(EmptyDiskId);
                    created.Add(disk);
                    // Prepack unowned merchandise without relaxing the player insertion
                    // filter. Each blank disk occupies one 2x2 cell slot in the 8x8 box.
                    disk.SetShape(new GridShapeBuilder(disk.modifiedShape).SetTransform(index % 4 * 2, index / 4 * 2, false, 0));
                    if (!inventory.UncheckedAccept(disk))
                        throw new InvalidOperationException("Could not pack the blank disks.");
                }
            }
            catch
            {
                foreach (GameItem disk in created) disk.Destroy();
                throw;
            }
        }

        internal void OnLoaded(GameItem item)
        {
            item.EnableTag("IMPORTANT_TAG");
            item.EnableTag("CONTAINER_TAG");
            // Saved child nodes are restored by the native decoder; never refill here.
        }
    }
}
