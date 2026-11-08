using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Logic.Gpu;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

internal static class GpuInspectionChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        LoadedMod loaded = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(loaded.Manifest);
        registration.Register(new ModItemDirectory(), definitions);
        CheckRolls(definitions);
        GpuPerformanceChecks.Run(definitions);
        var ui = new InspectionUIManager();
        ((RectTransform)ui.fuelUISmall.transform).sizeDelta = new Vector2(80, 160);
        ((RectTransform)ui.fuelUILarge.transform).sizeDelta = new Vector2(320, 640);
        Image nativeSmall = ui.fuelUISmall.gameObject.AddComponent<Image>();
        Image nativeLarge = ui.fuelUILarge.gameObject.AddComponent<Image>();
        nativeSmall.enabled = true;
        nativeLarge.enabled = false;
        try
        {
            foreach (ItemDefinition definition in definitions.Where(item => item.Kind == "gpu"))
            {
                GameItem gpu = DirectoryMaster.Item(definition.ItemId);
                string itemSprite = gpu.SpriteKey;
                long price = gpu.unitValue;
                string saved = gpu.GetTagReadonly(GpuInspectionState.StateTag).GetString();
                GpuInspectionState state = GpuInspectionState.Read(gpu);
                Assert(state != null, "Every new GPU must freeze its own component rolls.");
                Assert(gpu.IsTag("INSPECTABLE_TAG"), "Every GPU must accept the native magnifier.");
                gpu.DisableTag("INSPECTABLE_TAG");
                GpuTestState.SetReal(gpu, compute: 777);
                saved = gpu.GetTagReadonly(GpuInspectionState.StateTag).GetString();
                gpu.onLoaded(gpu);
                Assert(gpu.IsTag("INSPECTABLE_TAG") && GpuRuntime.GetComputePower(gpu) == 777 && gpu.unitValue == price && gpu.SpriteKey == itemSprite &&
                    gpu.GetTagReadonly(GpuInspectionState.StateTag).GetString() == saved,
                    "Old GPUs regain inspection without resetting saved hardware, price or item sprite.");
                ui.OnInspectItem(gpu);
                Image small = Board(ui.fuelUISmall);
                Image large = Board(ui.fuelUILarge);
                var smallLayers = Layers(ui.fuelUISmall);
                var largeLayers = Layers(ui.fuelUILarge);
                Assert(ui.fuelUISmall.gameObject.activeSelf && ui.fuelUILarge.gameObject.activeSelf && small.gameObject.activeSelf && large.gameObject.activeSelf &&
                    !nativeSmall.enabled && !nativeLarge.enabled && ReferenceEquals(small.sprite.texture, loaded.Textures["gpu_empty_board"]) && ReferenceEquals(large.sprite, small.sprite) &&
                    small.preserveAspect && large.maskable && !small.raycastTarget && !large.raycastTarget,
                    "Show the empty PCB in both native hosts with masking and no input interception.");
                GpuInspectionPart[] selected = state.Parts.Skip(1).Where(part => part.Sprite.Length > 0).ToArray();
                Assert(smallLayers.Length == selected.Length && largeLayers.Length == selected.Length,
                    "Only the frozen, nonempty slots should be drawn above the PCB.");
                for (int index = 0; index < selected.Length; index++)
                {
                    Image component = smallLayers[index];
                    var rect = (RectTransform)component.transform;
                    var largeRect = (RectTransform)largeLayers[index].transform;
                    float scale = 160f / 352;
                    Assert(ReferenceEquals(component.sprite.texture, loaded.Textures[selected[index].Sprite]) &&
                        Math.Abs(rect.localPosition.x - (selected[index].X + component.sprite.rect.width / 2 - 176) * scale) < 0.01 &&
                        Math.Abs(rect.localPosition.y - (88.5f - selected[index].Y - component.sprite.rect.height / 2) * scale) < 0.01 &&
                        Math.Abs(largeRect.localPosition.x - rect.localPosition.x * 4) < 0.01 &&
                        Math.Abs(largeRect.sizeDelta.y - rect.sizeDelta.y * 4) < 0.01 && component.maskable && !component.raycastTarget,
                        "Convert top-left pixels to matching small/large native lens coordinates.");
                }
                ui.OnInspectItem(gpu);
                Assert(ReferenceEquals(small, Board(ui.fuelUISmall)) && saved == gpu.GetTagReadonly(GpuInspectionState.StateTag).GetString(),
                    "Repeated inspection must reuse the view and preserve the saved roll.");
                Assert(Math.Abs(((RectTransform)small.transform).sizeDelta.x - 160) < 0.01 && Math.Abs(((RectTransform)large.transform).sizeDelta.x - 640) < 0.01,
                    "Preserve the small/large coordinate ratio while fitting the GPU's long edge.");
                ui.OnInspectItem(new GameItem { Id = "native.plasma" });
                Assert(nativeSmall.enabled && !nativeLarge.enabled && !small.gameObject.activeSelf && !large.gameObject.activeSelf &&
                    ui.fuelUISmall.transform.Children.Count == 1 && ui.fuelUILarge.transform.Children.Count == 1,
                    "Restore native enable states on switch and reuse the same view nodes.");
            }
            GameItem sample = DirectoryMaster.Item("age_of_agi.gpu0");
            // Simulate a pre-feature save: native decoding replaces factory-created Tags before onLoaded.
            sample.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(""));
            sample.DisableTag(GpuRuntime.ComponentStatsTag);
            sample.onLoaded(sample);
            Assert(GpuInspectionState.Read(sample) != null, "Older GPUs receive their first assembly on load.");
            string oldSaved = sample.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            sample.onLoaded(sample);
            Assert(oldSaved == sample.GetTagReadonly(GpuInspectionState.StateTag).GetString(), "Old saves must be repaired only once.");
            var empty = GpuInspectionCatalog.Load(definitions).Roll(sample.identifier, maximum => maximum - 1);
            foreach (var part in empty.Parts.Skip(2)) { part.Sprite = ""; part.Condition = "empty"; }
            sample.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString(JsonConvert.SerializeObject(empty)));
            ui.OnInspectItem(sample);
            Assert(Layers(ui.fuelUISmall).Length == 1, "Empty rolls must hide reused component views, leaving the core.");
            Sprite ownedSprite = Board(ui.fuelUISmall).sprite;
            sample.ModifyTag(GpuInspectionState.StateTag, tag => tag.SetString("corrupt"));
            ui.OnInspectItem(sample);
            Assert(ReferenceEquals(Board(ui.fuelUISmall).sprite, (Sprite)loaded.Sprites[GpuInspectionRenderer.SpriteKey]) &&
                sample.GetTagReadonly(GpuInspectionState.StateTag).GetString() == "corrupt" && Layers(ui.fuelUISmall).Length == 0,
                "Corrupt state should safely fall back without rerolling or replacing saved data.");
            loaded.IsEnabled = false; ui.OnInspectItem(sample);
            Assert(!ui.fuelUISmall.gameObject.activeSelf && nativeSmall.enabled, "Disabled mods must not show GPU art.");
            loaded.IsEnabled = true; ui.OnInspectItem(sample);
            var nodes = ui.fuelUISmall.transform.Children.Concat(ui.fuelUILarge.transform.Children).Select(child => child.gameObject).ToArray();
            GpuInspectionHelper.Shutdown();
            Assert(ui.Closed && nodes.All(node => node.Destroyed) && nativeSmall.enabled && !nativeLarge.enabled && ownedSprite.Destroyed &&
                !loaded.Textures["gpu_empty_board"].Destroyed,
                "Shutdown closes GPU inspection, releases nodes and restores native graphics.");
        }
        finally { GpuInspectionHelper.Shutdown(); registration.Unregister(); }
        Console.WriteLine("PASS: configurable GPU component rolls, frozen save state, layered magnifier views, pixel positions, restoration and cleanup.");
    }

    private static Image Board(MonoBehaviour host) => host.GetComponentsInChildren<Image>(true)
        .Single(image => image.gameObject.name == "AgeOfAgiGpuInspection");

    private static Image[] Layers(MonoBehaviour host) => host.GetComponentsInChildren<Image>(true)
        .Where(image => image.gameObject.name == "AgeOfAgiGpuComponent" && image.gameObject.activeSelf).ToArray();

    private static void CheckRolls(IReadOnlyList<ItemDefinition> definitions)
    {
        string csv = ModHelper.ReadModFileText("age_of_agi", GpuInspectionCatalog.CatalogPath);
        var rollTable = CsvFixture.Parse(csv);
        foreach (var row in rollTable.Rows.Where(row => row["slot"] != "board" && row["slot"] != "core"))
        {
            row["normal_weight"] = row["slot"].StartsWith("memory_") ? "7" : "9";
            row["lowfrequency_weight"] = row["slot"].StartsWith("memory_") ? "1" : "0";
            row["mismatch_weight"] = row["slot"].StartsWith("memory_") ? "1" : "0";
            row["empty_weight"] = "1";
        }
        csv = rollTable.ToString();
        var catalog = GpuInspectionCatalog.Parse(csv);
        catalog.ValidateMappings(definitions);
        foreach (var sample in new[] { (Roll: 0, Suffix: "normal"), (Roll: 7, Suffix: "lowfrequency"),
            (Roll: 8, Suffix: "mismatch"), (Roll: 9, Suffix: "") })
        {
            var state = catalog.Roll("age_of_agi.gpu0", maximum => Math.Min(maximum - 1, sample.Roll));
            foreach (var part in state.Parts.Where(part => part.Slot.StartsWith("memory_")))
                Assert(sample.Suffix.Length == 0 ? part.Sprite == "" : part.Sprite.Contains("_" + sample.Suffix + "_"),
                    "Memory weight boundaries must choose normal / low frequency / mismatch / empty.");
            Assert(state.Parts[0].Sprite == "gpu_empty_board" && state.Parts[1].Sprite == "gpu_core_nut",
                "A PCB and corresponding core are mandatory, even when all optional slots are empty.");
        }
        foreach (string id in new[] { "gpu0", "gpu1", "gpu2", "gpu3" })
            Assert(catalog.Roll("age_of_agi." + id).Parts[1].Sprite == "gpu_core_" +
                new Dictionary<string, string> { ["gpu0"] = "nut", ["gpu1"] = "normal", ["gpu2"] = "vector", ["gpu3"] = "matrix" }[id],
                "Each GPU must bind its own core asset.");
        var tuned = GpuInspectionCatalog.Parse(csv.Replace(",7,1,1,1", ",0,0,0,1"));
        Assert(tuned.Roll("age_of_agi.gpu0", _ => 0).Parts.Where(part => part.Slot.StartsWith("memory_")).All(part => part.Sprite == ""),
            "Changing the CSV weights must control component outcomes.");
        foreach (string invalid in new[]
        {
            csv.Replace(",7,1,1,1", ",-1,1,1,1"), csv.Replace(",7,1,1,1", ",0,0,0,0"),
            csv.Replace(",109,24,", ",-1,24,"), csv.Replace("gpu_mem_normal_rec,", "../outside,"),
            csv.Replace("core,age_of_agi.gpu0", "core,unknown.gpu0"),
            csv.Replace("core,age_of_agi.gpu0", "missing_core,age_of_agi.gpu0"),
            csv.Replace("board,*,0,0", "board,*,1,0"),
            csv + "memory_rec_1,*,109,24,gpu_mem_normal_rec,,,1,0,0,0\n"
        })
        {
            bool rejected = false;
            try { GpuInspectionCatalog.Parse(invalid).ValidateMappings(definitions); }
            catch (InvalidOperationException exception) { rejected = exception.Message.Contains(GpuInspectionCatalog.CatalogPath); }
            Assert(rejected, "Invalid positions, weights, mappings and duplicate slots must report the configuration path.");
        }
        try
        {
            foreach (string invalid in new[] { csv.Replace(",109,24,", ",349,24,"),
                csv.Replace("gpu_mem_normal_rec,", "missing_gpu_component,") })
            {
                ModHelper.FileOverrides[GpuInspectionCatalog.CatalogPath] = invalid;
                bool rejected = false;
                try { GpuInspectionCatalog.Load(definitions); }
                catch (InvalidOperationException exception) { rejected = exception.Message.Contains(GpuInspectionCatalog.CatalogPath); }
                Assert(rejected, "Reject out-of-bounds positions and missing component assets during registration.");
            }
        }
        finally { ModHelper.FileOverrides.Remove(GpuInspectionCatalog.CatalogPath); }
        CheckContactFakeCases(csv);
        CheckCoreVariants(csv, definitions);
    }

    private static void CheckCoreVariants(string csv, IReadOnlyList<ItemDefinition> definitions)
    {
        var table = CsvFixture.Parse(csv);
        var row = table.Rows.Single(entry => entry["slot"] == "core" && entry["gpu_id"] == "age_of_agi.gpu0");
        row["fakecase_1"] = "gpu_core_vector";
        row["fakecase_2"] = "gpu_core_matrix";
        row["normal_weight"] = "2";
        row["lowfrequency_weight"] = "3";
        row["mismatch_weight"] = "5";
        var cores = GpuCatalog.Load();
        var catalog = GpuInspectionCatalog.Parse(table.ToString());
        catalog.ValidateMappings(definitions);
        catalog.ValidateCores(cores);
        foreach (var sample in new[] { (Roll: 0, Sprite: "gpu_core_nut"), (Roll: 1, Sprite: "gpu_core_nut"),
            (Roll: 2, Sprite: "gpu_core_vector"), (Roll: 4, Sprite: "gpu_core_vector"),
            (Roll: 5, Sprite: "gpu_core_matrix"), (Roll: 9, Sprite: "gpu_core_matrix") })
        {
            int draw = 0;
            var state = catalog.Roll("age_of_agi.gpu0", maximum => draw++ == 1 ? sample.Roll : 0);
            Assert(state.Parts[1].Sprite == sample.Sprite, "Core variants must follow the configured weight boundaries.");
            var gpu = new GameItem { Id = "age_of_agi.gpu0" };
            gpu.ModifyTag(GpuInspectionState.StateTag, tag => tag.Enable().SetString(JsonConvert.SerializeObject(state)));
            GpuHelper.InitGpu(gpu, cores.Get(gpu.identifier), _ => { }, catalog, cores);
            var core = cores.GetByCore(sample.Sprite);
            var frozen = GpuInspectionState.Read(gpu);
            Assert(frozen.CoreComputePower == core.ComputePower && frozen.CorePowerPerCycle == core.PowerPerCycle &&
                GpuRuntime.GetComputePower(gpu) == core.ComputePower && GpuRuntime.GetPowerPerCycle(gpu) == core.PowerPerCycle,
                "Actual rolled core identity must supply compute and power instead of the item ID or variant condition.");
            string saved = gpu.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            gpu.onLoaded(gpu);
            Assert(saved == gpu.GetTagReadonly(GpuInspectionState.StateTag).GetString(),
                "Loading a current-format core variant must preserve its assembly and frozen performance.");
        }
        Assert(catalog.CreateStandardAssembly("age_of_agi.gpu0").Parts[1].Sprite == row["normal_sprite"],
            "Standard assemblies must keep the normal core regardless of replacement weights.");
        row["normal_weight"] = "0";
        GpuInspectionCatalog.Parse(table.ToString()).ValidateMappings(definitions);
        row["fakecase_2"] = "fixture_unknown_core";
        bool rejected = false;
        try { GpuInspectionCatalog.Parse(table.ToString()).ValidateCores(cores); }
        catch (InvalidOperationException exception)
        {
            rejected = exception.Message.Contains("age_of_agi.gpu0/core") && exception.Message.Contains("fixture_unknown_core");
        }
        Assert(rejected, "Every replacement core must have an attribute definition, with the offending slot and sprite reported.");
        row["fakecase_2"] = "gpu_core_matrix";
        foreach (string invalidField in new[] { "empty_weight", "normal_sprite" })
        {
            string previous = row[invalidField];
            row[invalidField] = invalidField == "empty_weight" ? "1" : "";
            rejected = false;
            try { GpuInspectionCatalog.Parse(table.ToString()).ValidateMappings(definitions); }
            catch (InvalidOperationException exception) { rejected = exception.Message.Contains("age_of_agi.gpu0/core"); }
            Assert(rejected, "Core variants must reject empty sockets and require a normal core for standard assemblies.");
            row[invalidField] = previous;
        }
    }

    private static void CheckContactFakeCases(string csv)
    {
        var table = CsvFixture.Parse(csv);
        foreach (var row in table.Rows.Where(row => row["slot"].StartsWith("goldfinger_")))
        {
            row["fakecase_1"] = "fixture_contact_fault_one";
            row["fakecase_2"] = "fixture_contact_fault_two";
            row["normal_weight"] = "2";
            row["lowfrequency_weight"] = "3";
            row["mismatch_weight"] = "5";
            row["empty_weight"] = "7";
        }
        var catalog = GpuInspectionCatalog.Parse(table.ToString());
        foreach (var sample in new[] { (Roll: 2, Sprite: "fixture_contact_fault_one", Condition: "lowfrequency"),
            (Roll: 5, Sprite: "fixture_contact_fault_two", Condition: "mismatch"), (Roll: 10, Sprite: "", Condition: "empty") })
        {
            var state = catalog.Roll("age_of_agi.gpu0", maximum => maximum == 17 ? sample.Roll : 0);
            foreach (var part in state.Parts.Where(part => part.Slot.StartsWith("goldfinger_")))
                Assert(part.Sprite == sample.Sprite && part.Condition == sample.Condition &&
                    catalog.ResolveCondition("age_of_agi.gpu0", part) == sample.Condition,
                    "Generic fake-case columns must select and resolve both contact variants and empty slots using their own weights.");
        }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
