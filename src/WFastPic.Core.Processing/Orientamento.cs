namespace WFastPic.Core.Processing;

/// <summary>
/// Gira di 90° e specchia, come i pulsanti di Picasa. Nella ricetta l'orientamento è il primo passo
/// (specchio orizzontale, poi rotazione oraria): raddrizzamento e ritaglio sono espressi sulla foto
/// già girata. Girando o specchiando una foto già ritagliata, il riquadro di ritaglio e l'angolo di
/// raddrizzamento vengono trasformati insieme alla foto, così il risultato resta lo stesso, solo girato.
/// </summary>
public static class Orientamento
{
    /// <summary>Gira di 90° in senso orario la foto come appare adesso.</summary>
    public static EditRecipe RuotaOrario(EditRecipe r)
    {
        // (u, v) → (1 - v, u)
        var (x0, y0, x1, y1) = (r.CropX, r.CropY, r.CropX + r.CropWidth, r.CropY + r.CropHeight);
        return r with
        {
            RotationDegrees = Normalizza(r.RotationDegrees + 90),
            CropX = 1 - y1,
            CropY = x0,
            CropWidth = y1 - y0,
            CropHeight = x1 - x0,
        };
    }

    /// <summary>Gira di 90° in senso antiorario la foto come appare adesso.</summary>
    public static EditRecipe RuotaAntiorario(EditRecipe r)
    {
        // (u, v) → (v, 1 - u)
        var (x0, y0, x1, y1) = (r.CropX, r.CropY, r.CropX + r.CropWidth, r.CropY + r.CropHeight);
        return r with
        {
            RotationDegrees = Normalizza(r.RotationDegrees + 270),
            CropX = y0,
            CropY = 1 - x1,
            CropWidth = y1 - y0,
            CropHeight = x1 - x0,
        };
    }

    /// <summary>
    /// Specchia orizzontalmente la foto come appare adesso. F·R(r) = R(-r)·F: la rotazione cambia verso,
    /// e anche il raddrizzamento (uno specchio inverte il senso di rotazione).
    /// </summary>
    public static EditRecipe Specchia(EditRecipe r) => r with
    {
        FlipHorizontal = !r.FlipHorizontal,
        RotationDegrees = Normalizza(360 - r.RotationDegrees),
        StraightenAngleDegrees = r.StraightenAngleDegrees == 0 ? 0 : -r.StraightenAngleDegrees,
        CropX = 1 - (r.CropX + r.CropWidth),
    };

    /// <summary>Dimensioni dopo l'orientamento (larghezza e altezza scambiate a 90° e 270°).</summary>
    public static (int Larghezza, int Altezza) Dimensioni(int larghezza, int altezza, EditRecipe r) =>
        Normalizza(r.RotationDegrees) is 90 or 270 ? (altezza, larghezza) : (larghezza, altezza);

    /// <summary>Applica specchio e rotazione a un buffer BGRA8; senza orientamento restituisce lo stesso buffer.</summary>
    public static (byte[] Bgra, int Larghezza, int Altezza) Applica(byte[] bgra, int larghezza, int altezza, EditRecipe r)
    {
        var rotazione = Normalizza(r.RotationDegrees);
        if (rotazione == 0 && !r.FlipHorizontal) return (bgra, larghezza, altezza);

        var (lo, ao) = Dimensioni(larghezza, altezza, r);
        var src = new ReadOnlySpan<byte>(bgra);
        var dst = new byte[bgra.Length];
        var dstU = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(dst.AsSpan());
        var srcU = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(src);

        for (var y = 0; y < altezza; y++)
        {
            var riga = y * larghezza;
            for (var x = 0; x < larghezza; x++)
            {
                var xs = r.FlipHorizontal ? larghezza - 1 - x : x;
                var (xd, yd) = rotazione switch
                {
                    90 => (altezza - 1 - y, xs),
                    180 => (larghezza - 1 - xs, altezza - 1 - y),
                    270 => (y, larghezza - 1 - xs),
                    _ => (xs, y),
                };
                dstU[yd * lo + xd] = srcU[riga + x];
            }
        }

        return (dst, lo, ao);
    }

    /// <summary>
    /// Porta un rettangolo normalizzato (0..1) della foto originale sulla foto orientata: per i volti
    /// trovati sull'originale, usati dai ritagli suggeriti.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) TrasformaRettangolo(
        double x0, double y0, double x1, double y1, EditRecipe r)
    {
        if (r.FlipHorizontal) (x0, x1) = (1 - x1, 1 - x0);
        for (var giri = Normalizza(r.RotationDegrees) / 90; giri > 0; giri--)
            (x0, y0, x1, y1) = (1 - y1, x0, 1 - y0, x1);
        return (x0, y0, x1, y1);
    }

    private static int Normalizza(int gradi) => ((gradi % 360) + 360) % 360 / 90 * 90;
}
