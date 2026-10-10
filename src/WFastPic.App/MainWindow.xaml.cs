using System.Collections.ObjectModel;
using System.Diagnostics;
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
        var impostazioni = ImpostazioniStore.Carica();
        _cellSize = Math.Clamp(impostazioni.DimensioneCellaGriglia, CellSizeMin, CellSizeMax);
        _nomiLuoghiOnline = impostazioni.NomiLuoghiOnline;
        Griglia.ItemsSource = _visibili;
        AggiornaStatusBar();
        AggiornaFiltroUI();

        _cartelleRecenti = RecentFoldersStore.Carica();
        CartelleRecentiList.ItemsSource = _cartelleRecenti;
        InizializzaAlberoCartelle();

        // handledEventsToo: la ScrollViewer interna della ListView consuma la rotella per lo
        // scroll e marca l'evento Handled prima che arrivi al gestore dichiarato in XAML.
        // ListViewItem può marcare Space/Enter come Handled prima che arrivino alla ListView: senza
        // handledEventsToo la selezione con Space non scatta.
        Griglia.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Griglia_KeyDown), handledEventsToo: true);
        Griglia.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Griglia_PointerWheelChanged), handledEventsToo: true);

        InizializzaViewer();

        Closed += (s, e) => ChiudiSessione();

        if (_cartelleRecenti.Count > 0 && Directory.Exists(_cartelleRecenti[0]))
        {
            var cartellaIniziale = _cartelleRecenti[0];
            DispatcherQueue.TryEnqueue(async () =>
            {
                await CaricaCartellaAsync(cartellaIniziale);
            });
        }
    }

    /// <summary>
    /// Sincrono di proposito: con un handler async il processo può terminare prima che la coda di
    /// scrittura sia svuotata, perdendo l'ultimo Space/Delete/rating. Il writer gira sul thread pool
    /// e non ha bisogno del thread UI, quindi l'attesa non va in deadlock.
    /// </summary>
    private void ChiudiSessione()
    {
        ImpostazioniStore.Salva(new Impostazioni(_cellSize) { NomiLuoghiOnline = _nomiLuoghiOnline });
        _thumbnailLoader?.Annulla();
        // Finestra chiusa col viewer aperto: le modifiche alla foto corrente vanno in coda prima
        // che la coda venga svuotata (un ritaglio non ancora applicato si perde, come in Picasa).
        if (_viewerAperto) SalvaRicettaCorrente();

        var selectionManager = _selectionManager;
        var db = _db;
        _selectionManager = null;
        _db = null;

        try
        {
            selectionManager?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // Errori di persistenza già notificati da ScritturaFallita: in chiusura non c'è altro da fare.
        }
        db?.Dispose();
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

        // Si staccano subito le risorse correnti: un secondo caricamento partito mentre questo è in
        // attesa (doppio click su una recente) non deve rilasciarle una seconda volta.
        if (_viewerAperto) ChiudiViewer(); // salva la ricetta sul sidecar della cartella che si lascia
        var selectionManagerPrecedente = _selectionManager;
        var dbPrecedente = _db;
        _selectionManager = null;
        _db = null;

        _tutte.Clear();
        _visibili.Clear();
        _perId.Clear();
        _gruppi.Clear();
        _conteggioSelezionate = 0;
        _conteggioScarti = 0;
        _filtroIndice = 0;
        _miniatureCompletate = 0;
        _miniatureTotali = 0;
        AggiornaAvanzamentoMiniature();

        using var overlayDelay = MostraCaricamentoConRitardo("Lettura foto…");

        // Progress<T> riporta sul thread UI; si aggiorna al massimo ogni 100ms per non intasare il
        // dispatcher con un evento per file su cartelle da migliaia di foto.
        var cronometroScansione = Stopwatch.StartNew();
        TimeSpan? ultimoAggiornamento = null;
        var progressoScansione = new Progress<(int Elaborate, int Totale)>(p =>
        {
            if (richiesta != _richiestaCaricamentoCartella) return;
            var adesso = cronometroScansione.Elapsed;
            if (p.Elaborate < p.Totale && ultimoAggiornamento is { } ultimo && adesso - ultimo < TimeSpan.FromMilliseconds(100)) return;
            ultimoAggiornamento = adesso;

            CaricamentoProgressBar.IsIndeterminate = p.Totale == 0;
            CaricamentoProgressBar.Maximum = Math.Max(1, p.Totale);
            CaricamentoProgressBar.Value = p.Elaborate;
            CaricamentoDettaglioText.Text = StimaAvanzamento.Descrivi(p.Elaborate, p.Totale, adesso);
        });

        if (selectionManagerPrecedente is not null)
        {
            selectionManagerPrecedente.ScritturaFallita -= SelectionManager_ScritturaFallita;
            await selectionManagerPrecedente.DisposeAsync();
        }
        dbPrecedente?.Dispose();

        LibrarySidecarDatabase db;
        IReadOnlyList<PhotoRecord> records;
        HashSet<long> conVideo;
        try
        {
            (db, records, conVideo) = await Task.Run(() =>
            {
                var database = LibrarySidecarDatabase.OpenOrCreate(cartella);
                try
                {
                    // Come Picasa: riaprire una cartella già vista legge solo le foto nuove o modificate.
                    var scanned = FolderScanner.Scan(cartella, database.CreaRisolutoreMetadatiNoti(), progressoScansione);
                    database.SyncScan(scanned);
                    var foto = database.GetAllPhotos();
                    // File.Exists ×2 per foto: su un disco esterno con ~1300 foto non deve girare sul thread UI.
                    var video = foto
                        .Where(r => CompanionVideoDetector.TrovaVideoAbbinato(Path.Combine(cartella, r.PathRelativo)) is not null)
                        .Select(r => r.Id)
                        .ToHashSet();
                    return (database, foto, video);
                }
                catch
                {
                    database.Dispose();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            // Cartella senza permessi, disco scollegato, DB bloccato: messaggio, mai crash.
            if (richiesta == _richiestaCaricamentoCartella)
            {
                StatusText.Text = $"Impossibile aprire «{cartella}»: {ex.Message}";
            }
            return;
        }

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
                Altitude = record.Altitude,
                Direction = record.Direction,
                GpsManuale = record.GpsManuale,
                HasVideo = conVideo.Contains(record.Id),
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
        _cronometroMiniature.Restart();
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

    private void SetStato(PhotoGridItem item, string nuovoStato) => SetStati([(item, nuovoStato)]);

    /// <summary>
    /// Applica più cambi di stato come un'unica azione utente (Ctrl+A, Shift+Space, Lascia):
    /// un solo suono, un solo aggiornamento di griglia e status bar invece di uno per foto.
    /// Il suono è quello del primo cambio, cioè dell'azione principale.
    /// </summary>
    private void SetStati(IReadOnlyList<(PhotoGridItem Item, string Stato)> modifiche)
    {
        string? statoSuono = null;
        var daNascondere = new List<PhotoGridItem>();
        var filtro = Filtri[_filtroIndice];

        foreach (var (item, nuovoStato) in modifiche)
        {
            if (!ApplicaStato(item, nuovoStato)) continue;
            statoSuono ??= nuovoStato;
            if (!CorrispondeAlFiltro(item, filtro)) daNascondere.Add(item);
        }

        if (statoSuono is null) return;

        ElementSoundPlayer.Play(statoSuono switch
        {
            StatoSelezione.Selected => ElementSoundKind.Invoke,
            StatoSelezione.Reject => ElementSoundKind.Hide,
            _ => ElementSoundKind.Focus,
        });

        NascondiDallaGriglia(daNascondere);
        AggiornaStatusBar();
    }

    private bool ApplicaStato(PhotoGridItem item, string nuovoStato)
    {
        var vecchioStato = item.Stato;
        if (vecchioStato == nuovoStato) return false;

        if (vecchioStato == StatoSelezione.Selected) _conteggioSelezionate--;
        if (vecchioStato == StatoSelezione.Reject) _conteggioScarti--;

        item.Stato = nuovoStato;

        if (nuovoStato == StatoSelezione.Selected) _conteggioSelezionate++;
        if (nuovoStato == StatoSelezione.Reject) _conteggioScarti++;

        _selectionManager?.SetStato(item.Id, nuovoStato);
        return true;
    }

    /// <summary>
    /// Toglie foto dalla griglia visibile (non più nel filtro, o rimosse dalla libreria). Se tra
    /// queste c'era la corrente, la selezione passa alla successiva rimasta (o alla precedente se
    /// era in fondo): l'utente resta nel punto in cui stava lavorando invece di tornare in cima.
    /// </summary>
    private void NascondiDallaGriglia(IReadOnlyCollection<PhotoGridItem> daNascondere)
    {
        if (daNascondere.Count == 0) return;

        var rimosse = daNascondere.ToHashSet();
        PhotoGridItem? nuovaCorrente = null;
        if (Griglia.SelectedItem is PhotoGridItem corrente && rimosse.Contains(corrente))
        {
            var indice = _visibili.IndexOf(corrente);
            nuovaCorrente = _visibili.Skip(indice + 1).FirstOrDefault(i => !rimosse.Contains(i))
                ?? _visibili.Take(indice).LastOrDefault(i => !rimosse.Contains(i));
        }

        if (rimosse.Count > _visibili.Count / 2)
        {
            // Rimozione massiccia (es. Ctrl+A nel filtro Scarti): ricostruire costa meno di N Remove O(n).
            var rimaste = _visibili.Where(i => !rimosse.Contains(i)).ToList();
            _visibili.Clear();
            foreach (var item in rimaste) _visibili.Add(item);
        }
        else
        {
            foreach (var item in rimosse) _visibili.Remove(item);
        }

        if (nuovaCorrente is null)
        {
            AssicuraSelezione();
            return;
        }

        Griglia.SelectedItem = nuovaCorrente;
        if (ViewerOverlay.Visibility == Visibility.Visible) return;

        // Il focus da tastiera era sul contenitore rimosso: senza riportarlo sulla nuova corrente
        // ←/→ ripartirebbero da un punto indefinito. Il contenitore esiste solo dopo il layout.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Griglia.SelectedItem == nuovaCorrente && Griglia.ContainerFromItem(nuovaCorrente) is ListViewItem contenitore)
            {
                contenitore.Focus(FocusState.Programmatic);
            }
        });
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

    // Un doppio click sul badge è un click "nervoso" sulla spunta, non una richiesta di aprire il viewer.
    private void SelectionBadge_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => e.Handled = true;

    private void Griglia_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is PhotoGridItem { Thumbnail: null } item)
        {
            _thumbnailLoader?.Priorita(item);
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
        StatusText.Text = $"Selezionate {_conteggioSelezionate} di {_tutte.Count} · Scarti {_conteggioScarti}";
    }

    private readonly Stopwatch _cronometroMiniature = new();

    private void ThumbnailLoader_ProgressoCambiato(int completate, int totale)
    {
        _miniatureCompletate = completate;
        _miniatureTotali = totale;
        AggiornaAvanzamentoMiniature();
    }

    /// <summary>Barra in basso, visibile solo finché mancano miniature: la griglia resta usabile.</summary>
    private void AggiornaAvanzamentoMiniature()
    {
        if (_miniatureTotali == 0 || _miniatureCompletate >= _miniatureTotali)
        {
            MiniaturePanel.Visibility = Visibility.Collapsed;
            return;
        }

        // Stesso principio dell'overlay: con le miniature già in cache finisce tutto in pochi ms, niente flash.
        if (_cronometroMiniature.Elapsed < TimeSpan.FromMilliseconds(300)) return;

        MiniaturePanel.Visibility = Visibility.Visible;
        MiniatureProgressBar.Maximum = _miniatureTotali;
        MiniatureProgressBar.Value = _miniatureCompletate;
        MiniatureText.Text = "Miniature " + StimaAvanzamento.Descrivi(
            _miniatureCompletate, _miniatureTotali, _cronometroMiniature.Elapsed);
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
        CaricamentoProgressBar.IsIndeterminate = true;
        CaricamentoProgressBar.Value = 0;
        CaricamentoDettaglioText.Text = "";
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
                var modificheRaffica = new List<(PhotoGridItem, string)> { (corrente, StatoSelezione.Selected) };
                if (_gruppi.TryGetValue(groupId, out var membriGruppo))
                {
                    modificheRaffica.AddRange(membriGruppo
                        .Where(m => m.Id != corrente.Id)
                        .Select(m => (m, StatoSelezione.Reject)));
                }
                SetStati(modificheRaffica);
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
                SetStati(_visibili.Select(i => (i, StatoSelezione.Selected)).ToList());
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
        ApplicaDimensioneCella();

        e.Handled = true;
    }

    // La dimensione salvata (ImpostazioniStore) si può applicare solo quando il pannello esiste.
    private void Griglia_Loaded(object sender, RoutedEventArgs e) => ApplicaDimensioneCella();

    private void ApplicaDimensioneCella()
    {
        if (Griglia.ItemsPanelRoot is ItemsWrapGrid wrap)
        {
            wrap.ItemWidth = _cellSize;
            wrap.ItemHeight = _cellSize;
        }
    }

    // --- Gestisci scarti… ---

    private void GestisciScartiButton_Click(object sender, RoutedEventArgs e) => _ = MostraDialogoScartiAsync();

    private async Task MostraDialogoScartiAsync()
    {
        if (_db is null) return;

        _rejectManagerCorrente = new RejectManager(_db);
        // Fonte di verità = stato in memoria, non il DB: la coda di scrittura può non aver ancora
        // persistito l'ultimo Delete/Space, e una foto appena "recuperata" finirebbe nel Cestino.
        _scartiCorrenti = _tutte
            .Where(i => i.IsReject)
            .Select(i => new PhotoRecord(i.Id, i.PathRelativo, i.FileSize, i.LastWriteUtc, i.CaptureDate, i.Stato, i.Rating))
            .ToList();

        var conVideo = _tutte.Count(i => i.IsReject && i.HasVideo);
        AggiornaDialogoScarti(conVideo > 0
            ? $"{_scartiCorrenti.Count} foto in scarto ({conVideo} con video abbinato, che verrà spostato insieme)"
            : $"{_scartiCorrenti.Count} foto in scarto");

        DialogScarti.XamlRoot = Content.XamlRoot;
        await DialogScarti.ShowAsync();
    }

    private bool _confermaCestinoRichiesta;

    private void AggiornaDialogoScarti(string riepilogo)
    {
        _confermaCestinoRichiesta = false;
        CestinoButton.Content = "Sposta nel Cestino";
        ScartiSummary.Text = riepilogo;
        // Il video abbinato segue la foto (Cestino o Scarti/): va detto prima di confermare.
        ScartiList.ItemsSource = _scartiCorrenti?
            .Select(s => _perId.TryGetValue(s.Id, out var item) && item.HasVideo ? $"{s.PathRelativo}  + video" : s.PathRelativo)
            .ToList();
    }

    private void CestinoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rejectManagerCorrente is null || _scartiCorrenti is null || _scartiCorrenti.Count == 0) return;

        // Conferma in due passi nello stesso dialogo (WinUI non consente un ContentDialog annidato).
        if (!_confermaCestinoRichiesta)
        {
            _confermaCestinoRichiesta = true;
            CestinoButton.Content = $"Conferma: {_scartiCorrenti.Count} foto nel Cestino";
            return;
        }

        var rejectManager = _rejectManagerCorrente;
        EseguiSuScarti(rejectManager.SpostaNelCestino, "nel Cestino");
    }

    private void SpostaInScartiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rejectManagerCorrente is null) return;
        var rejectManager = _rejectManagerCorrente;
        EseguiSuScarti(foto => rejectManager.SpostaInScarti(foto), "in Scarti/");
    }

    /// <summary>
    /// Applica l'azione foto per foto: un file bloccato o senza permessi non interrompe le altre
    /// né fa crashare l'app. Le foto non spostate restano nel dialogo con il motivo.
    /// </summary>
    private void EseguiSuScarti(Action<PhotoRecord> azione, string destinazione)
    {
        if (_scartiCorrenti is null) return;

        var rimosse = new List<PhotoGridItem>();
        var errori = new List<string>();
        foreach (var foto in _scartiCorrenti)
        {
            try
            {
                azione(foto);
                if (RimuoviDallaLibreria(foto.Id) is { } item) rimosse.Add(item);
            }
            catch (Exception ex)
            {
                errori.Add($"{foto.PathRelativo}: {ex.Message}");
            }
        }

        if (rimosse.Count > 0)
        {
            // Le raffiche vanno ricalcolate: etichette "n/m" e membri di gruppo puntano a foto non più presenti.
            RaggruppaRaffiche();
            NascondiDallaGriglia(rimosse);
            AggiornaStatusBar();
        }

        if (errori.Count == 0)
        {
            DialogScarti.Hide();
            return;
        }

        _scartiCorrenti = _scartiCorrenti.Where(f => _perId.ContainsKey(f.Id)).ToList();
        AggiornaDialogoScarti(
            $"{rimosse.Count} spostate {destinazione}, {errori.Count} non riuscite:\n" +
            string.Join("\n", errori.Take(5)) +
            (errori.Count > 5 ? $"\n… e altre {errori.Count - 5}" : ""));
    }

    private void LasciaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scartiCorrenti is null) return;
        var modifiche = _scartiCorrenti
            .Where(foto => _perId.ContainsKey(foto.Id))
            .Select(foto => (_perId[foto.Id], StatoSelezione.None))
            .ToList();
        SetStati(modifiche);
        DialogScarti.Hide();
    }

    private PhotoGridItem? RimuoviDallaLibreria(long photoId)
    {
        if (!_perId.TryGetValue(photoId, out var item)) return null;

        if (item.Stato == StatoSelezione.Reject) _conteggioScarti--;
        if (item.Stato == StatoSelezione.Selected) _conteggioSelezionate--;

        _tutte.Remove(item);
        _perId.Remove(photoId);
        return item;
    }
}
