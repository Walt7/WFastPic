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

        // La miniatura EXIF di un JPEG è salvata come i pixel della foto, cioè non girata: l'orientamento
        // del file va applicato a mano (telefoni Android, fotocamere). Le HEIC la danno già girata.
        var transform = EstensioniJpeg.Contains(Path.GetExtension(percorsoFoto), StringComparer.OrdinalIgnoreCase)
            ? TrasformazioneOrientamento(await LeggiOrientamentoAsync(decoder))
            : new BitmapTransform();

        using (embeddedThumb)
        {
            var thumbDecoder = await BitmapDecoder.CreateAsync(embeddedThumb);
            using var bitmap = await thumbDecoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

            return await EncodeJpegAsync(bitmap);
        }
    }

    private static readonly string[] EstensioniJpeg = [".jpg", ".jpeg"];

    private static async Task<ushort> LeggiOrientamentoAsync(BitmapDecoder decoder)
    {
        try
        {
            var valori = await decoder.BitmapProperties.GetPropertiesAsync(["System.Photo.Orientation"]);
            return valori.TryGetValue("System.Photo.Orientation", out var v) && v.Value is ushort o ? o : (ushort)1;
        }
        catch (Exception)
        {
            return 1; // proprietà assente o non leggibile: miniatura com'è
        }
    }

    /// <summary>
    /// Orientamento EXIF (1-8) come trasformazione WIC, che specchia prima e gira dopo:
    /// 5 = trasposta (specchio + 270°), 7 = trasversa (specchio + 90°).
    /// </summary>
    public static BitmapTransform TrasformazioneOrientamento(ushort orientamento) => orientamento switch
    {
        2 => new BitmapTransform { Flip = BitmapFlip.Horizontal },
        3 => new BitmapTransform { Rotation = BitmapRotation.Clockwise180Degrees },
        4 => new BitmapTransform { Flip = BitmapFlip.Vertical },
        5 => new BitmapTransform { Flip = BitmapFlip.Horizontal, Rotation = BitmapRotation.Clockwise270Degrees },
        6 => new BitmapTransform { Rotation = BitmapRotation.Clockwise90Degrees },
        7 => new BitmapTransform { Flip = BitmapFlip.Horizontal, Rotation = BitmapRotation.Clockwise90Degrees },
        8 => new BitmapTransform { Rotation = BitmapRotation.Clockwise270Degrees },
        _ => new BitmapTransform(),
    };

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
