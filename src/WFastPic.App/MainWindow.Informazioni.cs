using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

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
        nome.Children.Add(new TextBlock { Text = $"Versione {VersioneApp.Replace("v. ", "")}", Foreground = Risorsa("TestoSecondarioBrush") });
        testata.Children.Add(nome);
        contenuto.Children.Add(testata);

        contenuto.Children.Add(Paragrafo(
            "Scegliere e sistemare in fretta centinaia di foto, su Windows. Il successore open source di " +
            "Picasa, pensato per le grandi raccolte HEIC: le foto originali non vengono mai modificate."));

        var link = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(-12, 0, 0, 0) };
        link.Children.Add(new HyperlinkButton { Content = "Progetto su GitHub", NavigateUri = new Uri(IndirizzoProgetto) });
        link.Children.Add(new HyperlinkButton { Content = "Novità di questa versione", NavigateUri = new Uri($"{IndirizzoProgetto}/blob/master/CHANGELOG.md") });
        contenuto.Children.Add(link);

        contenuto.Children.Add(Titolo("Licenza"));
        contenuto.Children.Add(Paragrafo("MIT · © 2026 Walter. Libero da usare, modificare e ridistribuire."));

        contenuto.Children.Add(Titolo("Grazie a"));
        contenuto.Children.Add(Paragrafo(
            "Mappe © contributori di OpenStreetMap · nomi dei luoghi da Nominatim · immagini satellitari Esri · " +
            "Leaflet e Leaflet.markercluster · modelli YuNet e SFace di OpenCV · ONNX Runtime · SQLite · " +
            "Windows App SDK. Ispirato a Picasa.", secondario: true));

        contenuto.Children.Add(Titolo("Dati dell'app"));
        contenuto.Children.Add(Paragrafo(
            "Impostazioni, persone e modelli stanno qui; selezioni e modifiche di ogni cartella nella sua " +
            "cartella nascosta .wfastpic.", secondario: true));
        var apriDati = new HyperlinkButton { Content = cartellaDati, Margin = new Thickness(-12, -6, 0, 0) };
        apriDati.Click += (_, _) =>
        {
            Directory.CreateDirectory(cartellaDati);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{cartellaDati}\"") { UseShellExecute = true });
        };
        contenuto.Children.Add(apriDati);

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Informazioni su WFastPic",
            CloseButtonText = "Chiudi",
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
