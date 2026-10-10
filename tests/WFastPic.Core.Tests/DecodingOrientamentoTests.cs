using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WFastPic.Core.Decoding;
using Xunit;

namespace WFastPic.Core.Tests;

public class DecodingOrientamentoTests
{
    /// <summary>JPEG 400×300 (orizzontale nel file) con orientamento EXIF 6 = da ruotare di 90°: a video 300×400.</summary>
    private static async Task<string> CreaJpegRuotatoAsync()
    {
        var percorso = Path.Combine(Path.GetTempPath(), "wfastpic-orient-" + Guid.NewGuid() + ".jpg");
        const int larghezza = 400, altezza = 300;
        var pixel = new byte[larghezza * altezza * 4];
        for (var i = 0; i < pixel.Length; i += 4) { pixel[i] = 120; pixel[i + 1] = 160; pixel[i + 2] = 200; pixel[i + 3] = 255; }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, larghezza, altezza, 96, 96, pixel);
        await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet
        {
            ["System.Photo.Orientation"] = new BitmapTypedValue((ushort)6, PropertyType.UInt16),
        });
        await encoder.FlushAsync();

        stream.Seek(0);
        var dati = new byte[stream.Size];
        await stream.ReadAsync(dati.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(percorso, dati);
        return percorso;
    }

    [Fact]
    public async Task Preview_di_foto_ruotata_ha_le_proporzioni_giuste()
    {
        var percorso = await CreaJpegRuotatoAsync();
        try
        {
            // Senza riduzione (lato lungo > foto) e con riduzione: la scala WIC si applica prima della rotazione.
            foreach (var latoLungo in new[] { 2048, 200 })
            {
                var (_, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(percorso, latoLungo);
                Assert.True(h > w, $"lato {latoLungo}: attesa foto verticale, trovata {w}x{h}");
                Assert.InRange((double)w / h, 0.74, 0.76); // 300:400
            }
        }
        finally
        {
            File.Delete(percorso);
        }
    }
}
