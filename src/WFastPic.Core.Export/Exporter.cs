using Windows.Graphics.Imaging;
using WFastPic.Core.Decoding;
using WFastPic.Core.Metadata;
using WFastPic.Core.Persistence;
using WFastPic.Core.Processing;
using WFastPic.Core.Selection;

namespace WFastPic.Core.Export;

public sealed record RisultatoExport(long PhotoId, string PathRelativo, string PercorsoOutput, bool Successo, string? Errore);
public sealed record ProgressoExport(int Completate, int Totale, string FileCorrente);

/// <summary>
/// Orchestrazione dell'export batch: decodifica (convertita in sRGB) alla dimensione richiesta,
/// applica la ricetta salvata (raddrizza+crop+tono), filigrana opzionale, poi codifica JPEG con
/// tag personalizzati. Le foto originali non vengono mai toccate.
/// Il lavoro pesante gira fuori dal thread del chiamante (ConfigureAwait(false) + calcolo sul pool):
/// il dialog resta reattivo anche esportando foto da 48 MP alle dimensioni originali.
/// </summary>
public static class Exporter
{
    /// <summary>Selezionate, oppure tutte tranne gli scarti: uno scarto non va mai esportato.</summary>
    public static IReadOnlyList<PhotoRecord> FotoDaEsportare(LibrarySidecarDatabase db, ExportOptions opzioni) =>
        opzioni.SoloSelezionate
            ? db.GetByStato(StatoSelezione.Selected)
            : db.GetAllPhotos().Where(p => p.Stato != StatoSelezione.Reject).ToList();

    /// <summary>Foto elaborate insieme (misurato su 12 core: oltre 4 non si guadagna, la decodifica HEVC satura).</summary>
    public const int FotoInParallelo = 4;

    /// <summary>Alle dimensioni originali ogni foto occupa centinaia di MB (48 MP ≈ 190 MB per buffer): meno insieme.</summary>
    public const int FotoInParalleloOriginali = 3;

    /// <summary>
    /// Esporta più foto insieme (decodifica, ricetta e codifica sono indipendenti tra foto). L'annullamento
    /// ferma l'avvio di nuove foto (quelle in corso vengono completate, mai lasciate a metà) e restituisce
    /// i risultati fin lì, nell'ordine dell'elenco.
    /// </summary>
    public static async Task<IReadOnlyList<RisultatoExport>> EseguiAsync(
        LibrarySidecarDatabase db,
        ExportOptions opzioni,
        IProgress<ProgressoExport>? progresso = null,
        CancellationToken ct = default)
    {
        var foto = FotoDaEsportare(db, opzioni);
        if (foto.Count == 0) return [];

        Directory.CreateDirectory(opzioni.CartellaOutput);

        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var latoLungo = opzioni.LatoLungoMassimo is int lato
            ? Math.Clamp(lato, ExportOptions.LatoLungoMinimo, ExportOptions.LatoLungoMassimoConsentito)
            : int.MaxValue;

        var risultati = new RisultatoExport?[foto.Count];
        var completate = 0;
        var bloccoProgresso = new object();

        // Con più foto insieme i report arrivano da thread diversi: sotto lock, così il conteggio mostrato
        // non torna mai indietro (barra e tempo previsto restano stabili).
        void Segnala(string file, bool finita)
        {
            lock (bloccoProgresso)
            {
                if (finita) completate++;
                progresso?.Report(new ProgressoExport(completate, foto.Count, file));
            }
        }
        var grado = Math.Clamp(Environment.ProcessorCount / 2, 1,
            latoLungo > 4096 ? FotoInParalleloOriginali : FotoInParallelo);

        // Senza il token in Parallel: l'annullamento non deve lanciare, solo non far partire altre foto.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, foto.Count),
            new ParallelOptions { MaxDegreeOfParallelism = grado },
            async (i, _) =>
            {
                if (ct.IsCancellationRequested) return;

                var record = foto[i];
                var percorsoOriginale = Path.Combine(db.CartellaFoto, record.PathRelativo);
                Segnala(record.PathRelativo, finita: false);

                try
                {
                    var ricettaJson = db.GetRecipeJson(record.Id);
                    var ricetta = ricettaJson is not null ? EditRecipeSerializer.FromJson(ricettaJson) : EditRecipe.Default;

                    var (bgra, w, h) = await DecodificaInSrgbAsync(percorsoOriginale, latoLungo).ConfigureAwait(false);

                    var (elaborato, ew, eh) = await Task.Run(() =>
                    {
                        var r = RecipeApplier.Applica(bgra, w, h, ricetta);
                        if (!string.IsNullOrWhiteSpace(opzioni.TestoWatermark))
                            WatermarkRenderer.Applica(r.Bgra, r.Larghezza, r.Altezza, opzioni.TestoWatermark, opzioni.OpacitaWatermark, opzioni.PosizioneWatermark);
                        return r;
                    }).ConfigureAwait(false);

                    var nomeBase = Path.GetFileNameWithoutExtension(record.PathRelativo);
                    string percorsoOutput;
                    // Nomi unici (foto con lo stesso nome in sottocartelle diverse): la scelta è una per volta.
                    lock (usati)
                    {
                        percorsoOutput = NomiFileExport.PercorsoOutput(
                            opzioni.CartellaOutput, db.CartellaFoto, nomeBase, opzioni.SostituisciEsistenti, usati, File.Exists);
                    }

                    var (dataScatto, posizione) = MetadatiDaScrivere(record, percorsoOriginale, opzioni);

                    await JpegEncoder.SalvaAsync(percorsoOutput, elaborato, ew, eh, opzioni.QualitaJpeg,
                        dataScatto, posizione, opzioni.Tag).ConfigureAwait(false);

                    risultati[i] = new RisultatoExport(record.Id, record.PathRelativo, percorsoOutput, true, null);
                }
                catch (Exception ex)
                {
                    risultati[i] = new RisultatoExport(record.Id, record.PathRelativo, "", false, ex.Message);
                }

                Segnala(record.PathRelativo, finita: true);
            }).ConfigureAwait(false);

