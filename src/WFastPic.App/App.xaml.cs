using Microsoft.UI.Xaml;

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
        _window = new MainWindow();
        ErrorReporter.Installa(this, _window);
        _window.Activate();
    }
}
