using System;
using System.Collections.Generic;
using System.Globalization;
using AgeOfAgi.Items.Catalogs;

namespace AgeOfAgi.Items
{
    internal static class ItemCatalog
    {
        private const string CatalogPath = "Data/items.csv";
        private const string Header = "item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer";

        internal static IReadOnlyList<ItemDefinition> Load()
        {
            string csv = ModHelper.ReadModFileText(ModConstants.ModId, CatalogPath);
            if (csv == null)
            {
                throw new InvalidOperationException("Missing " + CatalogPath + ". Deploy the Data folder with the mod.");
            }

            return Parse(csv, spriteKey => ModHelper.ModFileExists(
                ModConstants.ModId, "Textures/Items/" + spriteKey + ".png"));
        }

        internal static IReadOnlyList<ItemDefinition> Parse(string csv, Func<string, bool> spriteExists)
        {
            if (csv == null)
            {
                throw new ArgumentNullException(nameof(csv));
            }

            if (spriteExists == null)
            {
                throw new ArgumentNullException(nameof(spriteExists));
            }

            var definitions = new List<ItemDefinition>();
            var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool headerFound = false;
            string[] lines = csv.TrimStart('\uFEFF').Split('\n');

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!headerFound)
                {
                    if (line != Header)
                    {
                        throw InvalidRow(index, "Expected header: " + Header);
                    }

                    headerFound = true;
                    continue;
                }

                // Common identifiers and base value live here; translated text lives in Localization/*.csv.
                string[] fields = line.Split(',');
                if (fields.Length != 11)
                {
                    throw InvalidRow(index, "Expected eleven unquoted fields; separate types with semicolons.");
                }

                for (int field = 0; field < 7; field++)
                {
                    fields[field] = fields[field].Trim();
                    if (field == 3)
                    {
                        continue;
                    }
                    if (field == 1 && fields[field].Length == 0)
                    {
                        fields[field] = "generic";
                    }
                    // Description/comment columns remain present, but their keys are optional.
                    if ((field == 5 || field == 6) && fields[field].Length == 0)
                    {
                        continue;
                    }
                    if (!IsIdentifier(fields[field], field == 0))
                    {
                        throw InvalidRow(index, "Invalid identifier in column " + (field + 1) + ".");
                    }
                }

                if (!long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out long baseValue))
                {
                    throw InvalidRow(index, "base_value in column 4 is required and must be a non-negative 64-bit integer.");
                }

                if (!fields[0].StartsWith(ModConstants.ModId + ".", StringComparison.Ordinal))
                {
                    throw InvalidRow(index, "Item ID must start with " + ModConstants.ModId + ".");
                }

                if (fields[0].Length == ModConstants.ModId.Length + 1)
                {
                    throw InvalidRow(index, "Item ID needs a name after the mod prefix.");
                }

                if (!itemIds.Add(fields[0]))
                {
                    throw InvalidRow(index, "Duplicate item ID.");
                }

                if (!spriteExists(fields[2]))
                {
                    throw InvalidRow(index, "Missing Textures/Items/" + fields[2] + ".png.");
                }

                fields[8] = fields[8].Trim();
                fields[9] = fields[9].Trim();
                fields[10] = fields[10].Trim();
                if (fields[10].Length > 0 && !IsIdentifier(fields[10], true))
                    throw InvalidRow(index, "Invalid manufacturer identifier in column 11.");
                try
                {
                    ItemAudioPresets.Resolve(fields[8]);
                    ItemContrabandLevels.Resolve(fields[9]);
                }
                catch (ArgumentException exception)
                {
                    throw InvalidRow(index, exception.Message);
                }

                definitions.Add(new ItemDefinition(fields[0], fields[1], fields[2], baseValue, fields[4], fields[5],
                    fields[6], ParseTypes(fields[7], index), fields[8], fields[9], fields[10]));
            }

            if (!headerFound || definitions.Count == 0)
            {
                throw new InvalidOperationException(CatalogPath + " must contain a header and at least one item.");
            }

            return definitions.AsReadOnly();
        }

        private static IReadOnlyList<string> ParseTypes(string field, int rowIndex)
        {
            var types = new List<string>();
            if (string.IsNullOrWhiteSpace(field))
            {
                return types;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string part in field.Split(';'))
            {
                string type = part.Trim().ToLowerInvariant();
                if (!IsIdentifier(type, false))
                {
                    throw InvalidRow(rowIndex, "Invalid type identifier in column 8. Use semicolons between non-empty identifiers.");
                }

                if (seen.Add(type))
                {
                    types.Add(type);
                }
            }

            return types;
        }

        private static bool IsIdentifier(string value, bool allowItemIdPunctuation)
        {
            if (value.Length == 0)
            {
                return false;
            }

            foreach (char character in value)
            {
                if ((character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') || character == '_' ||
                    (allowItemIdPunctuation && (character == '.' || character == '-')))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static InvalidOperationException InvalidRow(int index, string message)
        {
            return new InvalidOperationException(CatalogPath + " line " + (index + 1) + ": " + message);
        }
    }
}
