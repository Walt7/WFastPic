using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
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
/// </summary>
public partial class MainWindow
{
    private enum GrigliaComposizione { Terzi, Meta, Diagonali }

    private static readonly (string Etichetta, double? Rapporto)[] ProporzioniDisponibili =
    [
        ("libera", null),
        ("1:1", 1.0),
        ("4:3", 4.0 / 3),
        ("3:2", 3.0 / 2),
        ("16:9", 16.0 / 9),
    ];

    private static readonly string[] TagManiglie = ["TL", "T", "TR", "R", "BR", "B", "BL", "L"];
    private const double DimensioneManiglia = 14;
    private const double CropMinimoPx = 24;
    private const int UndoMax = 50;

    private int _proporzioneIndice;
    private double? ProporzioneCorrente => ProporzioniDisponibili[_proporzioneIndice].Rapporto;

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

    // Modalita geometria (ritaglia + raddrizza).
    private bool _geometriaAttiva;
    private bool _cropAutoPieno;                        // il crop segue il massimo rettangolo inscritto
    private double _cropN0X, _cropN0Y, _cropN1X, _cropN1Y; // crop normalizzato sulla tela (0..1)
    private string? _dragTag;
    private Point _dragStart;
    private (double X0, double Y0, double X1, double Y1) _dragRect;
    private readonly Dictionary<string, Rectangle> _maniglie = new();
    // Riquadro voluto dall'utente (normalizzato): raddrizzando lo si adatta sempre a partire da
    // questo, così tornando verso 0° il crop si riallarga invece di restare rimpicciolito.
    private (double X0, double Y0, double X1, double Y1) _cropVoluto = (0, 0, 1, 1);
    // Risultato mostrato prima del ritaglio: se si annulla (o non cambia nulla) torna subito.
    private (byte[] Pixel, int Larghezza, int Altezza)? _bufferPrimaGeometria;

    // Cache delle preview decodificate (foto corrente ± vicini) cosi' <-- / --> nel Viewer non
    // aspetta il decode HEIC ogni volta: il vicino e' gia' pronto quando ci si arriva.
    private readonly Dictionary<string, (byte[] Pixel, int Larghezza, int Altezza)> _previewCache = new();
    private readonly List<string> _previewCacheOrdine = new();
    private readonly HashSet<string> _prefetchInCorso = new();
    private const int PreviewCacheMax = 6;

