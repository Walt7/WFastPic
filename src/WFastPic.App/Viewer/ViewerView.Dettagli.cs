using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Colonna dei dettagli (destra) e delle modifiche (sinistra), entrambe comprimibili; striscia delle
/// foto della stessa raffica accanto alla foto; miniatura segnaposto mentre si scorre.
/// </summary>
public sealed partial class ViewerView
{
    // ---------------------------------------------------------------- colonne comprimibili

    /// <summary>Ricordate tra una sessione e l'altra (MainWindow le salva nelle impostazioni).</summary>
    internal bool PannelloModificheAperto
    {
        get => ViewerPannelloModificheToggle.IsChecked == true;
        set { ViewerPannelloModificheToggle.IsChecked = value; AggiornaPannelli(); }
    }

    internal bool PannelloDettagliAperto
    {
        get => ViewerPannelloDettagliToggle.IsChecked == true;
        set { ViewerPannelloDettagliToggle.IsChecked = value; AggiornaPannelli(); }
    }

    private void ViewerPannelli_Click(object sender, RoutedEventArgs e)
    {
        AggiornaPannelli();
        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    // Il SizeChanged dell'area della foto riadatta foto e maniglie alla larghezza nuova.
    private void AggiornaPannelli()
    {
        ViewerPannelloModifiche.Visibility = PannelloModificheAperto ? Visibility.Visible : Visibility.Collapsed;
        ViewerPannelloDettagli.Visibility = PannelloDettagliAperto ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- dettagli

    private void AggiornaDettagli(PhotoGridItem foto)
    {
        if (foto.CaptureDate is DateTime scatto)
        {
            var giorno = scatto.ToString("dddd", Cultura);
            ViewerGiornoSettimanaText.Text = char.ToUpper(giorno[0], Cultura) + giorno[1..];
            ViewerDataText.Text = scatto.ToString("d MMMM yyyy", Cultura);
            ViewerOraText.Text = scatto.ToString("HH:mm:ss", Cultura);
        }
        else
        {
            // Senza EXIF: si dice chiaramente che la data è quella del file, non dello scatto.
            var file = foto.LastWriteUtc.ToLocalTime();
            ViewerGiornoSettimanaText.Text = T("Data di scatto sconosciuta");
            ViewerDataText.Text = T("File modificato {0}", file.ToString("dddd d MMMM yyyy", Cultura));
            ViewerOraText.Text = file.ToString("HH:mm:ss", Cultura);
        }

        ViewerFileNomeText.Text = foto.PathRelativo;
        var estensione = Path.GetExtension(foto.PathRelativo).TrimStart('.').ToUpperInvariant();
        ViewerFileInfoText.Text = $"{DimensioneLeggibile(foto.FileSize)} · {estensione}";

        ViewerRafficaInfoText.Visibility = foto.HasGroup ? Visibility.Visible : Visibility.Collapsed;
        ViewerRafficaInfoText.Text = foto.HasGroup ? T("Raffica: foto {0} di {1}", foto.PosizioneNelGruppo, foto.DimensioneGruppo) : "";

        // Il voto cambia anche a foto ferma (tasti 0-5, analisi in background, nomi dati ai volti).
        if (_fotoDettagli != foto)
        {
            if (_fotoDettagli is not null) _fotoDettagli.PropertyChanged -= FotoDettagli_PropertyChanged;
            _fotoDettagli = foto;
            foto.PropertyChanged += FotoDettagli_PropertyChanged;
        }
        AggiornaVoto(foto);
        _ = AggiornaVoltiAsync(foto);
    }

    // ---------------------------------------------------------------- voto e volti

    private PhotoGridItem? _fotoDettagli;
    private int _richiestaVolti;

    private void FotoDettagli_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not PhotoGridItem foto || foto != _fotoDettagli) return;
        if (e.PropertyName is nameof(PhotoGridItem.Rating) or nameof(PhotoGridItem.VotoAuto)) AggiornaVoto(foto);
    }

    private void AggiornaVoto(PhotoGridItem foto)
    {
        ViewerVotoTuoText.Text = foto.Rating > 0 ? foto.RatingTesto : T("nessuno (tasti 1-5)");
        ViewerVotoTuoText.Opacity = foto.Rating > 0 ? 1 : 0.6;
        ViewerVotoAutoText.Text = foto.VotoAuto is not null ? foto.VotoAutoTesto : T("in analisi…");
        ViewerVotoAutoText.Opacity = foto.VotoAuto is null ? 0.6 : 1;
        ViewerVotoMotiviText.Text = foto.VotoAuto is { } v ? string.Join(" · ", EtichetteVoto.Motivi(v.Motivi)) : "";
    }

    /// <summary>Nomi, cerchie o volti cambiati (pannello Persone aggiornato): si rilegge la sezione Volti.</summary>
    internal void AggiornaVoltiFotoCorrente()
    {
        if (IsAperto && _fotoDettagli is { } foto) _ = AggiornaVoltiAsync(foto);
    }

