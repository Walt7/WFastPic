namespace WFastPic.Core.Processing;

/// <summary>
/// Auto-correzione: stretch istogramma per canale (percentile 1%-99%, ignora outlier estremi) +
/// recupero ombre leggero (gamma sulle zone scure). Lavora in-place su un buffer BGRA8 puro:
/// nessuna dipendenza da un decoder immagine, testabile con array sintetici.
/// </summary>
public static class AutoCorrectProcessor
{
    private const double GammaOmbre = 0.9; // <1 schiarisce leggermente le ombre

    public static void ApplyInPlace(byte[] bgra, int width, int height)
    {
        var totalePixel = width * height;
        if (totalePixel <= 0 || bgra.Length < totalePixel * 4) return;

        var histB = new int[256];
        var histG = new int[256];
        var histR = new int[256];

        for (var i = 0; i < bgra.Length; i += 4)
        {
            histB[bgra[i]]++;
            histG[bgra[i + 1]]++;
            histR[bgra[i + 2]]++;
        }

        var (loB, hiB) = TrovaPercentili(histB, totalePixel);
        var (loG, hiG) = TrovaPercentili(histG, totalePixel);
        var (loR, hiR) = TrovaPercentili(histR, totalePixel);

        var lutB = CostruisciLut(loB, hiB);
        var lutG = CostruisciLut(loG, hiG);
        var lutR = CostruisciLut(loR, hiR);

        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = lutB[bgra[i]];
            bgra[i + 1] = lutG[bgra[i + 1]];
            bgra[i + 2] = lutR[bgra[i + 2]];
        }
    }

    private static (int Lo, int Hi) TrovaPercentili(int[] istogramma, int totalePixel)
    {
        // Numero di pixel (dal basso e dall'alto) da considerare outlier e ignorare nello stretch.
        var pixelDaClippare = (int)(totalePixel * 0.01);

        var cumulativo = 0;
        var lo = 0;
        for (var v = 0; v < 256; v++)
        {
            cumulativo += istogramma[v];
            if (cumulativo > pixelDaClippare) { lo = v; break; }
        }

        cumulativo = 0;
        var hi = 255;
        for (var v = 255; v >= 0; v--)
        {
            cumulativo += istogramma[v];
            if (cumulativo > pixelDaClippare) { hi = v; break; }
        }

        if (hi <= lo) { lo = 0; hi = 255; }
        return (lo, hi);
    }

    private static byte[] CostruisciLut(int lo, int hi)
    {
        var lut = new byte[256];
        var range = Math.Max(1, hi - lo);

        for (var v = 0; v < 256; v++)
        {
            var allungato = Math.Clamp((v - lo) * 255.0 / range, 0, 255);
            var normalizzato = allungato / 255.0;
            var conOmbre = Math.Pow(normalizzato, GammaOmbre) * 255.0;
            lut[v] = (byte)Math.Clamp(Math.Round(conOmbre), 0, 255);
        }

        return lut;
    }
}
