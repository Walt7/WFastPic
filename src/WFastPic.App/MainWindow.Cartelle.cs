using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.Storage.Pickers;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WinRT.Interop;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>Una cartella nel pannello: nome in evidenza, numero di foto e periodo sotto.</summary>
public sealed class VoceCartella : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Percorso { get; init; }
    public required string Nome { get; init; }
    public required bool Esiste { get; init; }
    public required string Periodo { get; init; }
    public required CartellaArchivio Dati { get; init; }

    private string _dettaglio = "";
    /// <summary>"1.300 foto · 37 selezionate": per la cartella aperta segue le selezioni dal vivo.</summary>
    public string Dettaglio
    {
        get => _dettaglio;
        set { if (_dettaglio == value) return; _dettaglio = value; OnPropertyChanged(); }
    }

    public Visibility PeriodoVisibile => string.IsNullOrEmpty(Periodo) ? Visibility.Collapsed : Visibility.Visible;

    public double Opacita => Esiste ? 1.0 : 0.45;

    private bool _isCorrente;
    public bool IsCorrente
    {
        get => _isCorrente;
        set { if (_isCorrente == value) return; _isCorrente = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? nome = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}

/// <summary>Un anno nel pannello cartelle, come le intestazioni per anno di Picasa.</summary>
public sealed class GruppoAnno : ObservableCollection<VoceCartella>
{
    public required string Titolo { get; init; }
}

/// <summary>
/// Pannello cartelle stile Picasa: sempre visibile accanto alla griglia, con "le mie cartelle" (quelle
/// già aperte) raggruppate per anno di scatto, ognuna con numero di foto e periodo letti dal suo
/// sidecar. "Sfoglia il PC" resta una sezione secondaria per aggiungerne di nuove: dischi e
/// sottocartelle si leggono in background (un disco esterno che si avvia o un'unità di rete
/// scollegata non bloccano più la finestra) e le cartelle di sistema non compaiono.
/// </summary>
public partial class MainWindow
{
    private readonly ObservableCollection<GruppoAnno> _gruppiCartelle = new();
    private CollectionViewSource? _vistaCartelle;
    private bool _alberoInizializzato;
    private int _richiestaElencoCartelle;

    private void InizializzaPannelloCartelle(bool aperto)
    {
        RootSplitView.IsPaneOpen = aperto;
        _vistaCartelle = new CollectionViewSource { IsSourceGrouped = true, Source = _gruppiCartelle };
        ElencoCartelle.ItemsSource = _vistaCartelle.View;
        _ = RicaricaElencoCartelleAsync();
    }

    private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e) =>
        RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;

    /// <summary>Rilegge i riepiloghi in background (un sidecar per cartella) e ricostruisce i gruppi per anno.</summary>
    private async Task RicaricaElencoCartelleAsync()
    {
        var richiesta = ++_richiestaElencoCartelle;
        var percorsi = _cartelleRecenti.ToList();

        var cartelle = await Task.Run(() => percorsi.Select(percorso =>
        {
            var esiste = Directory.Exists(percorso);
            var riepilogo = esiste ? LibrarySidecarDatabase.LeggiRiepilogo(percorso) : null;
            return new CartellaArchivio(percorso, riepilogo?.NumeroFoto, riepilogo?.PrimoScatto, riepilogo?.UltimoScatto, esiste, riepilogo?.NumeroSelezionate);
        }).ToList());

        if (richiesta != _richiestaElencoCartelle) return;

        var corrente = _db?.CartellaFoto;
        _gruppiCartelle.Clear();
        foreach (var (anno, elenco) in ArchivioCartelle.RaggruppaPerAnno(cartelle))
        {
            var gruppo = new GruppoAnno { Titolo = ArchivioCartelle.TitoloAnno(anno) };
            foreach (var c in elenco)
            {
                gruppo.Add(new VoceCartella
                {
                    Percorso = c.Percorso,
                    Nome = ArchivioCartelle.Nome(c.Percorso),
                    Dettaglio = ArchivioCartelle.Dettaglio(c),
                    Periodo = ArchivioCartelle.PeriodoCartella(c),
                    Dati = c,
                    Esiste = c.Esiste,
                    IsCorrente = corrente is not null && StessoPercorso(c.Percorso, corrente),
                });
            }
            _gruppiCartelle.Add(gruppo);
        }

        PannelloCartelleVuotoText.Visibility = cartelle.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AggiornaCartellaCorrenteNelPannello();
    }

    /// <summary>
    /// Numeri della cartella aperta dallo stato in memoria (il sidecar può essere indietro di qualche
    /// scrittura in coda): chiamato a ogni cambio di selezione, senza rileggere i dischi.
    /// </summary>
    private void AggiornaCartellaCorrenteNelPannello()
    {
        if (_db?.CartellaFoto is not string corrente) return;
        foreach (var voce in _gruppiCartelle.SelectMany(g => g).Where(v => v.IsCorrente && StessoPercorso(v.Percorso, corrente)))
        {
            voce.Dettaglio = ArchivioCartelle.Dettaglio(voce.Dati with { NumeroFoto = _tutte.Count, NumeroSelezionate = _conteggioSelezionate });
        }
    }

    private static bool StessoPercorso(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Dopo l'apertura di una cartella: in cima ai recenti, evidenziata, conteggi aggiornati.</summary>
    private void RegistraCartellaAperta(string cartella)
    {
        _cartelleRecenti = RecentFoldersStore.AggiungiRecente(_cartelleRecenti, cartella);
        RecentFoldersStore.Salva(_cartelleRecenti);
        _ = RicaricaElencoCartelleAsync();
    }

    private async void ElencoCartelle_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not VoceCartella voce) return;
        if (!voce.Esiste)
        {
            StatusText.Text = T("Cartella non trovata: {0} (disco scollegato o cartella spostata)", voce.Percorso);
            return;
        }
        await CaricaCartellaAsync(voce.Percorso);
    }

    private async void AggiungiCartellaButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var cartella = await picker.PickSingleFolderAsync();
        if (cartella is not null) await CaricaCartellaAsync(cartella.Path);
    }

    // I MenuFlyout non sono nell'albero visuale: la voce cliccata passa alle righe del menu da qui.
    private void MenuCartella_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout { Target: FrameworkElement { DataContext: VoceCartella voce } } menu) return;
        foreach (var riga in menu.Items) riga.DataContext = voce;
    }

    // Rilegge la cartella da disco, sottocartelle comprese: foto nuove o tolte da fuori compaiono o spariscono.
    private async void CartellaRicarica_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VoceCartella voce }) return;
        if (!voce.Esiste)
        {
            StatusText.Text = T("Cartella non trovata: {0} (disco scollegato o cartella spostata)", voce.Percorso);
            return;
        }
        await CaricaCartellaAsync(voce.Percorso);
    }

    private void CartellaApriInEsplora_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VoceCartella { Esiste: true } voce }) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{voce.Percorso}\"") { UseShellExecute = true }); }
        catch { /* Esplora file non disponibile */ }
    }

    // Toglie solo la voce dall'elenco: la cartella, le foto e il sidecar restano dove sono.
    private void CartellaRimuoviDallElenco_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VoceCartella voce }) return;
        _cartelleRecenti = _cartelleRecenti.Where(c => !StessoPercorso(c, voce.Percorso)).ToList();
        RecentFoldersStore.Salva(_cartelleRecenti);
        _ = RicaricaElencoCartelleAsync();
    }

    // ---------------------------------------------------------------- Sfoglia il PC

    // L'albero si costruisce solo quando serve: chi usa "le mie cartelle" non paga mai l'enumerazione dei dischi.
    private async void SfogliaPcExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (_alberoInizializzato) return;
        _alberoInizializzato = true;

        var radici = await Task.Run(() =>
        {
            var elenco = new List<DirectoryTreeEntry>();
            foreach (var cloud in CartelleCloudDisponibili()) elenco.Add(new DirectoryTreeEntry(ArchivioCartelle.Nome(cloud), cloud, ""));

            // IsReady può fermarsi secondi su un'unità di rete scollegata: qui è fuori dal thread UI.
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    var lettera = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
                    var nome = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? lettera : $"{drive.VolumeLabel} ({lettera})";
                    elenco.Add(new DirectoryTreeEntry(nome, drive.RootDirectory.FullName, ""));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return elenco;
        });

        DirectoryTree.RootNodes.Clear();
        foreach (var radice in radici)
            DirectoryTree.RootNodes.Add(new TreeViewNode { Content = radice, HasUnrealizedChildren = true });
    }

    private static IEnumerable<string> CartelleCloudDisponibili()
    {
        var profilo = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var immagini = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var candidate = new[] { immagini, Path.Combine(profilo, "Dropbox") }
            .Concat(Directory.Exists(profilo)
                ? Directory.EnumerateDirectories(profilo, "OneDrive*", SearchOption.TopDirectoryOnly)
                : Enumerable.Empty<string>());

        return candidate.Where(c => !string.IsNullOrEmpty(c) && Directory.Exists(c)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async void DirectoryTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var nodo = args.Node;
        if (!nodo.HasUnrealizedChildren || nodo.Content is not DirectoryTreeEntry { Percorso: string percorso }) return;
        nodo.HasUnrealizedChildren = false;

        var figli = await Task.Run(() => SottocartelleVisibili(percorso));

        nodo.Children.Clear();
        foreach (var figlio in figli)
        {
            nodo.Children.Add(new TreeViewNode
            {
                Content = new DirectoryTreeEntry(ArchivioCartelle.Nome(figlio), figlio, ""),
                // Se ha sottocartelle si scopre all'espansione: niente lettura anticipata di ogni ramo.
                HasUnrealizedChildren = true,
            });
        }
    }

    private static List<string> SottocartelleVisibili(string percorso)
    {
        try
        {
            return new DirectoryInfo(percorso).EnumerateDirectories()
                .Where(d => !ArchivioCartelle.DaNascondere(d.Name, d.Attributes))
                .Select(d => d.FullName)
                .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return []; // cartella protetta o unità rimossa mentre si espandeva
        }
    }

    private async void DirectoryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var voce = args.InvokedItem switch
        {
            TreeViewNode { Content: DirectoryTreeEntry d } => d,
            DirectoryTreeEntry d => d,
            _ => null,
        };
        if (voce?.Percorso is string percorso) await CaricaCartellaAsync(percorso);
    }
}
