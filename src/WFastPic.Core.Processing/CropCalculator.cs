namespace WFastPic.Core.Processing;

public static class CropCalculator
{
    /// <summary>Converte il rettangolo di crop normalizzato (0..1) della ricetta in pixel, con clamping ai bordi immagine.</summary>
    public static (int X, int Y, int Width, int Height) ToPixelRect(EditRecipe recipe, int larghezzaImmagine, int altezzaImmagine)
    {
        var x = Math.Clamp(recipe.CropX, 0, 1);
        var y = Math.Clamp(recipe.CropY, 0, 1);
        var w = Math.Clamp(recipe.CropWidth, 0.01, 1 - x);
        var h = Math.Clamp(recipe.CropHeight, 0.01, 1 - y);

        var px = (int)Math.Round(x * larghezzaImmagine);
        var py = (int)Math.Round(y * altezzaImmagine);
        var pw = Math.Max(1, (int)Math.Round(w * larghezzaImmagine));
        var ph = Math.Max(1, (int)Math.Round(h * altezzaImmagine));

        pw = Math.Min(pw, larghezzaImmagine - px);
        ph = Math.Min(ph, altezzaImmagine - py);

        return (px, py, pw, ph);
    }
}
