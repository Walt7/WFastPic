using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using WFastPic.Core.Decoding;

namespace WFastPic.App;

/// <summary>
/// Genera le thumbnail L1 in background senza mai bloccare il thread UI. Cache su disco
/// (.wfastpic/thumbcache/{id}-{size}-{ticks}.jpg) così le riaperture successive della cartella non
/// ripagano il costo di estrazione. Concorrenza limitata per non saturare I/O su cartelle da centinaia di file.
/// Le celle che diventano visibili (scroll, cambio filtro) passano davanti al resto della coda.
/// </summary>
public sealed class ThumbnailCacheLoader
{
    private const int MaxParallelismo = 4;
    private static readonly TimeSpan EtaMinimaTemporaneiOrfani = TimeSpan.FromMinutes(5);

    private readonly string _cartellaCache;
    private readonly DispatcherQueue _dispatcher;
    private readonly ConcurrentQueue<PhotoGridItem> _coda = new();
    // LIFO: con uno scroll veloce le ultime celle comparse sono quelle che l'utente sta guardando.
    private readonly ConcurrentStack<PhotoGridItem> _prioritarie = new();
    private readonly ConcurrentDictionary<long, byte> _avviate = new();
    private CancellationTokenSource? _cts;
    private int _totale;
    private int _completate;

    /// <summary>Notifica (completate, totale) ad ogni thumbnail caricata/generata, per mostrare una percentuale in UI.</summary>
    public event Action<int, int>? ProgressoCambiato;

    public ThumbnailCacheLoader(string cartellaFoto, DispatcherQueue dispatcher)
    {
        _cartellaCache = Path.Combine(cartellaFoto, ".wfastpic", "thumbcache");
        Directory.CreateDirectory(_cartellaCache);
        _dispatcher = dispatcher;
    }

    /// <summary>Ferma il caricamento precedente (es. cambio cartella) e avvia la nuova coda.</summary>
    public void AvviaCaricamento(IReadOnlyList<PhotoGridItem> foto)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _coda.Clear();
        _prioritarie.Clear();
        _avviate.Clear();
        _totale = foto.Count;
        _completate = 0;
        foreach (var item in foto)
        {
            _coda.Enqueue(item);
        }

        for (var i = 0; i < MaxParallelismo; i++)
        {
            _ = Task.Run(() => WorkerAsync(token));
        }

        var nomiValidi = foto.Select(NomeFileCache).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ = Task.Run(() => PulisciCacheObsoleta(nomiValidi));
    }

    /// <summary>Porta in testa alla coda una foto la cui cella è appena diventata visibile.</summary>
    public void Priorita(PhotoGridItem item)
    {
        if (!_avviate.ContainsKey(item.Id))
        {
            _prioritarie.Push(item);
        }
    }

    /// <summary>Annulla il lavoro ancora in coda quando la cartella non è più attiva.</summary>
    public void Annulla() => _cts?.Cancel();

    private bool ProssimaFoto([NotNullWhen(true)] out PhotoGridItem? item)
    {
        while (_prioritarie.TryPop(out item) || _coda.TryDequeue(out item))
        {
            // Ogni foto può essere in entrambe le code: la prima che la estrae la elabora.
            if (_avviate.TryAdd(item.Id, 0)) return true;
        }
        return false;
    }

    private async Task WorkerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && ProssimaFoto(out var item))
        {
            try
            {
                var bytes = await CaricaOGeneraAsync(item, token);
                if (bytes is null || token.IsCancellationRequested) continue;

                _dispatcher.TryEnqueue(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        var bitmap = new BitmapImage();
                        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
                        await bitmap.SetSourceAsync(stream);
                        if (!token.IsCancellationRequested) item.Thumbnail = bitmap;
                    }
                    catch
                    {
                        // Bytes non decodificabili: la cella resta col placeholder, la griglia non si ferma.
                    }
                });
            }
            catch
            {
                // Un file HEIC corrotto/non leggibile non deve fermare la coda: si salta e basta.
            }
            finally
            {
                var completate = Interlocked.Increment(ref _completate);
                if (!token.IsCancellationRequested)
                    _dispatcher.TryEnqueue(() => ProgressoCambiato?.Invoke(completate, _totale));
            }
        }
    }

    /// <summary>Usato anche dalla mappa, che mostra le miniature già in cache dentro i segnaposto.</summary>
    internal static string NomeFileCache(PhotoGridItem item) =>
        $"{item.Id}-{item.FileSize}-{item.LastWriteUtc.Ticks}.jpg";

    private async Task<byte[]?> CaricaOGeneraAsync(PhotoGridItem item, CancellationToken token)
    {
        var percorsoCache = Path.Combine(_cartellaCache, NomeFileCache(item));
        if (File.Exists(percorsoCache))
        {
            var dallaCache = await File.ReadAllBytesAsync(percorsoCache, token);
            if (IsJpegCompleto(dallaCache)) return dallaCache;
            // File troncato (es. crash a metà scrittura in versioni precedenti): si rigenera.
        }

        var bytes = await ThumbnailExtractor.ExtractAsJpegAsync(item.PercorsoCompleto);
        if (bytes is not null)
        {
            // Scrittura atomica: un crash a metà lascia solo un .tmp, mai un .jpg troncato.
            var temporaneo = $"{percorsoCache}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temporaneo, bytes, token);
            File.Move(temporaneo, percorsoCache, overwrite: true);
        }

        return bytes;
    }

    private static bool IsJpegCompleto(byte[] bytes) =>
        bytes.Length >= 4
        && bytes[0] == 0xFF && bytes[1] == 0xD8
        && bytes[^2] == 0xFF && bytes[^1] == 0xD9;

    /// <summary>
    /// Elimina le thumbnail di foto modificate/rimosse (la chiave include size e data modifica, quindi
    /// ogni modifica lascerebbe un file orfano) e i .tmp rimasti da scritture interrotte.
    /// </summary>
    private void PulisciCacheObsoleta(HashSet<string> nomiValidi)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_cartellaCache))
            {
                var nome = Path.GetFileName(file);
                var obsoleto = nome.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    ? DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > EtaMinimaTemporaneiOrfani
                    : !nomiValidi.Contains(nome);

                if (!obsoleto) continue;
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
