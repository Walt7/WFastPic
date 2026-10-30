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
    DateTime Inizio, DateTime Aggiornato, bool Finita, string? Errore, string? Avanzamento, int Processo);

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
            var scansione = await Task.Run(() => FolderScanner.Scan(catalogo, db.CreaRisolutoreMetadatiNoti(), progresso, nonLette), ct);
            ct.ThrowIfCancellationRequested();
            db.SyncScan(scansione, nonLette);
            var importate = db.ImportaDaSidecarFigli();
            if (importate > 0) RegistroAttivita.Scrivi("cartella.import-figli", $"{importate} sottocartelle riversate in {catalogo}");
            Ricerca.RegistraCartella(catalogo);

            var daFare = db.GetAllPhotos()
                .Where(f => !f.PersoneAnalizzate)
                .Select(f => (Foto: f, Percorso: Path.Combine(catalogo, f.PathRelativo)))
                .Where(f => Ricerca.FotoNellaCartella(f.Percorso, radice))
                .OrderBy(f => f.Foto.PathRelativo, StringComparer.OrdinalIgnoreCase)
                .Take(massimoFoto is int quante and > 0 ? quante : int.MaxValue)
                .ToList();

            Aggiorna(stato with { Fase = "volti", Fatte = 0, Totale = daFare.Count, Avanzamento = null }, forzaFile: true);
            var analizza = AnalizzatoreDiProva;
            AnalizzatorePersone? analizzatore = null;
            try
            {
                if (analizza is null && daFare.Count > 0)
                {
                    Aggiorna(stato with { Fase = "modelli" }, forzaFile: true);
                    await ModelliPersone.AssicuraAsync(Http, ModelliPersone.CartellaPredefinita, ct: ct);
                    analizzatore = new AnalizzatorePersone(ModelliPersone.CartellaPredefinita);
                    var reale = analizzatore;
                    analizza = async percorso =>
                    {
                        var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(percorso, AnalizzatorePersone.LatoAnalisi);
                        return reale.Analizza(bgra, w, h);
                    };
                    Aggiorna(stato with { Fase = "volti" }, forzaFile: true);
                }

                var persone = Volti.PersoneNote();
                cronometro.Restart();
                for (var i = 0; i < daFare.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (foto, percorso) = daFare[i];
                    // Nomi dati intanto (dall'app o con assegna_nome) valgono subito anche per le foto che restano.
                    if (i % 50 == 49) persone = Volti.PersoneNote();
                    var volti = stato.Volti;
                    var errori = stato.Errori;
                    try
                    {
                        var trovati = await analizza!(percorso);
                        var daSalvare = await GestionePersone.DaSalvareAsync(trovati, persone, (b, w, h) => JpegBgra.CodificaAsync(b, w, h));
                        db.SalvaVoltiPersone(foto.Id, daSalvare);
                        volti += daSalvare.Count;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
                    {
                        errori++; // file illeggibile: si salta, si riproverà alla prossima analisi
                    }

                    Aggiorna(stato with
                    {
                        Fatte = i + 1, Volti = volti, Errori = errori,
                        Avanzamento = StimaAvanzamento.Descrivi(i + 1, daFare.Count, cronometro.Elapsed),
                    });
                    if ((i + 1) % 250 == 0)
                        RegistroAttivita.Scrivi("mcp.analisi.avanzamento", $"{radice}: {i + 1} di {daFare.Count} foto, {volti} volti, {errori} non lette");
                }
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
