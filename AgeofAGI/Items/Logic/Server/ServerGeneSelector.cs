using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgeOfAgi.Items.Logic.Server
{
    internal sealed class ServerGeneSelector
    {
        internal const string TraitTag = "AGE_OF_AGI_SERVER_SELECTED_GENE";
        internal const string SeedTag = "AGE_OF_AGI_SERVER_SELECTED_GENE_SEED";
        private readonly GameItem machine;
        private readonly Func<GameItem> getSeed;
        private readonly Func<bool> canChoose;
        private readonly MachineryText selected = new MachineryText(uppercase: false);
        private readonly MachineryButton choose;
        private PixelWindow popup;
        private GameItem sourceAtOpen;
        private bool enabled;
        private bool disposed;
        private string displayedText;
        internal readonly MachineryStack Content;
        internal Action SelectionChanged;

        internal ServerGeneSelector(GameItem machine, Func<GameItem> getSeed, Func<bool> canChoose)
        {
            this.machine = machine;
            this.getSeed = getSeed;
            this.canChoose = canChoose;
            choose = new MachineryButton(Text("server_choose_gene_button"));
            choose.SetCallback(Open);
            Content = MachineryStack.Vertical(selected, choose);
            selected.handler.gameObject.AddComponent<ServerGeneSelectorLifetime>().Selector = this;
            if (machine is GameItemElement element)
                element.handler.gameObject.AddComponent<ServerGeneSelectorLifetime>().Selector = this;
        }

        internal static PlantTrait SelectedTrait(GameItem machine, GameItem seed)
        {
            if (!ServerProduction.IsSeed(seed) || machine.GetTagReadonly(SeedTag).GetInt() != seed.GetUniqueID()) return null;
            string id = machine.GetTagReadonly(TraitTag).GetString();
            return PlantGeneticsHelper.GetExtractableTraits(seed).FirstOrDefault(trait => trait.id == id);
        }

        internal void Refresh(bool biological)
        {
            if (disposed) return;
            GameItem seed = getSeed();
            enabled = biological && ServerProduction.IsSeed(seed);
            if (!enabled || !canChoose() || (sourceAtOpen != null && !ReferenceEquals(seed, sourceAtOpen))) Close();
            PlantTrait trait = SelectedTrait(machine, seed);
            string text = trait?.displayName ?? Text("server_gene_unselected");
            if (displayedText != text) { displayedText = text; selected.SetText(text); }
            string caption = Text(trait == null ? "server_choose_gene_button" : "server_change_gene_button");
            if (choose.text != caption) choose.SetText(caption);
            if (enabled && canChoose()) choose.Enable();
            else choose.Disable();
        }

        private void Open()
        {
            GameItem seed = getSeed();
            if (disposed || !enabled || !canChoose() || !ServerProduction.IsSeed(seed)) return;
            DestroyPopup();
            var rows = new List<PixelElement>();
            foreach (PlantTrait trait in PlantGeneticsHelper.GetExtractableTraits(seed))
            {
                string id = trait.id;
                var button = new MachineryButton(trait.displayName);
                button.SetDescription(trait.description);
                button.SetCallback(() => Select(id));
                rows.Add(button);
            }
            if (rows.Count == 0) rows.Add(new MachineryText(Text("server_gene_none")));
            var cancel = new MachineryButton(Text("disk_writer_cancel"));
            cancel.SetCallback(Close);
            rows.Add(cancel);
            popup = global::MachineHelper.CreateMachineryWindow(true,
                global::MachineHelper.Panel(Text("server_gene_panel"), MachineryStack.Vertical(rows.ToArray()), "genes"));
            popup.closeCallback += Close;
            sourceAtOpen = seed;
            popup.Show(choose.rectTransform.TransformPoint(new Vector3(choose.widthPixels + 6, 0, 0)));
        }

        private void Select(string id)
        {
            GameItem seed = sourceAtOpen;
            if (disposed || popup == null || !popup.IsVisible() || !canChoose() || !ReferenceEquals(seed, getSeed()) ||
                !ServerProduction.IsSeed(seed) || !PlantGeneticsHelper.GetExtractableTraits(seed).Any(trait => trait.id == id))
            { Close(); return; }
            Close();
            machine.ModifyTag(SeedTag, state => state.Enable().SetInt(seed.GetUniqueID()));
            machine.ModifyTag(TraitTag, state => state.Enable().SetString(id));
            SelectionChanged?.Invoke();
        }

        internal void Close()
        {
            sourceAtOpen = null;
            if (popup != null && popup.handler != null) popup.Hide();
        }

        private void DestroyPopup()
        {
            Close();
            if (popup == null) return;
            GraphHandler.windowGraphNodeRoots?.Remove(popup);
            if (popup.handler != null) UnityEngine.Object.Destroy(popup.handler.gameObject);
            popup = null;
        }

        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DestroyPopup();
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }

    internal sealed class ServerGeneSelectorLifetime : MonoBehaviour
    {
        internal ServerGeneSelector Selector;
        private void OnDisable() => Selector?.Close();
        private void OnDestroy() => Selector?.Dispose();
    }
}
