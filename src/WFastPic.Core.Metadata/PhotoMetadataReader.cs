using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace WFastPic.Core.Metadata;

public sealed record PhotoExifMetadata(DateTime? CaptureDate, GeoCoordinate? Gps);

public static class PhotoMetadataReader
{
    /// <summary>
    /// Legge in un unico passaggio data di scatto e coordinate GPS da un file immagine.
    /// Non dipende dal decoder dei pixel.
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
                gps = new GeoCoordinate(location.Latitude, location.Longitude);
            }
        }
        catch
        {
            // File senza EXIF leggibile
        }

        return new PhotoExifMetadata(captureDate, gps);
    }
}
