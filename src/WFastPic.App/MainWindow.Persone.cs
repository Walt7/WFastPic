using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;

namespace WFastPic.App;

/// <summary>Una persona (o un gruppo di volti ancora senza nome) nel pannello Persone.</summary>
public sealed class PersonaUi
{
    public required string Nome { get; init; }
    public required string Dettaglio { get; init; }
    public ImageSource? Avatar { get; set; }
    public long? PersonaId { get; init; }
    public bool SenzaNome => PersonaId is null;

    public required IReadOnlyList<long> Volti { get; init; }
    public required IReadOnlyList<long> Proposte { get; init; }
    public required HashSet<long> Foto { get; init; }
    public required IReadOnlyList<float[]> Impronte { get; init; }
    public byte[]? AvatarJpeg { get; init; }
}

/// <summary>
/// Persone stile Picasa nel pannello di sinistra: chi compare nella cartella aperta (con nome) e i
/// gruppi di volti simili ancora senza nome. Clic = filtra la griglia su quella persona; tasto destro
/// = dai un nome / conferma le proposte / rinomina / togli il nome / ignora. Dare un nome aggiunge i
/// volti come campioni della persona (archivio globale) e ripropone subito i volti somiglianti.
/// </summary>
public partial class MainWindow
{
    private ArchivioPersone? _archivioPersone;
    private (string Nome, HashSet<long> Foto)? _filtroPersona;
    private int _richiestaPersone;

    private ArchivioPersone Archivio => _archivioPersone ??= ArchivioPersone.Apri(ArchivioPersone.PercorsoPredefinito);

    private void ChiudiArchivioPersone()
    {
        _archivioPersone?.Dispose();
        _archivioPersone = null;
    }

    private List<PersonaNota> PersoneNote() =>
        Archivio.Elenco()
            .Select(p => new PersonaNota(p.Id, p.Nome, p.Campioni.Select(Impronte.DaByte).ToList()))
            .ToList();

    private void RiconoscimentoPersoneSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        var attivo = RiconoscimentoPersoneSwitch.IsOn;
        if (_riconoscimentoPersone == attivo) return;
        _riconoscimentoPersone = attivo;
        SalvaImpostazioni();