    private void InizializzaViewer()
    {
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

        // B tenuto premuto e finestra che perde il focus: il KeyUp non arriverebbe mai.
        Activated += (_, a) =>
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

    private void AgganciaDragCrop(UIElement el)
    {
        el.PointerPressed += Crop_PointerPressed;
        el.PointerMoved += Crop_PointerMoved;
        el.PointerReleased += Crop_PointerReleased;
        el.PointerCaptureLost += Crop_PointerReleased;
    }

    private void ApriViewer(PhotoGridItem? foto)
    {
        if (foto is null || _viewerAperto) return;
        _ = CaricaFotoNelViewerAsync(foto);
        ViewerOverlay.Visibility = Visibility.Visible;
        _viewerAperto = true;
        ViewerOverlay.Focus(FocusState.Programmatic);
        ElementSoundPlayer.Play(ElementSoundKind.Show);
    }

    private async Task CaricaFotoNelViewerAsync(PhotoGridItem foto)
    {
        var richiesta = ++_richiestaCaricamentoViewer;
        if (_geometriaAttiva) EsciDaGeometria();
        SalvaRicettaCorrente();

        _fotoViewerCorrente = foto;
        _mostraOriginale = false;
        _gestoInCorso = false;
        _gestoTimer?.Stop();
        // Filmstrip e posizione si aggiornano subito, prima del decode: con ←/→ veloci
        // l'evidenziazione segue il tasto invece di inseguire l'immagine grande.
        AggiornaFilmstrip();
        ViewerStatusText.Text = $"{TestoPosizioneViewer(foto)} · caricamento…";

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
        var recipeJson = _selectionManager?.GetRecipeJson(foto.Id) ?? _db?.GetRecipeJson(foto.Id);
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

        ViewerOverlay.Focus(FocusState.Programmatic);

        _ = PrecaricaViciniAsync(foto);
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
        SincronizzaPannello();
        ViewerStatusText.Text = $"{TestoPosizioneViewer(foto)} · impossibile leggere la foto";
        AggiornaBadgeFoto(foto);
        ViewerSelectionBorder.Visibility = Visibility.Collapsed;
        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    // "IMG_1234.HEIC · 34 di 1300": come in Picasa si sa sempre dove si è nella cartella (o nel filtro).
    private string TestoPosizioneViewer(PhotoGridItem foto)
    {
        var indice = _visibili.IndexOf(foto);
        return indice < 0 ? foto.PathRelativo : $"{foto.PathRelativo} · {indice + 1} di {_visibili.Count}";
    }

    private async Task<(byte[] Pixel, int Larghezza, int Altezza)> OttieniPreviewAsync(string percorso)
    {
        if (_previewCache.TryGetValue(percorso, out var cache))
        {
            _previewCacheOrdine.Remove(percorso);
            _previewCacheOrdine.Add(percorso);
            return cache;
        }

        var risultato = await PreviewDecoder.DecodePreviewBgraAsync(percorso);
        MemorizzaInCache(percorso, risultato);
        return risultato;
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

    private async Task PrecaricaViciniAsync(PhotoGridItem corrente)
    {
        var indice = _visibili.IndexOf(corrente);
        if (indice < 0) return;

        var percorsi = new List<string>();
        if (indice > 0) percorsi.Add(_visibili[indice - 1].PercorsoCompleto);
        if (indice < _visibili.Count - 1) percorsi.Add(_visibili[indice + 1].PercorsoCompleto);

        foreach (var percorso in percorsi)
        {
            if (_previewCache.ContainsKey(percorso) || !_prefetchInCorso.Add(percorso)) continue;

            try
            {
                var risultato = await PreviewDecoder.DecodePreviewBgraAsync(percorso);
                MemorizzaInCache(percorso, risultato);
            }
            catch
            {
                // Ignora: se il prefetch fallisce, il decode verra' ritentato al caricamento effettivo.
            }
            finally
            {
                _prefetchInCorso.Remove(percorso);
            }
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
        _viewerPixelCorrenti = bgra;
        _displayWidth = larghezza;
        _displayHeight = altezza;
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

    // Griglia di composizione: sempre riferita all'intera immagine (non al riquadro di crop), sola
    // trasparenza sulle linee — deve restare leggibile per inquadrare il soggetto indipendentemente
    // dal fatto che il crop sia in modifica o meno.
    private void AggiornaGriglia()
    {
        var x0 = 0.0; var y0 = 0.0;
        var w = _viewerImgW; var h = _viewerImgH;

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

    // ---------------------------------------------------------------- geometria (ritaglia + raddrizza)

    private void ViewerCropButton_Click(object sender, RoutedEventArgs e) => EntraInGeometria(focusSlider: false);

    private void ViewerStraightenButton_Click(object sender, RoutedEventArgs e) => EntraInGeometria(focusSlider: true);

    private void EntraInGeometria(bool focusSlider)
    {
        if (!EditingPronto) return;

        if (!_geometriaAttiva)
        {
            _geometriaAttiva = true;
            _mostraOriginale = false;
            ImpostaZoomViewer(1);

            var r = _ricettaCorrente;
            _aggiornandoUi = true;
            ViewerStraightenSlider.Value = r.StraightenAngleDegrees;
            _aggiornandoUi = false;
            ViewerAngleText.Text = $"{r.StraightenAngleDegrees:F1}°";
            ViewerRotateTransform.Angle = r.StraightenAngleDegrees;

            _cropN0X = r.CropX; _cropN0Y = r.CropY;
            _cropN1X = r.CropX + r.CropWidth; _cropN1Y = r.CropY + r.CropHeight;

            // Crop pieno (o pari al massimo inscritto): segue l'angolo come in Picasa.
            _cropAutoPieno = !r.HasGeometry || (r.StraightenAngleDegrees != 0 && CropCoincideConInscritto(r));
            _cropVoluto = _cropAutoPieno ? (0, 0, 1, 1) : (_cropN0X, _cropN0Y, _cropN1X, _cropN1Y);

            // Foto già ritagliata/raddrizzata: si passa subito alla tela intera (solo il tono arriva
            // col render), invece di mostrare per un attimo maniglie e rotazione sulla foto ritagliata.
            _bufferPrimaGeometria = _viewerPixelCorrenti is not null ? (_viewerPixelCorrenti, _displayWidth, _displayHeight) : null;
            if (r.HasGeometry && _viewerPixelOriginali is not null)
                MostraBuffer(_viewerPixelOriginali, _viewerPixelWidth, _viewerPixelHeight);

            ViewerProporzioneToggle.Content = $"Proporzioni: {ProporzioniDisponibili[_proporzioneIndice].Etichetta}";
            ImpostaVisibilitaGeometria(true);
            SincronizzaPannello();
            RichiediRender();
        }

        if (focusSlider) ViewerStraightenSlider.Focus(FocusState.Programmatic);
    }

    private bool CropCoincideConInscritto(EditRecipe r)
    {
        var w = (double)_viewerPixelWidth;
        var h = (double)_viewerPixelHeight;
        var (x0, y0, x1, y1) = StraightenGeometry.FitRect(w, h, r.StraightenAngleDegrees, 0, 0, w, h);
        return Math.Abs(x0 / w - r.CropX) < 0.005 && Math.Abs(y0 / h - r.CropY) < 0.005
            && Math.Abs(x1 / w - (r.CropX + r.CropWidth)) < 0.005 && Math.Abs(y1 / h - (r.CropY + r.CropHeight)) < 0.005;
    }

    private void ImpostaVisibilitaGeometria(bool attiva)
    {
        PannelloGeometria.Visibility = attiva ? Visibility.Visible : Visibility.Collapsed;
        MostraTab(tuning: ViewerTabTuning.IsChecked == true);

        var vis = attiva ? Visibility.Visible : Visibility.Collapsed;
        CropRectBorder.Visibility = vis;
        DimTop.Visibility = vis; DimBottom.Visibility = vis; DimLeft.Visibility = vis; DimRight.Visibility = vis;
        foreach (var m in _maniglie.Values) m.Visibility = vis;

        // Come Picasa: durante ritaglio/raddrizza si vede solo la foto. Il filmstrip sparisce (più
        // spazio, nessun click accidentale che cambia foto a metà ritaglio); il SizeChanged
        // dell'area ridimensiona l'immagine e riposiziona le maniglie.
        Filmstrip.Visibility = attiva ? Visibility.Collapsed : Visibility.Visible;
        ViewerHintText.Text = attiva ? HintGeometria : HintViewer;

        AggiornaBadgeFoto(_fotoViewerCorrente);
        if (attiva) AggiornaOverlayCrop();
    }

    private const string HintViewer =
        "←/→ foto · Space seleziona · Delete scarta · 0-5 rating · rotella zoom, trascina per spostare · X ritaglia · L raddrizza · U ho fortuna · Ctrl+Z/Y annulla/ripeti · B originale · Esc chiudi";
    private const string HintGeometria =
        "Trascina gli angoli o i lati per ritagliare, trascina dentro per spostare · L / frecce raddrizza · Invio o X applica · Esc annulla";

    private void EsciDaGeometria()
    {
        _geometriaAttiva = false;
        _dragTag = null;
        ViewerRotateTransform.Angle = 0;
        ImpostaVisibilitaGeometria(false);
        SincronizzaPannello();
    }

    private void ViewerGeometriaApplica_Click(object sender, RoutedEventArgs e) => ApplicaGeometria();

    private void ViewerGeometriaAnnulla_Click(object sender, RoutedEventArgs e) => AnnullaGeometria();

    private void ApplicaGeometria()
    {
        if (!_geometriaAttiva) return;

        var nuova = _ricettaCorrente with
        {
            CropX = _cropN0X,
            CropY = _cropN0Y,
            CropWidth = _cropN1X - _cropN0X,
            CropHeight = _cropN1Y - _cropN0Y,
            StraightenAngleDegrees = Math.Round(ViewerStraightenSlider.Value, 1),
        };

        // Crop praticamente a tutta tela e senza angolo: ricetta pulita, niente crop "fantasma".
        if (nuova.StraightenAngleDegrees == 0 && nuova.CropWidth > 0.995 && nuova.CropHeight > 0.995)
            nuova = nuova with { CropX = 0, CropY = 0, CropWidth = 1, CropHeight = 1 };

        if (nuova == _ricettaCorrente)
        {
            AnnullaGeometria();
            return;
        }

        RegistraUndo(_ricettaCorrente);
        _ricettaCorrente = nuova;
        _bufferPrimaGeometria = null;
        EsciDaGeometria();
        RichiediRender();
    }

    private void AnnullaGeometria()
    {
        if (!_geometriaAttiva) return;
        EsciDaGeometria();

        // Ricetta invariata: il risultato di prima è ancora valido, niente ricalcolo né lampo.
        if (_bufferPrimaGeometria is { } prima) MostraBuffer(prima.Pixel, prima.Larghezza, prima.Altezza);
        else RichiediRender();
        _bufferPrimaGeometria = null;
    }

    private (double X0, double Y0, double X1, double Y1) CropPx() =>
        (_cropN0X * _viewerImgW, _cropN0Y * _viewerImgH, _cropN1X * _viewerImgW, _cropN1Y * _viewerImgH);

    private void ImpostaCropPx(double x0, double y0, double x1, double y1)
    {
        _cropN0X = x0 / _viewerImgW; _cropN0Y = y0 / _viewerImgH;
        _cropN1X = x1 / _viewerImgW; _cropN1Y = y1 / _viewerImgH;
        AggiornaOverlayCrop();
    }

    private void AggiornaOverlayCrop()
    {
        if (_viewerImgW <= 0 || _viewerImgH <= 0) return;

        var (x0, y0, x1, y1) = CropPx();

        Canvas.SetLeft(CropRectBorder, x0);
        Canvas.SetTop(CropRectBorder, y0);
        CropRectBorder.Width = Math.Max(1, x1 - x0);
        CropRectBorder.Height = Math.Max(1, y1 - y0);

        PosizionaRettangolo(DimTop, 0, 0, _viewerImgW, y0);
        PosizionaRettangolo(DimBottom, 0, y1, _viewerImgW, _viewerImgH - y1);
        PosizionaRettangolo(DimLeft, 0, y0, x0, y1 - y0);
        PosizionaRettangolo(DimRight, x1, y0, _viewerImgW - x1, y1 - y0);

        double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        PosizionaManiglia("TL", x0, y0); PosizionaManiglia("T", mx, y0); PosizionaManiglia("TR", x1, y0);
        PosizionaManiglia("R", x1, my); PosizionaManiglia("BR", x1, y1); PosizionaManiglia("B", mx, y1);
        PosizionaManiglia("BL", x0, y1); PosizionaManiglia("L", x0, my);
    }

    private static void PosizionaRettangolo(Rectangle r, double x, double y, double w, double h)
    {
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        r.Width = Math.Max(0, w);
        r.Height = Math.Max(0, h);
    }

    private void PosizionaManiglia(string tag, double x, double y)
    {
        var m = _maniglie[tag];
        Canvas.SetLeft(m, x - DimensioneManiglia / 2);
        Canvas.SetTop(m, y - DimensioneManiglia / 2);
    }

    // Proporzione scelta, orientata come la foto (4:3 su una foto verticale diventa 3:4).
    private double? RapportoEffettivo()
    {
        var r = ProporzioneCorrente;
        if (r is not double rapporto) return null;
        return _viewerImgH > _viewerImgW ? 1 / rapporto : rapporto;
    }

    private void Crop_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_geometriaAttiva || sender is not FrameworkElement el || el.Tag is not string tag) return;

        _dragTag = tag;
        _dragStart = e.GetCurrentPoint(ViewerCropCanvas).Position;
        _dragRect = CropPx();
        el.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Crop_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragTag is null || !_geometriaAttiva) return;

        var pos = e.GetCurrentPoint(ViewerCropCanvas).Position;
        var candidato = CalcolaDragCrop(_dragTag, pos);
        var angolo = ViewerStraightenSlider.Value;

        // Il crop non puo uscire dall'immagine ruotata (niente angoli vuoti): se il movimento
        // porta fuori, il riquadro si ferma esattamente sul bordo.
        var attuale = CropPx();
        var nuovo = StraightenGeometry.AvvicinaRect(_viewerImgW, _viewerImgH, angolo, attuale, candidato);
        if (nuovo != attuale)
        {
            _cropAutoPieno = false;
            ImpostaCropPx(nuovo.X0, nuovo.Y0, nuovo.X1, nuovo.Y1);
        }
    }

    private void Crop_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragTag is not null && !_cropAutoPieno) _cropVoluto = (_cropN0X, _cropN0Y, _cropN1X, _cropN1Y);
        _dragTag = null;
        if (sender is UIElement el) el.ReleasePointerCaptures();
    }

    private (double X0, double Y0, double X1, double Y1) CalcolaDragCrop(string tag, Point pos)
    {
        var (x0, y0, x1, y1) = _dragRect;
        double W = _viewerImgW, H = _viewerImgH;

        if (tag == "M")
        {
            var w = x1 - x0;
            var h = y1 - y0;
            var nx0 = Math.Clamp(x0 + (pos.X - _dragStart.X), 0, W - w);
            var ny0 = Math.Clamp(y0 + (pos.Y - _dragStart.Y), 0, H - h);
            return (nx0, ny0, nx0 + w, ny0 + h);
        }

        var px = Math.Clamp(pos.X, 0, W);
        var py = Math.Clamp(pos.Y, 0, H);
        var sinistra = tag.Contains('L');
        var destra = tag.Contains('R');
        var alto = tag.Contains('T');
        var basso = tag.Contains('B');

        double nx0b = x0, ny0b = y0, nx1b = x1, ny1b = y1;
        if (sinistra) nx0b = Math.Min(px, x1 - CropMinimoPx);
        if (destra) nx1b = Math.Max(px, x0 + CropMinimoPx);
        if (alto) ny0b = Math.Min(py, y1 - CropMinimoPx);
        if (basso) ny1b = Math.Max(py, y0 + CropMinimoPx);

        if (RapportoEffettivo() is not double r) return (nx0b, ny0b, nx1b, ny1b);

        var angolare = tag.Length == 2;
        if (angolare)
        {
            // Ancora = angolo opposto; la larghezza guida, l'altezza segue.
            var ancoraX = sinistra ? x1 : x0;
            var ancoraY = alto ? y1 : y0;
            var spazioX = sinistra ? ancoraX : W - ancoraX;
            var spazioY = alto ? ancoraY : H - ancoraY;
            var larghezza = Math.Abs((sinistra ? nx0b : nx1b) - ancoraX);
            larghezza = Math.Max(CropMinimoPx, Math.Min(larghezza, Math.Min(spazioX, spazioY * r)));
            var altezza = larghezza / r;
            return (
                sinistra ? ancoraX - larghezza : ancoraX,
                alto ? ancoraY - altezza : ancoraY,
                sinistra ? ancoraX : ancoraX + larghezza,
                alto ? ancoraY : ancoraY + altezza);
        }

        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;
        if (alto || basso)
        {
            var altezza = alto ? y1 - ny0b : ny1b - y0;
            altezza = Math.Max(CropMinimoPx, Math.Min(altezza, 2 * Math.Min(cx, W - cx) / r));
            var larghezza = altezza * r;
            return (cx - larghezza / 2, alto ? y1 - altezza : y0, cx + larghezza / 2, alto ? y1 : y0 + altezza);
        }
        else
        {
            var larghezza = sinistra ? x1 - nx0b : nx1b - x0;
            larghezza = Math.Max(CropMinimoPx, Math.Min(larghezza, 2 * Math.Min(cy, H - cy) * r));
            var altezza = larghezza / r;
            return (sinistra ? x1 - larghezza : x0, cy - altezza / 2, sinistra ? x1 : x0 + larghezza, cy + altezza / 2);
        }
    }

    private void ViewerProporzioneToggle_Click(object sender, RoutedEventArgs e)
    {
        _proporzioneIndice = (_proporzioneIndice + 1) % ProporzioniDisponibili.Length;
        ViewerProporzioneToggle.Content = $"Proporzioni: {ProporzioniDisponibili[_proporzioneIndice].Etichetta}";
        if (!_geometriaAttiva || RapportoEffettivo() is not double r) return;

        // Riadatta subito il riquadro alla nuova proporzione, a parita di area e attorno al centro.
        var (x0, y0, x1, y1) = CropPx();
        double W = _viewerImgW, H = _viewerImgH;
        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;
        var larghezza = Math.Sqrt((x1 - x0) * (y1 - y0) * r);
        var altezza = larghezza / r;
        var scala = Math.Min(1.0, Math.Min(2 * Math.Min(cx, W - cx) / larghezza, 2 * Math.Min(cy, H - cy) / altezza));
        larghezza *= scala;
        altezza *= scala;

        var (fx0, fy0, fx1, fy1) = StraightenGeometry.FitRect(W, H, ViewerStraightenSlider.Value,
            cx - larghezza / 2, cy - altezza / 2, cx + larghezza / 2, cy + altezza / 2);
        _cropAutoPieno = false;
        ImpostaCropPx(fx0, fy0, fx1, fy1);
        _cropVoluto = (_cropN0X, _cropN0Y, _cropN1X, _cropN1Y);
    }

    private void ViewerStraightenSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_aggiornandoUi) return;

