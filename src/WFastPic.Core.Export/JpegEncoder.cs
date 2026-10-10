using System.Globalization;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace WFastPic.Core.Export;

/// <summary>
/// Codifica un buffer BGRA8 in JPEG via WIC con data di scatto, posizione GPS e tag dell'utente.
/// I metadati si scrivono con query WIC dirette ("/app1/ifd/..."): l'encoder JPEG ignora in silenzio
/// i nomi del property system come "System.GPS.Latitude", quindi copiarli dall'originale non
/// funzionava. Data e posizione arrivano già lette (sidecar/EXIF) dal chiamante.
/// Scrive su un file temporaneo e lo rinomina solo a codifica finita: un export fallito non lascia
/// JPEG troncati e non distrugge quello di un export precedente.
/// </summary>
/// <summary>Posizione da scrivere nel JPEG; altitudine e direzione della camera se note.</summary>
public sealed record PosizioneExport(double Latitudine, double Longitudine, double? AltitudineMetri = null, double? DirezioneGradi = null);

public static class JpegEncoder
{
    public static async Task SalvaAsync(
        string percorsoOutput, byte[] bgra, int larghezza, int altezza, int qualitaJpeg,
        DateTime? dataScatto = null,
        PosizioneExport? posizione = null,
        IReadOnlyList<string>? tag = null)
    {
        var temporaneo = percorsoOutput + ".part";
        try
        {
            using (var fileStream = File.Create(temporaneo))
            using (var winrtStream = fileStream.AsRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, winrtStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)larghezza, (uint)altezza, 96, 96, bgra);

                var proprietaQualita = new BitmapPropertySet
                {
                    ["ImageQuality"] = new BitmapTypedValue(Math.Clamp(qualitaJpeg, 1, 100) / 100.0f, PropertyType.Single),
                };
                await encoder.BitmapProperties.SetPropertiesAsync(proprietaQualita);

                // Gruppi separati: se uno non viene accettato gli altri restano.
                if (dataScatto is DateTime data) await ProvaAScrivereAsync(encoder, ProprietaDataScatto(data));
                if (posizione is { } p) await ProvaAScrivereAsync(encoder, ProprietaPosizione(p));
                if (tag is { Count: > 0 }) await ProvaAScrivereAsync(encoder, ProprietaTag(tag));

                await encoder.FlushAsync();
            }

            File.Move(temporaneo, percorsoOutput, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporaneo); } catch { /* conserva l'errore originale */ }
            throw;
        }
    }

    /// <summary>DateTimeOriginal (e DateTime) EXIF: "yyyy:MM:dd HH:mm:ss", ora locale dello scatto.</summary>
    private static BitmapPropertySet ProprietaDataScatto(DateTime data)
    {
        var testo = data.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
        return new BitmapPropertySet
        {
            ["/app1/ifd/{ushort=306}"] = new BitmapTypedValue(testo, PropertyType.String),
            ["/app1/ifd/exif/{ushort=36867}"] = new BitmapTypedValue(testo, PropertyType.String),
            ["/app1/ifd/exif/{ushort=36868}"] = new BitmapTypedValue(testo, PropertyType.String),
        };
    }

    /// <summary>
    /// EXIF GPS. I RATIONAL EXIF passano come UInt64 (numeratore nei 32 bit bassi, denominatore in
    /// quelli alti): gradi, minuti, secondi positivi + riferimento N/S, E/W.
    /// </summary>
    public static BitmapPropertySet ProprietaPosizione(PosizioneExport posizione)
    {
        var (latitudine, longitudine) = (posizione.Latitudine, posizione.Longitudine);
        var proprieta = new BitmapPropertySet
        {
            ["/app1/ifd/gps/{ushort=0}"] = new BitmapTypedValue(new byte[] { 2, 2, 0, 0 }, PropertyType.UInt8Array),
            ["/app1/ifd/gps/{ushort=1}"] = new BitmapTypedValue(latitudine >= 0 ? "N" : "S", PropertyType.String),
            ["/app1/ifd/gps/{ushort=2}"] = new BitmapTypedValue(GradiMinutiSecondi(latitudine), PropertyType.UInt64Array),
            ["/app1/ifd/gps/{ushort=3}"] = new BitmapTypedValue(longitudine >= 0 ? "E" : "W", PropertyType.String),
            ["/app1/ifd/gps/{ushort=4}"] = new BitmapTypedValue(GradiMinutiSecondi(longitudine), PropertyType.UInt64Array),
        };

        // GPSAltitudeRef (0 = sopra il livello del mare) + GPSAltitude in centimetri di precisione.
        if (posizione.AltitudineMetri is double altitudine)
        {
            proprieta["/app1/ifd/gps/{ushort=5}"] = new BitmapTypedValue(new byte[] { (byte)(altitudine < 0 ? 1 : 0) }, PropertyType.UInt8Array);
            proprieta["/app1/ifd/gps/{ushort=6}"] = new BitmapTypedValue(Razionale((uint)Math.Round(Math.Abs(altitudine) * 100), 100), PropertyType.UInt64);
        }

        // GPSImgDirectionRef "T" (nord vero) + GPSImgDirection: verso dove guardava la camera.
        if (posizione.DirezioneGradi is double direzione)
        {
            proprieta["/app1/ifd/gps/{ushort=16}"] = new BitmapTypedValue("T", PropertyType.String);
            proprieta["/app1/ifd/gps/{ushort=17}"] = new BitmapTypedValue(Razionale((uint)Math.Round(direzione * 100), 100), PropertyType.UInt64);
        }

        return proprieta;
    }

    private static ulong Razionale(uint numeratore, uint denominatore) => ((ulong)denominatore << 32) | numeratore;

    /// <summary>
    /// Keywords come XPKeywords EXIF (letti da Esplora file, "Tag") e dc:subject XMP (Lightroom &amp; co.).
    /// XPKeywords è UTF-16LE terminato da zero, separato da ";".
    /// </summary>
    private static BitmapPropertySet ProprietaTag(IReadOnlyList<string> tag)
    {
        var xp = System.Text.Encoding.Unicode.GetBytes(string.Join(";", tag) + "\0");
        return new BitmapPropertySet
        {
            ["/app1/ifd/{ushort=40094}"] = new BitmapTypedValue(xp, PropertyType.UInt8Array),
        };
    }

    private static ulong[] GradiMinutiSecondi(double valore)
    {
        const uint denominatoreSecondi = 10000;

        var assoluto = Math.Abs(valore);
        var gradi = Math.Floor(assoluto);
        var minutiDecimali = (assoluto - gradi) * 60;
        var minuti = Math.Floor(minutiDecimali);
        var secondi = (minutiDecimali - minuti) * 60;
        return
        [
            Razionale((uint)gradi, 1),
            Razionale((uint)minuti, 1),
            Razionale((uint)Math.Round(secondi * denominatoreSecondi), denominatoreSecondi),
        ];
    }

    private static async Task ProvaAScrivereAsync(BitmapEncoder encoder, BitmapPropertySet proprieta)
    {
        if (proprieta.Count == 0) return;
        try { await encoder.BitmapProperties.SetPropertiesAsync(proprieta); }
        catch { /* l'encoder non accetta queste proprietà: non deve bloccare l'export */ }
    }
}
