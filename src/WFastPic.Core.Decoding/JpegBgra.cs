using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WFastPic.Core.Decoding;

/// <summary>Piccoli JPEG da pixel BGRA già in memoria: miniature dei volti, ritagli da mostrare.</summary>
public static class JpegBgra
{
    /// <summary>Codifica in JPEG; con <paramref name="latoMassimo"/> l'immagine più grande viene rimpicciolita.</summary>
    public static async Task<byte[]> CodificaAsync(byte[] bgra, int larghezza, int altezza, int? latoMassimo = null, double qualita = 0.9)
    {
        using var stream = new InMemoryRandomAccessStream();
        var opzioni = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(qualita, Windows.Foundation.PropertyType.Single) };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, opzioni);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)larghezza, (uint)altezza, 96, 96, bgra);
        if (latoMassimo is int lato && Math.Max(larghezza, altezza) > lato)
        {
            var scala = (double)lato / Math.Max(larghezza, altezza);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, Math.Round(larghezza * scala));
            encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, Math.Round(altezza * scala));
        }
        await encoder.FlushAsync();

        var dati = new byte[stream.Size];
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(dati);
        return dati;
    }

    /// <summary>
    /// Il riquadro (normalizzato 0..1) allargato di <paramref name="margine"/> volte il suo lato per parte e reso
    /// quadrato, dentro l'immagine: un volto con un po' di contesto attorno (capelli, spalle) si riconosce meglio.
    /// </summary>
    public static (int X, int Y, int Lato) RiquadroConContesto(int larghezza, int altezza, double x0, double y0, double x1, double y1, double margine = 0.5)
    {
        var cx = (x0 + x1) / 2 * larghezza;
        var cy = (y0 + y1) / 2 * altezza;
        var latoVolto = Math.Max((x1 - x0) * larghezza, (y1 - y0) * altezza);
        var lato = (int)Math.Round(Math.Min(Math.Min(larghezza, altezza), Math.Max(1, latoVolto * (1 + 2 * margine))));
        var x = (int)Math.Round(Math.Clamp(cx - lato / 2.0, 0, larghezza - lato));
        var y = (int)Math.Round(Math.Clamp(cy - lato / 2.0, 0, altezza - lato));
        return (x, y, lato);
    }

    /// <summary>Copia un rettangolo di pixel BGRA (deve stare dentro l'immagine).</summary>
    public static byte[] Ritaglia(byte[] bgra, int larghezza, int x, int y, int w, int h)
    {
        var risultato = new byte[w * h * 4];
        for (var riga = 0; riga < h; riga++)
            System.Buffer.BlockCopy(bgra, ((y + riga) * larghezza + x) * 4, risultato, riga * w * 4, w * 4);
        return risultato;
    }
}
