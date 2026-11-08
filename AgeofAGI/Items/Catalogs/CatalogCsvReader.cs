using AgeOfAgi.Items.Definitions;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AgeOfAgi.Items.Catalogs
{
    // Configuration tables use unquoted fields, matching Data/items.csv.
    internal sealed class CatalogCsvReader
    {
        private readonly Dictionary<string, string> fields;
        private readonly string path;
        private readonly string location;

        private CatalogCsvReader(Dictionary<string, string> fields, string path, int line)
        {
            this.fields = fields;
            this.path = path;
            location = "line " + line;
        }

        // Call only from the directory registration hook, after the mod is registered.
        internal static string ReadModFile(string path)
        {
            return ModHelper.ReadModFileText(ModConstants.ModId, path) ??
                throw Error(path, "", "Missing " + path + ". Deploy the Data folder with the mod.");
        }

        internal static List<CatalogCsvReader> ReadRows(string csv, string path, string[] columns)
        {
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            var rows = new List<CatalogCsvReader>();
            string[] header = null;
            string[] lines = csv.TrimStart('\uFEFF').Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string location = "line " + (index + 1);
                string[] values = line.Split(',');
                for (int field = 0; field < values.Length; field++)
                {
                    values[field] = values[field].Trim();
                    if (values[field].IndexOf('"') >= 0)
                        throw Error(path, location, "Expected an unquoted field" +
                            (header != null && field < header.Length ? " for " + header[field] : "") + ".");
                }
                if (header == null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string name in values)
                    {
                        if (Array.IndexOf(columns, name) < 0)
                            throw Error(path, location, "Unknown column: " + name);
                        if (!seen.Add(name)) throw Error(path, location, "Duplicate column: " + name);
                    }
                    foreach (string name in columns)
                    {
                        if (!seen.Contains(name)) throw Error(path, location, "Missing column: " + name);
                    }
                    header = values;
                    continue;
                }
                if (values.Length != header.Length)
                    throw Error(path, location, "Expected " + header.Length + " unquoted fields (" + string.Join(",", header) + ").");
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int field = 0; field < header.Length; field++) fields.Add(header[field], values[field]);
                rows.Add(new CatalogCsvReader(fields, path, index + 1));
            }
            if (header == null || rows.Count == 0)
                throw Error(path, "", "Expected a header and at least one data row.");
            return rows;
        }

        internal static Dictionary<string, T> ReadDefinitions<T>(string csv, string path,
            string definitionName, string[] columns, Func<CatalogCsvReader, string, T> createDefinition)
        {
            var definitions = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var entry in ReadDefinitionEntries(csv, path, definitionName, columns, createDefinition))
                definitions.Add(entry.Key, entry.Value);
            return definitions;
        }

        // Preserve file order for recipe selection and deterministic stock rolls.
        internal static List<KeyValuePair<string, T>> ReadDefinitionEntries<T>(string csv, string path,
            string definitionName, string[] columns, Func<CatalogCsvReader, string, T> createDefinition)
        {
            var definitions = new List<KeyValuePair<string, T>>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in ReadRows(csv, path, columns))
            {
                string id = row.ReadString("id");
                T definition = row.Create(definitionName + " '" + id + "'", () => createDefinition(row, id));
                if (!ids.Add(id)) throw row.Invalid("Duplicate " + definitionName + " ID: " + id);
                definitions.Add(new KeyValuePair<string, T>(id, definition));
            }
            return definitions;
        }

        internal T Create<T>(string definitionName, Func<T> create)
        {
            try { return create(); }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            {
                throw Invalid(definitionName + ": " + exception.Message, exception);
            }
        }

        internal string ReadString(string name) => fields[name];

        internal string ReadItemId(string name)
        {
            string id = ReadString(name);
            try { DefinitionValidation.RequireItemId(id, name, "Item"); }
            catch (ArgumentException exception) { throw Invalid("Invalid item ID for " + name + ": " + id, exception); }
            return id;
        }

        internal int ReadInt(string name)
        {
            if (!int.TryParse(ReadString(name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                throw Invalid(name + " is required and must be an integer within the supported Int32 range.");
            return value;
        }

        internal float ReadFloat(string name)
        {
            if (!float.TryParse(ReadString(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value))
                throw Invalid(name + " must be a finite number within the supported float range.");
            return value;
        }

        internal double ReadDouble(string name)
        {
            if (!double.TryParse(ReadString(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw Invalid(name + " must be a finite number.");
            return value;
        }

        internal InvalidOperationException Invalid(string message, Exception inner = null) => Error(path, location, message, inner);

        internal static InvalidOperationException Error(string path, string location, string message, Exception inner = null)
        {
            return new InvalidOperationException(path + (string.IsNullOrEmpty(location) ? "" : " " + location) + ": " + message, inner);
        }
    }
}
