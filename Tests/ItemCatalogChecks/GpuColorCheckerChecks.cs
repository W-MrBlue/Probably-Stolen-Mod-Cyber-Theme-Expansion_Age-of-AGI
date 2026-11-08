using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Npcs;
using UnityEngine;

internal static class GpuColorCheckerChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        var loaded = Program.PrepareLoadedMod(definitions);
        Texture2D source = loaded.Textures[GpuColorChecker.TextureKey];
        var pixels = Enumerable.Range(0, source.width * source.height)
            .Select(index => new Color32((byte)(index % 251), (byte)(index % 173), (byte)(index % 97), 255)).ToArray();
        source.SetPixels32(pixels);
        string iconKey = definitions.Single(item => item.ItemId == GpuColorChecker.ItemId).SpriteKey;
        Sprite originalIcon = loaded.Sprites[iconKey];
        var registration = new ItemRegistration(loaded.Manifest);
        registration.Register(new ModItemDirectory(), definitions);
        try
        {
            Sprite paper = loaded.PaperSprites["gpu_color_checker_reference"];
            Assert(ReferenceEquals(paper.texture, source) && paper.rect.width == source.width && paper.rect.height == source.height,
                "Reference paper must use the unmodified source image.");
            Assert(ReferenceEquals((Sprite)loaded.Sprites[iconKey], originalIcon) && source.GetPixels32().SequenceEqual(pixels),
                "The inventory thumbnail stays loader-owned and the separate detail image retains its exact pixels.");
            GameItem card = DirectoryMaster.Item(GpuColorChecker.ItemId);
            Assert(card.IsTag("DOCUMENT") && card.IsTag("IMPORTANT_TAG") && card.GetGameItemType().Contains("DOCUMENT") &&
                card.mayActivateSlotItemFunc(card, null, null) && card.canActivateSlotItemFunc(card, null, null),
                "Match native color reference activation and document classification.");
            card.SetValue(123);
            card.DisableTag("DOCUMENT"); card.onActivateSlotItemFunc = null;
            card.onLoaded(card);
            Assert(card.IsTag("DOCUMENT") && card.unitValue == 123 && card.onActivateSlotItemFunc != null,
                "Current-format loading restores interactions while preserving saved price.");
            PlayerStore.Instance = null; PlayerStore.ForbidLazyCreation = true;
            int reads = PlayerStore.InstanceReads, panels = PaperUI.Created.Count;
            card.onActivateSlotItemFunc(card, null, null);
            Assert(PlayerStore.InstanceReads == reads && PaperUI.Created.Count == panels, "No paper UI or lazy PlayerStore creation before a scene exists.");
            PlayerStore.ForbidLazyCreation = false; PlayerStore.Instance = new PlayerStore();
            card.onActivateSlotItemFunc(card, null, null); card.onActivateSlotItemFunc(card, null, null);
            var panel = PaperUI.Created.Last();
            Assert(PaperUI.Created.Count == panels + 1 && panel.Shows == 2 && panel.Visible &&
                panel.Background == "age_of_agi:gpu_color_checker_reference" && panel.Width == source.width * 4 && panel.Height == source.height * 4 &&
                card.unitCount == 1 && card.unitValue == 123,
                "Repeated activation reuses an independent native paper view without consuming or repricing the card.");
            panel.Close();
            card.onActivateSlotItemFunc(card, null, null);
            Assert(panel.Visible && panel.Data.chrome.Where(element => element.action == "close").All(element =>
                element.sprite == "age_of_agi:paper_buttons_close3" && element.hoverSprite == element.sprite),
                "The close icon and hover icon persist after reopening the paper view.");

            const string stockCsv = "section,itemIds,count,chance,dataAmount,minimumProductionSpeed,memoryGb,computeBonus,durability,trainingWork,trainingData\n" +
                "gifts,age_of_agi.disk_empty,1,100,-1,0,0,0,0,0,0\nstock,age_of_agi.gpu_color_checker,2,100,-1,0,0,0,0,0,0\n";
            var catalog = NightShiftCatalog.Parse(stockCsv, definitions, ElectronicsTrader.CatalogPath);
            Assert(catalog.Roll(7, 0).Count == 2 && catalog.Roll(7, 0).All(offer => offer.ItemId == GpuColorChecker.ItemId),
                "The tier-one supplier schema accepts color reference cards at independently configured quantities.");
            GpuColorCheckerHelper.Shutdown();
            Assert(panel.Closed && paper.Destroyed && !source.Destroyed && !originalIcon.Destroyed &&
                ReferenceEquals((Sprite)loaded.Sprites[iconKey], originalIcon),
                "Disable closes the view, releases the generated paper sprite and preserves the loader-owned thumbnail.");
            card.onActivateSlotItemFunc(card, null, null);
            Assert(PaperUI.Created.Count == panels + 1, "Old item delegates cannot reopen a disabled reference.");
            GpuColorCheckerHelper.PrepareAssets();
            GpuColorChecker.Open();
            Assert(PaperUI.Created.Count == panels + 2, "Re-enabling can prepare and open the reference again.");
        }
        finally
        {
            GpuColorCheckerHelper.Shutdown();
            registration.Unregister();
            PlayerStore.ForbidLazyCreation = false;
            PlayerStore.Instance = null;
        }
        Console.WriteLine("PASS: GPU color reference exact pixels, independent thumbnail, paper close icon, load binding, tier-one stock, cleanup and re-enable.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