    private async Task AggiornaVoltiAsync(PhotoGridItem foto)
    {
        var richiesta = ++_richiestaVolti;
        var volti = await Host.VoltiDellaFotoAsync(foto);
        if (richiesta != _richiestaVolti) return;

        NascondiPosizioneVolto();
        ViewerVoltiPanel.Children.Clear();
        ViewerVoltiStatoText.Text = volti switch
        {
            null => T("Volti non ancora analizzati (serve il riconoscimento delle persone, nel pannello a sinistra)."),
            [] => T("Nessun volto in questa foto."),
            _ => T("Clic su un volto per dire chi è."),
        };
        if (volti is null) return;

        foreach (var volto in volti)
        {
            var elemento = await CreaVoltoAsync(volto);
            if (richiesta != _richiestaVolti) return;
            ViewerVoltiPanel.Children.Add(elemento);
        }
    }

    private void NascondiPosizioneVolto() => ViewerVoltoCerchio.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Cerchio sulla foto dove si trova il volto. Il riquadro è salvato sulla foto originale: segue la stessa
    /// strada della ricetta (gira/specchia → raddrizza → ritaglia) per cadere sul punto giusto di quella mostrata.
    /// Con la geometria aperta (ritaglio/raddrizza) la foto è un'altra: niente cerchio.
    /// </summary>
    private void MostraPosizioneVolto(VoltoDellaFoto volto)
    {
        var larghezzaMostrata = ViewerImage.ActualWidth;
        var altezzaMostrata = ViewerImage.ActualHeight;
        if (_geometriaAttiva || larghezzaMostrata <= 0 || altezzaMostrata <= 0 || volto.X1 <= volto.X0) { NascondiPosizioneVolto(); return; }

        var r = _ricettaCorrente;
        var (ox0, oy0, ox1, oy1) = Core.Processing.Orientamento.TrasformaRettangolo(volto.X0, volto.Y0, volto.X1, volto.Y1, r);

        // Tela raddrizzata: la foto mostrata è un suo ritaglio, quindi le sue proporzioni si ricavano dal ritaglio.
        var cw = r.CropWidth > 0 ? r.CropWidth : 1;
        var ch = r.CropHeight > 0 ? r.CropHeight : 1;
        double tela = larghezzaMostrata / cw, telaAltezza = altezzaMostrata / ch;
        var (rx0, ry0, rx1, ry1) = Core.Processing.StraightenGeometry.RuotaRettangolo(
            tela, telaAltezza, r.StraightenAngleDegrees, ox0 * tela, oy0 * telaAltezza, ox1 * tela, oy1 * telaAltezza);

        // Dalla tela alla foto mostrata (scarto del ritaglio).
        double x0 = rx0 - r.CropX * tela, x1 = rx1 - r.CropX * tela;
        double y0 = ry0 - r.CropY * telaAltezza, y1 = ry1 - r.CropY * telaAltezza;
        double centroX = (x0 + x1) / 2, centroY = (y0 + y1) / 2;
        if (centroX < 0 || centroY < 0 || centroX > larghezzaMostrata || centroY > altezzaMostrata) { NascondiPosizioneVolto(); return; } // ritagliato via

        var diametro = Math.Max(24, Math.Max(x1 - x0, y1 - y0) * 1.5);
        ViewerVoltoCerchio.Width = diametro;
        ViewerVoltoCerchio.Height = diametro;
        Canvas.SetLeft(ViewerVoltoCerchio, centroX - diametro / 2);
        Canvas.SetTop(ViewerVoltoCerchio, centroY - diametro / 2);
        ViewerVoltoCerchio.Visibility = Visibility.Visible;
    }

