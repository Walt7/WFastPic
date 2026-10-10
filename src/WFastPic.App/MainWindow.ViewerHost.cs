using Microsoft.UI.Xaml;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;

namespace WFastPic.App;

/// <summary>Collegamento tra la finestra e il viewer (<see cref="ViewerView"/>).</summary>
public partial class MainWindow : IViewerHost
{
    private bool _pannelloCartelleAperto = true;

    private void InizializzaViewer()
    {
        Viewer.Inizializza(this);
        Viewer.Chiuso += (_, _) =>
        {
            RootSplitView.IsPaneOpen = _pannelloCartelleAperto;
            Griglia.Focus(FocusState.Programmatic);
        };
    }

    private void ApriViewer(PhotoGridItem? foto)
    {
        if (foto is null || Viewer.IsAperto) return;
        // Pannello cartelle nascosto mentre si edita: tutta la larghezza va alla foto. Si ripristina alla chiusura.
        _pannelloCartelleAperto = RootSplitView.IsPaneOpen;
        RootSplitView.IsPaneOpen = false;
        Viewer.Apri(foto);
    }

    IList<PhotoGridItem> IViewerHost.Visibili => _visibili;
    IReadOnlyList<PhotoGridItem> IViewerHost.Tutte => _tutte;
    LibrarySidecarDatabase? IViewerHost.Database => _db;
    SelectionManager? IViewerHost.SelectionManager => _selectionManager;
    ThumbnailCacheLoader? IViewerHost.ThumbnailLoader => _thumbnailLoader;
    Window IViewerHost.Finestra => this;

    void IViewerHost.SetStato(PhotoGridItem foto, string nuovoStato) => SetStato(foto, nuovoStato);
    void IViewerHost.SetRating(PhotoGridItem foto, int rating) => SetRating(foto, rating);

    Task<string?> IViewerHost.NomeLuogoAsync(double latitudine, double longitudine, CancellationToken ct) =>
        NomeLuogoAsync(latitudine, longitudine, ct);
}
