using System.Globalization;
using Microsoft.UI.Xaml;
using WFastPic.Core.Localizzazione;

namespace WFastPic.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WFASTPIC_LANG (it/en) forza la lingua per prove e segnalazioni, senza toccare le impostazioni.
        var preferenza = Environment.GetEnvironmentVariable("WFASTPIC_LANG") ?? ImpostazioniStore.Carica().Lingua;
        Testi.Imposta(Testi.Risolvi(preferenza, CultureInfo.CurrentUICulture));
        _window = new MainWindow();
        ErrorReporter.Installa(this, _window);
        _window.Activate();
    }
}
