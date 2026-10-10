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
