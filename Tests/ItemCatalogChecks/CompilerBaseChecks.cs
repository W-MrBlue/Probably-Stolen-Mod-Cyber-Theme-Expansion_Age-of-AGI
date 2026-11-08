using AgeOfAgi.Items.Logic.BaseModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;

internal static partial class CompilerChecks
{
    private static void CheckBaseModelCatalog(IReadOnlyList<ItemDefinition> items)
    {
        BaseModelCatalog catalog = BaseModelCatalog.Load();
        Assert(catalog.Count == 4 && catalog.Get("age_of_agi.base_grid").GridWidth == 4 &&
            catalog.Get("age_of_agi.base_grid").GridHeight == 6 && catalog.Get("age_of_agi.base_neural").GridWidth == 3 &&
            catalog.Get("age_of_agi.base_neural").GridHeight == 12 && catalog.Get("age_of_agi.base_tsfm").GridWidth == 6 &&
            catalog.Get("age_of_agi.base_tsfm").GridHeight == 8 && catalog.Get("age_of_agi.base_jepa").GridWidth == 8 &&
            catalog.Get("age_of_agi.base_jepa").GridHeight == 12 &&
            catalog.Get("age_of_agi.base_grid").MemoryMultiplier == 1, "Base model grid sizes or memory multiplier differ from the fixture.");
        catalog.ValidateMappings(items);
        foreach (string invalid in new[] { "", "id", "id,id", "id\nage_of_agi.invalid", ValidBaseModels + " {}",
            ValidBaseModels.Replace("age_of_agi.", "other_mod."), ValidBaseModels.Replace("gridWidth", "gridWidht") })
        {
            ExpectFailure(() => BaseModelCatalog.Parse(invalid), BaseModelCatalog.CatalogPath);
        }
        foreach (string value in new[] { "0", "-1", "1.5", "\"4\"", "2147483648" })
        {
            ExpectFailure(() => BaseModelCatalog.Parse(CsvFixture.Cell(ValidBaseModels, "gridWidth", value)),
                BaseModelCatalog.CatalogPath);
        }
        foreach (string value in new[] { "0", "-1", "NaN", "Infinity", "\"2\"" })
        {
            ExpectFailure(() => BaseModelCatalog.Parse(CsvFixture.Cell(ValidBaseModels, "memoryMultiplier", value)),
                BaseModelCatalog.CatalogPath);
        }
        foreach (string field in new[] { "id", "gridWidth", "gridHeight", "memoryMultiplier" })
        {
            var root = CsvFixture.Parse(ValidBaseModels);
            root.RemoveColumn(field);
            ExpectFailure(() => BaseModelCatalog.Parse(root.ToString()), BaseModelCatalog.CatalogPath);
        }
        var duplicates = CsvFixture.Parse(ValidBaseModels);
        duplicates.DuplicateFirstRow();
        duplicates.Rows[1]["id"] = "age_of_agi.BASE_GRID";
        ExpectFailure(() => BaseModelCatalog.Parse(duplicates.ToString()), BaseModelCatalog.CatalogPath);
        ExpectFailure(() => BaseModelCatalog.Parse(ValidBaseModels.Replace("age_of_agi.base_grid", "age_of_agi.base_extra"))
            .ValidateMappings(items), BaseModelCatalog.CatalogPath);
        ExpectFailure(() => catalog.ValidateMappings(items.Where(item => item.ItemId != "age_of_agi.base_grid").ToArray()),
            BaseModelCatalog.CatalogPath);
    }

    private static void CheckBaseModelItems(ModItemDirectory directory)
    {
        foreach (var expected in new[]
        {
            (Id: "age_of_agi.base_grid", Width: 4, Height: 6), (Id: "age_of_agi.base_neural", Width: 3, Height: 12),
            (Id: "age_of_agi.base_tsfm", Width: 6, Height: 8), (Id: "age_of_agi.base_jepa", Width: 8, Height: 12)
        })
        {
            GameItem item = directory.Factories[expected.Id]();
            Assert(BaseModelState.IsBaseModel(item) && BaseModelState.GetGridWidth(item) == expected.Width &&
                BaseModelState.GetGridHeight(item) == expected.Height && BaseModelState.GetMemoryMultiplier(item) == 1,
                "Base model tags do not match its CSV grid and memory multiplier.");
            item.ModifyTag(BaseModelState.GridWidthTag, state => state.SetInt(99));
            Assert(BaseModelState.GetGridWidth(directory.Factories[expected.Id]()) == expected.Width,
                "Base model instances must not share mutable tags.");
        }
        var mismatch = new GameItem { Id = "other.item" };
        var definition = BaseModelCatalog.Load().Get("age_of_agi.base_grid");
        ExpectArgument(() => BaseModelHelper.InitBaseModel(mismatch, definition, AudioHelper.InitSmallModule));
        Assert(!mismatch.IsTag(BaseModelState.BaseModelTag), "A mismatched definition must not mutate the item.");
        ExpectArgument(() => BaseModelHelper.InitBaseModel(null, definition, AudioHelper.InitSmallModule));
        ExpectArgument(() => BaseModelHelper.InitBaseModel(mismatch, null, AudioHelper.InitSmallModule));
    }

    private static void CheckBaseModelTooltip(ModItemDirectory directory)
    {
        GameItem baseGrid = directory.Factories["age_of_agi.base_grid"]();
        var mod = new AgeOfAgiMod();
        mod.Init(new ModManifest());
        mod.OnEnable();
        mod.OnEnable();
        try
        {
            ModHelper.Locale = "zh";
            ExpectTooltip(baseGrid, "组件网格：4×6", "模型内存倍率：×1");
            baseGrid.ModifyTag(BaseModelState.GridHeightTag, state => state.SetInt(7));
            ModHelper.Locale = "en";
            ExpectTooltip(baseGrid, "Assembly grid: 4×7", "Model memory multiplier: ×1");
            ModHelper.Locale = "fr";
            ExpectTooltip(baseGrid, "Assembly grid: 4×7", "Model memory multiplier: ×1");
            ExpectTooltip(new GameItem());
            mod.OnDisable();
            ExpectTooltip(baseGrid);
            mod.OnEnable();
            ExpectTooltip(baseGrid, "Assembly grid: 4×7", "Model memory multiplier: ×1");
        }
        finally
        {
            mod.OnDisable();
            ModHelper.Locale = "en";
        }
    }

}
