using System.Collections.ObjectModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
using WinRT.Interop;

namespace WFastPic.App;

public partial class MainWindow : Window
{
    private const int CellSizeMin = 80;
    private const int CellSizeMax = 400;
    private const int CellSizeStep = 20;

    private static readonly string[] Filtri = ["Tutte", "Selezionate", "Scarti"];

    private readonly ObservableCollection<PhotoGridItem> _visibili = new();
    private readonly List<PhotoGridItem> _tutte = new();
    private readonly Dictionary<long, PhotoGridItem> _perId = new();
    private readonly Dictionary<int, List<PhotoGridItem>> _gruppi = new();

    private LibrarySidecarDatabase? _db;
    private SelectionManager? _selectionManager;
    private ThumbnailCacheLoader? _thumbnailLoader;

    private int _filtroIndice;
    private int _cellSize = 160;
    private int _conteggioSelezionate;
    private int _conteggioScarti;

    private IReadOnlyList<PhotoRecord>? _scartiCorrenti;
    private RejectManager? _rejectManagerCorrente;

    public MainWindow()
    {
        InitializeComponent();
        Title = "WFastPic";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "icon.ico"));
        Griglia.ItemsSource = _visibili;
        AggiornaStatusBar();
        AggiornaFiltroText();

        // handledEventsToo: la ScrollViewer interna della ListView consuma la rotella per lo
        // scroll e marca l'evento Handled prima che arrivi al gestore dichiarato in XAML.
        Griglia.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Griglia_PointerWheelChanged), handledEventsToo: true);
    }

    private async void ApriCartellaButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var cartella = await picker.PickSingleFolderAsync();
        if (cartella is not null)
        {
            await CaricaCartellaAsync(cartella.Path);
        }
    }

    private async Task CaricaCartellaAsync(string cartella)
    {
        if (_selectionManager is not null)
        {
            await _selectionManager.DisposeAsync();
        }
        _db?.Dispose();

        _tutte.Clear();
        _visibili.Clear();
        _perId.Clear();
        _gruppi.Clear();
        _conteggioSelezionate = 0;
        _conteggioScarti = 0;
        _filtroIndice = 0;

        var (db, records) = await Task.Run(() =>
        {
            var scanned = FolderScanner.Scan(cartella);
            var database = LibrarySidecarDatabase.OpenOrCreate(cartella);
            database.SyncScan(scanned);
            return (database, database.GetAllPhotos());
        });

        _db = db;
        _selectionManager = new SelectionManager(_db);
        _thumbnailLoader = new ThumbnailCacheLoader(cartella, DispatcherQueue);

        foreach (var record in records)
        {
            var item = new PhotoGridItem
            {
                Id = record.Id,
                PathRelativo = record.PathRelativo,
                PercorsoCompleto = Path.Combine(cartella, record.PathRelativo),
                CaptureDate = record.CaptureDate,
                Stato = record.Stato,
                Rating = record.Rating,
            };

            _tutte.Add(item);
            _perId[item.Id] = item;

            if (item.Stato == StatoSelezione.Selected) _conteggioSelezionate++;
            if (item.Stato == StatoSelezione.Reject) _conteggioScarti++;
        }

        RaggruppaRaffiche();
        ApplicaFiltro();
        AggiornaStatusBar();
        Griglia.Focus(FocusState.Programmatic);
        _thumbnailLoader.AvviaCaricamento(_tutte);
    }

    // --- Raffiche: foto scattate a pochi secondi di distanza ---

    private void RaggruppaRaffiche()
    {
        _gruppi.Clear();

        var input = _tutte.Select(i => new BurstGroupingInput(i.Id, i.CaptureDate)).ToList();
        var mappa = BurstGrouper.Group(input);

        foreach (var item in _tutte)
        {
            if (mappa.TryGetValue(item.Id, out var groupId))
            {
                item.GroupId = groupId;
                if (!_gruppi.TryGetValue(groupId, out var lista))
                {
                    lista = new List<PhotoGridItem>();
                    _gruppi[groupId] = lista;
                }
                lista.Add(item);
            }
            else
            {
                item.GroupId = null;
            }
        }

        foreach (var gruppo in _gruppi.Values)
        {
            var ordinato = gruppo.OrderBy(i => i.CaptureDate).ToList();
            for (var i = 0; i < ordinato.Count; i++)
            {
                ordinato[i].PosizioneNelGruppo = i + 1;
                ordinato[i].DimensioneGruppo = ordinato.Count;
            }
        }
    }

    // --- Selezione / stato ---

    private void SetStato(PhotoGridItem item, string nuovoStato)
    {
        var vecchioStato = item.Stato;
        if (vecchioStato == nuovoStato) return;

        if (vecchioStato == StatoSelezione.Selected) _conteggioSelezionate--;
        if (vecchioStato == StatoSelezione.Reject) _conteggioScarti--;

        item.Stato = nuovoStato;

        if (nuovoStato == StatoSelezione.Selected) _conteggioSelezionate++;
        if (nuovoStato == StatoSelezione.Reject) _conteggioScarti++;

        _selectionManager?.SetStato(item.Id, nuovoStato);

        if (!CorrispondeAlFiltro(item, Filtri[_filtroIndice]))
        {
            _visibili.Remove(item);
        }

        AggiornaStatusBar();
    }

    private void SetRating(PhotoGridItem item, int rating)
    {
        item.Rating = rating;
        _selectionManager?.SetRating(item.Id, rating);
    }

    private static bool CorrispondeAlFiltro(PhotoGridItem item, string filtro) => filtro switch
    {
        "Selezionate" => item.Stato == StatoSelezione.Selected,
        "Scarti" => item.Stato == StatoSelezione.Reject,
        _ => true,
    };

    private void ApplicaFiltro()
    {
        var filtro = Filtri[_filtroIndice];
        _visibili.Clear();
        foreach (var item in _tutte.Where(i => CorrispondeAlFiltro(i, filtro)))
        {
            _visibili.Add(item);
        }
        AggiornaFiltroText();
        AssicuraSelezione();
    }

    /// <summary>
    /// Senza un elemento selezionato, Space/Delete/rating non hanno un "corrente" su cui agire:
    /// la griglia sembra non rispondere alla tastiera finché non si clicca manualmente un elemento.
    /// Garantisce sempre una selezione valida dopo caricamento o cambio filtro.
    /// </summary>
    private void AssicuraSelezione()
    {
        if (Griglia.SelectedItem is PhotoGridItem attuale && _visibili.Contains(attuale))
        {
            return;
        }

        Griglia.SelectedIndex = _visibili.Count > 0 ? 0 : -1;
    }

    private void AggiornaStatusBar() =>
        StatusText.Text = $"Tutte {_tutte.Count} · Selezionate {_conteggioSelezionate} · Scarti {_conteggioScarti}";

    private void AggiornaFiltroText() => FiltroText.Text = $"Filtro: {Filtri[_filtroIndice]}";

    // --- Tastiera: ← → SPACE è il ciclo critico, nessuna operazione qui deve bloccare ---

    private void Griglia_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var corrente = Griglia.SelectedItem as PhotoGridItem;
        var ctrlPremuto = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        var shiftPremuto = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.Space when corrente is not null && shiftPremuto && corrente.GroupId is int groupId:
                // Un'unica azione: tiene questa foto e scarta il resto della raffica.
                SetStato(corrente, StatoSelezione.Selected);
                if (_gruppi.TryGetValue(groupId, out var membriGruppo))
                {
                    foreach (var altro in membriGruppo.Where(m => m.Id != corrente.Id))
                    {
                        SetStato(altro, StatoSelezione.Reject);
                    }
                }
                e.Handled = true;
                break;

            case VirtualKey.Space when corrente is not null:
                SetStato(corrente, corrente.Stato == StatoSelezione.Selected ? StatoSelezione.None : StatoSelezione.Selected);
                e.Handled = true;
                break;

            case VirtualKey.Delete or VirtualKey.Back when corrente is not null:
                SetStato(corrente, StatoSelezione.Reject);
                e.Handled = true;
                break;

            case VirtualKey.Number0 or VirtualKey.NumberPad0 when corrente is not null:
                SetRating(corrente, 0); e.Handled = true; break;
            case VirtualKey.Number1 or VirtualKey.NumberPad1 when corrente is not null:
                SetRating(corrente, 1); e.Handled = true; break;
            case VirtualKey.Number2 or VirtualKey.NumberPad2 when corrente is not null:
                SetRating(corrente, 2); e.Handled = true; break;
            case VirtualKey.Number3 or VirtualKey.NumberPad3 when corrente is not null:
                SetRating(corrente, 3); e.Handled = true; break;
            case VirtualKey.Number4 or VirtualKey.NumberPad4 when corrente is not null:
                SetRating(corrente, 4); e.Handled = true; break;
            case VirtualKey.Number5 or VirtualKey.NumberPad5 when corrente is not null:
                SetRating(corrente, 5); e.Handled = true; break;

            case VirtualKey.Tab:
                _filtroIndice = (_filtroIndice + 1) % Filtri.Length;
                ApplicaFiltro();
                e.Handled = true;
                break;

            case VirtualKey.A when ctrlPremuto:
                foreach (var item in _visibili.ToList())
                {
                    SetStato(item, StatoSelezione.Selected);
                }
                e.Handled = true;
                break;
        }
    }

    // --- Shift + rotella: cambia dimensione celle griglia ---

    private void Griglia_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var shiftPremuto = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (!shiftPremuto) return;

        var delta = e.GetCurrentPoint(Griglia).Properties.MouseWheelDelta;
        _cellSize = Math.Clamp(_cellSize + Math.Sign(delta) * CellSizeStep, CellSizeMin, CellSizeMax);

        if (Griglia.ItemsPanelRoot is ItemsWrapGrid wrap)
        {
            wrap.ItemWidth = _cellSize;
            wrap.ItemHeight = _cellSize;
        }

        e.Handled = true;
    }

    // --- Gestisci scarti… ---

    private void GestisciScartiButton_Click(object sender, RoutedEventArgs e) => _ = MostraDialogoScartiAsync();

    private async Task MostraDialogoScartiAsync()
    {
        if (_db is null) return;

        _rejectManagerCorrente = new RejectManager(_db);
        _scartiCorrenti = _rejectManagerCorrente.GetScarti();

        ScartiSummary.Text = $"{_scartiCorrenti.Count} foto in scarto";
        ScartiList.ItemsSource = _scartiCorrenti.Select(s => s.PathRelativo).ToList();

        DialogScarti.XamlRoot = Content.XamlRoot;
        await DialogScarti.ShowAsync();
    }

    private void EliminaDefinitivamenteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rejectManagerCorrente is null || _scartiCorrenti is null) return;
        foreach (var foto in _scartiCorrenti)
        {
            _rejectManagerCorrente.EliminaDefinitivamente(foto);
            RimuoviDallaLibreria(foto.Id);
        }
        AggiornaStatusBar();
        DialogScarti.Hide();
    }

    private void SpostaInScartiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rejectManagerCorrente is null || _scartiCorrenti is null) return;
        foreach (var foto in _scartiCorrenti)
        {
            _rejectManagerCorrente.SpostaInScarti(foto);
            RimuoviDallaLibreria(foto.Id);
        }
        AggiornaStatusBar();
        DialogScarti.Hide();
    }

    private void LasciaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rejectManagerCorrente is null || _scartiCorrenti is null) return;
        foreach (var foto in _scartiCorrenti)
        {
            if (_perId.TryGetValue(foto.Id, out var item))
            {
                SetStato(item, StatoSelezione.None);
            }
        }
        DialogScarti.Hide();
    }

    private void RimuoviDallaLibreria(long photoId)
    {
        if (!_perId.TryGetValue(photoId, out var item)) return;

        if (item.Stato == StatoSelezione.Reject) _conteggioScarti--;
        if (item.Stato == StatoSelezione.Selected) _conteggioSelezionate--;

        _tutte.Remove(item);
        _visibili.Remove(item);
        _perId.Remove(photoId);
    }
}
