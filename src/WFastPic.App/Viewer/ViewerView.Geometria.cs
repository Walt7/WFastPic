using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WFastPic.Core.Processing;

namespace WFastPic.App;

/// <summary>Modalità geometria: ritaglia (maniglie, proporzioni) e raddrizza, con Applica/Annulla.</summary>
public sealed partial class ViewerView
{
    private enum GrigliaComposizione { Terzi, Meta, Diagonali }

    // NaN = "originale": le proporzioni della foto stessa, calcolate sulla foto aperta.
    private static readonly (string Etichetta, double? Rapporto)[] ProporzioniDisponibili =
    [
        ("libera", null),
        ("originale", double.NaN),
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
    private double? ProporzioneCorrente => RapportoProporzione(_proporzioneIndice);

    // In forma "orizzontale" (>= 1): RapportoEffettivo la gira per le foto verticali, come le altre.
    private double? RapportoProporzione(int indice) => ProporzioniDisponibili[indice].Rapporto switch
    {
        double r when double.IsNaN(r) && _viewerPixelWidth > 0 && _viewerPixelHeight > 0 =>
            Math.Max((double)_viewerPixelWidth / _viewerPixelHeight, (double)_viewerPixelHeight / _viewerPixelWidth),
        double r when double.IsNaN(r) => null,
        var r => r,
    };

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

    private void AgganciaDragCrop(UIElement el)
    {
        el.PointerPressed += Crop_PointerPressed;
        el.PointerMoved += Crop_PointerMoved;
        el.PointerReleased += Crop_PointerReleased;
        el.PointerCaptureLost += Crop_PointerReleased;
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
            {
                // Tela = foto già girata/specchiata: ritaglio e raddrizzamento sono espressi su quella.
                var tela = Orientamento.Applica(_viewerPixelOriginali, _viewerPixelWidth, _viewerPixelHeight, r);
                MostraBuffer(tela.Bgra, tela.Larghezza, tela.Altezza);
            }

            _riquadroVerticale = null; // ogni ritaglio parte orientato come la foto
            AggiornaEtichettaProporzione();
            ImpostaVisibilitaGeometria(true);
            SincronizzaPannello();
            RichiediRender();
            CalcolaSuggerimentiRitaglio();
        }

        if (focusSlider) ViewerStraightenSlider.Focus(FocusState.Programmatic);
    }

    private bool CropCoincideConInscritto(EditRecipe r)
    {
        var (lt, at) = Orientamento.Dimensioni(_viewerPixelWidth, _viewerPixelHeight, r);
        var w = (double)lt;
        var h = (double)at;
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
        Filmstrip.Visibility = attiva || _schermoIntero ? Visibility.Collapsed : Visibility.Visible;
        PannelloLuogo.Visibility = attiva ? Visibility.Collapsed : Visibility.Visible;
        ViewerHintText.Text = attiva ? HintGeometria : HintViewer;

        AggiornaBadgeFoto(_fotoViewerCorrente);
        if (_fotoViewerCorrente is { } corrente) AggiornaRaffica(corrente); // in ritaglio si vede solo la foto
        if (attiva) AggiornaOverlayCrop();
    }

    private const string HintViewer =
        "←/→ foto · Space seleziona · Delete scarta · 0-5 rating · rotella zoom, trascina per spostare · X ritaglia · L raddrizza · U ho fortuna · Ctrl+Z/Y annulla/ripeti · B originale · F11 schermo intero · Esc chiudi";
    private const string HintGeometria =
        "Trascina gli angoli o i lati per ritagliare, trascina dentro per spostare · L / frecce raddrizza · O riquadro orizzontale/verticale · Invio o X applica · Esc annulla";

    private void EsciDaGeometria()
    {
        _geometriaAttiva = false;
        _dragTag = null;
        SvuotaSuggerimentiRitaglio();
        ViewerRotateTransform.Angle = 0;
        ImpostaVisibilitaGeometria(false);
        AggiornaGriglia(); // torna sull'intera foto
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

        AggiornaGriglia();
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

    // Riquadro verticale (3:4) o orizzontale (4:3): null = come la foto. Si riparte da null a ogni ritaglio.
    private bool? _riquadroVerticale;

    private bool RiquadroVerticale => _riquadroVerticale ?? _viewerImgH > _viewerImgW;

    // Proporzione scelta, orientata come il riquadro (4:3 su una foto verticale diventa 3:4).
    private double? RapportoEffettivo()
    {
        var r = ProporzioneCorrente;
        if (r is not double rapporto) return null;
        return RiquadroVerticale ? 1 / rapporto : rapporto;
    }

    /// <summary>"Proporzioni: 3:4" quando il riquadro è verticale; il pulsante per girarlo serve solo se non è quadrato.</summary>
    private void AggiornaEtichettaProporzione()
    {
        var etichetta = ProporzioniDisponibili[_proporzioneIndice].Etichetta;
        var parti = etichetta.Split(':');
        if (RiquadroVerticale && parti.Length == 2) etichetta = $"{parti[1]}:{parti[0]}";
        else if (etichetta == "originale" && RiquadroVerticale != _viewerImgH > _viewerImgW) etichetta = "originale, girata";

        ViewerProporzioneToggle.Content = $"Proporzioni: {etichetta}";
        ViewerOrientamentoRiquadroButton.IsEnabled = ProporzioneCorrente is double rapporto && Math.Abs(rapporto - 1) > 0.001;
    }

    private void ViewerOrientamentoRiquadro_Click(object sender, RoutedEventArgs e)
    {
        if (!_geometriaAttiva || ProporzioneCorrente is null) return;
        _riquadroVerticale = !RiquadroVerticale;
        AggiornaEtichettaProporzione();
        RiadattaRiquadroAllaProporzione();
        ViewerOverlay.Focus(FocusState.Programmatic);
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
        AggiornaEtichettaProporzione();
        RiadattaRiquadroAllaProporzione();
    }

    // Riadatta subito il riquadro alla proporzione (e all'orientamento) scelti, a parità di area e attorno al centro.
    private void RiadattaRiquadroAllaProporzione()
    {
        if (!_geometriaAttiva || RapportoEffettivo() is not double r) return;

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
        ProgrammaSuggerimentiRitaglio();

        double W = _viewerImgW, H = _viewerImgH;
        var (x0, y0, x1, y1) = _cropAutoPieno
            ? (0.0, 0.0, W, H)
            : (_cropVoluto.X0 * W, _cropVoluto.Y0 * H, _cropVoluto.X1 * W, _cropVoluto.Y1 * H);
        var (fx0, fy0, fx1, fy1) = StraightenGeometry.FitRect(W, H, e.NewValue, x0, y0, x1, y1);
        ImpostaCropPx(fx0, fy0, fx1, fy1);
    }
}