    /// <summary>Miniatura tonda, nome sotto ("Anna?" se solo proposto) e chi è (Sono io, ★, Famiglia…).</summary>
    private async Task<UIElement> CreaVoltoAsync(VoltoDellaFoto volto)
    {
        var immagine = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        if (volto.Miniatura is not null)
        {
            using var stream = new MemoryStream(volto.Miniatura).AsRandomAccessStream();
            await immagine.SetSourceAsync(stream);
        }

        var nome = volto.Nome is null ? T("Senza nome") : volto.Proposto ? volto.Nome + "?" : volto.Nome;
        var cerchia = volto.Nome is null ? null : volto.Cerchia switch
        {
            Core.Persistence.CerchiaPersona.Io => T("Sono io"),
            Core.Persistence.CerchiaPersona.Famiglia => T("Famiglia"),
            Core.Persistence.CerchiaPersona.Amico => T("Amico"),
            Core.Persistence.CerchiaPersona.Conoscente => T("Conoscente"),
            _ => null,
        };
        if (volto.Preferita && volto.Nome is not null) cerchia = cerchia is null ? "★" : "★ " + cerchia;

        var contenuto = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        contenuto.Children.Add(new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(28),
            // Bordo oro su di te, come le foto "ci sei tu" in griglia.
            BorderThickness = new Thickness(volto.Cerchia == Core.Persistence.CerchiaPersona.Io && volto.Nome is not null ? 2 : 0),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gold),
            Child = new Image { Source = immagine, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill },
        });
        contenuto.Children.Add(new TextBlock
        {
            Text = nome, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 74,
            HorizontalAlignment = HorizontalAlignment.Center, Opacity = volto.Nome is null || volto.Proposto ? 0.65 : 1,
        });
        if (cerchia is not null)
            contenuto.Children.Add(new TextBlock { Text = cerchia, FontSize = 10, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gold), HorizontalAlignment = HorizontalAlignment.Center });

        var pulsante = new Button
        {
            Content = contenuto,
            Padding = new Thickness(2),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsTabStop = false,
            AllowFocusOnInteraction = false,
        };
        pulsante.PointerEntered += (_, _) => MostraPosizioneVolto(volto);
        pulsante.PointerExited += (_, _) => NascondiPosizioneVolto();
        ToolTipService.SetToolTip(pulsante, volto.Proposto ? T("Proposta: clic per confermare o correggere") : T("Chi è?"));
        pulsante.Click += async (_, _) =>
        {
            await Host.DaiNomeAlVoltoAsync(volto); // i nomi si aggiornano con il pannello Persone (AggiornaVoltiFotoCorrente)
            ViewerOverlay.Focus(FocusState.Programmatic);
        };
        return pulsante;
    }

    private static string DimensioneLeggibile(long byteFile) => byteFile switch
    {
        >= 1024 * 1024 => string.Format(Cultura, "{0:0.0} MB", byteFile / (1024.0 * 1024)),
        >= 1024 => string.Format(Cultura, "{0:0} KB", byteFile / 1024.0),
        _ => T("{0} byte", byteFile),
    };

    // ---------------------------------------------------------------- raffica accanto alla foto

    private PhotoGridItem? _rafficaMostrataPer;

    /// <summary>
    /// Foto della stessa raffica (tra quelle visibili, così ←/→ e "N di M" restano coerenti):
    /// sopra la foto se è orizzontale, a fianco se è verticale. Prima del decode l'orientamento viene
    /// dalla miniatura, poi dalla foto mostrata (che tiene conto di gira/ritaglio).
    /// </summary>
    private void AggiornaRaffica(PhotoGridItem foto)
    {
        List<PhotoGridItem>? gruppo = null;
        if (foto.GroupId is int id && !_geometriaAttiva)
        {
            gruppo = Host.Visibili.Where(p => p.GroupId == id).ToList();
            if (gruppo.Count < 2) gruppo = null;
        }

        if (gruppo is null)
        {
            _rafficaMostrataPer = null;
            ViewerRafficaOrizzontale.Visibility = Visibility.Collapsed;
            ViewerRafficaVerticale.Visibility = Visibility.Collapsed;
            ViewerRafficaOrizzontale.ItemsSource = null;
            ViewerRafficaVerticale.ItemsSource = null;
            return;
        }

        var verticale = FotoCorrenteVerticale(foto);
        var mostrata = verticale ? ViewerRafficaVerticale : ViewerRafficaOrizzontale;
        var nascosta = verticale ? ViewerRafficaOrizzontale : ViewerRafficaVerticale;
        nascosta.Visibility = Visibility.Collapsed;
        nascosta.ItemsSource = null;

        // Stessa raffica di prima: niente ricostruzione (l'evidenza della corrente segue IsCorrenteViewer).
        var stessoGruppo = _rafficaMostrataPer?.GroupId == foto.GroupId && mostrata.ItemsSource is List<PhotoGridItem> attuale
            && attuale.SequenceEqual(gruppo);
        if (!stessoGruppo) mostrata.ItemsSource = gruppo;
        mostrata.Visibility = Visibility.Visible;
        _rafficaMostrataPer = foto;
        mostrata.ScrollIntoView(foto);
    }

    private bool FotoCorrenteVerticale(PhotoGridItem foto)
    {
        if (_fotoRicetta == foto && _displayWidth > 0 && _displayHeight > 0)
            return _displayHeight > _displayWidth;

        var verticale = foto.MiniaturaVerticale;
        return foto.RotazioneMiniatura is 90 or 270 ? !verticale : verticale;
    }

    private async void ViewerRaffica_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoGridItem foto && foto != _fotoViewerCorrente)
            await CaricaFotoNelViewerAsync(foto);
    }

    // ---------------------------------------------------------------- segnaposto mentre si scorre

    /// <summary>
    /// Foto non ancora decodificata: subito la miniatura ingrandita al posto della foto precedente,
    /// così scorrendo si vede sempre la foto giusta; la preview nitida la sostituisce appena pronta.
    /// </summary>
    private void MostraSegnaposto(PhotoGridItem foto)
    {
        if (foto.Thumbnail is null)
        {
            NascondiSegnaposto();
            return;
        }

        ViewerSegnaposto.Source = foto.Thumbnail;
        ViewerSegnapostoTransform.Rotation = foto.RotazioneMiniatura;
        ViewerSegnapostoTransform.ScaleX = foto.ScalaXMiniatura;
        ViewerSegnaposto.Visibility = Visibility.Visible;
        ViewerImageContainer.Opacity = 0;
    }

    private void NascondiSegnaposto()
    {
        ViewerSegnaposto.Visibility = Visibility.Collapsed;
        ViewerSegnaposto.Source = null;
        ViewerImageContainer.Opacity = 1;
    }
}
