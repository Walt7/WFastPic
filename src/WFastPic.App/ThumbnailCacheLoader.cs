using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using WFastPic.Core.Decoding;

namespace WFastPic.App;

/// <summary>
/// Genera le thumbnail L1 in background senza mai bloccare il thread UI. Cache su disco
/// (.wfastpic/thumbcache/{id}.jpg) così le riaperture successive della cartella non ripagano
/// il costo di estrazione. Concorrenza limitata per non saturare I/O su cartelle da centinaia di file.
/// </summary>
public sealed class ThumbnailCacheLoader
{
    private const int MaxParallelismo = 4;

    private readonly string _cartellaCache;
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _semaforo = new(MaxParallelismo);
    private readonly ConcurrentQueue<PhotoGridItem> _coda = new();
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
        _totale = foto.Count;
        _completate = 0;
        foreach (var item in foto)
        {
            _coda.Enqueue(item);
        }

        for (var i = 0; i < MaxParallelismo; i++)
        {
            _ = Task.Run(() => WorkerAsync(token), token);
        }
    }

    private async Task WorkerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _coda.TryDequeue(out var item))
        {
            await _semaforo.WaitAsync(token);
            try
            {
                var bytes = await CaricaOGeneraAsync(item);
                if (bytes is null || token.IsCancellationRequested) continue;

                _dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;
                    item.Thumbnail = BytesToBitmapImage(bytes);
                });
            }
            catch
            {
                // Un file HEIC corrotto/non leggibile non deve fermare la coda: si salta e basta.
            }
            finally
            {
                _semaforo.Release();
                var completate = Interlocked.Increment(ref _completate);
                if (!token.IsCancellationRequested)
                    _dispatcher.TryEnqueue(() => ProgressoCambiato?.Invoke(completate, _totale));
            }
        }
    }

    private async Task<byte[]?> CaricaOGeneraAsync(PhotoGridItem item)
    {
        var percorsoCache = Path.Combine(_cartellaCache, $"{item.Id}.jpg");
        if (File.Exists(percorsoCache))
        {
            return await File.ReadAllBytesAsync(percorsoCache);
        }

        var bytes = await ThumbnailExtractor.ExtractAsJpegAsync(item.PercorsoCompleto);
        if (bytes is not null)
        {
            await File.WriteAllBytesAsync(percorsoCache, bytes);
        }

        return bytes;
    }

    private static BitmapImage BytesToBitmapImage(byte[] bytes)
    {
        // Niente 'using' qui: SetSource legge lo stream in modo asincrono internamente,
        // chiuderlo subito romperebbe la decodifica.
        var bitmap = new BitmapImage();
        var stream = new MemoryStream(bytes);
        bitmap.SetSource(stream.AsRandomAccessStream());
        return bitmap;
    }
}
