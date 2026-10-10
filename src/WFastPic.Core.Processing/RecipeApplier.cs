namespace WFastPic.Core.Processing;

/// <summary>
/// Applica in ordine le operazioni della ricetta (gira/specchia → raddrizza → ritaglia → tono) a un
/// buffer BGRA8 decodificato, "cuocendole" nei pixel. Usato sia in export sia nel Viewer (su
/// preview): il buffer in ingresso non viene mai modificato.
/// </summary>
public static class RecipeApplier
{
    public static (byte[] Bgra, int Larghezza, int Altezza) Applica(byte[] bgra, int larghezza, int altezza, EditRecipe ricetta)
    {
        // Ritaglio e raddrizzamento sono espressi sulla foto già girata (Orientamento).
        (bgra, larghezza, altezza) = Orientamento.Applica(bgra, larghezza, altezza, ricetta);

        var angolo = ricetta.StraightenAngleDegrees;
        var ruotato = angolo != 0 ? PixelOps.Rotate(bgra, larghezza, altezza, angolo) : bgra;

        var (cx, cy, cw, ch) = CropCalculator.ToPixelRect(ricetta, larghezza, altezza);

        // Ricette salvate da versioni precedenti (o modificate a mano) possono ritagliare angoli
        // vuoti dell'immagine ruotata: riporta il crop dentro l'immagine.
        if (angolo != 0)
        {
            var (x0, y0, x1, y1) = StraightenGeometry.FitRect(larghezza, altezza, angolo, cx, cy, cx + cw, cy + ch);
            cx = (int)Math.Ceiling(x0);
            cy = (int)Math.Ceiling(y0);
            cw = Math.Max(1, (int)Math.Floor(x1) - cx);
            ch = Math.Max(1, (int)Math.Floor(y1) - cy);
        }

        var ritagliaTutto = cx == 0 && cy == 0 && cw == larghezza && ch == altezza;
        var elaborato = ritagliaTutto ? ruotato : PixelOps.Crop(ruotato, larghezza, altezza, cx, cy, cw, ch);
        var larghezzaFinale = ritagliaTutto ? larghezza : cw;
        var altezzaFinale = ritagliaTutto ? altezza : ch;

        if (ricetta.HasTone)
        {
            if (ReferenceEquals(elaborato, bgra)) elaborato = (byte[])bgra.Clone();
            ToneProcessor.ApplyInPlace(elaborato, larghezzaFinale, altezzaFinale, ricetta);
        }

        return (elaborato, larghezzaFinale, altezzaFinale);
    }
}
