using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WFastPic.Core.Decoding;

/// <summary>
/// Estrae la thumbnail L1 per la griglia. Verdetto Fase 0: WIC scala male su HEIC (~200-500ms anche
/// per una thumbnail 256px). La thumbnail incorporata nel container (GetThumbnailAsync) costa ~7-14ms
/// per file: è questa la fonte usata qui, ricodificata in JPEG per una cache disco leggibile senza
/// tornare a decodificare l'HEIC originale.
/// </summary>
public static class ThumbnailExtractor
{
    public static async Task<byte[]?> ExtractAsJpegAsync(string percorsoFoto)
    {
        using var fs = File.OpenRead(percorsoFoto);
        using var winrtStream = fs.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(winrtStream);

        IRandomAccessStreamWithContentType embeddedThumb;
        try
        {
            embeddedThumb = await decoder.GetThumbnailAsync();
        }
        catch
        {
            // Nessuna thumbnail incorporata: fallback a decode scalato WIC (lento, ma solo per
            // questo caso raro — non deve mai succedere per la maggioranza dei file reali).
            return await DecodeScaledFallbackAsync(decoder);
        }

        using (embeddedThumb)
        {
            var thumbDecoder = await BitmapDecoder.CreateAsync(embeddedThumb);
            using var bitmap = await thumbDecoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            return await EncodeJpegAsync(bitmap);
        }
    }

    private static async Task<byte[]> DecodeScaledFallbackAsync(BitmapDecoder decoder)
    {
        const int latoLungo = 512;
        var ow = (double)decoder.OrientedPixelWidth;
        var oh = (double)decoder.OrientedPixelHeight;
        var scale = latoLungo / Math.Max(ow, oh);

        var transform = new BitmapTransform
        {
            InterpolationMode = BitmapInterpolationMode.Fant,
            // La scala WIC si applica prima della rotazione EXIF: misure sul file non ruotato.
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
        };

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

        return await EncodeJpegAsync(bitmap);
    }

    private static async Task<byte[]> EncodeJpegAsync(SoftwareBitmap bitmap)
    {
        using var outStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        var bytes = new byte[outStream.Size];
        using var reader = new DataReader(outStream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)outStream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
