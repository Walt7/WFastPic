using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WFastPic.Core.Decoding;
using WFastPic.Core.Processing;
using WFastPic.Core.Selection;

namespace WFastPic.App;

/// <summary>
/// Viewer a schermo pieno con editor stile Picasa: Correzioni base (ritaglia, raddrizza, ho
/// fortuna, auto contrasto, auto colore) e Regolazioni (luce di riempimento, evidenzia, ombre,
/// temperatura). L'editing e sempre non distruttivo: la ricetta (<see cref="EditRecipe"/>) viene
/// applicata alla preview con la stessa pipeline dell'export (<see cref="RecipeApplier"/>), su un
/// thread di background. Ritaglia/raddrizza lavorano in una "modalita geometria" con Applica/Annulla.
/// Tutto quello che non è del viewer (elenco, sidecar, selezione) arriva da <see cref="IViewerHost"/>.
/// </summary>
public sealed partial class ViewerView : UserControl
{
    private IViewerHost Host = null!;

    /// <summary>Viewer chiuso (Esc, pulsante, foto finite): la finestra riporta la tastiera alla griglia.</summary>
    internal event EventHandler? Chiuso;

    internal bool IsAperto => _viewerAperto;

    /// <summary>Foto in osservazione (per riprendere da qui al prossimo avvio).</summary>
    internal PhotoGridItem? FotoCorrente => _fotoViewerCorrente;

    public ViewerView()
    {
        InitializeComponent();
    }

    private bool _viewerAperto;
    private PhotoGridItem? _fotoViewerCorrente;
    private PhotoGridItem? _fotoRicetta;              // foto a cui appartiene _ricettaCorrente
    private byte[]? _viewerPixelOriginali;             // preview decodificata, mai modificata
    private int _viewerPixelWidth;
    private int _viewerPixelHeight;
    private byte[]? _viewerPixelCorrenti;              // ultimo risultato di RecipeApplier
    private int _displayWidth;
    private int _displayHeight;
    private WriteableBitmap? _viewerBitmap;
    private double _viewerImgW;
    private double _viewerImgH;
    private EditRecipe _ricettaCorrente = EditRecipe.Default;
    private EditRecipe _ricettaCaricata = EditRecipe.Default;
    private readonly List<EditRecipe> _undo = new();
    private readonly List<EditRecipe> _redo = new();
    private int _richiestaCaricamentoViewer;
    private double _viewerZoom = 1;
    private GrigliaComposizione _grigliaModo = GrigliaComposizione.Terzi;

    private bool _aggiornandoUi;
    private bool _mostraOriginale;
    private bool _renderInCorso;
    private bool _renderSporco;

    // Raggruppa in un solo passo di undo un intero trascinamento di slider.
    private bool _gestoInCorso;
    private DispatcherTimer? _gestoTimer;

    // Cache delle preview decodificate (foto corrente ± vicini) cosi' <-- / --> nel Viewer non
    // aspetta il decode HEIC ogni volta: il vicino e' gia' pronto quando ci si arriva.
    private readonly Dictionary<string, (byte[] Pixel, int Larghezza, int Altezza)> _previewCache = new();
    private readonly List<string> _previewCacheOrdine = new();
    private readonly Dictionary<string, Task<(byte[] Pixel, int Larghezza, int Altezza)>> _decodeInCorso = new();
    private readonly List<string> _codaPrecarica = new();
    private int _precaricheAttive;
    private int _direzioneNavigazione = 1;

    // 5 avanti + 1 indietro + corrente stanno comodi in 10 (~12 MB l'una a 2048 px).
    private const int PreviewCacheMax = 10;
    private const int PrecaricaAvanti = 5;
    private const int PrecaricaIndietro = 1;
    private const int PrecaricheParallele = 2;

