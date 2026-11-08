using System;
using System.IO;
using System.Linq;
using System.Globalization;
using AgeOfAgi.Items.Catalogs;

internal static class CatalogCsvChecks
{
    internal static void Run()
    {
        var catalogs = new (string Path, string Field, Action<string> Parse)[]
        {
            (GpuCatalog.CatalogPath, "computePower", csv => GpuCatalog.Parse(csv)),
            (CpuCatalog.CatalogPath, "powerBoostRate", csv => CpuCatalog.Parse(csv)),
            (MemoryCatalog.CatalogPath, "memory", csv => MemoryCatalog.Parse(csv)),
            (MachineCatalog.CatalogPath, "powerPerCycle", csv => MachineCatalog.Parse(csv)),
            (BaseModelCatalog.CatalogPath, "gridWidth", csv => BaseModelCatalog.Parse(csv)),
            (CompilerCatalog.CatalogPath, "initialDurability", csv => CompilerCatalog.Parse(csv)),
            (DiskWriterCatalog.CatalogPath, "decoderItemId", csv => DiskWriterCatalog.Parse(csv)),
            (AiPartCatalog.CatalogPath, "computeBonusPercent", csv => AiPartCatalog.Parse(csv)),
            (ServerCatalog.CatalogPath, "coolingMultiplier", csv => ServerCatalog.Parse(csv)),
            (EncryptedItemCatalog.CatalogPath, "work", csv => EncryptedItemCatalog.Parse(csv)),
            (DataContentCatalog.CatalogPath, "data_unit_value", csv => DataContentCatalog.Parse(csv)),
            (AiValueCatalog.CatalogPath, "value_per_use", csv => AiValueCatalog.Parse(csv))
        };
        foreach (var catalog in catalogs)
        {
            string csv = File.ReadAllText(Path.Combine(ModHelper.Root, catalog.Path));
            foreach (string malformed in new[] { "", "# comment only\n", string.Join(",", CsvFixture.Parse(csv).Columns), csv + "extra,row\n" })
                ExpectLocation(() => catalog.Parse(malformed), catalog.Path, "", "");
            var table = CsvFixture.Parse(csv);
            table.DuplicateFirstRow();
            int row = table.Rows.Count - 1;
            table.Rows[row][catalog.Path == DataContentCatalog.CatalogPath || catalog.Path == EncryptedItemCatalog.CatalogPath ? "item_id" :
                catalog.Path == AiValueCatalog.CatalogPath ? "task" : "id"] = "age_of_agi.diagnostic_probe";
            table.Rows[row][catalog.Field] = "true";
            ExpectLocation(() => catalog.Parse(table.ToString()), catalog.Path, "line " + (row + 2), catalog.Field);
            table.Rows[row][catalog.Field] = "";
            ExpectLocation(() => catalog.Parse(table.ToString()), catalog.Path, "line " + (row + 2), catalog.Field);
            table = CsvFixture.Parse(csv);
            table.AddColumn("unknownField", "1");
            ExpectLocation(() => catalog.Parse(table.ToString()), catalog.Path, "line 1", "unknownField");
            table.Columns[1] = table.Columns[0];
            ExpectLocation(() => catalog.Parse(table.ToString()), catalog.Path, "line 1", "Duplicate column");

            // BOM, CRLF, comments, whitespace and reordered headers must preserve values.
            table = CsvFixture.Parse(csv);
            table.Columns.Reverse();
            string reordered = "\uFEFF# comment\r\n\r\n" + table.ToString().Replace("\n", "\r\n");
            catalog.Parse(reordered);
        }
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            if (CpuCatalog.Parse("id,powerBoostRate,powerPerCycle\nage_of_agi.cpu0,1.5,2\n").Get("age_of_agi.cpu0").PowerBoostRate != 1.5f)
                throw new Exception("CSV numbers must use invariant culture.");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        if (Directory.GetFiles(Path.Combine(ModHelper.Root, "Data"), "*.json", SearchOption.AllDirectories).Any() ||
            File.Exists(Path.Combine(ModHelper.Root, "Data/data_content.csv")) ||
            File.Exists(Path.Combine(ModHelper.Root, "Data/Definitions/data_content.csv")))
            throw new Exception("Data must contain only current CSV configuration; use Definitions/data_carriers.csv.");
    }

    private static void ExpectLocation(Action parse, string path, string location, string field)
    {
        try { parse(); }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.Contains(path) && exception.Message.Contains(location) && exception.Message.Contains(field)) return;
            throw new Exception("CSV diagnostics lost their file, line or field: " + exception.Message, exception);
        }
        throw new Exception("Invalid CSV was accepted: " + path + " " + location + " " + field);
    }
}
