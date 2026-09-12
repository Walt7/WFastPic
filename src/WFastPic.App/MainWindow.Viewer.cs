using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using WFastPic.Core.Decoding;
using WFastPic.Core.Processing;
using WFastPic.Core.Selection;

namespace WFastPic.App;

/// <summary>
/// Fase 4: Viewer a schermo pieno con editing minimo (crop + raddrizza + auto-correzione soltanto,
/// come da piano). Stessi shortcut di navigazione/selezione della Grid cosi il ciclo principale
/// funziona identico anche qui.
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

    private int _proporzioneIndice;
    private double? ProporzioneCorrente => ProporzioniDisponibili[_proporzioneIndice].Rapporto;

    private bool _viewerAperto;
    private PhotoGridItem? _fotoViewerCorrente;
    private byte[]? _viewerPixelOriginali;
    private byte[]? _viewerPixelCorrenti;
    private int _viewerPixelWidth;
    private int _viewerPixelHeight;
    private double _viewerImgW;
    private double _viewerImgH;
    private EditRecipe _ricettaCorrente = EditRecipe.Default;
    private bool _cropModeAttivo;
    private string? _handleInDrag;
    private double _cropX0, _cropY0, _cropX1, _cropY1;
    private GrigliaComposizione _grigliaModo = GrigliaComposizione.Terzi;

    private void InizializzaViewer()
    {
        // handledEventsToo: i controlli interni (Slider, Button) possono marcare l'evento Handled
        // per uso proprio; gli shortcut del Viewer devono comunque sempre arrivare.
        ViewerOverlay.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(ViewerOverlay_KeyDown), handledEventsToo: true);
    }

    private void ApriViewer(PhotoGridItem? foto)
    {
        if (foto is null || _viewerAperto) return;
        _ = CaricaFotoNelViewerAsync(foto);
        ViewerOverlay.Visibility = Visibility.Visible;
        _viewerAperto = true;
        ElementSoundPlayer.Play(ElementSoundKind.Show);
    }

    private async Task CaricaFotoNelViewerAsync(PhotoGridItem foto)
    {
        SalvaRicettaCorrente();

        _fotoViewerCorrente = foto;
        ViewerStatusText.Text = "Caricamento…";

        var (pixel, larghezza, altezza) = await PreviewDecoder.DecodePreviewBgraAsync(foto.PercorsoCompleto);

        _viewerPixelOriginali = pixel;
        _viewerPixelWidth = larghezza;
        _viewerPixelHeight = altezza;

        var recipeJson = _db?.GetRecipeJson(foto.Id);
        _ricettaCorrente = recipeJson is not null ? EditRecipeSerializer.FromJson(recipeJson) : EditRecipe.Default;

        _viewerPixelCorrenti = (byte[])_viewerPixelOriginali.Clone();
        if (_ricettaCorrente.AutoCorrect)
        {
            AutoCorrectProcessor.ApplyInPlace(_viewerPixelCorrenti, larghezza, altezza);
        }

        ComputeViewerSize(larghezza, altezza);
        AggiornaImmagineViewer();

        ViewerStraightenSlider.Value = _ricettaCorrente.StraightenAngleDegrees;
        ViewerRotateTransform.Angle = _ricettaCorrente.StraightenAngleDegrees;
        ViewerAngleText.Text = $"{_ricettaCorrente.StraightenAngleDegrees:F1}°";

        ImpostaCropIniziale();
        AggiornaFilmstrip();
        ViewerStatusText.Text = foto.PathRelativo;
        ViewerVideoBadge.Visibility = foto.HasVideo ? Visibility.Visible : Visibility.Collapsed;

        ViewerCloseButton.Focus(FocusState.Programmatic);
    }

    private void ComputeViewerSize(int pixelWidth, int pixelHeight)
    {
        const double maxW = 1100, maxH = 620;
        var scale = Math.Min(maxW / pixelWidth, maxH / pixelHeight);
        scale = Math.Min(scale, 1.0);
        _viewerImgW = Math.Max(1, pixelWidth * scale);
        _viewerImgH = Math.Max(1, pixelHeight * scale);
    }

    private void AggiornaImmagineViewer()
    {
        if (_viewerPixelCorrenti is null) return;

        var wb = new WriteableBitmap(_viewerPixelWidth, _viewerPixelHeight);
        using (var stream = wb.PixelBuffer.AsStream())
        {
            stream.Write(_viewerPixelCorrenti, 0, _viewerPixelCorrenti.Length);
        }

        ViewerImage.Source = wb;
        ViewerImage.Width = _viewerImgW;
        ViewerImage.Height = _viewerImgH;
        ViewerCropCanvas.Width = _viewerImgW;
        ViewerCropCanvas.Height = _viewerImgH;
    }

    private void ImpostaCropIniziale()
    {
        var x = _ricettaCorrente.CropX * _viewerImgW;
        var y = _ricettaCorrente.CropY * _viewerImgH;
        var w = _ricettaCorrente.CropWidth * _viewerImgW;
        var h = _ricettaCorrente.CropHeight * _viewerImgH;
        PosizionaCrop(x, y, x + w, y + h);
    }

    private void PosizionaCrop(double x0, double y0, double x1, double y1)
    {
        _cropX0 = x0; _cropY0 = y0; _cropX1 = x1; _cropY1 = y1;

        Canvas.SetLeft(CropRectBorder, x0);
        Canvas.SetTop(CropRectBorder, y0);
        CropRectBorder.Width = Math.Max(1, x1 - x0);
        CropRectBorder.Height = Math.Max(1, y1 - y0);

        Canvas.SetLeft(HandleTL, x0 - 9);
        Canvas.SetTop(HandleTL, y0 - 9);
        Canvas.SetLeft(HandleBR, x1 - 9);
        Canvas.SetTop(HandleBR, y1 - 9);

        var w = x1 - x0;
        var h = y1 - y0;

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

        DiagLine1.X1 = x0; DiagLine1.Y1 = y0; DiagLine1.X2 = x1; DiagLine1.Y2 = y1;
        DiagLine2.X1 = x1; DiagLine2.Y1 = y0; DiagLine2.X2 = x0; DiagLine2.Y2 = y1;
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

        PosizionaCrop(_cropX0, _cropY0, _cropX1, _cropY1);
    }

    private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Rectangle r) return;
        _handleInDrag = r.Tag as string;
        r.CapturePointer(e.Pointer);
    }

    private void Handle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_handleInDrag is null) return;

        var pos = e.GetCurrentPoint(ViewerCropCanvas).Position;
        var x = Math.Clamp(pos.X, 0, _viewerImgW);
        var y = Math.Clamp(pos.Y, 0, _viewerImgH);
        var rapporto = ProporzioneCorrente;

        if (_handleInDrag == "TL")
        {
            var nx0 = Math.Min(x, _cropX1 - 20);
            var ny0 = Math.Min(y, _cropY1 - 20);

            if (rapporto is double r)
            {
                var w = _cropX1 - nx0;
                ny0 = _cropY1 - w / r;
                if (ny0 < 0)
                {
                    ny0 = 0;
                    nx0 = _cropX1 - (_cropY1 - ny0) * r;
                }
            }

            PosizionaCrop(nx0, ny0, _cropX1, _cropY1);
        }
        else
        {
            var nx1 = Math.Max(x, _cropX0 + 20);
            var ny1 = Math.Max(y, _cropY0 + 20);

            if (rapporto is double r)
            {
                var w = nx1 - _cropX0;
                ny1 = _cropY0 + w / r;
                if (ny1 > _viewerImgH)
                {
                    ny1 = _viewerImgH;
                    nx1 = _cropX0 + (ny1 - _cropY0) * r;
                }
            }

            PosizionaCrop(_cropX0, _cropY0, nx1, ny1);
        }
    }

    private void ViewerProporzioneToggle_Click(object sender, RoutedEventArgs e)
    {
        _proporzioneIndice = (_proporzioneIndice + 1) % ProporzioniDisponibili.Length;
        ViewerProporzioneToggle.Content = $"Proporzioni: {ProporzioniDisponibili[_proporzioneIndice].Etichetta}";
    }

    private void Handle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _handleInDrag = null;
        if (sender is UIElement el) el.ReleasePointerCaptures();
    }

    private void ViewerCropToggle_Click(object sender, RoutedEventArgs e)
    {
        _cropModeAttivo = !_cropModeAttivo;
        ViewerCropCanvas.Visibility = _cropModeAttivo ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewerStraightenSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ViewerRotateTransform.Angle = e.NewValue;
        ViewerAngleText.Text = $"{e.NewValue:F1}°";
    }

    private void ViewerAutoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerPixelOriginali is null) return;

        _ricettaCorrente = _ricettaCorrente with { AutoCorrect = !_ricettaCorrente.AutoCorrect };

        _viewerPixelCorrenti = (byte[])_viewerPixelOriginali.Clone();
        if (_ricettaCorrente.AutoCorrect)
        {
            AutoCorrectProcessor.ApplyInPlace(_viewerPixelCorrenti, _viewerPixelWidth, _viewerPixelHeight);
        }

        AggiornaImmagineViewer();
    }

    private void SalvaRicettaCorrente()
    {
        if (_fotoViewerCorrente is null || _viewerImgW <= 0 || _viewerImgH <= 0) return;

        var recipe = _ricettaCorrente with
        {
            CropX = _cropX0 / _viewerImgW,
            CropY = _cropY0 / _viewerImgH,
            CropWidth = (_cropX1 - _cropX0) / _viewerImgW,
            CropHeight = (_cropY1 - _cropY0) / _viewerImgH,
            StraightenAngleDegrees = ViewerStraightenSlider.Value,
        };

        _selectionManager?.SaveRecipe(_fotoViewerCorrente.Id, EditRecipeSerializer.ToJson(recipe), recipe.SchemaVersion);
    }

    private void ViewerCloseButton_Click(object sender, RoutedEventArgs e) => ChiudiViewer();

    private void ChiudiViewer()
    {
        SalvaRicettaCorrente();
        ViewerOverlay.Visibility = Visibility.Collapsed;
        _viewerAperto = false;
        _fotoViewerCorrente = null;
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

    private void AggiornaFilmstrip()
    {
        if (_fotoViewerCorrente is null) { Filmstrip.ItemsSource = null; return; }

        var indice = _visibili.IndexOf(_fotoViewerCorrente);
        if (indice < 0) { Filmstrip.ItemsSource = null; return; }

        const int raggio = 12;
        var inizio = Math.Max(0, indice - raggio);
        var fine = Math.Min(_visibili.Count - 1, indice + raggio);

        var finestra = new List<PhotoGridItem>();
        for (var i = inizio; i <= fine; i++) finestra.Add(_visibili[i]);
        Filmstrip.ItemsSource = finestra;
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

    private void ViewerOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_fotoViewerCorrente is null) return;

        // Lo Slider gestisce le frecce per conto proprio quando ha il focus: non intercettarle.
        if (e.OriginalSource is Slider) return;

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
                SetStato(_fotoViewerCorrente, _fotoViewerCorrente.Stato == StatoSelezione.Selected ? StatoSelezione.None : StatoSelezione.Selected);
                e.Handled = true;
                break;

            case VirtualKey.Delete or VirtualKey.Back:
                SetStato(_fotoViewerCorrente, StatoSelezione.Reject);
                e.Handled = true;
                break;

            case VirtualKey.X:
                ViewerCropToggle_Click(ViewerCropToggle, new RoutedEventArgs());
                e.Handled = true;
                break;

            case VirtualKey.L:
                ViewerStraightenSlider.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;

            case VirtualKey.U:
                ViewerAutoButton_Click(ViewerAutoButton, new RoutedEventArgs());
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
}