        ViewerAngleText.Text = $"{e.NewValue:F1}°";
        if (!_geometriaAttiva) return;

        ViewerRotateTransform.Angle = e.NewValue;

        double W = _viewerImgW, H = _viewerImgH;
        var (x0, y0, x1, y1) = _cropAutoPieno
            ? (0.0, 0.0, W, H)
            : (_cropVoluto.X0 * W, _cropVoluto.Y0 * H, _cropVoluto.X1 * W, _cropVoluto.Y1 * H);
        var (fx0, fy0, fx1, fy1) = StraightenGeometry.FitRect(W, H, e.NewValue, x0, y0, x1, y1);
        ImpostaCropPx(fx0, fy0, fx1, fy1);
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
    private void SalvaRicettaCorrente()
    {
        if (_fotoRicetta is null || _ricettaCorrente == _ricettaCaricata) return;

        _selectionManager?.SaveRecipe(_fotoRicetta.Id, EditRecipeSerializer.ToJson(_ricettaCorrente), _ricettaCorrente.SchemaVersion);
        _ricettaCaricata = _ricettaCorrente;
    }

    private void ViewerCloseButton_Click(object sender, RoutedEventArgs e) => ChiudiViewer();

    private void ChiudiViewer()
    {
        _richiestaCaricamentoViewer++;
        if (_geometriaAttiva) EsciDaGeometria();
        SalvaRicettaCorrente();
        ViewerOverlay.Visibility = Visibility.Collapsed;
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
        Griglia.Focus(FocusState.Programmatic);
        ElementSoundPlayer.Play(ElementSoundKind.GoBack);
    }

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

