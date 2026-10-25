using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WFastPic.Core.Library;
using WFastPic.Core.Selection;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Menu del tasto destro sulla griglia: le azioni di tastiera (apri, seleziona, scarta) più "Seleziona tutte",
/// "Inverti selezione" e, su una foto selezionata, "Salva selezione…": le foto selezionate della cartella
/// si salvano con un nome (<see cref="Selezioni"/>, lo stesso elenco che usa il server MCP) per ritrovarle
/// o prepararne un pacchetto da condividere.
/// </summary>
public partial class MainWindow
{
    private void Griglia_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var foto = (e.OriginalSource as FrameworkElement)?.DataContext as PhotoGridItem;
        if (foto is not null && !ReferenceEquals(Griglia.SelectedItem, foto)) Griglia.SelectedItem = foto;

        var menu = new MenuFlyout();
        void Voce(string testo, string glifo, Action azione, string? scorciatoia = null)
        {
            var voce = new MenuFlyoutItem
            {
                Text = testo,
                Icon = new FontIcon { Glyph = glifo },
                KeyboardAcceleratorTextOverride = scorciatoia ?? "",
            };
            voce.Click += (_, _) => azione();
            menu.Items.Add(voce);
        }

        if (foto is not null)
        {
            Voce(T("Apri"), "", () => ApriViewer(foto), "Invio");
            Voce(foto.Stato == StatoSelezione.Selected ? T("Togli la selezione") : T("Seleziona"),
                "", () => SetStato(foto, foto.Stato == StatoSelezione.Selected ? StatoSelezione.None : StatoSelezione.Selected), "Space");
            Voce(foto.Stato == StatoSelezione.Reject ? T("Rimetti tra le foto") : T("Scarta"),
                "", () => SetStato(foto, foto.Stato == StatoSelezione.Reject ? StatoSelezione.None : StatoSelezione.Reject), "Canc");
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        Voce(T("Seleziona tutte"), "", () => _ = CambiaSelezioneAsync(SelezionaTutteVisibili), "Ctrl+A");
        Voce(T("Inverti selezione"), "", () => _ = CambiaSelezioneAsync(InvertiSelezioneVisibili));

        if (foto is { Stato: StatoSelezione.Selected })
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Voce(T("Salva selezione…"), "", () => _ = SalvaSelezioneAsync());
        }

        if (foto is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Voce(T("Mostra in Esplora file"), "", () =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{foto.PercorsoCompleto}\"") { UseShellExecute = true }); }
                catch { /* Esplora file non disponibile */ }
            });
        }

        menu.ShowAt(Griglia, e.GetPosition(Griglia));
        e.Handled = true;
    }

    /// <summary>
    /// "Seleziona tutte" e "Inverti" cancellano la scelta fatta a mano: se ce n'è una, si avverte e si può
    /// prima salvarla (la selezione non ha annulla).
    /// </summary>
    private async Task CambiaSelezioneAsync(Action cambia)
    {
        if (_conteggioSelezionate > 0)
        {
            var avviso = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = T("Perdi la selezione fatta"),
                Content = T("Hai {0} selezionate: questa operazione cambia la selezione e non si può annullare. Vuoi salvarla prima?", Foto(_conteggioSelezionate)),
                PrimaryButtonText = T("Salva e continua"),
                SecondaryButtonText = T("Continua senza salvare"),
                CloseButtonText = T("Annulla"),
                DefaultButton = ContentDialogButton.Primary,
            };
            var risposta = await avviso.ShowAsync();
            if (risposta == ContentDialogResult.None) return;
            if (risposta == ContentDialogResult.Primary && !await SalvaSelezioneAsync()) return;
        }
        cambia();
    }

    private void SelezionaTutteVisibili() =>
        SetStati(_visibili.Select(i => (i, StatoSelezione.Selected)).ToList());

    /// <summary>Le selezionate diventano da decidere e viceversa; gli scarti restano scarti.</summary>
    private void InvertiSelezioneVisibili() =>
        SetStati(_visibili
            .Where(i => i.Stato != StatoSelezione.Reject)
            .Select(i => (i, i.Stato == StatoSelezione.Selected ? StatoSelezione.None : StatoSelezione.Selected))
            .ToList());

    private async Task<bool> SalvaSelezioneAsync()
    {
        if (_db is null) return false;
        // Tutte le selezionate della cartella, anche quelle nascoste dal filtro del momento.
        var percorsi = _tutte.Where(i => i.Stato == StatoSelezione.Selected).Select(i => i.PercorsoCompleto).ToList();
        if (percorsi.Count == 0) return false;

        var casella = new TextBox
        {
            PlaceholderText = T("Nome della selezione"),
            Text = ArchivioCartelle.Nome(_db.CartellaFoto),
            Width = 320,
        };
        casella.Loaded += (_, _) => { casella.Focus(FocusState.Programmatic); casella.SelectAll(); };

        var esistenti = Selezioni.Elenco().Select(s => s.Nome).ToList();
        var contenuto = new StackPanel { Spacing = 10 };
        contenuto.Children.Add(casella);
        contenuto.Children.Add(new TextBlock
        {
            Text = esistenti.Count > 0
                ? T("{0} da salvare. Selezioni già salvate: {1}.", Foto(percorsi.Count), string.Join(", ", esistenti.Take(6)))
                : T("{0} da salvare.", Foto(percorsi.Count)),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            MaxWidth = 340,
        });

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = T("Salva selezione"),
            Content = contenuto,
            PrimaryButtonText = T("Salva"),
            CloseButtonText = T("Annulla"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return false;

        var nome = casella.Text.Trim();
        if (nome.Length == 0) return false;

        var sovrascrivi = false;
        if (Selezioni.Trova(nome) is not null)
        {
            var conferma = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = T("Esiste già una selezione «{0}»", nome),
                Content = T("Vuoi sostituirla con queste {0}?", Foto(percorsi.Count)),
                PrimaryButtonText = T("Sostituisci"),
                CloseButtonText = T("Annulla"),
            };
            if (await conferma.ShowAsync() != ContentDialogResult.Primary) return false;
            sovrascrivi = true;
        }

        try
        {
            var salvata = Selezioni.Salva(nome, percorsi, sovrascrivi);
            StatusText.Text = T("Selezione «{0}» salvata: {1}", salvata.Nome, Foto(salvata.Percorsi.Count));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = T("Impossibile salvare la selezione: {0}", ex.Message);
            return false;
        }
    }
}
