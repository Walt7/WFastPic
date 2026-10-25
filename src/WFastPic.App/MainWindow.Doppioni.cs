using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WFastPic.Core.Decoding;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Processing;
using WFastPic.Core.Selection;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Doppioni come "Mostra file duplicati" di Picasa: le foto presenti più volte, identiche (stessa
/// dimensione, poi confronto del contenuto solo tra quelle) o quasi uguali (la stessa foto ridimensionata o
/// ricompressa, dall'impronta delle miniature già in cache), nella cartella aperta e nelle altre cartelle
/// note. Filtro «Doppioni» con la griglia divisa per gruppi, la copia da tenere suggerita e "Scarta le
/// copie" con conferma: mai una cancellazione, il Cestino passa sempre da «Gestisci scarti». Le altre
/// cartelle si leggono soltanto (sidecar in sola lettura) e i loro file non vengono mai toccati.
/// </summary>
public partial class MainWindow
{
    private const string FiltroDoppioni = "Doppioni";

    /// <summary>Una copia in un'altra cartella (id negativo, per non confondersi con le foto di qui).</summary>
    private sealed record CopiaAltrove(long Id, string Cartella, FotoAltraCartella Foto)
    {
        public string PercorsoCompleto => Path.Combine(Cartella, Foto.PathRelativo);
        public string NomeCartella => Path.GetFileName(Cartella.TrimEnd('\\', '/')) is { Length: > 0 } nome ? nome : Cartella;
    }

    private readonly Dictionary<int, List<PhotoGridItem>> _gruppiDoppioni = new();
    private readonly Dictionary<int, List<CopiaAltrove>> _altroveDoppioni = new();

    /// <summary>Gruppi di file identici, calcolati una volta per cartella (leggono i file).</summary>
    private IReadOnlyList<IReadOnlyList<long>>? _identiche;

    /// <summary>Solo le copie altrove che hanno un doppione qui (trovate in background), non tutte le altre cartelle.</summary>
    private Dictionary<long, CopiaAltrove> _copieAltrove = new();

    /// <summary>Pixel delle foto nei gruppi (dall'intestazione del file), per preferire l'originale.</summary>
    private readonly Dictionary<long, long> _pixelDoppioni = new();

    private void AzzeraDoppioni()
    {
        _gruppiDoppioni.Clear();
        _altroveDoppioni.Clear();
        _identiche = null;
        _copieAltrove = new();
        _pixelDoppioni.Clear();
    }

    /// <summary>Impronte calcolate in passato (riapertura): i quasi uguali ci sono subito.</summary>
    private static void AssegnaImpronteSalvate(IEnumerable<PhotoGridItem> foto, IReadOnlyDictionary<long, (ulong Impronta, double Rapporto)> salvate)
    {
        foreach (var item in foto)
        {
            if (!salvate.TryGetValue(item.Id, out var s)) continue;
            item.Impronta = s.Impronta;
            item.RapportoMiniatura = s.Rapporto;
        }
    }

    /// <summary>
    /// Prima analisi in background, a miniature finite: impronte dalle miniature in cache (veloce, niente
    /// originali); poi, una volta per cartella, le altre cartelle note (dai loro sidecar) e il confronto dei
    /// file con la stessa dimensione; infine le dimensioni delle sole foto doppie. Con il viewer aperto si
    /// mette in pausa.
    /// </summary>
    private Task AnalizzaDoppioniAsync(SelectionManager manager, CancellationToken token)
    {
        var db = _db;
        if (db is null) return Task.CompletedTask;

        var cartellaCache = Path.Combine(db.CartellaFoto, ".wfastpic", "thumbcache");
        var daCalcolare = _tutte
            .Where(f => f.Impronta is null)
            .Select(f => (Item: f, Miniatura: Path.Combine(cartellaCache, ThumbnailCacheLoader.NomeFileCache(f))))
            .ToList();
        var locali = _tutte.ToDictionary(f => f.Id, f => (f.PercorsoCompleto, Foto: new FotoPerDoppioni(f.Id, f.FileSize, f.CaptureDate, f.Impronta, f.RapportoMiniatura)));
        var altreCartelle = _identiche is null
            ? _cartelleRecenti.Where(c => !StessoPercorso(c, db.CartellaFoto)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : null;
        var cronometro = Stopwatch.StartNew();

        return Task.Run(async () =>
        {
            var calcolate = new List<(PhotoGridItem Item, ulong Impronta, double Rapporto)>();
            for (var i = 0; i < daCalcolare.Count && !token.IsCancellationRequested; i++)
            {
                while (Viewer.IsAperto && !token.IsCancellationRequested) await Task.Delay(500);
                var (item, miniatura) = daCalcolare[i];
                try
                {
                    if (!File.Exists(miniatura)) continue;
                    var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(miniatura, ImprontaPercettiva.LatoAnalisi);
                    var impronta = ImprontaPercettiva.Calcola(bgra, w, h);
                    var rapporto = (double)w / h;
                    manager.SalvaImpronta(item.Id, ImprontaPercettiva.Versione, impronta, rapporto);
                    calcolate.Add((item, impronta, rapporto));
                    locali[item.Id] = (item.PercorsoCompleto, locali[item.Id].Foto with { Impronta = impronta, Rapporto = rapporto });
                }
                catch (Exception)
                {
                    // Miniatura illeggibile: la foto resta fuori dai quasi uguali, non dagli identici.
                }

                if ((i + 1) % 25 == 0 || i + 1 == daCalcolare.Count)
                {
                    var completate = i + 1;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!token.IsCancellationRequested)
                            MostraAvanzamentoVolti(completate, daCalcolare.Count, cronometro.Elapsed, T("Doppioni") + " ");
                    });
                }
            }

            IReadOnlyList<IReadOnlyList<long>>? identiche = null;
            Dictionary<long, CopiaAltrove>? copieAltrove = null;
            var pixelAltrove = new Dictionary<long, long>();
            if (altreCartelle is not null && !token.IsCancellationRequested)
            {
                var percorsiQui = locali.Values.Select(l => Path.GetFullPath(l.PercorsoCompleto)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var tutteAltrove = LeggiAltreCartelle(altreCartelle, percorsiQui, token);

                // Identici: si leggono solo i file con la dimensione di una foto di qui (anche nelle altre cartelle).
                var dimensioniQui = locali.Values.Select(l => l.Foto.Size).ToHashSet();
                var percorsi = locali.ToDictionary(l => l.Key, l => l.Value.PercorsoCompleto);
                foreach (var copia in tutteAltrove.Values.Where(c => dimensioniQui.Contains(c.Foto.Size)))
                    percorsi[copia.Id] = copia.PercorsoCompleto;

                identiche = RicercaDoppioni.TrovaIdentiche(
                    percorsi.Keys.Select(id => (id, id > 0 ? locali[id].Foto.Size : tutteAltrove[id].Foto.Size)),
                    (a, b) =>
                    {
                        try { return RicercaDoppioni.StessoContenuto(percorsi[a], percorsi[b]); }
                        catch (Exception) { return false; } // file bloccato, sparito o disco scollegato: non è un doppione sicuro
                    },
                    token);

                // Delle altre cartelle si tengono solo le copie che hanno un doppione qui e che esistono ancora.
                var gruppi = RicercaDoppioni.Raggruppa(
                    locali.Values.Select(l => l.Foto).ToList(), identiche, tutteAltrove.Values.Select(PerDoppioni).ToList());
                copieAltrove = gruppi
                    .SelectMany(g => g.Altrove)
                    .Select(id => tutteAltrove[id])
                    .Where(c => File.Exists(c.PercorsoCompleto))
                    .ToDictionary(c => c.Id);

                foreach (var copia in copieAltrove.Values)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        var (w, h) = await PreviewDecoder.LeggiDimensioniAsync(copia.PercorsoCompleto);
                        pixelAltrove[copia.Id] = (long)w * h;
                    }
                    catch (Exception)
                    {
                        pixelAltrove[copia.Id] = 0;
                    }
                }
            }

            var gruppiPronti = new TaskCompletionSource<List<PhotoGridItem>>();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested) { gruppiPronti.TrySetResult([]); return; }
                foreach (var (item, impronta, rapporto) in calcolate)
                {
                    item.Impronta = impronta;
                    item.RapportoMiniatura = rapporto;
                }
                if (identiche is not null) _identiche = identiche;
                if (copieAltrove is not null) _copieAltrove = copieAltrove;
                foreach (var (id, p) in pixelAltrove) _pixelDoppioni[id] = p;
                RaggruppaDoppioni();
                MiniaturePanel.Visibility = Visibility.Collapsed;
                gruppiPronti.TrySetResult(_gruppiDoppioni.Values.SelectMany(g => g).Where(f => !_pixelDoppioni.ContainsKey(f.Id)).ToList());
            });

            // Solo le foto doppie: quante sono grandi davvero (la copia WhatsApp ha meno pixel dell'originale).
            var senzaPixel = await gruppiPronti.Task;
            var pixel = new Dictionary<long, long>();
            foreach (var foto in senzaPixel)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    var (w, h) = await PreviewDecoder.LeggiDimensioniAsync(foto.PercorsoCompleto);
                    pixel[foto.Id] = (long)w * h;
                }
                catch (Exception)
                {
                    pixel[foto.Id] = 0;
                }
            }

            if (pixel.Count == 0) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested) return;
                foreach (var (id, p) in pixel) _pixelDoppioni[id] = p;
                AggiornaMiglioriDoppioni();
            });
        }, token);
    }

    /// <summary>
    /// Le foto delle altre cartelle note, dai loro sidecar in sola lettura (mai creati, mai scritti). Una
    /// cartella su un disco scollegato o con un sidecar illeggibile non dà niente.
    /// </summary>
    /// <summary>
    /// Foto delle altre cartelle note. Una cartella madre e le sue sottocartelle catalogano gli stessi file:
    /// un file di qui (o già letto da un'altra cartella) è lo stesso file, non una copia, e resta fuori,
    /// altrimenti risulterebbe doppione di sé stesso e "Scarta le copie" scarterebbe l'unico originale.
    /// </summary>
    private static Dictionary<long, CopiaAltrove> LeggiAltreCartelle(
        IReadOnlyList<string> cartelle, HashSet<string> percorsiQui, CancellationToken token)
    {
        var copie = new Dictionary<long, CopiaAltrove>();
        var visti = new HashSet<string>(percorsiQui, StringComparer.OrdinalIgnoreCase);
        long prossimoId = -1;
        foreach (var cartella in cartelle)
        {
            if (token.IsCancellationRequested) break;
            foreach (var foto in LibrarySidecarDatabase.LeggiPerDoppioni(cartella, ImprontaPercettiva.Versione))
            {
                var copia = new CopiaAltrove(prossimoId, cartella, foto);
                if (!visti.Add(Path.GetFullPath(copia.PercorsoCompleto))) continue;
                copie[prossimoId] = copia;
                prossimoId--;
            }
        }
        return copie;
    }

    private static FotoPerDoppioni PerDoppioni(CopiaAltrove c) =>
        new(c.Id, c.Foto.Size, c.Foto.CaptureDate, c.Foto.Impronta, c.Foto.Rapporto);

    /// <summary>Ricalcola i gruppi da impronte e identici già noti (niente letture di file): dopo l'analisi e dopo il Cestino.</summary>
    private void RaggruppaDoppioni()
    {
        _gruppiDoppioni.Clear();
        _altroveDoppioni.Clear();
        var gruppi = RicercaDoppioni.Raggruppa(
            _tutte.Select(f => new FotoPerDoppioni(f.Id, f.FileSize, f.CaptureDate, f.Impronta, f.RapportoMiniatura)).ToList(),
            _identiche ?? [],
            _copieAltrove.Values.Select(PerDoppioni).ToList());

        foreach (var item in _tutte) item.DoppioneId = null;
        for (var i = 0; i < gruppi.Count; i++)
        {
            var membri = gruppi[i].Foto.Select(id => _perId[id]).ToList();
            var altrove = gruppi[i].Altrove.Select(id => _copieAltrove[id]).ToList();
            var cartelle = altrove.Count == 0 ? null : NomiCartelleCopie(altrove);
            foreach (var item in membri)
            {
                item.DoppioneId = i + 1;
                item.DoppioneIdentico = gruppi[i].Identiche;
                item.DoppioneAltrove = cartelle;
            }
            _gruppiDoppioni[i + 1] = membri;
            _altroveDoppioni[i + 1] = altrove;
        }

        AggiornaMiglioriDoppioni();
        if (Filtri[_filtroIndice] == FiltroDoppioni) ApplicaFiltro();
    }

    /// <summary>"«Puglia» (2), «Roma»".</summary>
    private static string NomiCartelleCopie(IEnumerable<CopiaAltrove> copie) =>
        string.Join(", ", copie
            .GroupBy(c => c.NomeCartella, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() > 1 ? $"«{g.Key}» ({g.Count()})" : $"«{g.Key}»"));

    /// <summary>
    /// La copia da tenere cambia con le scelte dell'utente (selezione, voto, scarto) e con il voto
    /// automatico: si ricalcola dopo ognuna, costa poco. Può essere una copia in un'altra cartella: allora
    /// tutte quelle di qui sono scartabili.
    /// </summary>
    private void AggiornaMiglioriDoppioni()
    {
        foreach (var (gruppo, membri) in _gruppiDoppioni)
        {
            var altrove = _altroveDoppioni.GetValueOrDefault(gruppo) ?? [];
            var candidati = membri.Select(f => new CandidatoDoppione(
                    f.Id, f.PathRelativo, f.FileSize,
                    Selezionata: f.Stato == StatoSelezione.Selected,
                    Scartata: f.Stato == StatoSelezione.Reject,
                    Voto: f.Rating,
                    Modificata: f.HaModifiche,
                    HaDataScatto: f.CaptureDate is not null,
                    Pixel: _pixelDoppioni.GetValueOrDefault(f.Id),
                    VotoAutomatico: f.VotoAuto?.Stelle ?? 0))
                .Concat(altrove.Select(c => new CandidatoDoppione(
                    c.Id, c.Foto.PathRelativo, c.Foto.Size,
                    Selezionata: c.Foto.Stato == StatoSelezione.Selected,
                    Scartata: c.Foto.Stato == StatoSelezione.Reject,
                    Voto: c.Foto.Rating,
                    Modificata: c.Foto.Modificata,
                    HaDataScatto: c.Foto.CaptureDate is not null,
                    Pixel: _pixelDoppioni.GetValueOrDefault(c.Id))))
                .ToList();
            var migliore = RicercaDoppioni.ScegliMigliore(candidati);
            var miglioreAltrove = altrove.FirstOrDefault(c => c.Id == migliore);

            foreach (var f in membri)
            {
                f.MiglioreDoppione = f.Id == migliore;
                f.MiglioreAltrove = miglioreAltrove is null ? null : $"«{miglioreAltrove.NomeCartella}»";
            }
        }
        AggiornaScartaDoppioni();
    }

    /// <summary>
    /// Le copie di qui che non sono la migliore e non sono ancora scartate. Quelle selezionate restano:
    /// se le hai scelte tu (anche due copie), non è un suggerimento automatico a toglierle.
    /// </summary>
    private List<PhotoGridItem> CopieDaScartare() =>
        _gruppiDoppioni.Values
            .SelectMany(g => g)
            .Where(f => !f.MiglioreDoppione && f.Stato is not (StatoSelezione.Reject or StatoSelezione.Selected))
            .ToList();

    private void AggiornaScartaDoppioni()
    {
        var quante = Filtri[_filtroIndice] == FiltroDoppioni ? CopieDaScartare().Count : 0;
        ScartaDoppioniButton.Visibility = quante > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScartaDoppioniText.Text = T("Scarta le copie ({0})", quante);
    }

    /// <summary>Titolo del gruppo nella griglia: "Foto identiche · 2 foto · anche in «Puglia»".</summary>
    private string TitoloGruppoDoppioni(GruppoGiorno gruppo)
    {
        if (gruppo.Count == 0) return "";
        var titolo = gruppo[0].DoppioneIdentico
            ? T("Foto identiche · {0}", Foto(gruppo.Count))
            : T("Stessa foto salvata più volte · {0}", Foto(gruppo.Count));
        return gruppo[0].DoppioneAltrove is string cartelle ? titolo + " · " + T("anche in {0}", cartelle) : titolo;
    }

    /// <summary>Tiene in ogni gruppo la copia suggerita e segna come scarti le altre, con conferma.</summary>
    private async void ScartaDoppioniButton_Click(object sender, RoutedEventArgs e)
    {
        var daScartare = CopieDaScartare();
        if (daScartare.Count == 0) return;

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = T("Scartare {0}?", Foto(daScartare.Count)),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Text = T("In ogni gruppo resta la copia da tenere (quella che hai scelto tu, altrimenti l'originale più " +
                         "grande, a volte in un'altra cartella) e le altre di questa cartella vengono solo segnate come " +
                         "scarti: puoi rivederle nel filtro «Scarti» e niente viene cancellato senza passare da " +
                         "«Gestisci scarti». Le altre cartelle non vengono toccate."),
            },
            PrimaryButtonText = T("Scarta"),
            CloseButtonText = T("Annulla"),
            DefaultButton = ContentDialogButton.Primary,
        };

        try
        {
            if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        }
        catch (Exception)
        {
            return; // un altro dialogo è già aperto
        }

        // Ricalcolate adesso: mentre il dialogo era aperto l'analisi può aver cambiato la copia da tenere.
        daScartare = CopieDaScartare();
        if (daScartare.Count == 0) return;
        SetStati(daScartare.Select(f => (f, StatoSelezione.Reject)).ToList());
    }
}

/// <summary>Testi dei doppioni (tooltip in griglia).</summary>
internal static class EtichetteDoppioni
{
    public static string Descrivi(bool identica, bool migliore, string? altrove, string? miglioreAltrove)
    {
        var parti = new List<string>
        {
            identica ? T("Doppione: file identico a un altro") : T("Doppione: la stessa foto salvata più volte"),
        };
        if (altrove is not null) parti.Add(T("anche in {0}", altrove));
        if (migliore) parti.Add(T("la copia da tenere"));
        else if (miglioreAltrove is not null) parti.Add(T("la copia da tenere è in {0}", miglioreAltrove));
        return string.Join(" · ", parti);
    }
}
