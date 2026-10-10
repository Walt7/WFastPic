namespace WFastPic.Core.Processing;

/// <summary>
/// Matematica dello zoom/trascinamento del Viewer. Convenzione: l'immagine (lato w×h a zoom 1) è
/// centrata nell'area, scalata attorno al proprio centro e poi traslata di (tx, ty) pixel schermo.
/// </summary>
public static class PanZoomGeometry
{
    /// <summary>
    /// Limita lo spostamento così la foto non si stacca mai dai bordi: si può scorrere solo finché
    /// c'è parte nascosta da quel lato. Se la foto zoomata è più piccola dell'area resta centrata.
    /// </summary>
    public static (double Tx, double Ty) Limita(
        double tx, double ty, double w, double h, double zoom, double areaW, double areaH)
    {
        var maxX = Math.Max(0, (w * zoom - areaW) / 2);
        var maxY = Math.Max(0, (h * zoom - areaH) / 2);
        return (Math.Clamp(tx, -maxX, maxX), Math.Clamp(ty, -maxY, maxY));
    }

    /// <summary>
    /// Nuovo spostamento per cambiare zoom tenendo fermo il punto sotto il mouse.
    /// (px, py) = posizione del mouse rispetto al centro dell'area.
    /// </summary>
    public static (double Tx, double Ty) ZoomVersoPunto(
        double tx, double ty, double px, double py, double zoomPrima, double zoomDopo)
    {
        var fattore = zoomDopo / zoomPrima;
        return (px - (px - tx) * fattore, py - (py - ty) * fattore);
    }
}