        var indice = _visibili.IndexOf(_fotoViewerCorrente);
        if (indice < 0) { Filmstrip.ItemsSource = null; return; }

        var larghezza = Filmstrip.ActualWidth > 0 ? Filmstrip.ActualWidth : ViewerOverlay.ActualWidth;
        var slot = PhotoGridItem.FilmstripLato + 2 * PhotoGridItem.FilmstripMargine;
        var slotCorrente = PhotoGridItem.FilmstripLatoCorrente + 2 * PhotoGridItem.FilmstripMargine;
        var raggio = Math.Clamp((int)((larghezza - slotCorrente) / 2 / slot), 0, raggioMassimo);

        var inizio = Math.Max(0, indice - raggio);
        var fine = Math.Min(_visibili.Count - 1, indice + raggio);

        var finestra = new List<PhotoGridItem>();
        for (var i = inizio; i <= fine; i++) finestra.Add(_visibili[i]);

        // Centro della corrente = centro della striscia, qualunque sia il numero di foto a sinistra.
        var primaDellaCorrente = indice - inizio;
        var paddingSinistro = larghezza / 2 - primaDellaCorrente * slot - slotCorrente / 2;
        Filmstrip.Padding = new Thickness(Math.Max(0, paddingSinistro), 0, 0, 0);