        // Riparte la ricerca col livello scelto (se le miniature sono già pronte).
        FermaRicercaVolti();
        if (_miniatureTotali > 0 && _miniatureCompletate >= _miniatureTotali) AvviaRicercaVolti();
        AggiornaPannelloPersone();
    }

    /// <summary>Ricostruisce l'elenco dal sidecar (dopo aver scritto quanto in coda) e dall'archivio persone.</summary>
    private async void AggiornaPannelloPersone()
    {
        var richiesta = ++_richiestaPersone;
        RiconoscimentoPersoneSwitch.IsOn = _riconoscimentoPersone == true;

        var db = _db;
        if (db is null || _riconoscimentoPersone != true)
        {
            PersoneList.ItemsSource = null;
            PersoneStatoText.Text = _riconoscimentoPersone == true ? "" : "Attiva il riconoscimento per raggruppare i volti per persona.";
            return;
        }

        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();

        List<(PersonaUi Persona, byte[]? Avatar)> elenco;
        try
        {
            var archivio = Archivio;
            elenco = await Task.Run(() => CostruisciElencoPersone(db, archivio));
        }
        catch (ObjectDisposedException)
        {
            return; // cartella cambiata nel frattempo
        }
        if (richiesta != _richiestaPersone) return;

        foreach (var (persona, avatar) in elenco)
        {
            if (avatar is null) continue;
            var immagine = new BitmapImage();
            using var stream = new MemoryStream(avatar).AsRandomAccessStream();
            await immagine.SetSourceAsync(stream);
            persona.Avatar = immagine;
        }
        if (richiesta != _richiestaPersone) return;

        PersoneList.ItemsSource = elenco.Select(e => e.Persona).ToList();
        var analizzate = _tutte.Count(f => f.PersoneAnalizzate);
        PersoneStatoText.Text = elenco.Count > 0
            ? (analizzate < _tutte.Count ? $"Analisi in corso: {analizzate} di {_tutte.Count} foto" : "")
            : analizzate < _tutte.Count ? "Analisi delle foto in corso…" : "Nessun gruppo di volti trovato in questa cartella.";
    }

    /// <summary>
    /// Persone con nome presenti nella cartella (proposte comprese) + gruppi senza nome da almeno 2
    /// volti (un volto isolato si confonderebbe con mille passanti: comparirà quando ne arriva un altro).
    /// </summary>
    private static List<(PersonaUi, byte[]?)> CostruisciElencoPersone(LibrarySidecarDatabase db, ArchivioPersone archivio)
    {
        var volti = db.GetVoltiPersone().Where(v => v.Impronta is not null).ToList();
        var persone = archivio.Elenco().ToDictionary(p => p.Id);
        var risultato = new List<(PersonaUi, byte[]?)>();

        foreach (var gruppo in volti
                     .Where(v => v.PersonaId is long id && persone.ContainsKey(id) && v.Stato is StatoPersona.Proposto or StatoPersona.Confermato)
                     .GroupBy(v => v.PersonaId!.Value)
                     .OrderByDescending(g => g.Select(v => v.PhotoId).Distinct().Count()))
        {
            var persona = persone[gruppo.Key];
            var foto = gruppo.Select(v => v.PhotoId).ToHashSet();
            var proposte = gruppo.Where(v => v.Stato == StatoPersona.Proposto).Select(v => v.Id).ToList();
            risultato.Add((new PersonaUi
            {
                Nome = persona.Nome,
                Dettaglio = proposte.Count > 0 ? $"{foto.Count} foto · {proposte.Count} da confermare" : $"{foto.Count} foto",
                PersonaId = persona.Id,
                Volti = gruppo.Select(v => v.Id).ToList(),
                Proposte = proposte,
                Foto = foto,
                Impronte = gruppo.Select(v => Impronte.DaByte(v.Impronta!)).ToList(),
            }, persona.Avatar ?? gruppo.First().Miniatura));
        }

        var senzaNome = volti.Where(v => v.PersonaId is null && v.Stato != StatoPersona.Rifiutato).ToList();
        var etichette = RiconoscimentoPersone.Raggruppa(senzaNome.Select(v => Impronte.DaByte(v.Impronta!)).ToList());
        foreach (var gruppo in senzaNome.Select((v, i) => (Volto: v, Gruppo: etichette[i]))
                     .GroupBy(x => x.Gruppo)
                     .Select(g => g.Select(x => x.Volto).ToList())
                     .Where(g => g.Count >= 2)
                     .OrderByDescending(g => g.Count))
        {
            var foto = gruppo.Select(v => v.PhotoId).ToHashSet();
            risultato.Add((new PersonaUi
            {
                Nome = "Senza nome",
                Dettaglio = $"{foto.Count} foto",
                PersonaId = null,
                Volti = gruppo.Select(v => v.Id).ToList(),
                Proposte = [],
                Foto = foto,
                Impronte = gruppo.Select(v => Impronte.DaByte(v.Impronta!)).ToList(),
                AvatarJpeg = gruppo.First().Miniatura,
            }, gruppo.First().Miniatura));
        }

        return risultato;
    }

    // ---------------------------------------------------------------- filtro griglia

    private void PersoneList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PersonaUi persona) return;
        _filtroPersona = (persona.SenzaNome ? "Senza nome" : persona.Nome, persona.Foto);
        FiltroPersonaText.Text = _filtroPersona.Value.Nome;
        FiltroPersonaButton.Visibility = Visibility.Visible;
        ApplicaFiltro();
        Griglia.Focus(FocusState.Programmatic);
    }

    private void FiltroPersonaButton_Click(object sender, RoutedEventArgs e)
    {
        _filtroPersona = null;
        FiltroPersonaButton.Visibility = Visibility.Collapsed;
        ApplicaFiltro();
    }

    private bool CorrispondeAllaPersona(PhotoGridItem foto) => _filtroPersona is not { } f || f.Foto.Contains(foto.Id);

    // ---------------------------------------------------------------- menu: nome, conferma, ignora

    private void PersoneList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not PersonaUi persona) return;

        var menu = new MenuFlyout();
        void Voce(string testo, string glifo, Action azione)
        {
            var voce = new MenuFlyoutItem { Text = testo, Icon = new FontIcon { Glyph = glifo } };
            voce.Click += (_, _) => azione();
            menu.Items.Add(voce);
        }

        if (persona.SenzaNome)
        {
            Voce("Dai un nome…", "", () => _ = DaiNomeAsync(persona, null));
            Voce("Ignora questi volti", "", () => _ = AssegnaEAggiornaAsync(persona.Volti, null, StatoPersona.Rifiutato));
        }
        else
        {
            if (persona.Proposte.Count > 0)
                Voce($"Conferma le proposte ({persona.Proposte.Count})", "", () => _ = ConfermaProposteAsync(persona));
            Voce("Rinomina…", "", () => _ = DaiNomeAsync(persona, persona.Nome));
            Voce("Togli il nome da questi volti", "", () => _ = AssegnaEAggiornaAsync(persona.Volti, null, StatoPersona.Nessuno));
        }

        menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
        e.Handled = true;
    }

    private async Task DaiNomeAsync(PersonaUi persona, string? nomeAttuale)
    {
        var nomiNoti = Archivio.Elenco().Select(p => p.Nome).ToList();
        var casella = new AutoSuggestBox { PlaceholderText = "Nome della persona", Text = nomeAttuale ?? "", Width = 300 };
        casella.TextChanged += (s, a) =>
        {
            if (a.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            s.ItemsSource = nomiNoti.Where(n => n.Contains(s.Text, StringComparison.CurrentCultureIgnoreCase)).Take(8).ToList();
        };

        var contenuto = new StackPanel { Spacing = 12 };
        if (persona.Avatar is not null)
            contenuto.Children.Add(new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(48), Child = new Image { Source = persona.Avatar, Stretch = Stretch.UniformToFill } });
        contenuto.Children.Add(casella);
        contenuto.Children.Add(new TextBlock
        {
            Text = nomeAttuale is null
                ? $"I {persona.Volti.Count} volti del gruppo prendono questo nome; WFastPic lo proporrà anche sui volti somiglianti, in tutte le cartelle."
                : "Rinomina la persona in tutte le cartelle.",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray), MaxWidth = 320,
        });

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = nomeAttuale is null ? "Chi è?" : "Rinomina",
            Content = contenuto,
            PrimaryButtonText = "Salva",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        var nome = casella.Text.Trim();
        if (nome.Length == 0) return;

        if (persona.PersonaId is long id)
        {
            try { Archivio.Rinomina(id, nome); }
            catch (Microsoft.Data.Sqlite.SqliteException) { StatusText.Text = $"Esiste già una persona chiamata «{nome}»."; return; }
            AggiornaPannelloPersone();
            return;
        }

        var nuovaId = Archivio.TrovaOCrea(nome, persona.AvatarJpeg);
        AggiungiCampioni(nuovaId, persona.Impronte);
        await AssegnaEAggiornaAsync(persona.Volti, nuovaId, StatoPersona.Confermato);
    }

    private async Task ConfermaProposteAsync(PersonaUi persona)
    {
        if (persona.PersonaId is not long id) return;
        var idProposte = persona.Proposte.ToHashSet();
        var impronte = persona.Volti.Select((v, i) => (v, i)).Where(x => idProposte.Contains(x.v)).Select(x => persona.Impronte[x.i]);
        AggiungiCampioni(id, impronte);
        await AssegnaEAggiornaAsync(persona.Proposte, id, StatoPersona.Confermato);
    }

    private void AggiungiCampioni(long personaId, IEnumerable<float[]> nuovi)
    {
        var esistenti = Archivio.Elenco().FirstOrDefault(p => p.Id == personaId)?.Campioni.Select(Impronte.DaByte).ToList() ?? [];
        var campioni = RiconoscimentoPersone.AggiornaCampioni(esistenti, nuovi);
        Archivio.ImpostaCampioni(personaId, campioni.Select(Impronte.InByte).ToList());
    }

    /// <summary>Applica la scelta, ripropone le persone sui volti ancora senza nome e aggiorna il pannello.</summary>
    private async Task AssegnaEAggiornaAsync(IReadOnlyList<long> volti, long? personaId, int stato)
    {
        var db = _db;
        if (db is null) return;
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();

        var persone = PersoneNote();
        await Task.Run(() =>
        {
            db.AssegnaPersona(volti, personaId, stato);

            // Con un nome nuovo (o più campioni) altri volti possono ora essere riconosciuti.
            foreach (var proposta in db.GetVoltiPersone()
                         .Where(v => v.PersonaId is null && v.Stato == StatoPersona.Nessuno && v.Impronta is not null)
                         .Select(v => (v.Id, Persona: RiconoscimentoPersone.Riconosci(Impronte.DaByte(v.Impronta!), persone)?.Persona))
                         .Where(x => x.Persona is not null)
                         .GroupBy(x => x.Persona!.Id))
            {
                db.AssegnaPersona(proposta.Select(x => x.Id).ToList(), proposta.Key, StatoPersona.Proposto);
            }
        });

        AggiornaPannelloPersone();
    }

    // ---------------------------------------------------------------- rianalisi

    private async void RianalizzaPersone_Click(object sender, RoutedEventArgs e)
    {
        var db = _db;
        if (db is null) return;

        var conferma = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Rianalizzare le persone di questa cartella?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, MaxWidth = 440,
                Text = "Volti e impronte di tutte le foto della cartella vengono ricalcolati da zero, in background. " +
                       "Le persone con un nome restano: i loro volti verranno riproposti in automatico, da confermare.",
            },
            PrimaryButtonText = "Rianalizza",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await conferma.ShowAsync() != ContentDialogResult.Primary) return;

        FermaRicercaVolti();
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();
        await Task.Run(db.AzzeraAnalisiVolti);
        foreach (var foto in _tutte)
        {
            foto.NumeroVolti = null;
            foto.PersoneAnalizzate = false;
        }

        _filtroPersona = null;
        FiltroPersonaButton.Visibility = Visibility.Collapsed;
        ApplicaFiltro();
        AggiornaPannelloPersone();
        AvviaRicercaVolti();
    }
}
