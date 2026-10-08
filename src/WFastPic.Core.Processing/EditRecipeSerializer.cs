using System.Text.Json;

namespace WFastPic.Core.Processing;

public static class EditRecipeSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string ToJson(EditRecipe recipe) => JsonSerializer.Serialize(recipe, Options);

    /// <summary>Ricetta corrotta o schema futuro sconosciuto -> ricetta di default, mai un'eccezione.</summary>
    public static EditRecipe FromJson(string json)
    {
        try
        {
            var recipe = JsonSerializer.Deserialize<EditRecipe>(json, Options) ?? EditRecipe.Default;
            return Migra(recipe);
        }
        catch (JsonException)
        {
            return EditRecipe.Default;
        }
    }

    // v1 aveva un solo flag "AutoCorrect" (stretch per canale): equivale a "Ho fortuna" di v2.
    private static EditRecipe Migra(EditRecipe recipe)
    {
        if (recipe.SchemaVersion >= EditRecipe.CurrentSchemaVersion) return recipe;

        return recipe with
        {
            SchemaVersion = EditRecipe.CurrentSchemaVersion,
            AutoCorrect = false,
            AutoContrast = recipe.AutoContrast || recipe.AutoCorrect,
            AutoColor = recipe.AutoColor || recipe.AutoCorrect,
        };
    }
}
