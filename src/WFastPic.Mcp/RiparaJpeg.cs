using WFastPic.Core.Decoding;

namespace WFastPic.Mcp;

/// <summary>
/// «WFastPic.Mcp ripara-jpeg [esegui] [massimo N]»: sistema i JPEG con intestazione anomala elencati dall'analisi
/// (jpeg-anomali.txt). Senza «esegui» è solo l'anteprima. Prima di toccare un file ne salva l'originale in una cartella di
/// backup, poi controlla che la copia riparata si decodifichi con gli stessi identici pixel; se qualcosa non torna, lascia
/// il file com'era. Data di creazione e di modifica restano quelle di prima.
/// </summary>
internal static class RiparaJpeg
{
    public static async Task<int> EseguiAsync(string[] args)
    {
        var esegui = args.Any(a => a.Equals("esegui", StringComparison.OrdinalIgnoreCase));
        var massimo = args.SkipWhile(a => !a.Equals("massimo", StringComparison.OrdinalIgnoreCase)).Skip(1).Select(a => int.TryParse(a, out var n) ? n : 0).FirstOrDefault();

        if (!File.Exists(AnalisiCartella.FileJpegAnomali))
        {
            Console.WriteLine("Nessun JPEG anomalo segnalato finora (manca " + AnalisiCartella.FileJpegAnomali + ").");
            return 0;
        }

        var daRiparare = File.ReadLines(AnalisiCartella.FileJpegAnomali)
            .Select(r => r.Split('|')[0]).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (massimo > 0) daRiparare = daRiparare.Take(massimo).ToList();
        Console.WriteLine($"{daRiparare.Count} JPEG da riparare{(esegui ? "" : " (anteprima: non scrivo niente, aggiungi «esegui»)")}.");

        int fatti = 0, saltati = 0;
        foreach (var percorso in daRiparare)
        {
            try
            {
                var esito = await RiparaUnoAsync(percorso, esegui);
                Console.WriteLine($"{(esito.Ok ? "OK     " : "SALTATO")} {percorso} — {esito.Messaggio}");
                if (esito.Ok) fatti++; else saltati++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"SALTATO {percorso} — {ex.Message}");
                saltati++;
            }
        }

        Console.WriteLine($"{fatti} {(esegui ? "riparati" : "riparabili")}, {saltati} saltati.");
        return saltati == 0 ? 0 : 2;
    }

    private static string PercorsoBackup(string percorso)
    {
        var radice = Path.GetPathRoot(percorso)!;
        return Path.Combine(radice, "Fotocamera-backup-jpeg", percorso[radice.Length..]);
    }

    private static async Task<(bool Ok, string Messaggio)> RiparaUnoAsync(string percorso, bool esegui)
    {
        var originale = await File.ReadAllBytesAsync(percorso);
        var riparato = IntestazioneJpeg.Ripara(originale);
        if (riparato is null) return (false, "intestazione già regolare");
        if (riparato.Length != originale.Length) return (false, "la riparazione cambierebbe la dimensione: non la faccio");

        // Stessi pixel prima e dopo (a risoluzione ridotta: veloce, e il decodificatore è lo stesso dell'app).
        var (pa, wa, ha) = await PreviewDecoder.DecodePreviewBgraAsync(originale, 512);
        var (pb, wb, hb) = await PreviewDecoder.DecodePreviewBgraAsync(riparato, 512);
        if (wa != wb || ha != hb || !pa.AsSpan().SequenceEqual(pb))
            return (false, "dopo la riparazione l'immagine non è identica: lascio il file com'è");

        var daCorreggere = 0;
        for (var i = 0; i < originale.Length; i++) if (originale[i] != riparato[i]) daCorreggere++;
        if (!esegui) return (true, $"{daCorreggere} byte da correggere, immagine identica");

        var info = new FileInfo(percorso);
        var (creato, modificato, attributi) = (info.CreationTimeUtc, info.LastWriteTimeUtc, info.Attributes);

        var backup = PercorsoBackup(percorso);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (!File.Exists(backup)) File.Copy(percorso, backup);
        if (new FileInfo(backup).Length != originale.Length) return (false, "il backup non è completo: non tocco il file");

        var temporaneo = percorso + ".riparazione";
        await File.WriteAllBytesAsync(temporaneo, riparato);
        if (info.IsReadOnly) info.IsReadOnly = false;
        File.Move(temporaneo, percorso, overwrite: true);
        File.SetCreationTimeUtc(percorso, creato);
        File.SetLastWriteTimeUtc(percorso, modificato);
        File.SetAttributes(percorso, attributi);
        return (true, $"{daCorreggere} byte corretti (originale in {backup})");
    }
}
