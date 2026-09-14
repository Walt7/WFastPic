using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace WFastPic.Core.Metadata;

public static class CaptureDateReader
{
    /// <summary>
    /// Legge la data di scatto (EXIF DateTimeOriginal) da un file immagine.
    /// Indipendente dal decoder immagine: funziona anche se il decoder pixel non è disponibile.
    /// </summary>
    public static DateTime? TryRead(string filePath) =>
        PhotoMetadataReader.Read(filePath).CaptureDate;
}
