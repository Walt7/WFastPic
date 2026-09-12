using WFastPic.Core.Metadata;

namespace WFastPic.Core.Library;

public static class FolderScanner
{
    private static readonly string[] EstensioniSupportate = [".heic", ".heif", ".jpg", ".jpeg"];

    /// <summary>
    /// Scansiona una singola cartella (non ricorsiva) e restituisce identità leggera + data scatto
    /// per ogni foto supportata. Nessun hash di contenuto: costo per file è un file-stat + una lettura EXIF.
    /// </summary>
    public static IReadOnlyList<ScannedPhoto> Scan(string cartella)
    {
        var risultato = new List<ScannedPhoto>();

        foreach (var path in Directory.EnumerateFiles(cartella))
        {
            var estensione = Path.GetExtension(path);
            if (!EstensioniSupportate.Contains(estensione, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(path);
            var identity = new PhotoIdentity(
                PathRelativo: Path.GetFileName(path),
                Size: info.Length,
                LastWriteUtc: info.LastWriteTimeUtc);

            var captureDate = CaptureDateReader.TryRead(path);
            risultato.Add(new ScannedPhoto(identity, captureDate));
        }

        return risultato;
    }
}
