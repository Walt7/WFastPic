using System.Threading.Channels;
using System.Diagnostics;
using System.Text.Json;
using WFastPic.Core.Decoding;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;

namespace WFastPic.Mcp;

/// <summary>Dove è arrivata un'analisi dei volti: fase (lettura della cartella, volti, finita…), foto fatte su quante.</summary>
public sealed record StatoAnalisi(
    string Cartella, string Catalogo, string Fase, int Fatte, int Totale, int Volti, int Errori,
    DateTime Inizio, DateTime Aggiornato, bool Finita, string? Errore, string? Avanzamento, int Processo, int JpegAnomali = 0);

/// <summary>
/// Cerca e riconosce i volti di una cartella (sottocartelle comprese) senza aprire l'app, come la ricerca in background
/// dell'app: scansione e catalogo (.wfastpic, lo stesso che crea l'app), poi una foto alla volta con YuNet + SFace,
/// risultato salvato subito nel sidecar. Le foto già analizzate si saltano, quindi si può fermare e riprendere quando
/// si vuole (anche tutto l'archivio, in più volte). Lo stato è in <c>analisi-volti.json</c>, leggibile anche da un altro
/// processo (es. l'analisi lanciata dal terminale con «WFastPic.Mcp analizza &lt;cartella&gt;»).
/// </summary>
public static class AnalisiCartella
{
    private static readonly Lock Lucchetto = new();
    private static readonly HttpClient Http = CreaClient();
    private static Task? _lavoro;
    private static CancellationTokenSource? _cts;
    private static StatoAnalisi? _stato;

    /// <summary>Per i test: al posto di decode + modelli, i volti di una foto dal suo percorso.</summary>
    public static Func<string, Task<IReadOnlyList<VoltoAnalizzato>>>? AnalizzatoreDiProva { get; set; }

    private static string FileStato => Path.Combine(Ricerca.DatiApp, "analisi-volti.json");

