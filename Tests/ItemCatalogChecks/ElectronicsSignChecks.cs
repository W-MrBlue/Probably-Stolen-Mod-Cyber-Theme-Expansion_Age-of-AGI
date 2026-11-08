using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Npcs;

internal static class ElectronicsSignChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        var loaded = Program.PrepareLoadedMod(definitions);
        var mod = new AgeOfAgiMod(); mod.Init(loaded.Manifest); mod.OnEnable(); mod.OnEnable();
        ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
        PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry();
        try
        {
            var sign = DirectoryMaster.Item(ElectronicsSignHelper.ItemId);
            Assert(sign.shape.width == 2 && sign.shape.height == 2 &&
                Enumerable.Range(0, 4).All(index => sign.shape.GetLocal(index % 2, index / 2) == 1),
                "The exported alpha silhouette must occupy all four cells of a real 2x2 item.");
            Assert(sign.unitValue == 100 && sign.IsTag("IMPORTANT_TAG") && sign.GetGameItemType().Count == 0 &&
                sign.GetTagReadonly("SHOWCASE_SPECIAL_ITEM_TAG").GetString() == "electronics",
                "Match the native sign price, important tag, empty categories and showcase type.");
            sign.SetValue(777); sign.DisableTag("IMPORTANT_TAG"); sign.DisableTag("SHOWCASE_SPECIAL_ITEM_TAG");
            sign.onLoaded(sign);
            Assert(sign.unitValue == 777 && sign.IsTag("IMPORTANT_TAG") && sign.IsTag("SHOWCASE_SPECIAL_ITEM_TAG"),
                "Loading repairs sign behavior without resetting a saved price.");

            var predictions = new List<(string, int, string)> { ("native_supplier", 75, "sign_material") };
            ElectronicsSignPredictionPatch.Postfix(predictions);
            Assert(predictions.Count == 1, "No sign in the showcase means no electronic supplier prediction.");
            EmporiumEntry.Instance.invElement.UncheckedAccept(sign);
            ElectronicsSignPredictionPatch.Postfix(predictions);
            Assert(predictions.Count == 1, "A sign in ordinary storage has no showcase effect.");
            EmporiumEntry.Instance.invElement.Remove(sign);
            EmporiumEntry.Instance.showcaseElement.UncheckedAccept(sign);
            ElectronicsSignPredictionPatch.Postfix(predictions);
            Assert(predictions.Count == 2 && predictions[0].Item1 == "native_supplier" &&
                new[] { ElectronicsMarket.SellerId, ElectronicsMarket.DishonestSellerId, ElectronicsMarket.GpuSupplierId, ElectronicsSalvageSellers.DishonestScavengerId, ElectronicsSalvageSellers.ScavengerId }.Contains(predictions[1].Item1) &&
                predictions[1].Item2 == 75 && predictions[1].Item3 == ElectronicsSignHelper.ItemId,
                "Append the native-style 75% supplier prediction without changing native signs.");
            var again = new List<(string, int, string)>(); ElectronicsSignPredictionPatch.Postfix(again);
            Assert(again.Single() == predictions[1], "UI prediction and customer generation use the same frozen seller pick.");
            EmporiumEntry.Instance.showcaseElement.UncheckedAccept(DirectoryMaster.Item(ElectronicsSignHelper.ItemId));
            ElectronicsSignPredictionPatch.Postfix(predictions);
            Assert(predictions.Count == 2, "Multiple signs and repeated prediction callbacks cannot stack attraction.");
            PlayerStore.Instance.UnlockedServices.Add(NightShiftClient.UpgradeId);
            ElectronicsSignPredictionClearPatch.Postfix();
            var advanced = new List<(string, int, string)>(); ElectronicsSignPredictionPatch.Postfix(advanced);
            Assert(new[] { ElectronicsMarket.AdvancedSellerId, ElectronicsMarket.SellerId, ElectronicsMarket.DishonestSellerId, ElectronicsMarket.GpuSupplierId, ElectronicsSalvageSellers.DishonestScavengerId, ElectronicsSalvageSellers.ScavengerId }
                .Contains(advanced.Single().Item1), "Unlocking tier two keeps the lower-level supplier branch eligible for the advanced dealer.");

            ModHook.RaiseInventorStock(new List<GameItem> { sign });
            Assert(!EmporiumEntry.Instance.frontInvinvElement.childItems.Any(item => item.identifier == ElectronicsSignHelper.ItemId),
                "The inventor does not offer an already-owned sign.");
            ModHook.RaiseInventorStock(new List<GameItem>());
            var stock = EmporiumEntry.Instance.frontInvinvElement.childItems.Where(item => item.identifier == ElectronicsSignHelper.ItemId).ToList();
            Assert(stock.Count == 1 && stock.Single().identifier == ElectronicsSignHelper.ItemId && !GeneralHelper.IsItemOwned(stock.Single()),
                "An unowned sign is paid native inventor stock, without being granted as a free gift.");
            ModHook.RaiseInventorStock(new List<GameItem>());
            Assert(EmporiumEntry.Instance.frontInvinvElement.childItems.Count(item => item.identifier == ElectronicsSignHelper.ItemId) == 1,
                "Repeated inventory callbacks do not duplicate an unpurchased sign on the counter.");
            Assert(AgeOfAgi.Debugging.ModDebugMenu.Items.Actions.ContainsKey("Spawn sign_electronics"), "Expose the sign through the F9 item group.");

            mod.OnDisable(); EmporiumEntry.Instance.frontInvinvElement.Items.Clear();
            ModHook.RaiseInventorStock(new List<GameItem>());
            var disabled = new List<(string, int, string)>(); ElectronicsSignPredictionPatch.Postfix(disabled);
            Assert(EmporiumEntry.Instance.frontInvinvElement.childItems.Count == 0 && disabled.Count == 0,
                "Disable removes stock injection and sign predictions.");
            PlayerStore.Instance = null; PlayerStore.ForbidLazyCreation = true;
            int reads = PlayerStore.InstanceReads;
            ElectronicsSignAttraction.OfferSign(new List<GameItem>()); ElectronicsSignAttraction.AddPrediction(disabled);
            Assert(PlayerStore.InstanceReads == reads, "Main-menu and cleanup paths must not construct PlayerStore.");
        }
        finally
        {
            PlayerStore.ForbidLazyCreation = false; mod.OnDisable(); PlayerStore.Instance = null;
        }
        Console.WriteLine("PASS: 2x2 electronic neon sign, native important/showcase tags, price preservation, inventor stock, supplier prediction cache, unlock gate and cleanup.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
