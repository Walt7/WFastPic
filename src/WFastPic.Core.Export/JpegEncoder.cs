using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WFastPic.Core.Export;

/// <summary>
/// Codifica un buffer BGRA8 in JPEG via WIC, copiando data scatto e GPS dall'originale e
/// scrivendo i tag personalizzati dell'utente come Keywords (letti da Explorer/Lightroom/ecc.).
/// </summary>
public static class JpegEncoder
{
    private static readonly string[] ProprietaDaCopiare =
    [
        "System.Photo.DateTaken",
        "System.GPS.LatitudeRef", "System.GPS.Latitude", "System.GPS.LatitudeDenominator", "System.GPS.LatitudeNumerator",
        "System.GPS.LongitudeRef", "System.GPS.Longitude", "System.GPS.LongitudeDenominator", "System.GPS.LongitudeNumerator",
    ];

    public static async Task SalvaAsync(
        string percorsoOutput, byte[] bgra, int larghezza, int altezza,
        int qualitaJpeg, string percorsoOriginale, IReadOnlyList<string> tag)
    {
        try
        {
            using var fileStream = File.Create(percorsoOutput);
            using var winrtStream = fileStream.AsRandomAccessStream();

            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, winrtStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)larghezza, (uint)altezza, 96, 96, bgra);

            var proprietaQualita = new BitmapPropertySet
            {
                ["ImageQuality"] = new BitmapTypedValue(qualitaJpeg / 100.0f, Windows.Foundation.PropertyType.Single),
            };
            await encoder.BitmapProperties.SetPropertiesAsync(proprietaQualita);

            await CopiaMetadatiOriginaliAsync(encoder, percorsoOriginale);

            if (tag.Count > 0)
            {
                var proprietaTag = new BitmapPropertySet
                {
                    ["System.Keywords"] = new BitmapTypedValue(tag.ToArray(), Windows.Foundation.PropertyType.StringArray),
                };
                try { await encoder.BitmapProperties.SetPropertiesAsync(proprietaTag); } catch { /* alcuni encoder non supportano tutte le proprietà: non deve bloccare l'export */ }
            }

            await encoder.FlushAsync();
        }
        catch
        {
            // Se la codifica o la scrittura fallisce, non lasciare un JPEG troncato nella destinazione.
            try { File.Delete(percorsoOutput); } catch { /* conserva l'errore originale */ }
            throw;
        }
    }

    private static async Task CopiaMetadatiOriginaliAsync(BitmapEncoder encoder, string percorsoOriginale)
    {
        try
        {
            using var fs = File.OpenRead(percorsoOriginale);
            using var winrtStream = fs.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(winrtStream);

            var valori = await decoder.BitmapProperties.GetPropertiesAsync(ProprietaDaCopiare);
            if (valori.Count == 0) return;

            var daScrivere = new BitmapPropertySet();
            foreach (var chiave in valori.Keys)
                daScrivere[chiave] = valori[chiave];

            await encoder.BitmapProperties.SetPropertiesAsync(daScrivere);
        }
        catch
        {
            // Metadati originali non leggibili/copiabili: l'export prosegue comunque senza EXIF/GPS.
        }
    }
}
