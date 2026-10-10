using WFastPic.Core.Metadata;

namespace WFastPic.Core.Library;

public static class FolderScanner
{
    private static readonly string[] EstensioniSupportate = [".heic", ".heif", ".jpg", ".jpeg"];

    /// <summary>
    /// Scansiona una singola cartella (non ricorsiva) e restituisce identità leggera + data scatto
    /// per ogni foto supportata. Nessun hash di contenuto: costo per file è un file-stat + una lettura EXIF.
    /// </summary>
    /// <param name="giaNote">
    /// Metadati già noti (es. dal sidecar) per un'identità invariata: se restituisce un valore la
    /// lettura EXIF viene saltata, così riaprire una cartella già vista costa solo i file-stat.
    /// </param>
    /// <param name="progresso">Riceve (elaborate, totale) dopo ogni file.</param>
    public static IReadOnlyList<ScannedPhoto> Scan(
        string cartella,
        Func<PhotoIdentity, ScannedPhoto?>? giaNote = null,
        IProgress<(int Elaborate, int Totale)>? progresso = null)
    {
        // Elenco completo prima di leggere i metadati: serve il totale per una barra di avanzamento vera.
        var percorsi = Directory.EnumerateFiles(cartella)
            .Where(p => EstensioniSupportate.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var risultato = new List<ScannedPhoto>(percorsi.Count);
        progresso?.Report((0, percorsi.Count));

        foreach (var path in percorsi)
        {
            var info = new FileInfo(path);
            var identity = new PhotoIdentity(
                PathRelativo: Path.GetFileName(path),
                Size: info.Length,
                LastWriteUtc: info.LastWriteTimeUtc);

            var nota = giaNote?.Invoke(identity);
            if (nota is not null)
            {
                risultato.Add(nota);
            }
            else
            {
                var metadata = PhotoMetadataReader.Read(path);
                risultato.Add(new ScannedPhoto(identity, metadata.CaptureDate, metadata.Gps));
            }

            progresso?.Report((risultato.Count, percorsi.Count));
        }

        return risultato;
    }
}