        _fotoViewerCorrente.IsCorrenteViewer = true;
        _filmstripCorrenteEvidenziata = _fotoViewerCorrente;
        Filmstrip.ItemsSource = finestra;

        foreach (var item in finestra)
        {
            if (item.Thumbnail is null) _thumbnailLoader?.Priorita(item);
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

    private async Task NavigaViewerAsync(int delta)
    {
        if (_fotoViewerCorrente is null) return;
        var indice = _visibili.IndexOf(_fotoViewerCorrente);
        if (indice < 0) return;

        var nuovoIndice = indice + delta;
        if (nuovoIndice < 0 || nuovoIndice >= _visibili.Count) return;

        await CaricaFotoNelViewerAsync(_visibili[nuovoIndice]);
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

        if (_geometriaAttiva)
        {
            GestisciTastoGeometria(e);
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                ChiudiViewer();
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

            case VirtualKey.B:
                if (!_mostraOriginale && _viewerPixelOriginali is not null)
                {
                    _mostraOriginale = true;
                    AggiornaBitmap();
                }
                e.Handled = true;
                break;

            case VirtualKey.Number0: SetRating(_fotoViewerCorrente, 0); e.Handled = true; break;
            case VirtualKey.Number1: SetRating(_fotoViewerCorrente, 1); e.Handled = true; break;
            case VirtualKey.Number2: SetRating(_fotoViewerCorrente, 2); e.Handled = true; break;
            case VirtualKey.Number3: SetRating(_fotoViewerCorrente, 3); e.Handled = true; break;
            case VirtualKey.Number4: SetRating(_fotoViewerCorrente, 4); e.Handled = true; break;
            case VirtualKey.Number5: SetRating(_fotoViewerCorrente, 5); e.Handled = true; break;
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
        var indice = _visibili.IndexOf(foto);
        var alternativa = indice >= 0
            ? _visibili.ElementAtOrDefault(indice + 1) ?? _visibili.ElementAtOrDefault(indice - 1)
            : null;

        SetStato(foto, nuovoStato);
        ViewerSelectionBorder.Visibility = foto.IsSelected ? Visibility.Visible : Visibility.Collapsed;

        if (_fotoViewerCorrente == foto && !_visibili.Contains(foto))
        {
            if (alternativa is not null && _visibili.Contains(alternativa))
                _ = CaricaFotoNelViewerAsync(alternativa);
            else if (_visibili.Count > 0)
                _ = CaricaFotoNelViewerAsync(_visibili[0]);
            else
                ChiudiViewer();
        }
        else
        {
            AggiornaFilmstrip();
        }
    }
}