    internal void Inizializza(IViewerHost host)
    {
        Host = host;

        // handledEventsToo: i controlli interni (Slider, Button) possono marcare l'evento Handled
        // per uso proprio; gli shortcut del Viewer devono comunque sempre arrivare.
        ViewerOverlay.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(ViewerOverlay_KeyDown), handledEventsToo: true);
        ViewerOverlay.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(ViewerOverlay_KeyUp), handledEventsToo: true);
        // Sull'intera area, non solo sulla foto: la rotella deve funzionare anche sui bordi vuoti.
        ViewerArea.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(ViewerArea_PointerWheelChanged), handledEventsToo: true);

        // Dopo aver usato uno slider col mouse la tastiera torna al Viewer (frecce = naviga).
        foreach (var slider in new[] { ViewerStraightenSlider, ViewerFillLightSlider, ViewerHighlightsSlider, ViewerShadowsSlider, ViewerTemperatureSlider })
        {
            slider.AddHandler(UIElement.PointerCaptureLostEvent,
                new PointerEventHandler((_, _) => ViewerOverlay.Focus(FocusState.Programmatic)), handledEventsToo: true);
        }

        foreach (var tag in TagManiglie)
        {
            var maniglia = new Rectangle
            {
                Width = DimensioneManiglia,
                Height = DimensioneManiglia,
                Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 179, 0)),
                Tag = tag,
                Visibility = Visibility.Collapsed,
            };
            AgganciaDragCrop(maniglia);
            ViewerCropCanvas.Children.Add(maniglia);
            _maniglie[tag] = maniglia;
        }

        AgganciaDragCrop(CropRectBorder);

        _gestoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _gestoTimer.Tick += (_, _) => { _gestoTimer.Stop(); _gestoInCorso = false; };

        // A schermo intero la barra in alto è nascosta: la posizione "N di M" si vede nel pannello.
        ViewerStatusText.RegisterPropertyChangedCallback(TextBlock.TextProperty,
            (_, _) => ViewerPosizioneSchermoInteroText.Text = ViewerStatusText.Text);
        Host.Finestra.AppWindow.Changed += AppWindow_Changed;

        // B tenuto premuto e finestra che perde il focus: il KeyUp non arriverebbe mai.
        Host.Finestra.Activated += (_, a) =>
        {
            if (a.WindowActivationState == WindowActivationState.Deactivated && _mostraOriginale)
            {
                _mostraOriginale = false;
                AggiornaBitmap();
            }
        };
    }

    /// <summary>
    /// La foto mostrata e la ricetta in memoria sono della foto corrente. Durante il decode della
    /// successiva si vede ancora la precedente: modificarla ora andrebbe perso (o finirebbe sulla
    /// foto sbagliata), quindi editing e ritaglio aspettano che il caricamento finisca.
    /// </summary>
    private bool EditingPronto =>
        _viewerPixelOriginali is not null && _fotoRicetta is not null && _fotoRicetta == _fotoViewerCorrente;

    internal void Apri(PhotoGridItem? foto)
    {
        if (foto is null || _viewerAperto) return;
        _ = CaricaFotoNelViewerAsync(foto);
        Visibility = Visibility.Visible;
        _viewerAperto = true;
        ViewerOverlay.Focus(FocusState.Programmatic);
        ElementSoundPlayer.Play(ElementSoundKind.Show);
    }

    private async Task CaricaFotoNelViewerAsync(PhotoGridItem foto)
    {
        var richiesta = ++_richiestaCaricamentoViewer;
        if (_geometriaAttiva) EsciDaGeometria();
        SalvaRicettaCorrente();

        // Direzione di marcia (frecce, filmstrip o raffica): il precaricamento guarda avanti da quella parte.
        if (_fotoViewerCorrente is { } precedente && precedente != foto)
        {
            var (da, a) = (Host.Visibili.IndexOf(precedente), Host.Visibili.IndexOf(foto));
            if (da >= 0 && a >= 0) _direzioneNavigazione = Math.Sign(a - da);
        }
        _fotoViewerCorrente = foto;
        _mostraOriginale = false;
        _gestoInCorso = false;
        _gestoTimer?.Stop();
        // Filmstrip e posizione si aggiornano subito, prima del decode: con ←/→ veloci
        // l'evidenziazione segue il tasto invece di inseguire l'immagine grande.
        AggiornaFilmstrip();
        AggiornaPannelloLuogo();
        AggiornaDettagli(foto);
        AggiornaRaffica(foto);
        ViewerStatusText.Text = $"{TestoPosizioneViewer(foto)} · caricamento…";

        // Preview già pronta (precaricata): niente segnaposto, si eviterebbe solo un lampo sfocato.
        if (!_previewCache.ContainsKey(foto.PercorsoCompleto)) MostraSegnaposto(foto);

        // Il precaricamento parte subito, non a foto finita: tenendo premuta → le prossime si
        // decodificano mentre questa arriva.
        PrecaricaVicini(foto);

        (byte[] Pixel, int Larghezza, int Altezza) preview;
        try
        {
            preview = await OttieniPreviewAsync(foto.PercorsoCompleto);
        }
        catch (Exception)
        {
            if (richiesta == _richiestaCaricamentoViewer && _viewerAperto) MostraFotoIllegibile(foto);
            return;
        }
        var (pixel, larghezza, altezza) = preview;

        if (richiesta != _richiestaCaricamentoViewer || !_viewerAperto)
            return;

        // Dal SelectionManager, non dal DB: una ricetta appena salvata può essere ancora in coda.
        var recipeJson = Host.SelectionManager?.GetRecipeJson(foto.Id) ?? Host.Database?.GetRecipeJson(foto.Id);
        _ricettaCorrente = recipeJson is not null ? EditRecipeSerializer.FromJson(recipeJson) : EditRecipe.Default;
        _ricettaCaricata = _ricettaCorrente;
        _fotoRicetta = foto;
        _undo.Clear();
        _redo.Clear();

        // La preview non viene mai modificata (RecipeApplier lavora su copie): si puo condividere con la cache.
        _viewerPixelOriginali = pixel;
        _viewerPixelWidth = larghezza;
        _viewerPixelHeight = altezza;

        ImpostaZoomViewer(1);
        SincronizzaPannello();
        await RichiediRenderAsync();

        if (richiesta != _richiestaCaricamentoViewer || !_viewerAperto)
            return;

        ViewerStatusText.Text = TestoPosizioneViewer(foto);
        AggiornaBadgeFoto(foto);
        AggiornaRaffica(foto); // orientamento vero della foto mostrata (gira/ritaglio compresi)

        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    private void AggiornaBadgeFoto(PhotoGridItem? foto)
    {
        var video = foto?.HasVideo == true ? Visibility.Visible : Visibility.Collapsed;
        ViewerVideoBadge.Visibility = video;
        // Sulla foto come nella griglia; in ritaglio si vede solo la foto.
        ViewerFotoVideoBadge.Visibility = _geometriaAttiva ? Visibility.Collapsed : video;
        ViewerSelectionBorder.Visibility = foto?.IsSelected == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // File corrotto o non decodificabile: non si resta su "caricamento…" mostrando la foto
    // precedente come se fosse questa. Navigazione, selezione e scarto continuano a funzionare.
    private void MostraFotoIllegibile(PhotoGridItem foto)
    {
        _fotoRicetta = null;
        _ricettaCorrente = EditRecipe.Default;
        _ricettaCaricata = EditRecipe.Default;
        _viewerPixelOriginali = null;
        _viewerPixelCorrenti = null;
        _undo.Clear();
        _redo.Clear();
        ViewerImage.Source = null;
        NascondiSegnaposto();
        SincronizzaPannello();
        ViewerStatusText.Text = $"{TestoPosizioneViewer(foto)} · impossibile leggere la foto";
        AggiornaBadgeFoto(foto);
        ViewerSelectionBorder.Visibility = Visibility.Collapsed;
        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    // "IMG_1234.HEIC · 34 di 1300": come in Picasa si sa sempre dove si è nella cartella (o nel filtro).
    private string TestoPosizioneViewer(PhotoGridItem foto)
    {
        var indice = Host.Visibili.IndexOf(foto);
        return indice < 0 ? foto.PathRelativo : $"{foto.PathRelativo} · {indice + 1} di {Host.Visibili.Count}";
    }

    /// <summary>
    /// Dalla cache, oppure dal decode già in corso (precaricamento) per la stessa foto: arrivando su
    /// una foto mentre la si sta precaricando non parte un secondo decode, si aspetta il primo.
    /// Tutto sul thread UI: cache e decode in corso non hanno bisogno di lock.
    /// </summary>
    private async Task<(byte[] Pixel, int Larghezza, int Altezza)> OttieniPreviewAsync(string percorso)
    {
        if (_previewCache.TryGetValue(percorso, out var cache))
        {
            _previewCacheOrdine.Remove(percorso);
            _previewCacheOrdine.Add(percorso);
            return cache;
        }

        if (!_decodeInCorso.TryGetValue(percorso, out var decode))
        {
            decode = DecodificaEMemorizzaAsync(percorso);
            _decodeInCorso[percorso] = decode;
        }
        return await decode;
    }

    private async Task<(byte[] Pixel, int Larghezza, int Altezza)> DecodificaEMemorizzaAsync(string percorso)
    {
        try
        {
            var risultato = await PreviewDecoder.DecodePreviewBgraAsync(percorso);
            MemorizzaInCache(percorso, risultato);
            return risultato;
        }
        finally
        {
            _decodeInCorso.Remove(percorso);
        }
    }

    private void MemorizzaInCache(string percorso, (byte[] Pixel, int Larghezza, int Altezza) valore)
    {
        _previewCache[percorso] = valore;
        _previewCacheOrdine.Remove(percorso);
        _previewCacheOrdine.Add(percorso);

        while (_previewCacheOrdine.Count > PreviewCacheMax)
        {
            var piuVecchio = _previewCacheOrdine[0];
            _previewCacheOrdine.RemoveAt(0);
            _previewCache.Remove(piuVecchio);
        }
    }

    /// <summary>
    /// Precarica le prossime <see cref="PrecaricaAvanti"/> foto nella direzione in cui si sta scorrendo
    /// (e <see cref="PrecaricaIndietro"/> alle spalle), dalla più vicina. La coda si rifà a ogni foto:
    /// quelle già superate non si decodificano più; al massimo <see cref="PrecaricheParallele"/> decode
    /// insieme, per non togliere CPU alla foto che si sta guardando.
    /// </summary>
    private void PrecaricaVicini(PhotoGridItem corrente)
    {
        var indice = Host.Visibili.IndexOf(corrente);
        if (indice < 0) return;

        var direzione = _direzioneNavigazione >= 0 ? 1 : -1;
        var indici = Enumerable.Range(1, PrecaricaAvanti).Select(k => indice + direzione * k)
            .Concat(Enumerable.Range(1, PrecaricaIndietro).Select(k => indice - direzione * k));

        _codaPrecarica.Clear();
        foreach (var i in indici)
        {
            if (i >= 0 && i < Host.Visibili.Count) _codaPrecarica.Add(Host.Visibili[i].PercorsoCompleto);
        }
        PompaPrecarica();
    }

    private void PompaPrecarica()
    {
        while (_viewerAperto && _precaricheAttive < PrecaricheParallele && _codaPrecarica.Count > 0)
        {
            var percorso = _codaPrecarica[0];
            _codaPrecarica.RemoveAt(0);
            if (_previewCache.ContainsKey(percorso) || _decodeInCorso.ContainsKey(percorso)) continue;

            _precaricheAttive++;
            _ = PrecaricaUnaAsync(percorso);
        }
    }

    private async Task PrecaricaUnaAsync(string percorso)
    {
        try
        {
            await OttieniPreviewAsync(percorso);
        }
        catch
        {
            // Ignora: se il precaricamento fallisce, il decode viene ritentato quando si arriva sulla foto.
        }
        finally
        {
            _precaricheAttive--;
            PompaPrecarica();
        }
    }

    // ---------------------------------------------------------------- render

    // In modalita geometria si mostra l'intera tela (solo tono): raddrizzamento e ritaglio sono
    // resi da RenderTransform + overlay, e vengono cotti nei pixel solo con "Applica".
    private EditRecipe RicettaDiVisualizzazione() => _geometriaAttiva
        ? _ricettaCorrente with { CropX = 0, CropY = 0, CropWidth = 1, CropHeight = 1, StraightenAngleDegrees = 0 }
        : _ricettaCorrente;

    /// <summary>
    /// Ricalcola la preview in background. Richieste ravvicinate (slider) si fondono: ne gira una
    /// alla volta e, se nel frattempo e cambiato qualcosa, una sola in piu con la ricetta piu recente.
    /// </summary>
    private async Task RichiediRenderAsync()
    {
        if (_viewerPixelOriginali is null) return;
        if (_renderInCorso) { _renderSporco = true; return; }

        _renderInCorso = true;
        try
        {
            do
            {
                _renderSporco = false;
                var sorgente = _viewerPixelOriginali;
                if (sorgente is null || !_viewerAperto) break;

                var w = _viewerPixelWidth;
                var h = _viewerPixelHeight;
                var ricetta = RicettaDiVisualizzazione();
                var geometria = _geometriaAttiva;

                var risultato = await Task.Run(() => RecipeApplier.Applica(sorgente, w, h, ricetta));

                // Foto cambiata, o entrati/usciti dal ritaglio durante il calcolo: il risultato ha
                // la geometria sbagliata (es. foto già ritagliata sotto le maniglie), non va mostrato.
                if (!ReferenceEquals(sorgente, _viewerPixelOriginali) || !_viewerAperto || geometria != _geometriaAttiva)
                {
                    _renderSporco = _viewerPixelOriginali is not null && _viewerAperto;
                    continue;
                }

                MostraBuffer(risultato.Bgra, risultato.Larghezza, risultato.Altezza);
            }
            while (_renderSporco);
        }
        finally
        {
            _renderInCorso = false;
        }
    }

    private void RichiediRender() => _ = RichiediRenderAsync();

    private void MostraBuffer(byte[] bgra, int larghezza, int altezza)
    {
        // I simboli delle modifiche sulla miniatura (filmstrip e griglia) seguono l'editing dal vivo.
        _fotoRicetta?.AggiornaModifiche(_ricettaCorrente);
        // Risultato della foto corrente (non di quella lasciata mentre si scorreva): via il segnaposto.
        if (_fotoRicetta is not null && _fotoRicetta == _fotoViewerCorrente) NascondiSegnaposto();
        _viewerPixelCorrenti = bgra;
        _displayWidth = larghezza;
        _displayHeight = altezza;
        // Girata o ritagliata da orizzontale a verticale (o viceversa): la raffica cambia lato.
        var rafficaVerticale = ViewerRafficaVerticale.Visibility == Visibility.Visible;
        if (_rafficaMostrataPer is { } inRaffica && rafficaVerticale != (altezza > larghezza)) AggiornaRaffica(inRaffica);
        AggiornaBitmap();
    }

    // Con B premuto si mostra la preview originale (intera tela) al posto del risultato.
    private void AggiornaBitmap()
    {
        var mostraOriginale = _mostraOriginale && _viewerPixelOriginali is not null;
        var pixel = mostraOriginale ? _viewerPixelOriginali : _viewerPixelCorrenti;
        var w = mostraOriginale ? _viewerPixelWidth : _displayWidth;
        var h = mostraOriginale ? _viewerPixelHeight : _displayHeight;
        if (pixel is null || w <= 0 || h <= 0) return;

        if (_viewerBitmap is null || _viewerBitmap.PixelWidth != w || _viewerBitmap.PixelHeight != h)
            _viewerBitmap = new WriteableBitmap(w, h);

        using (var stream = _viewerBitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixel, 0, w * h * 4);
        }
        _viewerBitmap.Invalidate();
        ViewerImage.Source = _viewerBitmap;

        ApplicaLayoutViewer(w, h);
    }

    private void ApplicaLayoutViewer(int pixelWidth, int pixelHeight)
    {
        // Area non ancora misurata (viewer appena aperto): niente misure inventate che farebbero
        // uscire la foto dall'area; ci pensa ViewerArea_SizeChanged appena il layout è pronto.
        if (ViewerArea.ActualWidth <= 40 || ViewerArea.ActualHeight <= 40) return;

        // In ritaglio serve aria attorno alla foto: le maniglie (14px) devono stare tutte dentro
        // l'area ed essere afferrabili anche sui bordi, non a filo con pannello o barra.
        var margine = _geometriaAttiva ? 2 * (DimensioneManiglia + 16) : 24;
        var areaW = ViewerArea.ActualWidth - margine;
        var areaH = ViewerArea.ActualHeight - margine;
        var scala = Math.Min(Math.Min(areaW / pixelWidth, areaH / pixelHeight), 2.0);
        _viewerImgW = Math.Max(1, pixelWidth * scala);
        _viewerImgH = Math.Max(1, pixelHeight * scala);

        ViewerImage.Width = _viewerImgW;
        ViewerImage.Height = _viewerImgH;
        ViewerImageHost.Width = _viewerImgW;
        ViewerImageHost.Height = _viewerImgH;
        ViewerCropCanvas.Width = _viewerImgW;
        ViewerCropCanvas.Height = _viewerImgH;

        // Finestra ridimensionata o foto cambiata: lo spostamento deve restare entro i nuovi bordi.
        ImpostaTraslazione(ViewerPanTransform.X, ViewerPanTransform.Y);
        AggiornaGriglia();
        if (_geometriaAttiva) AggiornaOverlayCrop();
    }

    private void ViewerArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Zoom (ScaleTransform) e raddrizza (RotateTransform) non cambiano il layout: senza clip la
        // foto ingrandita o ruotata verrebbe disegnata sopra il pannello e sotto il filmstrip.
        ViewerArea.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        if (_viewerAperto && _viewerPixelOriginali is not null) AggiornaBitmap();
    }

    // Griglia di composizione: come Picasa, durante il ritaglio segue il riquadro (terzi del taglio che
    // si sta scegliendo, non della foto intera); fuori dal ritaglio copre l'immagine.
    private void AggiornaGriglia()
    {
        var x0 = 0.0; var y0 = 0.0;
        var w = _viewerImgW; var h = _viewerImgH;
        if (_geometriaAttiva)
        {
            var (cx0, cy0, cx1, cy1) = CropPx();
            (x0, y0, w, h) = (cx0, cy0, Math.Max(1, cx1 - cx0), Math.Max(1, cy1 - cy0));
        }

        var mostraTerzi = _grigliaModo == GrigliaComposizione.Terzi;
        var mostraMeta = _grigliaModo == GrigliaComposizione.Meta;
        var mostraDiagonali = _grigliaModo == GrigliaComposizione.Diagonali;

        ThirdsV1.Visibility = (mostraTerzi || mostraMeta) ? Visibility.Visible : Visibility.Collapsed;
        ThirdsH1.Visibility = (mostraTerzi || mostraMeta) ? Visibility.Visible : Visibility.Collapsed;
        ThirdsV2.Visibility = mostraTerzi ? Visibility.Visible : Visibility.Collapsed;
        ThirdsH2.Visibility = mostraTerzi ? Visibility.Visible : Visibility.Collapsed;
        DiagLine1.Visibility = mostraDiagonali ? Visibility.Visible : Visibility.Collapsed;
        DiagLine2.Visibility = mostraDiagonali ? Visibility.Visible : Visibility.Collapsed;

        var frazioneV1 = mostraMeta ? 0.5 : 1.0 / 3;
        var frazioneH1 = mostraMeta ? 0.5 : 1.0 / 3;

        Canvas.SetLeft(ThirdsV1, x0 + w * frazioneV1); Canvas.SetTop(ThirdsV1, y0); ThirdsV1.Height = h;
        Canvas.SetLeft(ThirdsV2, x0 + w * 2 / 3); Canvas.SetTop(ThirdsV2, y0); ThirdsV2.Height = h;
        Canvas.SetLeft(ThirdsH1, x0); Canvas.SetTop(ThirdsH1, y0 + h * frazioneH1); ThirdsH1.Width = w;
        Canvas.SetLeft(ThirdsH2, x0); Canvas.SetTop(ThirdsH2, y0 + h * 2 / 3); ThirdsH2.Width = w;

        DiagLine1.X1 = x0; DiagLine1.Y1 = y0; DiagLine1.X2 = x0 + w; DiagLine1.Y2 = y0 + h;
        DiagLine2.X1 = x0 + w; DiagLine2.Y1 = y0; DiagLine2.X2 = x0; DiagLine2.Y2 = y0 + h;
    }

    private void ViewerGrigliaToggle_Click(object sender, RoutedEventArgs e)
    {
        _grigliaModo = _grigliaModo switch
        {
            GrigliaComposizione.Terzi => GrigliaComposizione.Meta,
            GrigliaComposizione.Meta => GrigliaComposizione.Diagonali,
            _ => GrigliaComposizione.Terzi,
        };

        ViewerGrigliaToggle.Content = _grigliaModo switch
        {
            GrigliaComposizione.Terzi => "Griglia: terzi",
            GrigliaComposizione.Meta => "Griglia: metà",
            _ => "Griglia: diagonali",
        };

        AggiornaGriglia();
    }

    // ---------------------------------------------------------------- pannello / undo

    private void RegistraUndo(EditRecipe precedente)
    {
        _undo.Add(precedente);
        if (_undo.Count > UndoMax) _undo.RemoveAt(0);
        _redo.Clear();
    }

    // Un trascinamento di slider genera molti ValueChanged: un solo passo di undo per gesto.
    private void AvviaGesto()
    {
        if (!_gestoInCorso)
        {
            RegistraUndo(_ricettaCorrente);
            _gestoInCorso = true;
        }

        _gestoTimer?.Stop();
        _gestoTimer?.Start();
    }

    private void ModificaRicetta(EditRecipe nuova)
    {
        if (nuova == _ricettaCorrente) return;
        RegistraUndo(_ricettaCorrente);
        _ricettaCorrente = nuova;
        SincronizzaPannello();
        RichiediRender();
    }

    private void SincronizzaPannello()
    {
        _aggiornandoUi = true;
        try
        {
            var r = _ricettaCorrente;
            ViewerFillLightSlider.Value = r.FillLight * 100;
            ViewerHighlightsSlider.Value = r.Highlights * 100;
            ViewerShadowsSlider.Value = r.Shadows * 100;
            ViewerTemperatureSlider.Value = r.Temperature * 100;
            AggiornaTestiTuning();

            ViewerAutoContrastToggle.IsChecked = r.AutoContrast;
            ViewerAutoColorToggle.IsChecked = r.AutoColor;
            ViewerLuckyToggle.IsChecked = r.AutoContrast && r.AutoColor;

            if (!_geometriaAttiva)
            {
                ViewerStraightenSlider.Value = r.StraightenAngleDegrees;
                ViewerAngleText.Text = $"{r.StraightenAngleDegrees:F1}°";
            }

            AggiornaStatoUndoRedoUi();
        }
        finally
        {
            _aggiornandoUi = false;
        }
    }

    private void AggiornaTestiTuning()
    {
        ViewerFillLightText.Text = $"{ViewerFillLightSlider.Value:F0}";
        ViewerHighlightsText.Text = $"{ViewerHighlightsSlider.Value:F0}";
        ViewerShadowsText.Text = $"{ViewerShadowsSlider.Value:F0}";
        ViewerTemperatureText.Text = $"{ViewerTemperatureSlider.Value:F0}";
    }

    private void ViewerTabBase_Click(object sender, RoutedEventArgs e) => MostraTab(tuning: false);

    private void ViewerTabTuning_Click(object sender, RoutedEventArgs e) => MostraTab(tuning: true);

    private void MostraTab(bool tuning)
    {
        ViewerTabBase.IsChecked = !tuning;
        ViewerTabTuning.IsChecked = tuning;
        PannelloBase.Visibility = !tuning && !_geometriaAttiva ? Visibility.Visible : Visibility.Collapsed;
        PannelloTuning.Visibility = tuning && !_geometriaAttiva ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewerTuningSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_aggiornandoUi) return;
        // Slider mosso mentre la foto sta caricando: torna al valore della ricetta.
        if (!EditingPronto) { SincronizzaPannello(); return; }

        var nuova = _ricettaCorrente with
        {
            FillLight = ViewerFillLightSlider.Value / 100,
            Highlights = ViewerHighlightsSlider.Value / 100,
            Shadows = ViewerShadowsSlider.Value / 100,
            Temperature = ViewerTemperatureSlider.Value / 100,
        };
        if (nuova == _ricettaCorrente) return;

        AvviaGesto();
        _ricettaCorrente = nuova;
        AggiornaTestiTuning();
        AggiornaStatoUndoRedoUi();
        RichiediRender();
    }

    private void AggiornaStatoUndoRedoUi()
    {
        ViewerUndoButton.IsEnabled = !_geometriaAttiva && _undo.Count > 0;
        ViewerRedoButton.IsEnabled = !_geometriaAttiva && _redo.Count > 0;
        ViewerResetButton.IsEnabled = !_geometriaAttiva && _ricettaCorrente != EditRecipe.Default;
    }

    private void ViewerTuningReset_Click(object sender, RoutedEventArgs e)
    {
        if (!EditingPronto) return;
        ModificaRicetta(_ricettaCorrente with { FillLight = 0, Highlights = 0, Shadows = 0, Temperature = 0 });
    }

    private void ViewerLuckyToggle_Click(object sender, RoutedEventArgs e) => ToggleLucky();

    private void ViewerRuotaAntiorario_Click(object sender, RoutedEventArgs e) => Orienta(Orientamento.RuotaAntiorario);

    private void ViewerRuotaOrario_Click(object sender, RoutedEventArgs e) => Orienta(Orientamento.RuotaOrario);

    private void ViewerSpecchia_Click(object sender, RoutedEventArgs e) => Orienta(Orientamento.Specchia);

    /// <summary>
    /// Gira di 90° / specchia come Picasa: un passo di ricetta (annullabile), il ritaglio già fatto
    /// gira con la foto. Lo zoom torna al 100%: la foto cambia proporzioni.
    /// </summary>
    private void Orienta(Func<EditRecipe, EditRecipe> operazione)
    {
        if (!EditingPronto || _geometriaAttiva) return;
        ImpostaZoomViewer(1);
        ModificaRicetta(operazione(_ricettaCorrente));
    }

    private void ToggleLucky()
    {
        if (!EditingPronto || _geometriaAttiva) { SincronizzaPannello(); return; }
        var attivo = !(_ricettaCorrente.AutoContrast && _ricettaCorrente.AutoColor);
        ModificaRicetta(_ricettaCorrente with { AutoContrast = attivo, AutoColor = attivo });
    }

    private void ViewerAutoContrastToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!EditingPronto) { SincronizzaPannello(); return; }
        ModificaRicetta(_ricettaCorrente with { AutoContrast = !_ricettaCorrente.AutoContrast });
    }

    private void ViewerAutoColorToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!EditingPronto) { SincronizzaPannello(); return; }
        ModificaRicetta(_ricettaCorrente with { AutoColor = !_ricettaCorrente.AutoColor });
    }

    private void ViewerUndo_Click(object sender, RoutedEventArgs e) => Annulla();

    private void ViewerRedo_Click(object sender, RoutedEventArgs e) => Ripeti();

    private void Annulla()
    {
        if (!EditingPronto || _geometriaAttiva || _undo.Count == 0) return;
        _redo.Add(_ricettaCorrente);
        _ricettaCorrente = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _gestoInCorso = false;
        SincronizzaPannello();
        RichiediRender();
    }

    private void Ripeti()
    {
        if (!EditingPronto || _geometriaAttiva || _redo.Count == 0) return;
        _undo.Add(_ricettaCorrente);
        _ricettaCorrente = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _gestoInCorso = false;
        SincronizzaPannello();
        RichiediRender();
    }

    private void ViewerReset_Click(object sender, RoutedEventArgs e)
    {
        if (!EditingPronto || _geometriaAttiva) return;
        ModificaRicetta(EditRecipe.Default);
    }


    // ---------------------------------------------------------------- zoom

    private void ViewerArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_viewerPixelOriginali is null || _geometriaAttiva) return;

        var punto = e.GetCurrentPoint(ViewerArea);
        var delta = punto.Properties.MouseWheelDelta;
        if (delta == 0) return;

        // Come Picasa: si ingrandisce verso il punto che si sta guardando, non sempre verso il centro.
        ImpostaZoomViewer(_viewerZoom * (delta > 0 ? 1.15 : 1 / 1.15), punto.Position);
        e.Handled = true;
    }

    private void ImpostaZoomViewer(double zoom, Point? verso = null)
    {
        var zoomPrima = _viewerZoom;
        _viewerZoom = Math.Clamp(zoom, 1, 4);
        ViewerZoomTransform.ScaleX = _viewerZoom;
        ViewerZoomTransform.ScaleY = _viewerZoom;
        ViewerZoomText.Text = $"Zoom {_viewerZoom * 100:F0}%";

        var (tx, ty) = (ViewerPanTransform.X, ViewerPanTransform.Y);
        if (verso is Point p)
        {
            (tx, ty) = PanZoomGeometry.ZoomVersoPunto(
                tx, ty, p.X - ViewerArea.ActualWidth / 2, p.Y - ViewerArea.ActualHeight / 2, zoomPrima, _viewerZoom);
        }
        // Tornando al 100% il limite riporta la foto al centro da solo.
        ImpostaTraslazione(tx, ty);
    }

    private void ImpostaTraslazione(double tx, double ty)
    {
        (ViewerPanTransform.X, ViewerPanTransform.Y) = PanZoomGeometry.Limita(
            tx, ty, _viewerImgW, _viewerImgH, _viewerZoom, ViewerArea.ActualWidth, ViewerArea.ActualHeight);
    }

    // ---------------------------------------------------------------- trascinamento (foto zoomata)

    private bool _panInCorso;
    private Point _panInizio;
    private (double X, double Y) _panTraslazioneIniziale;

    private void ViewerArea_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // In ritaglio il trascinamento appartiene alle maniglie; al 100% non c'è nulla da spostare.
        if (_geometriaAttiva || _viewerZoom <= 1 || _viewerPixelOriginali is null) return;

        var punto = e.GetCurrentPoint(ViewerArea);
        if (!punto.Properties.IsLeftButtonPressed) return;

        _panInCorso = ViewerArea.CapturePointer(e.Pointer);
        _panInizio = punto.Position;
        _panTraslazioneIniziale = (ViewerPanTransform.X, ViewerPanTransform.Y);
        e.Handled = true;
    }

    private void ViewerArea_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panInCorso) return;

        var posizione = e.GetCurrentPoint(ViewerArea).Position;
        ImpostaTraslazione(
            _panTraslazioneIniziale.X + posizione.X - _panInizio.X,
            _panTraslazioneIniziale.Y + posizione.Y - _panInizio.Y);
        e.Handled = true;
    }

    private void ViewerArea_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_panInCorso) return;
        _panInCorso = false;
        ViewerArea.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- salvataggio / chiusura

    // Salva solo se la ricetta e cambiata rispetto a quella caricata: niente scritture inutili
    // per ogni foto semplicemente guardata.
    internal void SalvaRicettaCorrente()
    {
        if (_fotoRicetta is null || _ricettaCorrente == _ricettaCaricata) return;

        Host.SelectionManager?.SaveRecipe(_fotoRicetta.Id, EditRecipeSerializer.ToJson(_ricettaCorrente), _ricettaCorrente.SchemaVersion);
        _ricettaCaricata = _ricettaCorrente;
    }

    private void ViewerCloseButton_Click(object sender, RoutedEventArgs e) => Chiudi();

    // ---------------------------------------------------------------- schermo intero

    private bool _schermoIntero;

    private void ViewerSchermoIntero_Click(object sender, RoutedEventArgs e) => ImpostaSchermoIntero(!_schermoIntero);

    /// <summary>
    /// Finestra a tutto schermo con la sola foto e il pannello di modifica: barra in alto, filmstrip
    /// e suggerimenti spariscono; la posizione "N di M" passa in cima al pannello. Il ridimensionamento
    /// dell'area arriva da ViewerArea_SizeChanged, che riadatta foto e maniglie del ritaglio.
    /// </summary>
    private void ImpostaSchermoIntero(bool attivo)
    {
        if (_schermoIntero == attivo) { ViewerSchermoInteroToggle.IsChecked = attivo; return; }
        _schermoIntero = attivo;

        try
        {
            Host.Finestra.AppWindow.SetPresenter(attivo ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        }
        catch
        {
            // Presenter non disponibile: si nasconde comunque il superfluo per dare spazio alla foto.
        }

        ViewerSchermoInteroToggle.IsChecked = attivo;
        var visibileFuori = attivo ? Visibility.Collapsed : Visibility.Visible;
        ViewerBarraSuperiore.Visibility = visibileFuori;
        ViewerHintText.Visibility = visibileFuori;
        ViewerPosizioneSchermoInteroText.Visibility = attivo ? Visibility.Visible : Visibility.Collapsed;
        Filmstrip.Visibility = attivo || _geometriaAttiva ? Visibility.Collapsed : Visibility.Visible;
        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    // Uscita dallo schermo intero fatta da Windows (es. Win+Shift+Invio): il viewer si riallinea.
    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange && _schermoIntero && sender.Presenter.Kind != AppWindowPresenterKind.FullScreen)
            ImpostaSchermoIntero(false);
    }

    internal void Chiudi()
    {
        ImpostaSchermoIntero(false);
        _ctsLuogo?.Cancel();
        ImpostaModificaLuogo(false);
        _richiestaCaricamentoViewer++;
        if (_geometriaAttiva) EsciDaGeometria();
        SalvaRicettaCorrente();
        Visibility = Visibility.Collapsed;
        _viewerAperto = false;
        _fotoViewerCorrente = null;
        _fotoRicetta = null;
        _viewerPixelOriginali = null;
        _viewerPixelCorrenti = null;
        _viewerBitmap = null;
        _bufferPrimaGeometria = null;
        _mostraOriginale = false;
        _undo.Clear();
        _redo.Clear();
        NascondiSegnaposto();
        _rafficaMostrataPer = null;
        _codaPrecarica.Clear();
        ViewerRafficaOrizzontale.ItemsSource = null;
        ViewerRafficaVerticale.ItemsSource = null;
        ElementSoundPlayer.Play(ElementSoundKind.GoBack);
        Chiuso?.Invoke(this, EventArgs.Empty);
    }


    private async Task NavigaViewerAsync(int delta)
    {
        if (_fotoViewerCorrente is null) return;
        var indice = Host.Visibili.IndexOf(_fotoViewerCorrente);
        if (indice < 0) return;

        var nuovoIndice = indice + delta;
        if (nuovoIndice < 0 || nuovoIndice >= Host.Visibili.Count) return;

        await CaricaFotoNelViewerAsync(Host.Visibili[nuovoIndice]);
    }

    // ---------------------------------------------------------------- tastiera

    private static bool TastoPremuto(VirtualKey tasto) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(tasto).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void ViewerOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_fotoViewerCorrente is null) return;

        // Gli Slider gestiscono le frecce per conto proprio quando hanno il focus: non intercettarle.
        if (e.OriginalSource is Slider && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
            or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)
            return;

        var ctrl = TastoPremuto(VirtualKey.Control);

        if (ctrl && e.Key is VirtualKey.Z or VirtualKey.Y)
        {
            if (e.Key == VirtualKey.Y || TastoPremuto(VirtualKey.Shift)) Ripeti(); else Annulla();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.F11)
        {
            ImpostaSchermoIntero(!_schermoIntero);
            e.Handled = true;
            return;
        }

        if (_geometriaAttiva)
        {
            GestisciTastoGeometria(e);
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                // Come Picasa: Esc chiude prima la correzione della posizione, poi lo schermo intero,
                // infine il viewer.
                if (_luogoInModifica) AggiornaPannelloLuogo();
                else if (_schermoIntero) ImpostaSchermoIntero(false);
                else Chiudi();
                e.Handled = true;
                break;

            case VirtualKey.Left:
                _ = NavigaViewerAsync(-1);
                e.Handled = true;
                break;

            case VirtualKey.Right:
                _ = NavigaViewerAsync(1);
                e.Handled = true;
                break;

            case VirtualKey.Space:
                SetStatoNelViewer(_fotoViewerCorrente, _fotoViewerCorrente.Stato == StatoSelezione.Selected ? StatoSelezione.None : StatoSelezione.Selected);
                e.Handled = true;
                break;

            case VirtualKey.Delete or VirtualKey.Back:
                SetStatoNelViewer(_fotoViewerCorrente, StatoSelezione.Reject);
                e.Handled = true;
                break;

            case VirtualKey.X:
                EntraInGeometria(focusSlider: false);
                e.Handled = true;
                break;

            case VirtualKey.L:
                EntraInGeometria(focusSlider: true);
                e.Handled = true;
                break;

            case VirtualKey.U:
                ToggleLucky();
                e.Handled = true;
                break;

            case VirtualKey.R:
                Orienta(TastoPremuto(VirtualKey.Shift) ? Orientamento.RuotaAntiorario : Orientamento.RuotaOrario);
                e.Handled = true;
                break;

            case VirtualKey.B:
                if (!_mostraOriginale && _viewerPixelOriginali is not null)
                {
                    _mostraOriginale = true;
                    AggiornaBitmap();
                }
                e.Handled = true;
                break;

            case VirtualKey.Number0: Host.SetRating(_fotoViewerCorrente, 0); e.Handled = true; break;
            case VirtualKey.Number1: Host.SetRating(_fotoViewerCorrente, 1); e.Handled = true; break;
            case VirtualKey.Number2: Host.SetRating(_fotoViewerCorrente, 2); e.Handled = true; break;
            case VirtualKey.Number3: Host.SetRating(_fotoViewerCorrente, 3); e.Handled = true; break;
            case VirtualKey.Number4: Host.SetRating(_fotoViewerCorrente, 4); e.Handled = true; break;
            case VirtualKey.Number5: Host.SetRating(_fotoViewerCorrente, 5); e.Handled = true; break;
        }
    }

    /// <summary>
    /// Come in Picasa, durante ritaglia/raddrizza si lavora solo su quella foto: niente
    /// navigazione, selezione o scarto (cambierebbero foto buttando via il ritaglio). Le frecce
    /// raddrizzano di 0,1° (Shift: 1°) anche quando lo slider non ha il focus.
    /// </summary>
    private void GestisciTastoGeometria(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                AnnullaGeometria();
                break;

            case VirtualKey.Enter or VirtualKey.X:
                ApplicaGeometria();
                break;

            case VirtualKey.L:
                ViewerStraightenSlider.Focus(FocusState.Programmatic);
                break;

            case VirtualKey.O:
                // Riquadro orizzontale ↔ verticale (4:3 ↔ 3:4), come il pulsante accanto a Proporzioni.
                ViewerOrientamentoRiquadro_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case VirtualKey.Space:
                // Seleziona/deseleziona resta disponibile anche in ritaglio: non cambia foto, quindi non
                // interferisce con la geometria in corso (a differenza di navigazione e scarto).
                if (_fotoViewerCorrente is not null)
                {
                    SetStatoNelViewer(_fotoViewerCorrente,
                        _fotoViewerCorrente.Stato == StatoSelezione.Selected ? StatoSelezione.None : StatoSelezione.Selected);
                }
                break;

            case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down:
                var passo = TastoPremuto(VirtualKey.Shift) ? 1.0 : 0.1;
                var verso = e.Key is VirtualKey.Left or VirtualKey.Down ? -1 : 1;
                ViewerStraightenSlider.Value = Math.Clamp(
                    Math.Round(ViewerStraightenSlider.Value + verso * passo, 1),
                    ViewerStraightenSlider.Minimum, ViewerStraightenSlider.Maximum);
                break;
        }

        // Anche i tasti ignorati: in ritaglio nessuno deve arrivare ad altri gestori.
        e.Handled = true;
    }

    private void ViewerOverlay_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.B && _mostraOriginale)
        {
            _mostraOriginale = false;
            AggiornaBitmap();
            e.Handled = true;
        }
    }

    private void SetStatoNelViewer(PhotoGridItem foto, string nuovoStato)
    {
        var indice = Host.Visibili.IndexOf(foto);
        var alternativa = indice >= 0
            ? Host.Visibili.ElementAtOrDefault(indice + 1) ?? Host.Visibili.ElementAtOrDefault(indice - 1)
            : null;

        Host.SetStato(foto, nuovoStato);
        ViewerSelectionBorder.Visibility = foto.IsSelected ? Visibility.Visible : Visibility.Collapsed;

        if (_fotoViewerCorrente == foto && !Host.Visibili.Contains(foto))
        {
            if (alternativa is not null && Host.Visibili.Contains(alternativa))
                _ = CaricaFotoNelViewerAsync(alternativa);
            else if (Host.Visibili.Count > 0)
                _ = CaricaFotoNelViewerAsync(Host.Visibili[0]);
            else
                Chiudi();
        }
        else
        {
            AggiornaFilmstrip();
        }
    }
}
