using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace WFastPic.Core.Metadata;

public static class CaptureDateReader
{
    /// <summary>
    /// Legge la data di scatto (EXIF DateTimeOriginal) da un file immagine.
    /// Indipendente dal decoder immagine: funziona anche se il decoder pixel non è disponibile.
    /// </summary>
    public static DateTime? TryRead(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifSubIfd is not null && exifSubIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime))
            {
                return dateTime;
            }
        }
        catch
        {
            // File senza EXIF leggibile (es. JPG generico): nessuna data scatto disponibile.
        }

        return null;
    }
}