    private static HttpClient CreaClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WFastPic/1.0");
        return http;
    }

    /// <summary>
    /// Il catalogo in cui vanno i volti di <paramref name="cartella"/>: quello della cartella madre più in alto che ne
    /// ha uno (le ricerche leggono quello), altrimenti uno nuovo nella cartella stessa.
    /// </summary>
    public static string Catalogo(string cartella)
    {
        string? trovato = null;
        for (var su = cartella; !string.IsNullOrEmpty(su); su = Path.GetDirectoryName(su))
            if (Ricerca.HaSidecar(su)) trovato = su;
        return trovato ?? cartella;
    }

    /// <summary>
    /// True se <paramref name="radice"/> è una sottocartella del <paramref name="catalogo"/> e tutte le sue foto sono già
    /// nel catalogo (stesso numero dei file su disco): l'inventario dell'intero archivio si può saltare.
    /// </summary>
    internal static bool CartellaGiaCatalogata(LibrarySidecarDatabase db, string catalogo, string radice)
    {
        if (string.Equals(radice, catalogo, StringComparison.OrdinalIgnoreCase)) return false;
        var note = db.GetAllPhotos().Count(f => Ricerca.FotoNellaCartella(Path.Combine(catalogo, f.PathRelativo), radice));
        return note > 0 && note == FolderScanner.ContaFoto(radice);
    }

    /// <summary>Avvia l'analisi in background e torna subito; una sola alla volta.</summary>
    public static StatoAnalisi Avvia(string cartella, int? massimoFoto)
    {
        var radice = Normalizza(cartella);
        lock (Lucchetto)
        {
            if (_lavoro is { IsCompleted: false } && _stato is { } inCorso)
                throw new InvalidOperationException($"C'è già un'analisi in corso su «{inCorso.Cartella}» ({inCorso.Fase}, {inCorso.Fatte} di {inCorso.Totale}). Fermala con stato_analisi ferma=true o aspetta che finisca.");
            if (LeggiFile() is { Finita: false } altro && altro.Processo != Environment.ProcessId && ProcessoVivo(altro.Processo))
                throw new InvalidOperationException($"C'è già un'analisi in corso in un altro processo su «{altro.Cartella}» ({altro.Fatte} di {altro.Totale}).");

            var cts = new CancellationTokenSource();
            _cts = cts;
            _stato = Nuovo(radice, Catalogo(radice), "avvio");
            _lavoro = Task.Run(() => EseguiAsync(radice, massimoFoto, cts.Token));
            return _stato;
        }
    }

    /// <summary>Lo stato dell'analisi di questo processo, o l'ultima scritta su file (anche da un altro processo).</summary>
    public static StatoAnalisi? Stato()
    {
        lock (Lucchetto)
        {
            if (_stato is not null) return _stato;
        }
        return LeggiFile();
    }

    public static bool Ferma()
    {
        lock (Lucchetto)
        {
            if (_lavoro is not { IsCompleted: false }) return false;
            _cts?.Cancel();
            return true;
        }
    }

    /// <summary>Per i test e per la riga di comando: aspetta la fine dell'analisi in corso (se c'è).</summary>
    public static Task AttendiAsync()
    {
        lock (Lucchetto) return _lavoro ?? Task.CompletedTask;
    }

    /// <summary>L'analisi vera e propria; <paramref name="avanzamento"/> riceve ogni aggiornamento dello stato.</summary>
    public static async Task EseguiAsync(string cartella, int? massimoFoto, CancellationToken ct, Action<StatoAnalisi>? avanzamento = null)
    {
        var radice = Normalizza(cartella);
        var catalogo = Catalogo(radice);
        var stato = Nuovo(radice, catalogo, "lettura della cartella");
        var ultimoSuFile = Stopwatch.StartNew();
        void Aggiorna(StatoAnalisi nuovo, bool forzaFile = false)
        {
            stato = nuovo with { Aggiornato = DateTime.Now };
            lock (Lucchetto) _stato = stato;
            avanzamento?.Invoke(stato);
            if (forzaFile || ultimoSuFile.Elapsed > TimeSpan.FromSeconds(2))
            {
                ScriviFile(stato);
                ultimoSuFile.Restart();
            }
        }

        using var _ = NonDormire.Attiva();
        Volatile.Write(ref _jpegAnomali, 0);
        RegistroAttivita.Scrivi("mcp.analisi.avvio", $"{radice} (catalogo {catalogo}){(massimoFoto is int m ? $", al massimo {m} foto" : "")}");
        Aggiorna(stato, forzaFile: true);
        try
        {
            using var db = LibrarySidecarDatabase.OpenOrCreate(catalogo);

            // Come all'apertura nell'app: le foto già lette saltano l'EXIF, i sidecar delle sottocartelle si riversano qui.
            var cronometro = Stopwatch.StartNew();
            var nonLette = new List<string>();
            var progresso = new Subito<(int Elaborate, int Totale)>(p =>
                Aggiorna(stato with { Fatte = p.Elaborate, Totale = p.Totale, Avanzamento = StimaAvanzamento.Descrivi(p.Elaborate, p.Totale, cronometro.Elapsed) }));
            // Se si analizza una sola cartella dentro un archivio grande (E:\Fotocamera: 240.000 foto) e le sue foto sono già
            // tutte nel catalogo, non serve rileggere l'intero archivio: si passa direttamente ai volti.
            var giaCatalogata = await Task.Run(() => CartellaGiaCatalogata(db, catalogo, radice), ct);
            if (giaCatalogata)
                RegistroAttivita.Scrivi("mcp.analisi.salta-inventario", $"{radice}: già catalogata in {catalogo}, inventario dell'archivio saltato");
            else
            {
                var scansione = await Task.Run(() => FolderScanner.Scan(catalogo, db.CreaRisolutoreMetadatiNoti(), progresso, nonLette), ct);
                ct.ThrowIfCancellationRequested();
                db.SyncScan(scansione, nonLette);
                var importate = db.ImportaDaSidecarFigli();
                if (importate > 0) RegistroAttivita.Scrivi("cartella.import-figli", $"{importate} sottocartelle riversate in {catalogo}");
                Ricerca.RegistraCartella(catalogo);
            }

            var daFare = db.GetAllPhotos()
                .Where(f => !f.PersoneAnalizzate)
                .Select(f => (Foto: f, Percorso: Path.Combine(catalogo, f.PathRelativo)))
                .Where(f => Ricerca.FotoNellaCartella(f.Percorso, radice))
                .OrderBy(f => f.Foto.PathRelativo, StringComparer.OrdinalIgnoreCase)
                .Take(massimoFoto is int quante and > 0 ? quante : int.MaxValue)
                .ToList();

            Aggiorna(stato with { Fase = "volti", Fatte = 0, Totale = daFare.Count, Avanzamento = null }, forzaFile: true);
            var analizza = AnalizzatoreDiProva;
            Func<byte[], Task<IReadOnlyList<VoltoAnalizzato>>>? analizzaDati = null;
            AnalizzatorePersone? analizzatore = null;
            try
            {
                if (analizza is null && daFare.Count > 0)
                {
                    Aggiorna(stato with { Fase = "modelli" }, forzaFile: true);
                    await ModelliPersone.AssicuraAsync(Http, ModelliPersone.CartellaPredefinita, ct: ct);
                    analizzatore = new AnalizzatorePersone(ModelliPersone.CartellaPredefinita);
                    var reale = analizzatore;
                    analizzaDati = async dati =>
                    {
                        var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(dati, AnalizzatorePersone.LatoAnalisi);
                        return reale.Analizza(bgra, w, h);
                    };
                    Aggiorna(stato with { Fase = "volti" }, forzaFile: true);
                }

                var persone = Volti.PersoneNote();
                cronometro.Restart();

                // Decodifica + rilevamento + impronte in parallelo (le sessioni ONNX sono thread-safe);
                // il salvataggio resta su un solo thread. L'ordine di arrivo non conta: ogni foto salvata
                // risulta analizzata, quelle non arrivate si riprendono alla prossima analisi.
                var motiviNonLette = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
                var pronte = Channel.CreateBounded<(int Indice, IReadOnlyList<VoltoAnalizzato>? Volti)>(GradoParallelo * 2);
                // Un solo lettore legge i file interi, nell'ordine, su un canale limitato (pochi file in RAM):
                // l'HDD lavora in sequenza invece di far saltare la testina tra 6 file insieme.
                var letti = Channel.CreateBounded<(int Indice, byte[]? Dati)>(GradoParallelo * 2);
                var lettore = Task.Run(async () =>
                {
                    try
                    {
                        for (var i = 0; i < daFare.Count; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            byte[]? dati = null;
                            if (analizzaDati is not null)
                            {
                                try { dati = await File.ReadAllBytesAsync(daFare[i].Percorso, ct); }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* illeggibile: si salta */ }
                                if (dati is not null && IntestazioneJpeg.Problema(dati) is { } problema)
                                {
                                                                        SegnalaJpegAnomalo(daFare[i].Percorso, problema, dati.Length);
                                }
                            }
                            await letti.Writer.WriteAsync((i, dati), ct);
                        }
                        letti.Writer.TryComplete();
                    }
                    catch (Exception ex) { letti.Writer.TryComplete(ex); }
                }, CancellationToken.None);
                var produttori = Task.Run(async () =>
                {
                    try
                    {
                        await Parallel.ForEachAsync(
                            letti.Reader.ReadAllAsync(CancellationToken.None),
                            new ParallelOptions { MaxDegreeOfParallelism = GradoParallelo, CancellationToken = ct },
                            async (lettura, token) =>
                            {
                                IReadOnlyList<VoltoAnalizzato>? trovati = null;
                                try
                                {
                                    trovati = analizzaDati is not null
                                        ? lettura.Dati is null ? null : await analizzaDati(lettura.Dati)
                                        : await analizza!(daFare[lettura.Indice].Percorso);
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException) { motiviNonLette[lettura.Indice] = ex.Message; /* file illeggibile: si salta */ }
                                await pronte.Writer.WriteAsync((lettura.Indice, trovati), token);
                            });
                        pronte.Writer.TryComplete();
                    }
                    catch (Exception ex) { pronte.Writer.TryComplete(ex); }
                }, CancellationToken.None);

                var fatte = 0;
                await foreach (var (indice, trovati) in pronte.Reader.ReadAllAsync(CancellationToken.None))
                {
                    ct.ThrowIfCancellationRequested();
                    var (foto, _) = daFare[indice];
                    // Nomi dati intanto (dall'app o con assegna_nome) valgono subito anche per le foto che restano.
                    if (fatte % 50 == 49) persone = Volti.PersoneNote();
                    var volti = stato.Volti;
                    var errori = stato.Errori;
                    try
                    {
                        if (trovati is null) throw new IOException("foto non letta");
                        var daSalvare = await GestionePersone.DaSalvareAsync(trovati, persone, (b, w, h) => JpegBgra.CodificaAsync(b, w, h));
                        db.SalvaVoltiPersone(foto.Id, daSalvare);
                        volti += daSalvare.Count;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
                    {
                        errori++; // file illeggibile: si salta, si riproverà alla prossima analisi
                        var motivo = motiviNonLette.TryRemove(indice, out var motivoLettura) ? motivoLettura : ex.Message;
                        Console.Error.WriteLine($"Foto non letta: {daFare[indice].Percorso} ({motivo})");
                    }

                    fatte++;
                    Aggiorna(stato with
                    {
                        Fatte = fatte, Volti = volti, Errori = errori, JpegAnomali = Volatile.Read(ref _jpegAnomali),
                        Avanzamento = DescriviLungo(fatte, daFare.Count, cronometro.Elapsed)
                            + (Volatile.Read(ref _jpegAnomali) is > 0 and var anomali ? $" · {anomali} JPEG con intestazione anomala" : ""),
                    });
                    if (fatte % 250 == 0)
                        RegistroAttivita.Scrivi("mcp.analisi.avanzamento", $"{radice}: {fatte} di {daFare.Count} foto, {volti} volti, {errori} non lette");
                }
                await lettore;
                await produttori;
            }
            finally
            {
                analizzatore?.Dispose();
            }

            // Ultimo giro: i volti senza nome confrontati con le persone note (anche nomi dati durante l'analisi).
            GestionePersone.Riproponi(db, Volti.PersoneNote());
            Volti.Invalida();
            Aggiorna(stato with { Fase = "finita", Finita = true, Avanzamento = null }, forzaFile: true);
            RegistroAttivita.Scrivi("mcp.analisi.fine", $"{radice}: {stato.Fatte} foto analizzate, {stato.Volti} volti, {stato.Errori} non lette");
        }
        catch (OperationCanceledException)
        {
            Aggiorna(stato with { Fase = "fermata", Finita = true, Avanzamento = null }, forzaFile: true);
            RegistroAttivita.Scrivi("mcp.analisi.fermata", $"{radice}: {stato.Fatte} di {stato.Totale} foto (si riprende da qui)");
        }
        catch (Exception ex)
        {
            Aggiorna(stato with { Fase = "errore", Finita = true, Errore = ex.Message, Avanzamento = null }, forzaFile: true);
            RegistroAttivita.Errore("mcp.analisi.errore", ex, radice);
            if (avanzamento is not null) throw; // dalla riga di comando l'errore si vede
        }
    }

    /// <summary>Foto elaborate insieme (decodifica e inferenza); il salvataggio resta su un solo thread.</summary>
    private static readonly int GradoParallelo = Math.Clamp(Environment.ProcessorCount / 3, 2, 6);

    private static int _jpegAnomali;

    /// <summary>Elenco (percorso | motivo | byte) dei JPEG con intestazione anomala trovati dalle analisi.</summary>
    public static string FileJpegAnomali => Path.Combine(Ricerca.DatiApp, "jpeg-anomali.txt");

    /// <summary>
    /// Il decodificatore di Windows scrive «Invalid SOS parameters for sequential JPEG» senza dire di che file si tratta:
    /// qui, che il file lo conosciamo, lo diciamo noi (a video accanto all'avviso e in un elenco su file).
    /// </summary>
    private static void SegnalaJpegAnomalo(string percorso, string problema, int byteFile)
    {
        Interlocked.Increment(ref _jpegAnomali);
        Console.Error.WriteLine($"JPEG con intestazione anomala: {percorso} ({problema})");
        try
        {
            Directory.CreateDirectory(Ricerca.DatiApp);
            lock (FileJpegAnomali) File.AppendAllText(FileJpegAnomali, $"{percorso}|{problema}|{byteFile}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* l'elenco è un aiuto */ }
    }

    private static readonly Queue<(DateTime Quando, int Fatte)> Campioni = new();

    /// <summary>
    /// Come StimaAvanzamento.Descrivi, ma per lavori di ore e sul ritmo degli ultimi minuti (non sulla media da inizio:
    /// una sospensione del PC o un tratto lento non falsano la stima): "circa 8 h 05 min rimanenti · fine verso le 16:10".
    /// </summary>
    private static string DescriviLungo(int fatte, int totale, TimeSpan trascorso)
    {
        var adesso = DateTime.Now;
        Campioni.Enqueue((adesso, fatte));
        while (Campioni.Count > 2 && adesso - Campioni.Peek().Quando > TimeSpan.FromMinutes(3)) Campioni.Dequeue();

        var primo = Campioni.Peek();
        var secondi = (adesso - primo.Quando).TotalSeconds;
        var ritmo = secondi >= 20 ? (fatte - primo.Fatte) / secondi : 0;
        if (ritmo <= 0 || fatte >= totale) return StimaAvanzamento.Descrivi(fatte, totale, trascorso);

        var r = TimeSpan.FromSeconds((totale - fatte) / ritmo);
        if (r < TimeSpan.FromMinutes(60)) return $"{fatte} di {totale} · circa {StimaAvanzamento.Durata(r)} rimanenti · {ritmo:F1} foto/s";

        var minuti = (int)Math.Ceiling(r.TotalMinutes);
        var fine = adesso + r;
        var giorno = fine.Date == adesso.Date ? "" : fine.Date == adesso.Date.AddDays(1) ? " (domani)" : $" ({fine:dd/MM})";
        return $"{fatte} di {totale} · circa {minuti / 60} h {minuti % 60:00} min rimanenti · fine verso le {fine:HH:mm}{giorno} · {ritmo:F1} foto/s";
    }

    private static StatoAnalisi Nuovo(string cartella, string catalogo, string fase) =>
        new(cartella, catalogo, fase, 0, 0, 0, 0, DateTime.Now, DateTime.Now, false, null, null, Environment.ProcessId);

    private static string Normalizza(string cartella)
    {
        if (string.IsNullOrWhiteSpace(cartella) || !Path.IsPathRooted(cartella))
            throw new ArgumentException("Indica il percorso completo della cartella (es. E:\\Fotocamera\\2017).");
        var radice = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella));
        if (!Directory.Exists(radice)) throw new ArgumentException($"La cartella «{radice}» non esiste.");
        return radice;
    }

    private static bool ProcessoVivo(int id)
    {
        try { using var p = Process.GetProcessById(id); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static StatoAnalisi? LeggiFile()
    {
        try { return File.Exists(FileStato) ? JsonSerializer.Deserialize<StatoAnalisi>(File.ReadAllText(FileStato)) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void ScriviFile(StatoAnalisi stato)
    {
        try
        {
            Directory.CreateDirectory(Ricerca.DatiApp);
            var temporaneo = FileStato + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temporaneo, JsonSerializer.Serialize(stato));
            File.Move(temporaneo, FileStato, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* lo stato su file è un aiuto */ }
    }

    // Progress<T> passa dal pool di thread e gli aggiornamenti arriverebbero in disordine: qui basta chiamare subito.
    private sealed class Subito<T>(Action<T> azione) : IProgress<T>
    {
        public void Report(T valore) => azione(valore);
    }
}
