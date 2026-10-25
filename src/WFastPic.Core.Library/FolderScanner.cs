using WFastPic.Core.Metadata;

namespace WFastPic.Core.Library;

public static class FolderScanner
{
    private static readonly string[] EstensioniSupportate = [".heic", ".heif", ".jpg", ".jpeg"];

    // Cartelle prodotte dall'app stessa: rileggerle riporterebbe in libreria scarti ed export.
    private static readonly string[] CartelleEscluse = ["Scarti", "Export"];

    /// <summary>
    /// Scansiona la cartella e le sue sottocartelle (nascoste, di sistema, Scarti ed Export escluse) e
    /// restituisce identità leggera + data scatto per ogni foto supportata; <c>PathRelativo</c> è il
    /// percorso dentro la cartella ("Giorno 2\IMG_1.HEIC"). Nessun hash di contenuto: costo per file è
    /// un file-stat + una lettura EXIF.
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
        var percorsi = ElencaFoto(cartella, radice: true).ToList();

        var risultato = new List<ScannedPhoto>(percorsi.Count);
        progresso?.Report((0, percorsi.Count));

        foreach (var path in percorsi)
        {
            var info = new FileInfo(path);
            var identity = new PhotoIdentity(
                PathRelativo: Path.GetRelativePath(cartella, path),
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

    /// <summary>Sottocartelle che la scansione non visita: nascoste, di sistema, Scarti ed Export.</summary>
    public static bool DaSaltare(DirectoryInfo cartella) =>
        ArchivioCartelle.DaNascondere(cartella.Name, cartella.Attributes)
        || CartelleEscluse.Contains(cartella.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ElencaFoto(string cartella, bool radice)
    {
        foreach (var file in FotoDiretteDi(cartella, radice)) yield return file;

        foreach (var figlio in Sottocartelle(cartella, radice))
        {
            foreach (var file in ElencaFoto(figlio, radice: false)) yield return file;
        }
    }

    private static List<string> FotoDiretteDi(string cartella, bool radice)
    {
        try
        {
            return Directory.EnumerateFiles(cartella)
                .Where(p => EstensioniSupportate.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (!radice && ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static List<string> Sottocartelle(string cartella, bool radice)
    {
        try
        {
            return new DirectoryInfo(cartella).EnumerateDirectories()
                .Where(d => !DaSaltare(d))
                .Select(d => d.FullName)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (!radice && ex is UnauthorizedAccessException or IOException)
        {
            return []; // sottocartella protetta o rimossa durante la scansione
        }
    }
}
