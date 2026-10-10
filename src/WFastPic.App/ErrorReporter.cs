using System.Diagnostics;
using System.Text;
using WFastPic.Core.Localizzazione;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace WFastPic.App;

/// <summary>
/// Gestione centralizzata degli errori inattesi: ogni eccezione non gestita viene scritta in un report
/// testuale in LocalAppData (mai persa), poi l'utente la vede in un dialogo non bloccante per il flusso
/// (un solo dialogo alla volta) con "Copia" e "Apri cartella report". Nessun invio in rete.
/// </summary>
public static class ErrorReporter
{
    private const int MassimoReportConservati = 30;

    private static readonly string CartellaReport = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WFastPic", "reports");

    private static readonly object _lock = new();
    private static Window? _finestra;
    private static bool _dialogoAperto;
    private static int _soppressiDuranteDialogo;

    public static void Installa(Application app, Window finestra)
    {
        _finestra = finestra;
        app.UnhandledException += (_, e) =>
        {
            e.Handled = true; // l'app resta viva: l'utente non perde la selezione in corso
            Segnala(e.Exception, "UI (UnhandledException)");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Segnala(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()),
                "AppDomain" + (e.IsTerminating ? " (fatale)" : ""));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Segnala(e.Exception, "Task non osservato");
        };
    }

    /// <summary>Scrive il report su disco e mostra il dialogo. Sicuro da qualunque thread; non lancia mai.</summary>
    public static void Segnala(Exception? eccezione, string origine)
    {
        try
        {
            var percorso = ScriviReport(eccezione, origine, out var testo);
            var finestra = _finestra;
            var coda = finestra?.DispatcherQueue;
            if (coda is null) return;
            coda.TryEnqueue(() => _ = MostraDialogoAsync(finestra!, eccezione, testo, percorso));
        }
        catch
        {
            // Il gestore di errori non deve mai produrre un errore a sua volta.
        }
    }

    private static string ScriviReport(Exception? eccezione, string origine, out string testo)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Testi.T("WFastPic — report errore"));
        sb.AppendLine($"{Testi.T("Data:"),-11}{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{Testi.T("Origine:"),-11}{origine}");
        sb.AppendLine($"{Testi.T("Versione:"),-11}{typeof(ErrorReporter).Assembly.GetName().Version}");
        sb.AppendLine($"OS:        {Environment.OSVersion}");
        sb.AppendLine($"Runtime:   {Environment.Version}");
        sb.AppendLine($"{Testi.T("Memoria:"),-11}{Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)} MB");
        sb.AppendLine();
        sb.AppendLine(eccezione?.ToString() ?? Testi.T("(eccezione nulla)"));
        testo = sb.ToString();

        lock (_lock)
        {
            Directory.CreateDirectory(CartellaReport);
            var percorso = Path.Combine(CartellaReport, $"report-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(percorso, testo);
            PotaVecchi();
            return percorso;
        }
    }

    private static void PotaVecchi()
    {
        try
        {
            foreach (var vecchio in new DirectoryInfo(CartellaReport).GetFiles("report-*.txt")
                         .OrderByDescending(f => f.CreationTimeUtc).Skip(MassimoReportConservati))
                vecchio.Delete();
        }
        catch
        {
            // Pulizia best-effort.
        }
    }

    private static async Task MostraDialogoAsync(Window finestra, Exception? eccezione, string testo, string percorso)
    {
        // Una raffica di errori (es. loop di rendering) non deve impilare dialoghi: i report finiscono comunque su disco.
        if (_dialogoAperto)
        {
            _soppressiDuranteDialogo++;
            return;
        }
        if (finestra.Content?.XamlRoot is not { } xamlRoot) return;

        _dialogoAperto = true;
        try
        {
            var dettagli = new TextBox
            {
                Text = testo,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 11,
                Height = 220,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(dettagli, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(dettagli, ScrollBarVisibility.Auto);

            var contenuto = new StackPanel { Spacing = 8, Width = 560 };
            contenuto.Children.Add(new TextBlock
            {
                Text = $"{eccezione?.GetType().Name}: {eccezione?.Message}",
                TextWrapping = TextWrapping.Wrap,
            });
            contenuto.Children.Add(new TextBlock
            {
                Text = Testi.T("Le foto originali non sono state toccate. Il report è stato salvato; puoi continuare a lavorare."),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
            contenuto.Children.Add(dettagli);

            var dialogo = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = Testi.T("Si è verificato un errore inatteso"),
                Content = contenuto,
                PrimaryButtonText = Testi.T("Copia report"),
                SecondaryButtonText = Testi.T("Apri cartella report"),
                CloseButtonText = Testi.T("Chiudi"),
                DefaultButton = ContentDialogButton.Close,
            };

            while (true)
            {
                var risultato = await dialogo.ShowAsync();
                if (risultato == ContentDialogResult.Primary)
                {
                    var pacchetto = new DataPackage();
                    pacchetto.SetText(testo);
                    Clipboard.SetContent(pacchetto);
                    Clipboard.Flush();
                }
                else if (risultato == ContentDialogResult.Secondary)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{percorso}\"") { UseShellExecute = true });
                }
                else break;
                // Copia/Apri non chiudono il flusso: riproponi il dialogo finché l'utente non preme Chiudi.
            }
        }
        catch
        {
            // Dialogo non mostrabile (finestra in chiusura, altro ContentDialog aperto): resta il report su disco.
        }
        finally
        {
            _dialogoAperto = false;
            _soppressiDuranteDialogo = 0;
        }
    }
}
