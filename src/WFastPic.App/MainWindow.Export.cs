using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WFastPic.Core.Export;
using WinRT.Interop;

namespace WFastPic.App;

public partial class MainWindow
{
    private void EsportaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_db is null) return;

        ExportCartellaTextBox.Text = ExportOptions.CartellaOutputDefault(_db.CartellaFoto);
        ExportProgressBar.Visibility = Visibility.Collapsed;
        ExportProgressBar.Value = 0;
        ExportStatusText.Text = "";

        _ = DialogExport.ShowAsync();
    }

    private async void ExportScegliCartellaButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var cartella = await picker.PickSingleFolderAsync();
        if (cartella is not null)
        {
            ExportCartellaTextBox.Text = cartella.Path;
        }
    }

    private async void DialogExport_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_db is null) return;

        args.Cancel = true; // resta aperto per mostrare avanzamento; l'utente chiude con "Chiudi" a fine export
        var deferral = args.GetDeferral();
        try
        {
            var opzioni = LeggiOpzioniDalDialog();

            ExportProgressBar.Visibility = Visibility.Visible;
            ExportProgressBar.Value = 0;
            ExportStatusText.Text = "Esportazione in corso…";

            var progresso = new Progress<ProgressoExport>(p =>
            {
                if (p.Totale == 0) return;
                ExportProgressBar.Value = 100.0 * p.Completate / p.Totale;
                ExportStatusText.Text = $"{p.Completate}/{p.Totale} · {p.FileCorrente}";
            });

            // L'export legge ricette e selezioni dal DB: prima vanno scritte quelle ancora in coda.
            if (_selectionManager is not null) await _selectionManager.SvuotaAsync();
            if (_db is null) return;

            var risultati = await Exporter.EseguiAsync(_db, opzioni, progresso);

            var errori = risultati.Count(r => !r.Successo);
            ExportStatusText.Text = errori == 0
                ? $"Completato: {risultati.Count} foto esportate in {opzioni.CartellaOutput}"
                : $"Completato con {errori} errori su {risultati.Count} foto — vedi {opzioni.CartellaOutput}";
        }
        catch (Exception ex)
        {
            ExportStatusText.Text = $"Errore: {ex.Message}";
        }
        finally
        {
            deferral.Complete();
        }
    }

    private ExportOptions LeggiOpzioniDalDialog()
    {
        var cartellaOutput = string.IsNullOrWhiteSpace(ExportCartellaTextBox.Text)
            ? ExportOptions.CartellaOutputDefault(_db!.CartellaFoto)
            : ExportCartellaTextBox.Text.Trim();

        var latoLungo = ExportLatoLungoCombo.SelectedIndex switch
        {
            0 => PresetLatoLungo.Px1600,
            1 => PresetLatoLungo.Px2048,
            2 => PresetLatoLungo.Px3200,
            _ => PresetLatoLungo.Originale,
        };

        var posizioneWatermark = ExportPosizioneCombo.SelectedIndex switch
        {
            0 => PosizioneWatermark.AltoSinistra,
            1 => PosizioneWatermark.AltoDestra,
            2 => PosizioneWatermark.BassoSinistra,
            4 => PosizioneWatermark.Centro,
            _ => PosizioneWatermark.BassoDestra,
        };

        var tag = ExportTagTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new ExportOptions
        {
            CartellaOutput = cartellaOutput,
            LatoLungo = latoLungo,
            QualitaJpeg = (int)ExportQualitaSlider.Value,
            TestoWatermark = string.IsNullOrWhiteSpace(ExportWatermarkTextBox.Text) ? null : ExportWatermarkTextBox.Text.Trim(),
            OpacitaWatermark = ExportOpacitaSlider.Value / 100.0,
            PosizioneWatermark = posizioneWatermark,
            Tag = tag,
            SoloSelezionate = ExportSoloSelezionateCheck.IsChecked == true,
        };
    }
}
