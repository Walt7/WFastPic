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

    /// <summary>
    /// Dove finisce un rettangolo della foto originale dopo il raddrizzamento (stessa rotazione di
    /// <see cref="PixelOps.Rotate"/>): riquadro che contiene i 4 angoli ruotati, entro la tela.
    /// Serve ai volti, salvati sulla foto non raddrizzata e usati sulla tela raddrizzata.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) RuotaRettangolo(
        double larghezza, double altezza, double angoloGradi, double x0, double y0, double x1, double y1)
    {
        if (angoloGradi == 0) return (x0, y0, x1, y1);

        var rad = angoloGradi * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var cx = larghezza / 2;
        var cy = altezza / 2;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (px, py) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
        {
            var dx = px - cx;
            var dy = py - cy;
            var rx = cos * dx - sin * dy + cx;
            var ry = sin * dx + cos * dy + cy;
            minX = Math.Min(minX, rx); maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry); maxY = Math.Max(maxY, ry);
        }

        return (Math.Clamp(minX, 0, larghezza), Math.Clamp(minY, 0, altezza), Math.Clamp(maxX, 0, larghezza), Math.Clamp(maxY, 0, altezza));
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
    /// Trascinamento del crop: se il rettangolo richiesto esce dall'immagine ruotata, si ferma
    /// esattamente sul bordo lungo il percorso da quello attuale (valido) invece di restare indietro.
    /// L'interpolazione lineare conserva ancora e proporzioni di entrambi i rettangoli.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) AvvicinaRect(
        double larghezza, double altezza, double angoloGradi,
        (double X0, double Y0, double X1, double Y1) da,
        (double X0, double Y0, double X1, double Y1) verso)
    {
        if (ContainsRect(larghezza, altezza, angoloGradi, verso.X0, verso.Y0, verso.X1, verso.Y1)) return verso;
        if (!ContainsRect(larghezza, altezza, angoloGradi, da.X0, da.Y0, da.X1, da.Y1)) return da;

        (double, double, double, double) Interpola(double t) => (
            da.X0 + (verso.X0 - da.X0) * t, da.Y0 + (verso.Y0 - da.Y0) * t,
            da.X1 + (verso.X1 - da.X1) * t, da.Y1 + (verso.Y1 - da.Y1) * t);

        double lo = 0, hi = 1;
        for (var i = 0; i < 30; i++)
        {
            var t = (lo + hi) / 2;
            var (x0, y0, x1, y1) = Interpola(t);
            if (ContainsRect(larghezza, altezza, angoloGradi, x0, y0, x1, y1)) lo = t; else hi = t;
        }

        return Interpola(lo);
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
