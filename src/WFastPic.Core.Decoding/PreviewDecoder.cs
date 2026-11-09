using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace WFastPic.Core.Decoding;

/// <summary>
/// Decode scalato per il Viewer (non per la griglia: quello resta su ThumbnailExtractor).
/// Costo ~200-450ms per foto (verdetto Fase 0) — accettabile qui perché riguarda solo la foto
/// corrente + prefetch dei vicini, mai centinaia di foto in una volta.
/// </summary>
public static class PreviewDecoder
{
    public static async Task<(byte[] Bgra, int Width, int Height)> DecodePreviewBgraAsync(
        string percorsoFoto, int latoLungo = 2048, ColorManagementMode gestioneColore = ColorManagementMode.DoNotColorManage,
        BitmapInterpolationMode interpolazione = BitmapInterpolationMode.Fant)
    {
        using var fs = File.OpenRead(percorsoFoto);
        return await DecodePreviewBgraAsync(fs, latoLungo, gestioneColore, interpolazione);
    }

    /// <summary>Dai byte del file già letti: una lettura sola e sequenziale (su un HDD, molte letture a pezzi di file diversi fanno saltare la testina).</summary>
    public static async Task<(byte[] Bgra, int Width, int Height)> DecodePreviewBgraAsync(
        byte[] datiFile, int latoLungo = 2048, ColorManagementMode gestioneColore = ColorManagementMode.DoNotColorManage,
        BitmapInterpolationMode interpolazione = BitmapInterpolationMode.Fant)
    {
        using var ms = new MemoryStream(datiFile, writable: false);
        return await DecodePreviewBgraAsync(ms, latoLungo, gestioneColore, interpolazione);
    }

    private static async Task<(byte[] Bgra, int Width, int Height)> DecodePreviewBgraAsync(
        Stream flusso, int latoLungo, ColorManagementMode gestioneColore, BitmapInterpolationMode interpolazione)
    {
        using var winrtStream = flusso.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(winrtStream);

        var ow = (double)decoder.OrientedPixelWidth;
        var oh = (double)decoder.OrientedPixelHeight;
        var scale = Math.Min(1.0, latoLungo / Math.Max(ow, oh));

        var transform = new BitmapTransform
        {
            InterpolationMode = interpolazione,
            // WIC applica la scala PRIMA della rotazione EXIF: le misure vanno date sul file non ruotato
            // (PixelWidth/Height), altrimenti una foto con orientamento 90° esce stirata.
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
        };

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, gestioneColore);

        var larghezza = bitmap.PixelWidth;
        var altezza = bitmap.PixelHeight;
        var buffer = new byte[4 * larghezza * altezza];
        bitmap.CopyToBuffer(buffer.AsBuffer());

        return (buffer, larghezza, altezza);
    }

    /// <summary>Larghezza × altezza della foto (già girata), leggendo solo l'intestazione del file.</summary>
    public static async Task<(int Width, int Height)> LeggiDimensioniAsync(string percorsoFoto)
    {
        using var fs = File.OpenRead(percorsoFoto);
        using var winrtStream = fs.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(winrtStream);
        return ((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
    }
}
