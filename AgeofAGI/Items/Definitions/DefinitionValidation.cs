using System;

namespace AgeOfAgi.Items.Definitions
{
    // Shared constructor guards. Definitions choose their own field rules; catalogs
    // remain responsible for file formats, duplicate IDs and cross-file mappings.
    internal static class DefinitionValidation
    {
        internal static void RequireItemId(string itemId, string parameterName, string definitionName)
        {
            string prefix = ModConstants.ModId + ".";
            if (string.IsNullOrEmpty(itemId) || !itemId.StartsWith(prefix, StringComparison.Ordinal) || itemId.Length == prefix.Length)
            {
                throw new ArgumentException(definitionName + " ID must start with " + prefix + " and include an item name.", parameterName);
            }

            foreach (char character in itemId)
            {
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') || character == '_' || character == '-' || character == '.'))
                {
                    throw new ArgumentException("Invalid " + definitionName + " ID: " + itemId, parameterName);
                }
            }
        }

        internal static void RequirePositive(int value, string parameterName, string fieldName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, fieldName + " must be positive.");
            }
        }

        internal static void RequireFinitePositive(float value, string parameterName, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, fieldName + " must be finite and positive.");
            }
        }

        internal static void RequireFinitePositive(double value, string parameterName, string fieldName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > (double)decimal.MaxValue)
                throw new ArgumentOutOfRangeException(parameterName, fieldName + " must be finite, positive and within decimal range.");
        }

        internal static void RequireFiniteNonNegative(float value, string parameterName, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, fieldName + " must be finite and non-negative.");
            }
        }

        internal static void RequireNonNegative(long value, string parameterName, string fieldName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, fieldName + " must be non-negative.");
            }
        }
    }
}
