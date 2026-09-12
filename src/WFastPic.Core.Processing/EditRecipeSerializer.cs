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
            return JsonSerializer.Deserialize<EditRecipe>(json, Options) ?? EditRecipe.Default;
        }
        catch (JsonException)
        {
            return EditRecipe.Default;
        }
    }
}
