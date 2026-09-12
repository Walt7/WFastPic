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

    public SelectionManager(LibrarySidecarDatabase db)
    {
        _db = db;
        _writerTask = Task.Run(ProcessQueueAsync);
    }

    public void SetStato(long photoId, string stato) =>
        _codaScritture.Writer.TryWrite(() => _db.UpdateStato(photoId, stato));

    public void SetRating(long photoId, int rating) =>
        _codaScritture.Writer.TryWrite(() => _db.UpdateRating(photoId, rating));

    private async Task ProcessQueueAsync()
    {
        await foreach (var azione in _codaScritture.Reader.ReadAllAsync())
        {
            try
            {
                azione();
            }
            catch
            {
                // Una scrittura fallita non deve mai bloccare le successive né il thread UI.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _codaScritture.Writer.Complete();
        await _writerTask;
    }
}
