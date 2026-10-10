using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>"Informazioni su WFastPic": versione, licenza, progetto, crediti e cartella dei dati.</summary>
public partial class MainWindow
{
    private const string IndirizzoProgetto = "https://github.com/Walt7/WFastPic";

    private async void InformazioniButton_Click(object sender, RoutedEventArgs e)
    {
        var cartellaDati = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic");

        var contenuto = new StackPanel { Spacing = 12, MaxWidth = 460 };

        var testata = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        testata.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/app.png")),
            Width = 64,
            Height = 64,
        });
        var nome = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        nome.Children.Add(new TextBlock { Text = "WFastPic", FontSize = 24, FontWeight = FontWeights.SemiBold });
        nome.Children.Add(new TextBlock { Text = T("Versione {0}", VersioneApp.Replace("v. ", "")), Foreground = Risorsa("TestoSecondarioBrush") });
        testata.Children.Add(nome);
        contenuto.Children.Add(testata);

        contenuto.Children.Add(Paragrafo(T(
            "Scegliere e sistemare in fretta centinaia di foto, su Windows. Un tributo open source a " +
            "Picasa, pensato per le grandi raccolte HEIC: le foto originali non vengono mai modificate.")));

        var link = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(-12, 0, 0, 0) };
        link.Children.Add(new HyperlinkButton { Content = T("Progetto su GitHub"), NavigateUri = new Uri(IndirizzoProgetto) });
        link.Children.Add(new HyperlinkButton { Content = T("Novità di questa versione"), NavigateUri = new Uri($"{IndirizzoProgetto}/blob/master/{T("CHANGELOG.md")}") });
        link.Children.Add(new HyperlinkButton { Content = T("Privacy"), NavigateUri = new Uri($"{IndirizzoProgetto}/blob/master/{T("PRIVACY.md")}") });
        contenuto.Children.Add(link);

        contenuto.Children.Add(Titolo(T("Licenza")));
        contenuto.Children.Add(Paragrafo(T("MIT · © 2026 Walter. Libero da usare, modificare e ridistribuire.")));

        contenuto.Children.Add(Titolo(T("Grazie a")));
        contenuto.Children.Add(Paragrafo(T(
            "Mappe © contributori di OpenStreetMap · nomi dei luoghi da Nominatim · immagini satellitari Esri · " +
            "Leaflet e Leaflet.markercluster · modelli YuNet e SFace di OpenCV · ONNX Runtime · SQLite · " +
            "Windows App SDK. Ispirato a Picasa, marchio di Google LLC: WFastPic non è affiliato a Google."), secondario: true));

        contenuto.Children.Add(Titolo(T("Dati dell'app")));
        contenuto.Children.Add(Paragrafo(T(
            "Impostazioni, persone e modelli stanno qui; selezioni e modifiche di ogni cartella nella sua " +
            "cartella nascosta .wfastpic."), secondario: true));
        var apriDati = new HyperlinkButton { Content = cartellaDati, Margin = new Thickness(-12, -6, 0, 0) };
        apriDati.Click += (_, _) =>
        {
            Directory.CreateDirectory(cartellaDati);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{cartellaDati}\"") { UseShellExecute = true });
        };
        contenuto.Children.Add(apriDati);

        contenuto.Children.Add(Titolo(T("Lingua")));
        contenuto.Children.Add(CreaSelettoreLingua());

        contenuto.Children.Add(Titolo(T("Aggiornamenti")));
        var avvisa = new CheckBox
        {
            Content = T("Avvisami quando c'è una versione nuova"),
            IsChecked = _controllaAggiornamenti,
        };
        avvisa.Click += (_, _) =>
        {
            _controllaAggiornamenti = avvisa.IsChecked == true;
            if (!_controllaAggiornamenti) NuovaVersioneButton.Visibility = Visibility.Collapsed;
            SalvaImpostazioni();
        };
        contenuto.Children.Add(avvisa);
        contenuto.Children.Add(Paragrafo(T(
            "All'avvio WFastPic chiede a GitHub qual è l'ultima versione, come quando si apre la pagina nel browser: " +
            "non invia foto né dati dell'app e non scarica niente da solo. Se ce n'è una nuova compare un piccolo avviso in basso."), secondario: true));

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = T("Informazioni su WFastPic"),
            CloseButtonText = T("Chiudi"),
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = contenuto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };

        try
        {
            await dialogo.ShowAsync();
        }
        catch (Exception)
        {
            // Un altro dialogo è già aperto (es. export): niente.
        }
        Griglia.Focus(FocusState.Programmatic);
    }

    private string _preferenzaLingua = "auto";

    /// <summary>Automatica / Italiano / English. La lingua cambia al prossimo avvio: i testi sono già costruiti.</summary>
    private StackPanel CreaSelettoreLingua()
    {
        var voci = new (string Codice, string Testo)[]
        {
            ("auto", T("Automatica (lingua di Windows)")),
            ("it", "Italiano"),
            ("en", "English"),
        };

        var elenco = new ComboBox { MinWidth = 260 };
        foreach (var (_, testo) in voci) elenco.Items.Add(testo);
        elenco.SelectedIndex = Math.Max(0, Array.FindIndex(voci, v => v.Codice == _preferenzaLingua));

        var avviso = Paragrafo(T("Riavvia WFastPic per applicare la lingua."), secondario: true);
        avviso.Visibility = Visibility.Collapsed;

        elenco.SelectionChanged += (_, _) =>
        {
            var scelta = voci[Math.Max(0, elenco.SelectedIndex)].Codice;
            if (scelta == _preferenzaLingua) return;
            _preferenzaLingua = scelta;
            SalvaImpostazioni();
            avviso.Visibility = Visibility.Visible;
        };

        var pannello = new StackPanel { Spacing = 8 };
        pannello.Children.Add(elenco);
        pannello.Children.Add(avviso);
        return pannello;
    }

    private static TextBlock Titolo(string testo) =>
        new() { Text = testo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, -6) };

    private static TextBlock Paragrafo(string testo, bool secondario = false)
    {
        var blocco = new TextBlock { Text = testo, TextWrapping = TextWrapping.Wrap };
        if (secondario) blocco.Foreground = Risorsa("TestoSecondarioBrush");
        return blocco;
    }

    private static Microsoft.UI.Xaml.Media.Brush Risorsa(string chiave) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[chiave];
}