        return risultati.Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>
    /// Data e posizione dal sidecar (già lette dall'EXIF alla scansione). La posizione corretta a
    /// mano nell'editor vince su quella del file; se l'utente l'ha tolta, o chiede di rimuovere il
    /// GPS, non se ne scrive nessuna. Il file si rilegge solo se il sidecar non ha ancora i dati.
    /// Per una posizione del file si conservano anche altitudine e direzione della camera (droni,
    /// telefoni); una posizione corretta a mano non ha né l'una né l'altra.
    /// </summary>
    private static (DateTime? Data, PosizioneExport? Posizione) MetadatiDaScrivere(
        PhotoRecord record, string percorsoOriginale, ExportOptions opzioni)
    {
        var data = record.CaptureDate;
        var posizione = record is { Latitude: double lat, Longitude: double lon }
            ? new PosizioneExport(lat, lon, record.Altitude)
            : null;

        if (data is null || (posizione is null && !record.GpsManuale && !opzioni.RimuoviGps))
        {
            var exif = PhotoMetadataReader.Read(percorsoOriginale);
            data ??= exif.CaptureDate;
            if (posizione is null && !record.GpsManuale && exif.Gps is { } gps)
                posizione = new PosizioneExport(gps.Latitude, gps.Longitude, gps.Altitude);
        }

        if (opzioni.RimuoviGps || posizione is null) return (data, null);

        if (!record.GpsManuale && PhotoMetadataReader.LeggiDatiRipresa(percorsoOriginale).DirezioneGradi is double direzione)
            posizione = posizione with { DirezioneGradi = direzione };

        return (data, posizione);
    }

    // Le foto iPhone sono in Display P3: senza conversione il JPEG (letto come sRGB) esce spento.
    // Se il decoder non sa convertire quel file si ripiega sui valori così come sono.
    private static async Task<(byte[] Bgra, int Width, int Height)> DecodificaInSrgbAsync(string percorso, int latoLungo)
    {
        try
        {
            return await PreviewDecoder.DecodePreviewBgraAsync(percorso, latoLungo, ColorManagementMode.ColorManageToSRgb).ConfigureAwait(false);
        }
        catch (Exception) when (File.Exists(percorso))
        {
            return await PreviewDecoder.DecodePreviewBgraAsync(percorso, latoLungo).ConfigureAwait(false);
        }
    }
}
