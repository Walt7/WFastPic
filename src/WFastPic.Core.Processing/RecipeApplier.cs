namespace WFastPic.Core.Processing;

/// <summary>
/// Applica in ordine le tre operazioni della ricetta (raddrizza → ritaglia → auto-correggi) a un
/// buffer BGRA8 decodificato, "cuocendole" davvero nei pixel — usato solo in export: nel Viewer
/// restano anteprime visual-only (RenderTransform / overlay), mai scritte sull'originale.
/// </summary>
public static class RecipeApplier
{
    public static (byte[] Bgra, int Larghezza, int Altezza) Applica(byte[] bgra, int larghezza, int altezza, EditRecipe ricetta)
    {
        var ruotato = ricetta.StraightenAngleDegrees != 0
            ? PixelOps.Rotate(bgra, larghezza, altezza, ricetta.StraightenAngleDegrees)
            : bgra;

        var (cx, cy, cw, ch) = CropCalculator.ToPixelRect(ricetta, larghezza, altezza);
        var ritagliaTutto = cx == 0 && cy == 0 && cw == larghezza && ch == altezza;
        var elaborato = ritagliaTutto ? ruotato : PixelOps.Crop(ruotato, larghezza, altezza, cx, cy, cw, ch);
        var larghezzaFinale = ritagliaTutto ? larghezza : cw;
        var altezzaFinale = ritagliaTutto ? altezza : ch;

        if (ricetta.AutoCorrect)
            AutoCorrectProcessor.ApplyInPlace(elaborato, larghezzaFinale, altezzaFinale);

        return (elaborato, larghezzaFinale, altezzaFinale);
    }
}
