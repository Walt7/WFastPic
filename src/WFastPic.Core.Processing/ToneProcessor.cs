namespace WFastPic.Core.Processing;

/// <summary>
/// Regolazioni tonali (Auto contrasto, Auto colore, Luce di riempimento, Evidenzia, Ombre,
/// Temperatura) su buffer BGRA8. Tutto si riduce a una LUT per canale (le parti automatiche
/// leggono l'istogramma una sola volta) + un unico passaggio sui pixel, cosi resta abbastanza
/// veloce per l'anteprima live sugli slider. Nessuna dipendenza da decoder/UI.
/// </summary>
public static class ToneProcessor
{
    private const double ClipPercentile = 0.005;
    private const double GuadagnoColoreMin = 0.8;
    private const double GuadagnoColoreMax = 1.25;

    public static void ApplyInPlace(byte[] bgra, int width, int height, EditRecipe ricetta)
    {
        var totale = width * height;
        if (totale <= 0 || bgra.Length < totale * 4 || !ricetta.HasTone) return;

        var autoContrast = ricetta.AutoContrast || ricetta.AutoCorrect;
        var autoColor = ricetta.AutoColor || ricetta.AutoCorrect;

        double loLuma = 0, hiLuma = 255;
        if (autoContrast) (loLuma, hiLuma) = PercentiliLuminanza(bgra, totale);

        var guadagni = new[] { 1.0, 1.0, 1.0 }; // B, G, R
        if (autoColor) guadagni = GuadagniColore(bgra, totale);

        var temp = Math.Clamp(ricetta.Temperature, -1, 1);
        var guadagniTemp = new[] { 1 - 0.2 * temp, 1.0, 1 + 0.2 * temp };

        var lut = new byte[3][];
        for (var c = 0; c < 3; c++)
        {
            lut[c] = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                var v = i / 255.0;
                v = Math.Clamp(v * guadagni[c], 0, 1);
                if (autoContrast && hiLuma - loLuma >= 8)
                    v = Math.Clamp((v * 255 - loLuma) / (hiLuma - loLuma), 0, 1);

                v = FillLight(v, ricetta.FillLight);
                v = Highlights(v, ricetta.Highlights);
                v = Shadows(v, ricetta.Shadows);
                v = Math.Clamp(v * guadagniTemp[c], 0, 1);

                lut[c][i] = (byte)Math.Round(v * 255);
            }
        }

        var lutB = lut[0]; var lutG = lut[1]; var lutR = lut[2];
        for (var i = 0; i < totale * 4; i += 4)
        {
            bgra[i] = lutB[bgra[i]];
            bgra[i + 1] = lutG[bgra[i + 1]];
            bgra[i + 2] = lutR[bgra[i + 2]];
        }
    }

    // Gamma: bianchi e neri fissi, ombre e mezzitoni piu chiari. Monotona.
    private static double FillLight(double v, double f) =>
        f <= 0 ? v : Math.Pow(v, 1.0 / (1 + 0.8 * Math.Clamp(f, 0, 1)));

    // Tira giu le alte luci: derivata 1 - 0.9*h*v^2 >= 0.1, quindi monotona.
    private static double Highlights(double v, double h) =>
        h <= 0 ? v : Math.Clamp(v - 0.3 * Math.Clamp(h, 0, 1) * v * v * v, 0, 1);

    // Scurisce le ombre: derivata 1 + 0.9*s*(1-v)^2 > 0, monotona (il nero resta 0 per clamp).
    private static double Shadows(double v, double s)
    {
        if (s <= 0) return v;
        var inv = 1 - v;
        return Math.Clamp(v - 0.3 * Math.Clamp(s, 0, 1) * inv * inv * inv, 0, 1);
    }

    private static (double Lo, double Hi) PercentiliLuminanza(byte[] bgra, int totale)
    {
        var hist = new int[256];
        for (var i = 0; i < totale * 4; i += 4)
        {
            var luma = (bgra[i] * 114 + bgra[i + 1] * 587 + bgra[i + 2] * 299 + 500) / 1000;
            hist[luma]++;
        }

        var daClippare = (int)(totale * ClipPercentile);

        var cumulativo = 0;
        var lo = 0;
        for (var v = 0; v < 256; v++)
        {
            cumulativo += hist[v];
            if (cumulativo > daClippare) { lo = v; break; }
        }

        cumulativo = 0;
        var hi = 255;
        for (var v = 255; v >= 0; v--)
        {
            cumulativo += hist[v];
            if (cumulativo > daClippare) { hi = v; break; }
        }

        return (lo, hi);
    }

    private static double[] GuadagniColore(byte[] bgra, int totale)
    {
        long sommaB = 0, sommaG = 0, sommaR = 0;
        for (var i = 0; i < totale * 4; i += 4)
        {
            sommaB += bgra[i];
            sommaG += bgra[i + 1];
            sommaR += bgra[i + 2];
        }

        double mB = (double)sommaB / totale, mG = (double)sommaG / totale, mR = (double)sommaR / totale;
        if (mB <= 0 || mG <= 0 || mR <= 0) return [1.0, 1.0, 1.0];

        var target = (mB + mG + mR) / 3;
        return
        [
            Math.Clamp(target / mB, GuadagnoColoreMin, GuadagnoColoreMax),
            Math.Clamp(target / mG, GuadagnoColoreMin, GuadagnoColoreMax),
            Math.Clamp(target / mR, GuadagnoColoreMin, GuadagnoColoreMax),
        ];
    }
}
