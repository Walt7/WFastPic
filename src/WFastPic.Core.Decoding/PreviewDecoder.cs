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
        string percorsoFoto, int latoLungo = 2048, ColorManagementMode gestioneColore = ColorManagementMode.DoNotColorManage)
    {
        using var fs = File.OpenRead(percorsoFoto);
        using var winrtStream = fs.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(winrtStream);

        var ow = (double)decoder.OrientedPixelWidth;
        var oh = (double)decoder.OrientedPixelHeight;
        var scale = Math.Min(1.0, latoLungo / Math.Max(ow, oh));

        var transform = new BitmapTransform
        {
            InterpolationMode = BitmapInterpolationMode.Fant,
            ScaledWidth = (uint)Math.Max(1, Math.Round(ow * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(oh * scale)),
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
}
