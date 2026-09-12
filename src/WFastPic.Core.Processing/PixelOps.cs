namespace WFastPic.Core.Processing;

/// <summary>
/// Operazioni pixel pure (nessun decoder/UI) usate per "cuocere" la ricetta in fase di export:
/// il crop e il raddrizzamento restano visual-only nel Viewer, ma qui vanno applicati davvero
/// al buffer BGRA8 prima di codificare il JPEG finale.
/// </summary>
public static class PixelOps
{
    /// <summary>Ruota l'immagine attorno al centro, mantenendo la stessa tela (i bordi esposti diventano trasparenti).</summary>
    public static byte[] Rotate(byte[] bgra, int larghezza, int altezza, double angoloGradi)
    {
        if (angoloGradi == 0) return bgra;

        var radianti = angoloGradi * Math.PI / 180.0;
        var cos = Math.Cos(-radianti);
        var sin = Math.Sin(-radianti);
        var cx = larghezza / 2.0;
        var cy = altezza / 2.0;

        var risultato = new byte[bgra.Length];

        for (var y = 0; y < altezza; y++)
        {
            for (var x = 0; x < larghezza; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var srcX = cos * dx - sin * dy + cx;
                var srcY = sin * dx + cos * dy + cy;

                var destOffset = (y * larghezza + x) * 4;
                CampionaBilineare(bgra, larghezza, altezza, srcX, srcY, risultato, destOffset);
            }
        }

        return risultato;
    }

    private static void CampionaBilineare(byte[] sorgente, int larghezza, int altezza, double srcX, double srcY, byte[] destinazione, int destOffset)
    {
        if (srcX < 0 || srcY < 0 || srcX >= larghezza - 1 || srcY >= altezza - 1)
        {
            if (srcX < -1 || srcY < -1 || srcX >= larghezza || srcY >= altezza) return;

            var x0c = Math.Clamp((int)Math.Round(srcX), 0, larghezza - 1);
            var y0c = Math.Clamp((int)Math.Round(srcY), 0, altezza - 1);
            var srcOffsetC = (y0c * larghezza + x0c) * 4;
            Array.Copy(sorgente, srcOffsetC, destinazione, destOffset, 4);
            return;
        }

        var x0 = (int)Math.Floor(srcX);
        var y0 = (int)Math.Floor(srcY);
        var fx = srcX - x0;
        var fy = srcY - y0;

        for (var canale = 0; canale < 4; canale++)
        {
            var p00 = sorgente[(y0 * larghezza + x0) * 4 + canale];
            var p10 = sorgente[(y0 * larghezza + x0 + 1) * 4 + canale];
            var p01 = sorgente[((y0 + 1) * larghezza + x0) * 4 + canale];
            var p11 = sorgente[((y0 + 1) * larghezza + x0 + 1) * 4 + canale];

            var top = p00 + (p10 - p00) * fx;
            var bottom = p01 + (p11 - p01) * fx;
            var valore = top + (bottom - top) * fy;

            destinazione[destOffset + canale] = (byte)Math.Clamp(Math.Round(valore), 0, 255);
        }
    }

    /// <summary>Ritaglia un rettangolo pixel dal buffer BGRA8, con clamping ai bordi.</summary>
    public static byte[] Crop(byte[] bgra, int larghezza, int altezza, int x, int y, int larghezzaCrop, int altezzaCrop)
    {
        x = Math.Clamp(x, 0, larghezza - 1);
        y = Math.Clamp(y, 0, altezza - 1);
        larghezzaCrop = Math.Clamp(larghezzaCrop, 1, larghezza - x);
        altezzaCrop = Math.Clamp(altezzaCrop, 1, altezza - y);

        var risultato = new byte[larghezzaCrop * altezzaCrop * 4];
        for (var riga = 0; riga < altezzaCrop; riga++)
        {
            var srcOffset = ((y + riga) * larghezza + x) * 4;
            var destOffset = riga * larghezzaCrop * 4;
            Array.Copy(bgra, srcOffset, risultato, destOffset, larghezzaCrop * 4);
        }

        return risultato;
    }
}
