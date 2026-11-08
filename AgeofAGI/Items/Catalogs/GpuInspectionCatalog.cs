using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Logic.Gpu;

namespace AgeOfAgi.Items.Catalogs
{
    internal sealed class GpuInspectionCatalog
    {
        internal const string CatalogPath = "Textures/gpu_details/pos.csv";
        private static readonly Random random = new Random();
        internal static readonly string[] Conditions = { "normal", "lowfrequency", "mismatch", "empty" };
        private readonly List<Slot> slots;

        private GpuInspectionCatalog(List<Slot> slots) { this.slots = slots; }

        internal static GpuInspectionCatalog Load(IReadOnlyList<ItemDefinition> items)
        {
            var catalog = Parse(CatalogCsvReader.ReadModFile(CatalogPath));
            catalog.ValidateMappings(items);
            LoadedMod mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == ModConstants.ModId);
            foreach (string sprite in catalog.slots.SelectMany(slot => slot.Sprites).Where(key => key.Length > 0).Distinct())
            {
                if (!ModHelper.ModFileExists(ModConstants.ModId, "Textures/gpu_details/" + sprite + ".png"))
                    throw CatalogCsvReader.Error(CatalogPath, "", "Missing component sprite: " + sprite);
                if (mod == null || !mod.Textures.TryGetValue(sprite, out var texture) || texture == null)
                    throw CatalogCsvReader.Error(CatalogPath, "", "Missing loaded texture: " + sprite + ". Build and restart to load GPU textures.");
            }
            foreach (ItemDefinition item in items.Where(item => item.Kind == "gpu"))
            {
                Slot[] applicable = catalog.ForGpu(item.ItemId).ToArray();
                var board = mod.Textures[applicable.Single(slot => slot.Id == "board").Sprites[0]];
                foreach (Slot slot in applicable)
                    foreach (string sprite in slot.Sprites.Where(key => key.Length > 0))
                    {
                        var texture = mod.Textures[sprite];
                        if ((long)slot.X + texture.width > board.width || (long)slot.Y + texture.height > board.height)
                            throw CatalogCsvReader.Error(CatalogPath, item.ItemId + "/" + slot.Id, "Component exceeds the PCB bounds: " + sprite);
                    }
            }
            return catalog;
        }

