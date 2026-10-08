namespace WFastPic.Core.Processing;

/// <summary>
/// Geometria del raddrizzamento: l'immagine ruotata attorno al centro, sulla stessa tela, lascia
/// angoli vuoti. Un crop valido deve stare tutto dentro l'immagine ruotata (come Picasa, che
/// ingrandisce per nascondere i bordi). Coordinate in pixel, origine in alto a sinistra.
/// </summary>
public static class StraightenGeometry
{
    private const double Tolleranza = 1e-3;

    public static bool ContainsPoint(double larghezza, double altezza, double angoloGradi, double px, double py)
    {
        var rad = angoloGradi * Math.PI / 180.0;
        var cos = Math.Cos(-rad);
        var sin = Math.Sin(-rad);
        var dx = px - larghezza / 2;
        var dy = py - altezza / 2;
        var sx = cos * dx - sin * dy;
        var sy = sin * dx + cos * dy;
        return Math.Abs(sx) <= larghezza / 2 + Tolleranza && Math.Abs(sy) <= altezza / 2 + Tolleranza;
    }

    /// <summary>Il rettangolo sta nella tela e i suoi 4 angoli cadono dentro l'immagine ruotata.</summary>
    public static bool ContainsRect(double larghezza, double altezza, double angoloGradi, double x0, double y0, double x1, double y1)
    {
        if (x0 < -Tolleranza || y0 < -Tolleranza || x1 > larghezza + Tolleranza || y1 > altezza + Tolleranza) return false;

        return ContainsPoint(larghezza, altezza, angoloGradi, x0, y0)
            && ContainsPoint(larghezza, altezza, angoloGradi, x1, y0)
            && ContainsPoint(larghezza, altezza, angoloGradi, x0, y1)
            && ContainsPoint(larghezza, altezza, angoloGradi, x1, y1);
    }

    /// <summary>
    /// Restringe il rettangolo (stesse proporzioni, attorno al suo centro) finche sta dentro
    /// l'immagine ruotata. Se il centro e fuori, si riparte dal centro della tela.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) FitRect(
        double larghezza, double altezza, double angoloGradi, double x0, double y0, double x1, double y1)
    {
        if (ContainsRect(larghezza, altezza, angoloGradi, x0, y0, x1, y1)) return (x0, y0, x1, y1);

        var mezzaL = (x1 - x0) / 2;
        var mezzaA = (y1 - y0) / 2;
        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;
        if (cx < 0 || cy < 0 || cx > larghezza || cy > altezza || !ContainsPoint(larghezza, altezza, angoloGradi, cx, cy))
        {
            cx = larghezza / 2;
            cy = altezza / 2;
        }

        double lo = 0, hi = 1;
        for (var i = 0; i < 40; i++)
        {
            var t = (lo + hi) / 2;
            if (ContainsRect(larghezza, altezza, angoloGradi, cx - mezzaL * t, cy - mezzaA * t, cx + mezzaL * t, cy + mezzaA * t))
                lo = t;
            else
                hi = t;
        }

        return (cx - mezzaL * lo, cy - mezzaA * lo, cx + mezzaL * lo, cy + mezzaA * lo);
    }
}
