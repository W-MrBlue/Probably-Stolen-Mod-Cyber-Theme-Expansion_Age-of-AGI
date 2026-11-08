using System;
using System.Collections.Generic;
using AgeOfAgi.Items.Catalogs;
using UnityEngine;

namespace AgeOfAgi.Items.Logic.DiskWriter
{
    internal sealed class DiskWriterSelector
    {
        private readonly GameItem machine;
        private readonly DiskWriterCatalog catalog;
        private readonly GameSlotInventory chip;
        private readonly List<MachineryButton> choices = new List<MachineryButton>();
        private PixelWindow popup;
        private GameItem sourceAtOpen;
        private string displayedResult;
        private bool disposed;
        internal readonly MachineryButton ChooseButton;
        internal readonly DiskWriterComponentPreview Preview;
        internal readonly MachineryStack Content;
        internal Func<bool> CanChoose;
        internal Action SelectionChanged;

        internal DiskWriterSelector(GameItem machine, DiskWriterCatalog catalog, GameSlotInventory chip)
        {
            this.machine = machine;
            this.catalog = catalog;
            this.chip = chip;
            Preview = new DiskWriterComponentPreview();
            ChooseButton = new MachineryButton(Text("disk_writer_choose"));
            ChooseButton.SetCallback(Open);
            Content = MachineryStack.Vertical(Preview, ChooseButton);
            Preview.handler.gameObject.AddComponent<DiskWriterSelectorLifetime>().Selector = this;
            if (machine is GameItemElement element)
                element.handler.gameObject.AddComponent<DiskWriterSelectorLifetime>().Selector = this;
        }

        internal void Refresh(string resultId, bool hasChip)
        {
            if (disposed) return;
            if (sourceAtOpen != null && (!hasChip || !ReferenceEquals(chip.childItem, sourceAtOpen))) Close();
            if (hasChip) ChooseButton.Enable();
            else ChooseButton.Disable();
            ChooseButton.SetDescription(!hasChip ? Text("disk_writer_need_chip") :
                resultId == null ? Text("disk_writer_unselected") : null);
            string caption = Text(resultId == null ? "disk_writer_choose" : "disk_writer_change");
            if (ChooseButton.text != caption) ChooseButton.SetText(caption);
            if (displayedResult == resultId) return;
            Sprite sprite = null;
            if (resultId != null) sprite = ReadSprite(resultId, out _);
            displayedResult = resultId;
            Preview.SetSprite(resultId, sprite);
        }

        private Sprite ReadSprite(string itemId, out string name)
        {
            GameItem item = DirectoryMaster.Item(itemId);
            try
            {
                name = item.GetDisplayName();
                Sprite sprite = CustomUIManager.Instance.GetModSprite(ModConstants.ModId, item.spritePath);
                if (sprite == null) throw new InvalidOperationException("Disk writer component sprite is missing: " + itemId);
                return sprite;
            }
            finally { item.Destroy(); }
        }

        private void Open()
        {
            GameItem source = chip.childItem;
            if (disposed || CanChoose?.Invoke() == false || source == null || !GeneralHelper.IsItemOwned(source) ||
                !DiskWriterCatalog.IsChip(source.identifier)) return;
            try
            {
                if (popup == null)
                {
                    var rows = new List<PixelElement>();
                    foreach (var definition in catalog.Recipes)
                    {
                        string recipeId = definition.RecipeId;
                        var button = new MachineryButton();
                        button.SetCallback(() => Select(recipeId));
                        choices.Add(button);
                        rows.Add(button);
                    }
                    var cancel = new MachineryButton(Text("disk_writer_cancel"));
                    cancel.SetCallback(Close);
                    rows.Add(cancel);
                    popup = global::MachineHelper.CreateMachineryWindow(true,
                        global::MachineHelper.Panel(null, MachineryStack.Vertical(rows.ToArray()), "choices"));
                    popup.closeCallback += Close;
                }
                for (int index = 0; index < catalog.Recipes.Count; index++)
                {
                    catalog.TryGetResult(source.identifier, catalog.Recipes[index].RecipeId, out string resultId);
                    Sprite sprite = ReadSprite(resultId, out string name);
                    choices[index].SetIcon(sprite).SetSize(60, (int)sprite.rect.height + 4).SetDescription(name);
                }
                sourceAtOpen = source;
                popup.Validate();
                // Native Show accepts world UI coordinates and clamps to the screen.
                // Start beside the button rather than at the centre of the screen.
                popup.Show(ChooseButton.rectTransform.TransformPoint(new Vector3(ChooseButton.widthPixels + 6, 0, 0)));
            }
            catch
            {
                Close();
                throw;
            }
        }

        private void Select(string recipeId)
        {
            GameItem source = sourceAtOpen;
            if (disposed || popup == null || !popup.IsVisible() || CanChoose?.Invoke() == false || source == null ||
                !ReferenceEquals(chip.childItem, source) || !GeneralHelper.IsItemOwned(source) ||
                !catalog.TryGetResult(source.identifier, recipeId, out _))
            {
                Close();
                return;
            }
            Close();
            machine.ModifyTag(DiskWriterState.SelectedRecipeTag, state => state.Enable().SetString(recipeId));
            SelectionChanged?.Invoke();
        }

        internal void Close()
        {
            sourceAtOpen = null;
            if (popup != null && popup.handler != null) popup.Hide();
        }

        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Close();
            if (popup == null) return;
            GraphHandler.windowGraphNodeRoots?.Remove(popup);
            if (popup.handler != null) UnityEngine.Object.Destroy(popup.handler.gameObject);
            popup = null;
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }

    internal sealed class DiskWriterSelectorLifetime : MonoBehaviour
    {
        internal DiskWriterSelector Selector;
        private void OnDisable() => Selector?.Close();
        private void OnDestroy() => Selector?.Dispose();
    }
}
