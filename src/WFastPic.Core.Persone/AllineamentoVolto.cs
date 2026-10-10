namespace WFastPic.Core.Persone;

/// <summary>
/// Allineamento del volto per SFace, come <c>FaceRecognizerSF::alignCrop</c> di OpenCV: la
/// trasformazione di similitudine (rotazione + scala + traslazione) ai minimi quadrati che porta i 5
/// punti trovati da YuNet sulla posizione standard in un riquadro 112×112, poi campionamento bilineare.
/// Un volto inclinato o lontano diventa così confrontabile con gli altri.
/// </summary>
public static class AllineamentoVolto
{
    public const int Lato = 112;

    // Posizioni standard (ArcFace/SFace) di occhi, naso e angoli della bocca nel riquadro 112×112.
    public static readonly (float X, float Y)[] Modello =
    [
        (38.2946f, 51.6963f), (73.5318f, 51.5014f), (56.0252f, 71.7366f), (41.5493f, 92.3655f), (70.7299f, 92.2041f),
    ];

    /// <summary>
    /// Trasformazione sorgente → modello: x' = a·x − b·y + tx, y' = b·x + a·y + ty.
    /// Soluzione chiusa ai minimi quadrati (Umeyama ristretto al 2D senza riflessioni).
    /// </summary>
    public static (float A, float B, float Tx, float Ty) StimaSimilitudine((float X, float Y)[] sorgente, (float X, float Y)[] destinazione)
    {
        var n = sorgente.Length;
        float msx = 0, msy = 0, mdx = 0, mdy = 0;
        for (var i = 0; i < n; i++) { msx += sorgente[i].X; msy += sorgente[i].Y; mdx += destinazione[i].X; mdy += destinazione[i].Y; }
        msx /= n; msy /= n; mdx /= n; mdy /= n;

        float num1 = 0, num2 = 0, den = 0;
        for (var i = 0; i < n; i++)
        {
            var sx = sorgente[i].X - msx; var sy = sorgente[i].Y - msy;
            var dx = destinazione[i].X - mdx; var dy = destinazione[i].Y - mdy;
            num1 += sx * dx + sy * dy;
            num2 += sx * dy - sy * dx;
            den += sx * sx + sy * sy;
        }

        var a = den > 0 ? num1 / den : 1;
        var b = den > 0 ? num2 / den : 0;
        return (a, b, mdx - (a * msx - b * msy), mdy - (b * msx + a * msy));
    }

    /// <summary>Volto allineato 112×112 in RGB float 0..255, formato CHW (ingresso di SFace).</summary>
    public static float[] Allinea(byte[] bgra, int larghezza, int altezza, (float X, float Y)[] punti)
    {
        var (a, b, tx, ty) = StimaSimilitudine(punti, Modello);

        // Inversa della similitudine: per ogni pixel del riquadro, dove pescare nella foto.
        var det = a * a + b * b;
        var (ia, ib) = (a / det, -b / det);

        var risultato = new float[3 * Lato * Lato];
        var piano = Lato * Lato;
        for (var v = 0; v < Lato; v++)
        {
            for (var u = 0; u < Lato; u++)
            {
                var px = u - tx; var py = v - ty;
                var sx = ia * px - ib * py;
                var sy = ib * px + ia * py;
                var (r, g, bl) = Campiona(bgra, larghezza, altezza, sx, sy);
                var i = v * Lato + u;
                risultato[i] = r;
                risultato[piano + i] = g;
                risultato[2 * piano + i] = bl;
            }
        }

        return risultato;
    }

    private static (float R, float G, float B) Campiona(byte[] bgra, int larghezza, int altezza, float x, float y)
    {
        if (x < 0 || y < 0 || x > larghezza - 1 || y > altezza - 1) return (0, 0, 0);

        var x0 = (int)x; var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, larghezza - 1); var y1 = Math.Min(y0 + 1, altezza - 1);
        var fx = x - x0; var fy = y - y0;

        float Canale(int c)
        {
            var v00 = bgra[(y0 * larghezza + x0) * 4 + c];
            var v10 = bgra[(y0 * larghezza + x1) * 4 + c];
            var v01 = bgra[(y1 * larghezza + x0) * 4 + c];
            var v11 = bgra[(y1 * larghezza + x1) * 4 + c];
            return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
        }

        return (Canale(2), Canale(1), Canale(0));
    }
}