        internal static GpuInspectionCatalog Parse(string csv)
        {
            var slots = new List<Slot>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in CatalogCsvReader.ReadRows(csv, CatalogPath, new[]
            {
                "slot", "gpu_id", "x", "y", "normal_sprite", "fakecase_1", "fakecase_2",
                "normal_weight", "lowfrequency_weight", "mismatch_weight", "empty_weight"
            }))
            {
                var slot = new Slot
                {
                    Id = row.ReadString("slot"), GpuId = row.ReadString("gpu_id"),
                    X = row.ReadInt("x"), Y = row.ReadInt("y"),
                    Sprites = new[] { row.ReadString("normal_sprite"), row.ReadString("fakecase_1"), row.ReadString("fakecase_2"), "" },
                    Weights = new[] { row.ReadInt("normal_weight"), row.ReadInt("lowfrequency_weight"), row.ReadInt("mismatch_weight"), row.ReadInt("empty_weight") }
                };
                if (!IsKey(slot.Id) || slot.GpuId.Length == 0 || slot.X < 0 || slot.Y < 0)
                    throw row.Invalid("Expected a slot identifier, gpu_id and non-negative pixel coordinates.");
                long total = 0;
                for (int index = 0; index < slot.Weights.Length; index++)
                {
                    if (slot.Weights[index] < 0) throw row.Invalid("Component weights must be non-negative.");
                    if (index < 3 && ((slot.Weights[index] > 0 && slot.Sprites[index].Length == 0) ||
                        (slot.Sprites[index].Length > 0 && !IsKey(slot.Sprites[index]))))
                        throw row.Invalid("A weighted component requires a valid sprite key.");
                    total += slot.Weights[index];
                }
                if (total <= 0 || total > int.MaxValue) throw row.Invalid("Total component weight must be a positive Int32.");
                slot.TotalWeight = (int)total;
                if (!keys.Add(slot.GpuId + ":" + slot.Id)) throw row.Invalid("Duplicate GPU slot: " + slot.Id);
                slots.Add(slot);
            }
            return new GpuInspectionCatalog(slots);
        }

        internal void ValidateMappings(IReadOnlyList<ItemDefinition> items)
        {
            var ids = new HashSet<string>(items.Where(item => item.Kind == "gpu").Select(item => item.ItemId));
            if (slots.Any(slot => slot.GpuId != "*" && !ids.Contains(slot.GpuId)))
                throw CatalogCsvReader.Error(CatalogPath, "", "gpu_id must reference a configured GPU or *.");
            foreach (string id in ids)
            {
                Slot[] applicable = ForGpu(id).ToArray();
                if (applicable.Select(slot => slot.Id).Distinct().Count() != applicable.Length)
                    throw CatalogCsvReader.Error(CatalogPath, id, "Duplicate slot after applying shared entries.");
                Slot board = applicable.SingleOrDefault(slot => slot.Id == "board");
                if (board == null || board.X != 0 || board.Y != 0 || board.Weights[0] == 0 ||
                    board.Weights.Skip(1).Any(weight => weight != 0))
                    throw CatalogCsvReader.Error(CatalogPath, id + "/board", "A fixed board at (0,0) is required; only normal_weight may be positive.");
                Slot core = applicable.SingleOrDefault(slot => slot.Id == "core");
                if (core == null || core.Sprites[0].Length == 0 || core.Weights[3] != 0)
                    throw CatalogCsvReader.Error(CatalogPath, id + "/core", "A core with normal_sprite is required; replacement cores are allowed, but empty_weight must be zero.");
            }
        }

        internal GpuInspectionState Roll(string gpuId, Func<int, int> next = null)
        {
            next ??= maximum => random.Next(maximum);
            var state = new GpuInspectionState();
            // Draw the board first, then the model's core, then the independently rolled slots.
            foreach (Slot slot in ForGpu(gpuId).OrderBy(slot => slot.Id == "board" ? 0 : slot.Id == "core" ? 1 : 2))
            {
                int roll = next(slot.TotalWeight);
                if (roll < 0 || roll >= slot.TotalWeight) throw new ArgumentOutOfRangeException(nameof(next));
                int selected = 0;
                while (roll >= slot.Weights[selected]) roll -= slot.Weights[selected++];
                state.Parts.Add(new GpuInspectionPart { Slot = slot.Id, Sprite = slot.Sprites[selected], Condition = Conditions[selected], X = slot.X, Y = slot.Y });
            }
            return state;
        }

        private IEnumerable<Slot> ForGpu(string id) => slots.Where(slot => slot.GpuId == "*" || slot.GpuId == id);

        internal GpuInspectionState CreateStandardAssembly(string gpuId)
        {
            var state = new GpuInspectionState();
            // Standard cards use fixed normal parts, independent of spawn weights.
            // Also used to repair pre-inspection cards that had no randomized defects.
            foreach (Slot slot in ForGpu(gpuId).OrderBy(slot => slot.Id == "board" ? 0 : slot.Id == "core" ? 1 : 2))
                state.Parts.Add(new GpuInspectionPart
                {
                    Slot = slot.Id, Sprite = slot.Sprites[0], X = slot.X, Y = slot.Y,
                    Condition = slot.Sprites[0].Length == 0 ? "empty" : "normal"
                });
            return state;
        }

        internal void ValidateCores(GpuCatalog cores)
        {
            foreach (Slot slot in slots.Where(slot => slot.Id == "core"))
                foreach (string sprite in slot.Sprites.Where(key => key.Length > 0).Distinct())
                    if (!cores.TryGetByCore(sprite, out _))
                        throw CatalogCsvReader.Error(CatalogPath, slot.GpuId + "/core",
                            "Core sprite must have a coreSprite definition in " + GpuCatalog.CatalogPath + ": " + sprite);
        }

        internal string ResolveCondition(string gpuId, GpuInspectionPart part)
        {
            if (part.Sprite.Length == 0) return "empty";
            Slot slot = ForGpu(gpuId).FirstOrDefault(candidate => candidate.Id == part.Slot);
            int index = slot == null ? -1 : Array.IndexOf(slot.Sprites, part.Sprite);
            if (index >= 0) return Conditions[index];
            // Original saved assemblies had sprite identities but no semantic condition.
            if (part.Sprite == "gpu_mem_normal_rec" || part.Sprite == "gpu_mem_normal_square") return "normal";
            if (part.Sprite == "gpu_mem_lowfrequency_rec" || part.Sprite == "gpu_mem_lowfrequency_square") return "lowfrequency";
            if (part.Sprite == "gpu_mem_mismatch_rec" || part.Sprite == "gpu_mem_mismatch_square") return "mismatch";
            return part.Slot.StartsWith("memory_", StringComparison.Ordinal) ? null : "normal";
        }

        internal static bool IsKey(string key) => !string.IsNullOrEmpty(key) &&
            key.All(character => character >= 'a' && character <= 'z' || character >= '0' && character <= '9' || character == '_');

        private sealed class Slot
        {
            internal string Id, GpuId;
            internal int X, Y, TotalWeight;
            internal string[] Sprites;
            internal int[] Weights;
        }
    }
}
