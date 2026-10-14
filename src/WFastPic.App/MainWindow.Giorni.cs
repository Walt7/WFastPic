using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using WFastPic.Core.Library;

namespace WFastPic.App;

/// <summary>
/// Un gruppo della griglia divisa: un giorno di scatto, oppure (filtro Doppioni) un gruppo di copie della
/// stessa foto. Il titolo segue il numero di foto.
/// </summary>
public sealed class GruppoGiorno : ObservableCollection<PhotoGridItem>
{
    public DateOnly? Giorno { get; init; }

    /// <summary>Titolo diverso da quello del giorno (gruppi di doppioni).</summary>
    public Func<GruppoGiorno, string>? TitoloPersonalizzato { get; init; }

    public string Titolo => TitoloPersonalizzato?.Invoke(this) ?? GiorniScatto.Titolo(Giorno, Count);

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnCollectionChanged(e);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Titolo)));
    }
}

/// <summary>
/// Griglia divisa per giorni (opzionale). <c>_visibili</c> resta l'unico elenco di riferimento
/// (tastiera, viewer, filtri, scarti lavorano su quello): qui se ne tiene allineata una vista
/// raggruppata. Le rimozioni singole (Delete con filtro attivo) si applicano subito al gruppo; le
/// ricostruzioni (cambio filtro, ricarica) si accorpano in un solo passaggio.
/// </summary>
public partial class MainWindow
{
    private bool _dividiPerGiorni;
    private readonly ObservableCollection<GruppoGiorno> _gruppiGiorno = new();
    private CollectionViewSource? _vistaGiorni;
    private bool _ricostruzioneGiorniProgrammata;

    /// <summary>Griglia divisa in gruppi: per giorni se scelto, sempre nel filtro Doppioni.</summary>
    private bool VistaRaggruppata => _dividiPerGiorni || Filtri[_filtroIndice] == FiltroDoppioni;

    /// <summary>Com'è la sorgente della griglia adesso (per cambiarla solo quando serve).</summary>
    private bool _vistaRaggruppataAttiva;

    private void InizializzaGiorni()
    {
        _visibili.CollectionChanged += Visibili_CollectionChanged;
        DividiPerGiorniToggle.IsChecked = _dividiPerGiorni;
        ApplicaModalitaGiorni();
    }

    private void DividiPerGiorniToggle_Click(object sender, RoutedEventArgs e)
    {
        _dividiPerGiorni = DividiPerGiorniToggle.IsChecked == true;
        ApplicaModalitaGiorni();
        Griglia.Focus(FocusState.Programmatic);
    }

    private void ApplicaModalitaGiorni()
    {
        var selezionata = Griglia.SelectedItem as PhotoGridItem;
        _vistaRaggruppataAttiva = VistaRaggruppata;

        if (_vistaRaggruppataAttiva)
        {
            RicostruisciGiorni();
            _vistaGiorni ??= new CollectionViewSource { IsSourceGrouped = true, Source = _gruppiGiorno };
            Griglia.ItemsSource = _vistaGiorni.View;
        }
        else
        {
            Griglia.ItemsSource = _visibili;
            _gruppiGiorno.Clear();
        }

        RipristinaSelezione(selezionata);
    }

    private void RicostruisciGiorni()
    {
        _gruppiGiorno.Clear();
        if (Filtri[_filtroIndice] == FiltroDoppioni)
        {
            foreach (var copie in _visibili.GroupBy(f => f.DoppioneId))
            {
                var gruppo = new GruppoGiorno { TitoloPersonalizzato = TitoloGruppoDoppioni };
                foreach (var f in copie) gruppo.Add(f);
                _gruppiGiorno.Add(gruppo);
            }
            return;
        }

        foreach (var (giorno, foto) in GiorniScatto.Raggruppa(_visibili, f => f.CaptureDate))
        {
            var gruppo = new GruppoGiorno { Giorno = giorno };
            foreach (var f in foto) gruppo.Add(f);
            _gruppiGiorno.Add(gruppo);
        }
    }

    private void RipristinaSelezione(PhotoGridItem? selezionata)
    {
        if (selezionata is not null && _visibili.Contains(selezionata))
        {
            Griglia.SelectedItem = selezionata;
            Griglia.ScrollIntoView(selezionata);
        }
        else
        {
            AssicuraSelezione();
        }
    }

    private void Visibili_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_vistaRaggruppataAttiva) return;

        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null && !_ricostruzioneGiorniProgrammata)
        {
            foreach (PhotoGridItem foto in e.OldItems)
            {
                var gruppo = _gruppiGiorno.FirstOrDefault(g => g.Contains(foto));
                if (gruppo is null) continue;
                gruppo.Remove(foto);
                if (gruppo.Count == 0) _gruppiGiorno.Remove(gruppo);
            }
            return;
        }

        // Clear + Add in serie (cambio filtro, ricarica cartella): una sola ricostruzione a fine giro.
        if (_ricostruzioneGiorniProgrammata) return;
        _ricostruzioneGiorniProgrammata = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _ricostruzioneGiorniProgrammata = false;
            if (!_vistaRaggruppataAttiva) return;

            var selezionata = Griglia.SelectedItem as PhotoGridItem;
            RicostruisciGiorni();
            RipristinaSelezione(selezionata);
        });
    }
}
