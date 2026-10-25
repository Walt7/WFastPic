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
    /// <param name="cartelleNonLette">
    /// Riceve i percorsi relativi delle sottocartelle che non si sono potute leggere (permessi, rete):
    /// le loro foto non sono sparite, quindi il sidecar non deve dimenticarle (<c>SyncScan</c>).
    /// </param>
    public static IReadOnlyList<ScannedPhoto> Scan(
        string cartella,
        Func<PhotoIdentity, ScannedPhoto?>? giaNote = null,
        IProgress<(int Elaborate, int Totale)>? progresso = null,
        ICollection<string>? cartelleNonLette = null)
    {
        // Elenco completo prima di leggere i metadati: serve il totale per una barra di avanzamento vera.
        var nonLette = new List<string>();
        var percorsi = ElencaFoto(cartella, radice: true, nonLette).ToList();
        foreach (var nonLetta in nonLette) cartelleNonLette?.Add(Path.GetRelativePath(cartella, nonLetta));

        var risultato = new List<ScannedPhoto>(percorsi.Count);
        progresso?.Report((0, percorsi.Count));

        foreach (var path in percorsi)
        {
            var info = new FileInfo(path);
            long dimensione;
            try { dimensione = info.Length; }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                continue; // spostato o cancellato mentre si leggeva la cartella (OneDrive, Esplora file)
            }
            var identity = new PhotoIdentity(
                PathRelativo: Path.GetRelativePath(cartella, path),
                Size: dimensione,
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

    /// <summary>File che segna una cartella di export: la scansione non la visita (i JPEG lì dentro sono copie).</summary>
    public const string FileSegnoExport = ".wfastpic-export";

    /// <summary>
    /// Segna come cartella di export una cartella senza foto proprie (nuova o già segnata). Una cartella con
    /// foto non si segna mai: sparirebbero dalla scansione e il sidecar le dimenticherebbe.
    /// </summary>
    public static void SegnaCartellaExport(string cartella)
    {
        // "Export" (la cartella predefinita) e "Scarti" la scansione li salta già per nome.
        if (CartelleEscluse.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(cartella)), StringComparer.OrdinalIgnoreCase)) return;
        var segno = Path.Combine(cartella, FileSegnoExport);
        if (File.Exists(segno)) return;
        var conFoto = Directory.EnumerateFiles(cartella)
            .Any(p => EstensioniSupportate.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));
        if (conFoto) return;
        File.WriteAllText(segno, "Cartella di export di WFastPic: le foto qui dentro sono copie e non vengono catalogate.\r\n");
        File.SetAttributes(segno, File.GetAttributes(segno) | FileAttributes.Hidden);
    }

    /// <summary>
    /// Vero se la scansione di <paramref name="radice"/> arriverebbe a <paramref name="cartella"/> (che deve
    /// esserci dentro, o coincidere): nessuna cartella del percorso è nascosta, Scarti, Export, di export.
    /// </summary>
    public static bool ScansioneVedrebbe(string radice, string cartella)
    {
        var relativo = Path.GetRelativePath(radice, cartella);
        if (relativo == ".") return true;
        if (relativo.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativo)) return false;
        var corrente = Path.GetFullPath(radice);
        foreach (var parte in relativo.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            corrente = Path.Combine(corrente, parte);
            var info = new DirectoryInfo(corrente);
            if (info.Exists && DaSaltare(info)) return false;
            if (!info.Exists && (CartelleEscluse.Contains(parte, StringComparer.OrdinalIgnoreCase) || parte.StartsWith('.'))) return false;
        }
        return true;
    }

    /// <summary>
    /// Sottocartelle che la scansione non visita: nascoste, di sistema, Scarti, Export e le cartelle di
    /// export segnate (<see cref="FileSegnoExport"/>), e i collegamenti
    /// (junction, link simbolici), che possono puntare a una cartella sopra e far girare la scansione
    /// all'infinito. I segnaposto di OneDrive non sono collegamenti (<c>LinkTarget</c> nullo) e si visitano.
    /// </summary>
    public static bool DaSaltare(DirectoryInfo cartella) =>
        ArchivioCartelle.DaNascondere(cartella.Name, cartella.Attributes)
        || CartelleEscluse.Contains(cartella.Name, StringComparer.OrdinalIgnoreCase)
        || File.Exists(Path.Combine(cartella.FullName, FileSegnoExport))
        || (cartella.Attributes.HasFlag(FileAttributes.ReparsePoint) && ECollegamento(cartella));

    private static bool ECollegamento(DirectoryInfo cartella)
    {
        try { return cartella.LinkTarget is not null; }
        catch (IOException) { return true; } // collegamento illeggibile: meglio saltarlo
    }

    private static IEnumerable<string> ElencaFoto(string cartella, bool radice, List<string> nonLette)
    {
        // Una sottocartella illeggibile conta per intero come non letta, anche se metà era già elencata.
        List<string> foto, figli;
        try
        {
            foto = Directory.EnumerateFiles(cartella)
                .Where(p => EstensioniSupportate.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .ToList();
            figli = new DirectoryInfo(cartella).EnumerateDirectories()
                .Where(d => !DaSaltare(d))
                .Select(d => d.FullName)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (!radice && ex is UnauthorizedAccessException or IOException)
        {
            nonLette.Add(cartella); // sottocartella protetta, disco di rete che non risponde, rimossa adesso
            yield break;
        }

        foreach (var file in foto) yield return file;
        foreach (var figlio in figli)
        {
            foreach (var file in ElencaFoto(figlio, radice: false, nonLette)) yield return file;
        }
    }
}
