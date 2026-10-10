using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;
using WFastPic.Core.Export;
using WFastPic.Core.Library;
using WinRT.Interop;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Dialog "Esporta" stile Picasa. L'export gira in background (il dialog resta reattivo), si può
/// annullare con Chiudi/Esc, mostra quante foto esporterà prima di partire e quali sono fallite
/// alla fine; a export riuscito apre la cartella, come Picasa.
/// </summary>
public partial class MainWindow
{
    // Automatica, Massima, Normale, Minima; l'ultima voce della combo è "Personalizzata" (slider).
    private static readonly int[] QualitaPreset = [90, 100, 85, 70];
    private const int QualitaPersonalizzataIndice = 4;

    private ImpostazioniExport _impostazioniExport = new();
    private CancellationTokenSource? _exportCts;
    private bool _exportInCorso;
    private bool _aggiornandoDialogExport;
    // Gli eventi Checked/ValueChanged scattano già durante InitializeComponent, a controlli non pronti.
    private bool _dialogExportCaricato;

    private void EsportaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_db is null) return;

        CaricaDialogExport();
        _ = DialogExport.ShowAsync();
    }

    private void CaricaDialogExport()
    {
        var s = _impostazioniExport;
        _dialogExportCaricato = true;
        _aggiornandoDialogExport = true;
        try
        {
            ExportAmbitoSelezionateRadio.Content = T("Solo le foto selezionate ({0})", _conteggioSelezionate);
            ExportAmbitoTutteRadio.Content = T("Tutte tranne gli scarti ({0})", FotoNonScartate());
            // Nessuna selezionata: si propone direttamente "tutte", invece di un export vuoto.
            ExportAmbitoSelezionateRadio.IsChecked = _conteggioSelezionate > 0;
            ExportAmbitoTutteRadio.IsChecked = _conteggioSelezionate == 0;

            ExportPosizioneTextBox.Text = s.Posizione ?? _db!.CartellaFoto;
            ExportNomeCartellaTextBox.Text = s.NomeCartella;

            ExportOriginaleRadio.IsChecked = s.DimensioniOriginali;
            ExportRidimensionaRadio.IsChecked = !s.DimensioniOriginali;
            var lato = Math.Clamp(s.LatoLungo, ExportOptions.LatoLungoMinimo, ExportOptions.LatoLungoMassimoConsentito);
            ExportLatoNumberBox.Value = lato;
            ExportLatoSlider.Value = Math.Clamp(lato, ExportLatoSlider.Minimum, ExportLatoSlider.Maximum);

            ExportQualitaCombo.SelectedIndex = Math.Clamp(s.QualitaPreset, 0, QualitaPersonalizzataIndice);
            ExportQualitaSlider.Value = Math.Clamp(s.QualitaPersonalizzata, 40, 100);

            ExportFiligranaCheck.IsChecked = s.Filigrana;
            ExportWatermarkTextBox.Text = s.TestoFiligrana;
            ExportOpacitaSlider.Value = Math.Clamp(s.OpacitaFiligrana, 10, 100);
            ExportPosizioneCombo.SelectedIndex = Math.Clamp(s.PosizioneFiligrana, 0, 4);

            ExportRimuoviGpsCheck.IsChecked = s.RimuoviGps;
            ExportSostituisciCheck.IsChecked = s.SostituisciEsistenti;

            ExportProgressBar.Visibility = Visibility.Collapsed;
            ExportProgressBar.Value = 0;
            ExportStatusText.Text = "";
            ExportErroriList.ItemsSource = null;
            ExportErroriList.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _aggiornandoDialogExport = false;
        }

        AggiornaStatoDialogExport();
    }

    private int FotoNonScartate() => _tutte.Count(f => !f.IsReject);

    private int FotoDaEsportareNelDialog() =>
        ExportAmbitoSelezionateRadio.IsChecked == true ? _conteggioSelezionate : FotoNonScartate();

    /// <summary>Abilitazioni, anteprima della destinazione e "Esporta N foto" sempre coerenti con le scelte.</summary>
    private void AggiornaStatoDialogExport()
    {
        if (_aggiornandoDialogExport || !_dialogExportCaricato) return;

        var modificabile = !_exportInCorso;
        var ridimensiona = ExportRidimensionaRadio.IsChecked == true;
        ExportLatoSlider.IsEnabled = modificabile && ridimensiona;
        ExportLatoNumberBox.IsEnabled = modificabile && ridimensiona;

        var personalizzata = ExportQualitaCombo.SelectedIndex == QualitaPersonalizzataIndice;
        ExportQualitaSlider.IsEnabled = modificabile && personalizzata;
        ExportQualitaText.Text = $"{QualitaScelta()}%";

        var filigrana = ExportFiligranaCheck.IsChecked == true;
        ExportWatermarkTextBox.IsEnabled = modificabile && filigrana;
        ExportOpacitaSlider.IsEnabled = modificabile && filigrana;
        ExportPosizioneCombo.IsEnabled = modificabile && filigrana;

        var cartella = CartellaOutputDialog(out var errore);
        ExportDestinazioneText.Text = cartella is not null ? T("Le foto verranno salvate in {0}", cartella) : errore;

        var numero = FotoDaEsportareNelDialog();
        DialogExport.PrimaryButtonText = T("Esporta {0}", Foto(numero));
        DialogExport.IsPrimaryButtonEnabled = modificabile && numero > 0 && cartella is not null;
        DialogExport.CloseButtonText = _exportInCorso ? T("Annulla esportazione") : T("Chiudi");
    }

    private int QualitaScelta() => ExportQualitaCombo.SelectedIndex is >= 0 and < QualitaPersonalizzataIndice
        ? QualitaPreset[ExportQualitaCombo.SelectedIndex]
        : (int)ExportQualitaSlider.Value;

    private string? CartellaOutputDialog(out string? errore)
    {
        var posizione = ExportPosizioneTextBox.Text.Trim().Trim('"');
        var nome = ExportNomeCartellaTextBox.Text.Trim();

        if (!Path.IsPathFullyQualified(posizione))
        {
            errore = T("Indica un percorso completo, ad esempio D:\\Foto.");
            return null;
        }
        if (nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errore = T("Il nome della cartella contiene caratteri non validi.");
            return null;
        }

        errore = null;
        return nome.Length == 0 ? posizione : Path.Combine(posizione, nome);
    }

    private void ExportOpzione_Changed(object sender, RoutedEventArgs e) => AggiornaStatoDialogExport();

    private void ExportPercorso_TextChanged(object sender, TextChangedEventArgs e) => AggiornaStatoDialogExport();

    private void ExportLatoSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_aggiornandoDialogExport || !_dialogExportCaricato) return;
        _aggiornandoDialogExport = true;
        ExportLatoNumberBox.Value = Math.Round(e.NewValue);
        _aggiornandoDialogExport = false;
    }

    // Il valore esatto si scrive nel NumberBox (anche oltre la corsa dello slider, es. 8000 px).
    private void ExportLatoNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_aggiornandoDialogExport || !_dialogExportCaricato) return;
        _aggiornandoDialogExport = true;
        if (double.IsNaN(e.NewValue)) sender.Value = Math.Round(ExportLatoSlider.Value);
        else ExportLatoSlider.Value = Math.Clamp(e.NewValue, ExportLatoSlider.Minimum, ExportLatoSlider.Maximum);
        _aggiornandoDialogExport = false;
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
            ExportPosizioneTextBox.Text = cartella.Path;
        }
    }

    private async void DialogExport_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Il dialog resta aperto per mostrare avanzamento ed esito; si chiude con "Chiudi".
        args.Cancel = true;
        if (_db is null || _exportInCorso) return;

        var cartella = CartellaOutputDialog(out var errore);
        if (cartella is null)
        {
            ExportStatusText.Text = errore ?? "";
            return;
        }

        var opzioni = LeggiOpzioniDalDialog(cartella);
        SalvaImpostazioniExport();

        var db = _db;
        var cts = new CancellationTokenSource();
        _exportCts = cts;
        _exportInCorso = true;
        AggiornaStatoDialogExport();

        ExportErroriList.ItemsSource = null;
        ExportErroriList.Visibility = Visibility.Collapsed;
        ExportProgressBar.Visibility = Visibility.Visible;
        ExportProgressBar.Value = 0;
        ExportStatusText.Text = T("Preparazione…");
        var cronometro = Stopwatch.StartNew();

        try
        {
            // L'export legge ricette e selezioni dal DB: prima vanno scritte quelle ancora in coda.
            if (_selectionManager is not null) await _selectionManager.SvuotaAsync();

            var progresso = new Progress<ProgressoExport>(p =>
            {
                if (p.Totale == 0) return;
                ExportProgressBar.Value = 100.0 * p.Completate / p.Totale;
                if (cts.IsCancellationRequested) return;
                // Tempo previsto: velocità media dall'inizio (le foto con filigrana o ridimensionamento pesano uguale).
                var rimanente = StimaAvanzamento.TempoRimanente(p.Completate, p.Totale, cronometro.Elapsed);
                var stima = rimanente is { } r ? " · " + T("circa {0} rimanenti", StimaAvanzamento.Durata(r)) : "";
                ExportStatusText.Text = T("{0} di {1}", Math.Min(p.Completate + 1, p.Totale), p.Totale) + stima + " · " + p.FileCorrente;
            });

            var risultati = await Task.Run(() => Exporter.EseguiAsync(db, opzioni, progresso, cts.Token));
            MostraEsitoExport(risultati, opzioni.CartellaOutput, cts.IsCancellationRequested, cronometro.Elapsed);
        }
        catch (Exception ex)
        {
            ExportStatusText.Text = T("Errore: {0}", ex.Message);
        }
        finally
        {
            _exportInCorso = false;
            _exportCts = null;
            cts.Dispose();
            AggiornaStatoDialogExport();
        }
    }

    private void MostraEsitoExport(IReadOnlyList<RisultatoExport> risultati, string cartella, bool annullato, TimeSpan durata)
    {
        var riuscite = risultati.Count(r => r.Successo);
        var fallite = risultati.Where(r => !r.Successo).ToList();

        ExportStatusText.Text = (annullato, fallite.Count) switch
        {
            (true, _) => T("Esportazione annullata: {0} esportate in {1}", Foto(riuscite), cartella),
            (false, 0) => T("Completato: {0} esportate in {1} (in {2})", Foto(riuscite), cartella, StimaAvanzamento.Durata(durata)),
            _ => T("{0} esportate, {1} non riuscite:", Foto(riuscite), fallite.Count),
        };

        if (fallite.Count > 0)
        {
            ExportErroriList.ItemsSource = fallite.Select(r => $"{r.PathRelativo} — {r.Errore}").ToList();
            ExportErroriList.Visibility = Visibility.Visible;
        }

        // Come Picasa: a export finito si apre la cartella con le foto pronte da condividere.
        if (!annullato && riuscite > 0 && Directory.Exists(cartella))
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{cartella}\"") { UseShellExecute = true }); }
            catch { /* Explorer non disponibile: l'esito resta comunque nel dialog */ }
        }
    }

    // Chiudi o Esc durante l'export = annulla. Il dialog resta aperto finché la foto in corso è
    // finita, così l'esito (quante esportate) si vede e non resta un export orfano in background.
    private void DialogExport_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!_exportInCorso)
        {
            SalvaImpostazioniExport();
            return;
        }

        args.Cancel = true;
        _exportCts?.Cancel();
        ExportStatusText.Text = T("Annullamento… si completa la foto in corso");
    }

    private ExportOptions LeggiOpzioniDalDialog(string cartellaOutput)
    {
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

        var filigrana = ExportFiligranaCheck.IsChecked == true && !string.IsNullOrWhiteSpace(ExportWatermarkTextBox.Text);

        return new ExportOptions
        {
            CartellaOutput = cartellaOutput,
            LatoLungoMassimo = ExportOriginaleRadio.IsChecked == true ? null : LatoLungoScelto(),
            QualitaJpeg = QualitaScelta(),
            TestoWatermark = filigrana ? ExportWatermarkTextBox.Text.Trim() : null,
            OpacitaWatermark = ExportOpacitaSlider.Value / 100.0,
            PosizioneWatermark = posizioneWatermark,
            Tag = tag,
            SoloSelezionate = ExportAmbitoSelezionateRadio.IsChecked == true,
            RimuoviGps = ExportRimuoviGpsCheck.IsChecked == true,
            SostituisciEsistenti = ExportSostituisciCheck.IsChecked == true,
        };
    }

    private int LatoLungoScelto()
    {
        var valore = double.IsNaN(ExportLatoNumberBox.Value) ? ExportLatoSlider.Value : ExportLatoNumberBox.Value;
        return (int)Math.Clamp(Math.Round(valore), ExportOptions.LatoLungoMinimo, ExportOptions.LatoLungoMassimoConsentito);
    }

    private void SalvaImpostazioniExport()
    {
        var posizione = ExportPosizioneTextBox.Text.Trim().Trim('"');
        var predefinita = _db is null || posizione.Length == 0
            || (Path.IsPathFullyQualified(posizione) && NomiFileExport.StessaCartella(posizione, _db.CartellaFoto));

        _impostazioniExport = new ImpostazioniExport
        {
            Posizione = predefinita || !Path.IsPathFullyQualified(posizione) ? null : posizione,
            NomeCartella = ExportNomeCartellaTextBox.Text.Trim(),
            DimensioniOriginali = ExportOriginaleRadio.IsChecked == true,
            LatoLungo = LatoLungoScelto(),
            QualitaPreset = Math.Max(0, ExportQualitaCombo.SelectedIndex),
            QualitaPersonalizzata = (int)ExportQualitaSlider.Value,
            Filigrana = ExportFiligranaCheck.IsChecked == true,
            TestoFiligrana = ExportWatermarkTextBox.Text,
            OpacitaFiligrana = ExportOpacitaSlider.Value,
            PosizioneFiligrana = Math.Max(0, ExportPosizioneCombo.SelectedIndex),
            RimuoviGps = ExportRimuoviGpsCheck.IsChecked == true,
            SostituisciEsistenti = ExportSostituisciCheck.IsChecked == true,
        };
    }
}
