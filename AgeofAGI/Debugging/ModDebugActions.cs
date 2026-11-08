using AgeOfAgi.Items.Logic.ComplexData;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.Compiler;
using System;
using System.Collections.Generic;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Npcs;

namespace AgeOfAgi.Debugging
{
    internal static class ModDebugActions
    {
        internal const string FastGrowerDnaActionName = "Spawn complex_data (Fast Grower)";

        internal static void Register(IReadOnlyList<ItemDefinition> definitions)
        {
            Unregister();
            ModDebugMenu.Characters.Add(
                "Spawn enterprise_computing_service", () => DataBuyers.QueueDebug(DataBuyers.EnterpriseId));
            foreach (string id in new[] { ElectronicsMarket.ScavengerBuyerId, ElectronicsMarket.RepairBuyerId,
                ElectronicsMarket.UpperBuyerId, ElectronicsMarket.SecondhandBuyerId,
                ElectronicsMarket.SellerId, ElectronicsMarket.AdvancedSellerId, ElectronicsSalvageSellers.ScavengerId,
                ElectronicsSalvageSellers.PettyThiefId, ElectronicsSalvageSellers.ThiefId,
                ElectronicsMarket.DishonestSellerId, ElectronicsSalvageSellers.DishonestScavengerId,
                ElectronicsMarket.GpuSupplierId })
                ModDebugMenu.Characters.Add(
                    id == ElectronicsMarket.DishonestSellerId ? "召唤不诚实电子卖家（队首）" :
                        "Spawn " + id.Substring(ModConstants.ModId.Length + 1), () => ElectronicsMarket.QueueDebug(id));
            ModDebugMenu.Characters.Add(
                NightShiftClient.DebugActionName, () => { NightShiftClient.QueueDebug(); });
            ModDebugMenu.Characters.Add(
                ElectronicsTrader.DebugActionName, () => { ElectronicsTrader.Supplier.QueueDebug(); });
            ModDebugMenu.Items.Add(
                NightShiftClient.ClaimActionName, NightShiftClient.ClaimGifts);
            ModDebugMenu.Stories.Add(
                QuorraStoryClient.DebugActionName, () => { QuorraStoryClient.QueueFirst(); });
            ModDebugMenu.Stories.Add(
                Items.Logic.StoryDocuments.QuorraDocuments.ClaimActionName, Items.Logic.StoryDocuments.QuorraDocuments.DeliverPending);
            foreach (ItemDefinition definition in definitions)
            {
                // Item IDs stay unique even when several variants share the same sprite.
                ModDebugMenu.Items.Add(
                    "Spawn " + definition.ItemId.Substring(ModConstants.ModId.Length + 1),
                    () => SpawnItem(definition.ItemId));
                if (definition.ItemId == ComplexDataState.ItemId)
                {
                    ModDebugMenu.Items.Add(
                        FastGrowerDnaActionName, SpawnFastGrowerDna);
                }
            }
            foreach (StoreEventBlueprint blueprint in StoreEventManager.normalEventBlueprints)
            {
                if (blueprint?.identifier == null || !blueprint.identifier.StartsWith(ModConstants.ModId + ".", StringComparison.Ordinal)) continue;
                ModDebugMenu.Events.Add("Spawn " + blueprint.identifier.Substring(ModConstants.ModId.Length + 1), () =>
                {
                    if (ElectronicsEvents.Ready && PlayerStore.HasInstance && StoreStation.Instance?.storeEventManager != null)
                        StoreStation.Instance.storeEventManager.ActivateEvent(blueprint.storeEventFunc(), debugLog: true);
                });
            }
            foreach (ModDebugMenu menu in ModDebugMenu.All)
                ModHelper.AddDebugFunction(ModConstants.ModId, ModHelper.DEBUG_CATEGORY_SPAWN, menu.Title, menu.Open);
        }

        internal static void Unregister()
        {
            ModHelper.RemoveDebugFunctions(ModConstants.ModId);
            foreach (ModDebugMenu menu in ModDebugMenu.All) menu.Clear();
        }

        private static void SpawnFastGrowerDna()
        {
            PlantTrait trait = PlantTraitRegistry.Get(PlantTraitList.FAST_GROWER)
                ?? throw new InvalidOperationException("The native Fast Grower plant trait is unavailable.");
            GameItem item = ModHelper.SpawnItem(ComplexDataState.ItemId);
            try
            {
                ComplexDataState.Write(item, trait);
                ModHelper.MoveItemToInventory(item);
            }
            catch
            {
                item.Destroy();
                throw;
            }
        }

        private static void SpawnItem(string itemId)
        {
            GameItem item = ModHelper.SpawnItem(itemId);
            if (itemId == AiModelState.UntrainedId)
            {
                AiModelState.Apply(item, new AiModelAttributes(
                    AiTaskTypes.Computing, 1000f, 2,
                    20f, 5f, 250));
            }
            else if (itemId == AiModelState.TrainedId)
            {
                AiModelState.ApplyTrained(item, AiTaskTypes.Computing, 2, 20, 10);
            }
            ModHelper.MoveItemToInventory(item);
        }
    }
}
