using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace WFastPic.Core.Decoding;

/// <summary>
/// Rilevamento volti con il FaceDetector di Windows: integrato nel sistema, offline, nessun dato
/// esce dal PC. Serve ai ritagli suggeriti (i volti non si tagliano). Lavora su una copia ridotta
/// in scala di grigi: bastano ~1024 px per trovare i volti di una foto di gruppo, in poche decine di ms.
/// </summary>
public static class RilevatoreVolti
{
    public const int LatoAnalisi = 1024;

    public static bool Disponibile => FaceDetector.IsSupported;

    /// <summary>Volti come rettangoli normalizzati (0..1); vuoto se non supportato o nessun volto.</summary>
    public static async Task<IReadOnlyList<(double X0, double Y0, double X1, double Y1)>> RilevaAsync(
        byte[] bgra, int larghezza, int altezza, CancellationToken ct = default)
    {
        if (!FaceDetector.IsSupported || larghezza <= 0 || altezza <= 0) return [];

        try
        {
            var (ridotta, w, h) = Riduci(bgra, larghezza, altezza);
            using var bgraBitmap = SoftwareBitmap.CreateCopyFromBuffer(ridotta.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
            using var grigio = SoftwareBitmap.Convert(bgraBitmap, BitmapPixelFormat.Gray8);

            var rilevatore = await FaceDetector.CreateAsync();
            ct.ThrowIfCancellationRequested();
            var volti = await rilevatore.DetectFacesAsync(grigio);

            return volti
                .Select(v => ((double)v.FaceBox.X / w, (double)v.FaceBox.Y / h,
                              (double)(v.FaceBox.X + v.FaceBox.Width) / w, (double)(v.FaceBox.Y + v.FaceBox.Height) / h))
                .ToList();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return []; // rilevatore non disponibile: i ritagli si basano solo sul resto della foto
        }
    }

    // Riduzione a blocchi a ~1024 px; dimensioni pari (alcuni formati del rilevatore le richiedono).
    private static (byte[] Bgra, int W, int H) Riduci(byte[] bgra, int larghezza, int altezza)
    {
        var fattore = Math.Max(1, (int)Math.Ceiling((double)Math.Max(larghezza, altezza) / LatoAnalisi));
        var w = Math.Max(2, larghezza / fattore & ~1);
        var h = Math.Max(2, altezza / fattore & ~1);
        if (fattore == 1 && w == larghezza && h == altezza) return (bgra, w, h);

        var risultato = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                long b = 0, g = 0, r = 0, n = 0;
                for (var sy = y * fattore; sy < Math.Min(altezza, (y + 1) * fattore); sy++)
                {
                    for (var sx = x * fattore; sx < Math.Min(larghezza, (x + 1) * fattore); sx++)
                    {
                        var o = (sy * larghezza + sx) * 4;
                        b += bgra[o]; g += bgra[o + 1]; r += bgra[o + 2]; n++;
                    }
                }
                var d = (y * w + x) * 4;
                risultato[d] = (byte)(b / n); risultato[d + 1] = (byte)(g / n); risultato[d + 2] = (byte)(r / n); risultato[d + 3] = 255;
            }
        }
        return (risultato, w, h);
    }
}
