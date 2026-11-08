using System;

namespace AgeOfAgi.Items.Definitions
{
    internal sealed class DiskWriterRecipeDefinition
    {
        internal string RecipeId { get; }
        internal string DecoderItemId { get; }
        internal string EncoderItemId { get; }

        internal DiskWriterRecipeDefinition(string recipeId, string decoderItemId, string encoderItemId)
        {
            if (string.IsNullOrEmpty(recipeId))
                throw new ArgumentException("Disk writer recipe ID must not be empty.", nameof(recipeId));
            foreach (char character in recipeId)
            {
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') || character == '_' || character == '-'))
                    throw new ArgumentException("Invalid disk writer recipe ID: " + recipeId, nameof(recipeId));
            }
            DefinitionValidation.RequireItemId(decoderItemId, nameof(decoderItemId), "Disk writer decoder output");
            DefinitionValidation.RequireItemId(encoderItemId, nameof(encoderItemId), "Disk writer encoder output");
            RecipeId = recipeId;
            DecoderItemId = decoderItemId;
            EncoderItemId = encoderItemId;
        }
    }
}
