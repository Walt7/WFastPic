using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WFastPic.App;

/// <summary>Filmstrip stile Picasa sotto la foto, con la corrente sempre al centro.</summary>
public sealed partial class ViewerView
{
    private async void Filmstrip_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoGridItem foto)
        {
            await CaricaFotoNelViewerAsync(foto);
        }
    }

    private PhotoGridItem? _filmstripCorrenteEvidenziata;
    private readonly List<PhotoGridItem> _filmstripGruppoEvidenziato = new();

    private void Filmstrip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Finestra ridimensionata: cambia quante miniature stanno ai lati e dove cade il centro.
        if (_viewerAperto && e.PreviousSize.Width != e.NewSize.Width) AggiornaFilmstrip();
    }

    /// <summary>
    /// Mostra attorno alla foto corrente solo le miniature che stanno nella larghezza disponibile
    /// (al massimo ±12) e la tiene esattamente al centro, anche a inizio/fine cartella: lo spazio
    /// mancante da un lato diventa padding sinistro invece di spostare la corrente.
    /// </summary>
    private void AggiornaFilmstrip()
    {
        const int raggioMassimo = 12;

        if (_filmstripCorrenteEvidenziata is not null) _filmstripCorrenteEvidenziata.IsCorrenteViewer = false;
        foreach (var item in _filmstripGruppoEvidenziato) item.EvidenziaGruppoViewer = false;
        _filmstripGruppoEvidenziato.Clear();
        _filmstripCorrenteEvidenziata = null;

        if (_fotoViewerCorrente is null) { Filmstrip.ItemsSource = null; return; }

        var indice = Host.Visibili.IndexOf(_fotoViewerCorrente);
        if (indice < 0) { Filmstrip.ItemsSource = null; return; }

        var larghezza = Filmstrip.ActualWidth > 0 ? Filmstrip.ActualWidth : ViewerOverlay.ActualWidth;
        var slot = PhotoGridItem.FilmstripLato + 2 * PhotoGridItem.FilmstripMargine;
        var slotCorrente = PhotoGridItem.FilmstripLatoCorrente + 2 * PhotoGridItem.FilmstripMargine;
        var raggio = Math.Clamp((int)((larghezza - slotCorrente) / 2 / slot), 0, raggioMassimo);

        var inizio = Math.Max(0, indice - raggio);
        var fine = Math.Min(Host.Visibili.Count - 1, indice + raggio);

        var finestra = new List<PhotoGridItem>();
        for (var i = inizio; i <= fine; i++) finestra.Add(Host.Visibili[i]);

        // Centro della corrente = centro della striscia, qualunque sia il numero di foto a sinistra.
        var primaDellaCorrente = indice - inizio;
        var paddingSinistro = larghezza / 2 - primaDellaCorrente * slot - slotCorrente / 2;
        Filmstrip.Padding = new Thickness(Math.Max(0, paddingSinistro), 0, 0, 0);

        _fotoViewerCorrente.IsCorrenteViewer = true;
        _filmstripCorrenteEvidenziata = _fotoViewerCorrente;
        Filmstrip.ItemsSource = finestra;

        foreach (var item in finestra)
        {
            if (item.Thumbnail is null) Host.ThumbnailLoader?.Priorita(item);
        }

        if (_fotoViewerCorrente.GroupId is not null)
        {
            foreach (var item in finestra)
            {
                if (item != _fotoViewerCorrente && item.GroupId == _fotoViewerCorrente.GroupId)
                {
                    item.EvidenziaGruppoViewer = true;
                    _filmstripGruppoEvidenziato.Add(item);
                }
            }
        }
    }
}
