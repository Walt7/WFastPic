using System.Collections.Concurrent;
using System.Threading.Channels;
using WFastPic.Core.Persistence;

namespace WFastPic.Core.Selection;

/// <summary>
/// Scrittura stato/rating sempre su coda in background: il ciclo ← → SPACE non deve mai aspettare
/// una write SQLite. Il chiamante aggiorna il proprio stato in memoria (es. ViewModel) e poi accoda
/// qui la persistenza, fire-and-forget.
/// </summary>
public sealed class SelectionManager : IAsyncDisposable
{
    private readonly LibrarySidecarDatabase _db;
    private readonly Channel<Action> _codaScritture = Channel.CreateUnbounded<Action>();
    private readonly Task _writerTask;
    private readonly ConcurrentDictionary<long, string> _ricettePendenti = new();

    /// <summary>Notifica l'applicazione solo dopo tre tentativi falliti di persistenza.</summary>
    public event Action<Exception>? ScritturaFallita;

    public SelectionManager(LibrarySidecarDatabase db)
    {
        _db = db;
        _writerTask = Task.Run(ProcessQueueAsync);
    }

    public void SetStato(long photoId, string stato) =>
        _codaScritture.Writer.TryWrite(() => _db.UpdateStato(photoId, stato));

    public void SetRating(long photoId, int rating) =>
        _codaScritture.Writer.TryWrite(() => _db.UpdateRating(photoId, rating));

    /// <summary>
    /// La ricetta resta leggibile da <see cref="GetRecipeJson"/> finché la coda non l'ha scritta:
    /// tornando subito su una foto appena modificata non si rilegge la versione vecchia dal DB.
    /// </summary>
    public void SaveRecipe(long photoId, string recipeJson, int schemaVersion)
    {
        _ricettePendenti[photoId] = recipeJson;
        _codaScritture.Writer.TryWrite(() =>
        {
            _db.SaveRecipe(photoId, recipeJson, schemaVersion);
            // Solo se nel frattempo non ne è stata accodata una più recente per la stessa foto.
            _ricettePendenti.TryRemove(new KeyValuePair<long, string>(photoId, recipeJson));
        });
    }

    public void SetPosizioneManuale(long photoId, double? latitude, double? longitude) =>
        _codaScritture.Writer.TryWrite(() => _db.SetPosizioneManuale(photoId, latitude, longitude));

    public void RipristinaPosizioneOriginale(long photoId, WFastPic.Core.Metadata.GeoCoordinate? gpsDelFile) =>
        _codaScritture.Writer.TryWrite(() => _db.RipristinaPosizioneOriginale(photoId, gpsDelFile));

    public string? GetRecipeJson(long photoId) =>
        _ricettePendenti.TryGetValue(photoId, out var json) ? json : _db.GetRecipeJson(photoId);

    /// <summary>Completa quando tutte le scritture accodate finora sono su disco (es. prima di un export).</summary>
    public Task SvuotaAsync()
    {
        var completata = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_codaScritture.Writer.TryWrite(() => completata.TrySetResult()))
            completata.TrySetResult(); // coda già chiusa: non c'è più niente da aspettare
        return completata.Task;
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var azione in _codaScritture.Reader.ReadAllAsync())
        {
            Exception? ultimoErrore = null;
            for (var tentativo = 0; tentativo < 3; tentativo++)
            {
                try
                {
                    azione();
                    ultimoErrore = null;
                    break;
                }
                catch (Exception ex)
                {
                    ultimoErrore = ex;
                    if (tentativo < 2)
                        await Task.Delay(TimeSpan.FromMilliseconds(100 * (tentativo + 1)));
                }
            }

            if (ultimoErrore is not null)
                ScritturaFallita?.Invoke(ultimoErrore);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // TryComplete: chiamate ripetute (es. cambio cartella + chiusura finestra) non devono lanciare.
        _codaScritture.Writer.TryComplete();
        await _writerTask;
    }
}
