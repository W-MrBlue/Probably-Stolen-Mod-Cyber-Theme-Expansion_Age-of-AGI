using System;
using System.Collections.Generic;
using System.Linq;

// Mutate test tables independently of the production parser.
internal sealed class CsvFixture
{
    internal List<string> Columns { get; private set; }
    internal List<Dictionary<string, string>> Rows { get; private set; }

    internal static CsvFixture Parse(string csv)
    {
        string[] lines = csv.TrimStart('\uFEFF').Split('\n')
            .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("#")).ToArray();
        var columns = lines[0].Split(',').ToList();
        return new CsvFixture
        {
            Columns = columns,
            Rows = lines.Skip(1).Select(line => line.Split(',').Select((value, index) =>
                new { Name = columns[index], Value = value }).ToDictionary(field => field.Name, field => field.Value)).ToList()
        };
    }

    internal void RemoveColumn(string name) => Columns.Remove(name);
    internal void AddColumn(string name, string value)
    {
        Columns.Add(name);
        foreach (var row in Rows) row[name] = value;
    }
    internal void DuplicateFirstRow() => Rows.Add(new Dictionary<string, string>(Rows[0]));
    internal static string Cell(string csv, string column, string value)
    {
        var table = Parse(csv);
        table.Rows[0][column] = value;
        return table.ToString();
    }
    public override string ToString() => string.Join(",", Columns) + "\n" +
        string.Join("\n", Rows.Select(row => string.Join(",", Columns.Select(column => row[column])))) + "\n";
}
