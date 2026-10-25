using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using WFastPic.Core.Localizzazione;

namespace WFastPic.App;

/// <summary>
/// Traduce i testi scritti nello XAML (che resta in italiano, la lingua originale): dopo
/// <c>InitializeComponent()</c> percorre l'albero logico e sostituisce ogni testo letterale con la sua
/// traduzione, se la lingua non è l'italiano. Un testo senza traduzione resta in italiano (il test
/// <c>TraduzioniTests</c> garantisce che non ce ne siano). Non serve l'albero visuale: funziona prima
/// che la finestra venga mostrata, anche per dialoghi e menu non ancora aperti. I testi dentro i
/// <c>DataTemplate</c> non esistono ancora come elementi: lì si usa <see cref="Etichette"/>.
/// </summary>
internal static class Localizzatore
{
    public static void Applica(object? radice)
    {
        if (Testi.Corrente == Lingua.Italiano || radice is null) return;
        Visita(radice, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static string? Tr(string? testo) => string.IsNullOrEmpty(testo) ? testo : Testi.T(testo);

    private static void Visita(object? nodo, HashSet<object> visti)
    {
        if (nodo is null || !visti.Add(nodo)) return;

        if (nodo is DependencyObject oggetto)
        {
            // Proprietà allegate comuni a tutti gli elementi.
            if (ToolTipService.GetToolTip(oggetto) is string tip) ToolTipService.SetToolTip(oggetto, Tr(tip));
            if (AutomationProperties.GetName(oggetto) is { Length: > 0 } nome) AutomationProperties.SetName(oggetto, Tr(nome));
        }

        if (nodo is UIElement ui && ui.ContextFlyout is { } contesto) Visita(contesto, visti);

        switch (nodo)
        {
            case TextBlock testo:
                if (!string.IsNullOrEmpty(testo.Text)) testo.Text = Tr(testo.Text)!;
                foreach (var inline in testo.Inlines) Visita(inline, visti);
                return;

            case Run run:
                if (!string.IsNullOrEmpty(run.Text)) run.Text = Tr(run.Text)!;
                return;

            case Span span: // Hyperlink, Bold…
                foreach (var inline in span.Inlines) Visita(inline, visti);
                return;

            case MenuFlyoutSubItem sub:
                sub.Text = Tr(sub.Text)!;
                foreach (var voce in sub.Items) Visita(voce, visti);
                return;

            case MenuFlyoutItem voce:
                voce.Text = Tr(voce.Text)!;
                return;

            case MenuFlyout menu:
                foreach (var voce in menu.Items) Visita(voce, visti);
                return;

            case Flyout flyout:
                Visita(flyout.Content, visti);
                return;
        }

        // Controlli con testi propri, poi il contenuto.
        switch (nodo)
        {
            case ContentDialog dialogo:
                if (dialogo.Title is string titolo) dialogo.Title = Tr(titolo);
                dialogo.PrimaryButtonText = Tr(dialogo.PrimaryButtonText)!;
                dialogo.SecondaryButtonText = Tr(dialogo.SecondaryButtonText)!;
                dialogo.CloseButtonText = Tr(dialogo.CloseButtonText)!;
                break;
            case TextBox casella:
                casella.PlaceholderText = Tr(casella.PlaceholderText)!;
                if (casella.Header is string h1) casella.Header = Tr(h1);
                break;
            case ComboBox combo:
                combo.PlaceholderText = Tr(combo.PlaceholderText)!;
                if (combo.Header is string h2) combo.Header = Tr(h2);
                break;
            case NumberBox numero:
                if (numero.Header is string h3) numero.Header = Tr(h3);
                break;
            case Slider cursore:
                if (cursore.Header is string h4) cursore.Header = Tr(h4);
                break;
            case ToggleSwitch interruttore:
                if (interruttore.Header is string h5) interruttore.Header = Tr(h5);
                if (interruttore.OnContent is string on) interruttore.OnContent = Tr(on);
                if (interruttore.OffContent is string off) interruttore.OffContent = Tr(off);
                break;
            case InfoBar info:
                info.Title = Tr(info.Title)!;
                info.Message = Tr(info.Message)!;
                break;
            case Expander espansore:
                if (espansore.Header is string h6) espansore.Header = Tr(h6);
                else Visita(espansore.Header, visti);
                break;
            case Button pulsante:
                if (pulsante.Flyout is { } flyoutPulsante) Visita(flyoutPulsante, visti);
                break;
        }

        // Contenitori.
        switch (nodo)
        {
            case ContentControl contenuto:
                if (contenuto.Content is string testo) contenuto.Content = Tr(testo);
                else Visita(contenuto.Content, visti);
                break;
            case Panel pannello:
                foreach (var figlio in pannello.Children) Visita(figlio, visti);
                break;
            case Border bordo:
                Visita(bordo.Child, visti);
                break;
            case Viewbox vista:
                Visita(vista.Child, visti);
                break;
            case SplitView diviso:
                Visita(diviso.Pane, visti);
                Visita(diviso.Content, visti);
                break;
            case UserControl utente:
                Visita(utente.Content, visti);
                break;
        }

        if (nodo is ItemsControl elenco && elenco.ItemsSource is null)
        {
            for (var i = 0; i < elenco.Items.Count; i++)
            {
                if (elenco.Items[i] is string voce) elenco.Items[i] = Tr(voce)!;
                else Visita(elenco.Items[i], visti);
            }
        }

        // Dizionari di risorse con elementi veri (non stili né modelli).
        if (nodo is FrameworkElement fe)
        {
            foreach (var risorsa in fe.Resources.Values)
            {
                if (risorsa is ContentDialog or Flyout or MenuFlyout) Visita(risorsa, visti);
            }
        }
    }
}

/// <summary>
/// Testi usati dentro i <c>DataTemplate</c> con <c>{x:Bind local:Etichette.…}</c>: lì il traduttore XAML
/// non arriva, e un'etichetta statica va comunque letta al momento giusto (dopo la scelta della lingua).
/// </summary>
public static class Etichette
{
    public static string RicaricaCartella => Testi.T("Ricarica");
    public static string ApriInEsplora => Testi.T("Apri in Esplora file");
    public static string TogliDallElenco => Testi.T("Togli dall'elenco");
    public static string LivePhoto => Testi.T("Live Photo: video abbinato (.mov/.mp4)");
    public static string FotoConGps => Testi.T("Foto con coordinate GPS");
    public static string FotoModificata => Testi.T("Foto modificata (ritaglio, raddrizzamento, colore)");
}
