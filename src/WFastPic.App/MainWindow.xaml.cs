using System.Collections.ObjectModel;
using System.Threading;
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

    private static readonly string[] Filtri = ["Tutte", "Selezionate", "Scarti", "Geolocalizzate"];

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

    private List<string> _cartelleRecenti = [];

    private int _miniatureCompletate;
    private int _miniatureTotali;
    private int _richiestaCaricamentoCartella;

    public MainWindow()
    {
        InitializeComponent();
        Title = "WFastPic";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "icon.ico"));
        Griglia.ItemsSource = _visibili;
        AggiornaStatusBar();
        AggiornaFiltroUI();

        _cartelleRecenti = RecentFoldersStore.Carica();
        CartelleRecentiList.ItemsSource = _cartelleRecenti;
        InizializzaAlberoCartelle();

        // handledEventsToo: la ScrollViewer interna della ListView consuma la rotella per lo
        // scroll e marca l'evento Handled prima che arrivi al gestore dichiarato in XAML.
        Griglia.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Griglia_PointerWheelChanged), handledEventsToo: true);

        InizializzaViewer();

        Closed += async (s, e) =>
        {
            if (_selectionManager is not null)
            {
                await _selectionManager.DisposeAsync();
                _db?.Dispose();
            }
        };

        if (_cartelleRecenti.Count > 0 && Directory.Exists(_cartelleRecenti[0]))
        {
            var cartellaIniziale = _cartelleRecenti[0];
            DispatcherQueue.TryEnqueue(async () =>
            {
                await CaricaCartellaAsync(cartellaIniziale);
            });
        }
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

    private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e) =>
        RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;

    private void InizializzaAlberoCartelle()
    {
        foreach (var cartellaCloud in CartelleCloudDisponibili())
            DirectoryTree.RootNodes.Add(CreaNodoCartella(cartellaCloud, "\uE753"));

        var questoPc = new TreeViewNode
        {
            Content = new DirectoryTreeEntry("Questo PC", null, "\uE770"),
            IsExpanded = true,
        };
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var nome = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd(Path.DirectorySeparatorChar)
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})";
            questoPc.Children.Add(CreaNodoCartella(drive.RootDirectory.FullName, "\uE8B7", nome));
        }
        DirectoryTree.RootNodes.Add(questoPc);
    }

    private static IEnumerable<string> CartelleCloudDisponibili()
    {
        var profilo = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = new[] { Path.Combine(profilo, "Dropbox") }
            .Concat(Directory.Exists(profilo)
                ? Directory.EnumerateDirectories(profilo, "OneDrive*", SearchOption.TopDirectoryOnly)
                : Enumerable.Empty<string>());

        return candidate.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static TreeViewNode CreaNodoCartella(string percorso, string icona, string? nome = null) => new()
    {
        Content = new DirectoryTreeEntry(nome ?? Path.GetFileName(percorso.TrimEnd(Path.DirectorySeparatorChar)), percorso, icona),
        HasUnrealizedChildren = HaSottocartelle(percorso),
    };

    private static bool HaSottocartelle(string percorso)
    {
        try { return Directory.EnumerateDirectories(percorso).Any(); }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static void CaricaFigli(TreeViewNode nodo, string percorso)
    {
        nodo.Children.Clear();
        try
        {
            foreach (var figlio in Directory.EnumerateDirectories(percorso)
                         .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase))
            {
                nodo.Children.Add(CreaNodoCartella(figlio, "\uE8B7"));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Cartella protetta: il ramo resta vuoto, come in Explorer senza autorizzazione.
        }
        catch (IOException)
        {
            // Un'unità rimovibile può sparire mentre il ramo viene espanso.
        }
    }

    private void DirectoryTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.HasUnrealizedChildren && args.Node.Content is DirectoryTreeEntry { Percorso: not null } voce)
        {
            CaricaFigli(args.Node, voce.Percorso);
            args.Node.HasUnrealizedChildren = false;
        }
    }

    private async void DirectoryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not DirectoryTreeEntry { Percorso: not null } voce) return;

        RootSplitView.IsPaneOpen = false;
        await CaricaCartellaAsync(voce.Percorso);
    }

    private async void CartellaRecente_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string percorso)
        {
            RootSplitView.IsPaneOpen = false;
            await CaricaCartellaAsync(percorso);
        }
    }

    private async void PercorsoTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        var percorso = PercorsoTextBox.Text.Trim().Trim('"');
        if (!Directory.Exists(percorso))
        {
            PercorsoTextBox.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
            return;
        }

        PercorsoTextBox.ClearValue(TextBox.BorderBrushProperty);
        await CaricaCartellaAsync(percorso);
    }

    private async Task CaricaCartellaAsync(string cartella)
    {
        var richiesta = ++_richiestaCaricamentoCartella;
        if (_thumbnailLoader is not null)
        {
            _thumbnailLoader.ProgressoCambiato -= ThumbnailLoader_ProgressoCambiato;
            _thumbnailLoader.Annulla();
            _thumbnailLoader = null;
        }

        if (_selectionManager is not null)
        {
            await _selectionManager.DisposeAsync();
            _selectionManager.ScritturaFallita -= SelectionManager_ScritturaFallita;
        }
        _db?.Dispose();

        _tutte.Clear();
        _visibili.Clear();
        _perId.Clear();
        _gruppi.Clear();
        _conteggioSelezionate = 0;
        _conteggioScarti = 0;
        _filtroIndice = 0;
        _miniatureCompletate = 0;
        _miniatureTotali = 0;

        using var overlayDelay = MostraCaricamentoConRitardo("Scansione cartella…");

        var (db, records) = await Task.Run(() =>
        {
            var scanned = FolderScanner.Scan(cartella);
            var database = LibrarySidecarDatabase.OpenOrCreate(cartella);
            database.SyncScan(scanned);
            return (database, database.GetAllPhotos());
        });

        if (richiesta != _richiestaCaricamentoCartella)
        {
            db.Dispose();
            return;
        }

        _db = db;
        _selectionManager = new SelectionManager(_db);
        _selectionManager.ScritturaFallita += SelectionManager_ScritturaFallita;
        _thumbnailLoader = new ThumbnailCacheLoader(cartella, DispatcherQueue);
        _thumbnailLoader.ProgressoCambiato += ThumbnailLoader_ProgressoCambiato;

        foreach (var record in records)
        {
            var percorsoCompleto = Path.Combine(cartella, record.PathRelativo);
            var item = new PhotoGridItem
            {
                Id = record.Id,
                PathRelativo = record.PathRelativo,
                PercorsoCompleto = percorsoCompleto,
                FileSize = record.Size,
                LastWriteUtc = record.LastWriteUtc,
                CaptureDate = record.CaptureDate,
                Latitude = record.Latitude,
                Longitude = record.Longitude,
                HasVideo = CompanionVideoDetector.TrovaVideoAbbinato(percorsoCompleto) is not null,
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

        PercorsoTextBox.Text = cartella;
        _cartelleRecenti = RecentFoldersStore.AggiungiRecente(_cartelleRecenti, cartella);
        RecentFoldersStore.Salva(_cartelleRecenti);
        CartelleRecentiList.ItemsSource = null;
        CartelleRecentiList.ItemsSource = _cartelleRecenti;
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

        ElementSoundPlayer.Play(nuovoStato switch
        {
            StatoSelezione.Selected => ElementSoundKind.Invoke,
            StatoSelezione.Reject => ElementSoundKind.Hide,
            _ => ElementSoundKind.Focus,
        });

        if (!CorrispondeAlFiltro(item, Filtri[_filtroIndice]))
        {
            _visibili.Remove(item);
            AssicuraSelezione();
        }

        AggiornaStatusBar();
    }

    private PhotoGridItem? _correnteEvidenziata;

    private void Griglia_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_correnteEvidenziata is not null)
        {
            _correnteEvidenziata.IsCorrenteGriglia = false;
        }

        if (Griglia.SelectedItem is PhotoGridItem nuovo)
        {
            _correnteEvidenziata = nuovo;
            _correnteEvidenziata.IsCorrenteGriglia = true;
        }
        else
        {
            _correnteEvidenziata = null;
        }
    }

    private void SelectionBadge_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoGridItem item })
        {
            SetStato(item, item.Stato == StatoSelezione.Selected ? StatoSelezione.None : StatoSelezione.Selected);
            e.Handled = true;
        }
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
        "Geolocalizzate" => item.HasGps,
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
        AggiornaFiltroUI();
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

    private void AggiornaStatusBar()
    {
        var testo = $"Tutte {_tutte.Count} · Selezionate {_conteggioSelezionate} · Scarti {_conteggioScarti}";
        if (_miniatureTotali > 0 && _miniatureCompletate < _miniatureTotali)
        {
            var percento = (int)(100.0 * _miniatureCompletate / _miniatureTotali);
            testo += $" · Miniature {_miniatureCompletate}/{_miniatureTotali} ({percento}%)";
        }
        StatusText.Text = testo;
    }

    private void ThumbnailLoader_ProgressoCambiato(int completate, int totale)
    {
        _miniatureCompletate = completate;
        _miniatureTotali = totale;
        AggiornaStatusBar();
    }

    private void SelectionManager_ScritturaFallita(Exception ex)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = $"Errore nel salvare le modifiche: {ex.Message}";
        });
    }

    /// <summary>
    /// Mostra l'overlay di caricamento solo se l'operazione supera i 300ms (evita flash inutile
    /// su cartelle piccole/già in cache). Il Dispose nasconde l'overlay e annulla il timer se
    /// l'operazione è già finita prima dei 300ms.
    /// </summary>
    private IDisposable MostraCaricamentoConRitardo(string messaggio)
    {
        CaricamentoText.Text = messaggio;
        var cts = new CancellationTokenSource();
        _ = MostraOverlayDopoRitardoAsync(cts.Token);
        return new AzioneAllaFine(() =>
        {
            cts.Cancel();
            CaricamentoOverlay.Visibility = Visibility.Collapsed;
        });
    }

    private async Task MostraOverlayDopoRitardoAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            CaricamentoOverlay.Visibility = Visibility.Visible;
        }
        catch (TaskCanceledException)
        {
            // Operazione finita prima dei 300ms: overlay mai mostrato, nessun flash.
        }
    }

    private sealed class AzioneAllaFine(Action azione) : IDisposable
    {
        public void Dispose() => azione();
    }

    private bool _aggiornamentoFiltroInCorso;

    private void FiltroComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aggiornamentoFiltroInCorso) return;
        if (FiltroComboBox.SelectedIndex >= 0 && FiltroComboBox.SelectedIndex < Filtri.Length && FiltroComboBox.SelectedIndex != _filtroIndice)
        {
            _filtroIndice = FiltroComboBox.SelectedIndex;
            ApplicaFiltro();
        }
    }

    private void AggiornaFiltroUI()
    {
        if (FiltroComboBox.SelectedIndex != _filtroIndice)
        {
            _aggiornamentoFiltroInCorso = true;
            try
            {
                FiltroComboBox.SelectedIndex = _filtroIndice;
            }
            finally
            {
                _aggiornamentoFiltroInCorso = false;
            }
        }
    }

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

            case VirtualKey.Enter when corrente is not null:
                ApriViewer(corrente);
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

    private void Griglia_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Griglia.SelectedItem is PhotoGridItem corrente)
        {
            ApriViewer(corrente);
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
