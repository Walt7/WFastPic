using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.UI.Xaml;
using WFastPic.Core.Library;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Avviso di versione nuova, in piccolo nella barra in basso: qualche secondo dopo l'avvio si chiede a
/// GitHub qual è l'ultima release (nessun dato dell'utente, è la stessa pagina pubblica del sito). Niente
/// si scarica da solo: il link apre la pagina della release. Si spegne dalle Informazioni.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan RitardoControllo = TimeSpan.FromSeconds(5);

    private bool _controllaAggiornamenti = true;
    private string? _indirizzoNuovaVersione;

    private async void AvviaControlloAggiornamenti()
    {
        if (!_controllaAggiornamenti) return;
        try
        {
            // Dopo l'avvio: la prima cartella e le miniature hanno la precedenza.
            await Task.Delay(RitardoControllo);
            var inUso = typeof(MainWindow).Assembly.GetName().Version;
            if (inUso is null || !_controllaAggiornamenti) return;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WFastPic", $"{inUso.Major}.{inUso.Minor}.{Math.Max(0, inUso.Build)}"));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var json = await http.GetStringAsync(ControlloVersione.IndirizzoUltimaRelease);

            if (ControlloVersione.Confronta(json, inUso) is not { } nuova || !_controllaAggiornamenti) return;
            _indirizzoNuovaVersione = nuova.Indirizzo;
            NuovaVersioneText.Text = T("Nuova versione {0} disponibile", nuova.Versione.ToString(3));
            NuovaVersioneButton.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // Offline, GitHub non raggiungibile o limite di richieste: nessun avviso, si riprova al prossimo avvio.
        }
    }

    private void NuovaVersioneButton_Click(object sender, RoutedEventArgs e)
    {
        if (_indirizzoNuovaVersione is null) return;
        Process.Start(new ProcessStartInfo(_indirizzoNuovaVersione) { UseShellExecute = true });
    }
}
