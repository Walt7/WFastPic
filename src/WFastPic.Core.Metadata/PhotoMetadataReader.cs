using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Xmp;

namespace WFastPic.Core.Metadata;

public sealed record PhotoExifMetadata(DateTime? CaptureDate, GeoCoordinate? Gps);

public static class PhotoMetadataReader
{
    /// <summary>
    /// Legge in un unico passaggio data di scatto e coordinate GPS (con altitudine e direzione della
    /// camera, anche dall'XMP dei droni DJI) da un file immagine. Non dipende dal decoder dei pixel.
    /// </summary>
    public static PhotoExifMetadata Read(string filePath)
    {
        DateTime? captureDate = null;
        GeoCoordinate? gps = null;

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifSubIfd is not null && exifSubIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime))
            {
                captureDate = dateTime;
            }

            var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
            var location = gpsDir?.GetGeoLocation();
            if (location is { IsZero: false })
            {
                var ripresa = Analizza(directories);
                gps = new GeoCoordinate(location.Latitude, location.Longitude, ripresa.AltitudineMetri, ripresa.DirezioneGradi);
            }
        }
        catch
        {
            // File senza EXIF leggibile
        }

        return new PhotoExifMetadata(captureDate, gps);
    }

    /// <summary>
    /// Dettagli di ripresa per pannello Luogo e mappa, letti su richiesta (una foto alla volta):
    /// dispositivo, altitudine, direzione della camera (EXIF GPSImgDirection, presente anche sugli
    /// iPhone) e, per i droni DJI, altezza dal punto di decollo e inclinazione del gimbal dall'XMP.
    /// </summary>
    public static DatiRipresa LeggiDatiRipresa(string filePath)
    {
        try
        {
            return Analizza(ImageMetadataReader.ReadMetadata(filePath));
        }
        catch
        {
            return DatiRipresa.Vuoti;
        }
    }

    private static DatiRipresa Analizza(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();

        double? direzione = null;
        if (gpsDir is not null && gpsDir.TryGetRational(GpsDirectory.TagImgDirection, out var dir) && dir.Denominator != 0)
            direzione = dir.ToDouble();

        var xmp = directories.OfType<XmpDirectory>()
            .SelectMany(d => d.GetXmpProperties())
            .GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        return DatiRipresa.Componi(
            gpsDir is null ? null : LeggiAltitudine(gpsDir), direzione, xmp,
            ifd0?.GetString(ExifDirectoryBase.TagMake), ifd0?.GetString(ExifDirectoryBase.TagModel));
    }

    // GPSAltitude (metri) + GPSAltitudeRef: 1 = sotto il livello del mare.
    private static double? LeggiAltitudine(GpsDirectory gpsDir)
    {
        if (!gpsDir.TryGetRational(GpsDirectory.TagAltitude, out var altitudine) || altitudine.Denominator == 0)
            return null;

        var metri = altitudine.ToDouble();
        return gpsDir.TryGetByte(GpsDirectory.TagAltitudeRef, out var riferimento) && riferimento == 1 ? -metri : metri;
    }
}
