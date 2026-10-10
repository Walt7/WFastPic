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
    private List<PersonaUi> _personeCorrenti = [];
    private (LibrarySidecarDatabase Db, Func<Task> Azione)? _annullaPersone;

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
        if (_annullaPersone is { } annulla && annulla.Db != _db) NascondiAnnullaPersone(); // cartella cambiata
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

        _personeCorrenti = elenco.Select(e => e.Persona).ToList();
        PersoneList.ItemsSource = _personeCorrenti;
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
        // Persone unite: i volti della persona assorbita valgono come quelli della persona che resta.
        var unioni = archivio.Unioni();
        var volti = db.GetVoltiPersone()
            .Where(v => v.Impronta is not null)
            .Select(v => v.PersonaId is long id && unioni.TryGetValue(id, out var resta) ? v with { PersonaId = resta } : v)
            .ToList();
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

        var altre = _personeCorrenti.Where(p => p != persona).ToList();
        if (altre.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var unisci = new MenuFlyoutSubItem { Text = "È la stessa persona di…", Icon = new FontIcon { Glyph = "\uE71B" } };
            foreach (var altra in altre)
            {
                var voce = new MenuFlyoutItem { Text = $"{altra.Nome} · {altra.Dettaglio}" };
                voce.Click += (_, _) => _ = UnisciPersoneAsync(persona, altra);
                unisci.Items.Add(voce);
            }
            menu.Items.Add(unisci);
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

        var dbCorrente = _db;
        if (dbCorrente is null) return;
        var esisteva = Archivio.EsisteNome(nome);
        var nuovaId = Archivio.TrovaOCrea(nome, persona.AvatarJpeg);
        var campioniPrima = Archivio.Elenco().FirstOrDefault(p => p.Id == nuovaId)?.Campioni.ToList() ?? [];
        var voltiPrima = await Task.Run(() => FotografaVolti(dbCorrente));
        AggiungiCampioni(nuovaId, persona.Impronte);
        await AssegnaEAggiornaAsync(persona.Volti, nuovaId, StatoPersona.Confermato);

        ImpostaAnnulla(dbCorrente, $"Nome «{nome}» dato a {persona.Volti.Count} volti", async () =>
        {
            await Task.Run(() => RipristinaVolti(dbCorrente, voltiPrima));
            if (esisteva) Archivio.ImpostaCampioni(nuovaId, campioniPrima); else Archivio.Elimina(nuovaId);
        });
    }

    // ---------------------------------------------------------------- unione ("è la stessa persona") con annulla

    private static List<(long Id, long? Persona, int Stato)> FotografaVolti(LibrarySidecarDatabase db) =>
        db.GetVoltiPersone().Select(v => (v.Id, v.PersonaId, v.Stato)).ToList();

    private static void RipristinaVolti(LibrarySidecarDatabase db, List<(long Id, long? Persona, int Stato)> prima)
    {
        var ora = db.GetVoltiPersone().ToDictionary(v => v.Id);
        foreach (var gruppo in prima
                     .Where(p => ora.TryGetValue(p.Id, out var v) && (v.PersonaId != p.Persona || v.Stato != p.Stato))
                     .GroupBy(p => (p.Persona, p.Stato)))
        {
            db.AssegnaPersona(gruppo.Select(x => x.Id).ToList(), gruppo.Key.Persona, gruppo.Key.Stato);
        }
    }

    private void ImpostaAnnulla(LibrarySidecarDatabase db, string testo, Func<Task> azione)
    {
        _annullaPersone = (db, azione);
        PersoneAnnullaText.Text = testo;
        PersoneAnnullaPanel.Visibility = Visibility.Visible;
    }

    private void NascondiAnnullaPersone()
    {
        _annullaPersone = null;
        PersoneAnnullaPanel.Visibility = Visibility.Collapsed;
    }

    private async void PersoneAnnulla_Click(object sender, RoutedEventArgs e)
    {
        if (_annullaPersone is not { } annulla) return;
        NascondiAnnullaPersone();
        if (annulla.Db != _db) return;
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();
        await annulla.Azione();
        AggiornaPannelloPersone();
    }

    /// <summary>
    /// Due gruppi/persone sono la stessa persona. Nome + senza nome: i volti senza nome prendono il nome.
    /// Due senza nome: si chiede il nome per tutti. Due con nome: si sceglie quale nome tenere; l'altra
    /// non viene cancellata, confluisce (in tutte le cartelle) e si può separare con Annulla.
    /// </summary>
    private async Task UnisciPersoneAsync(PersonaUi cliccata, PersonaUi altra)
    {
        var db = _db;
        if (db is null) return;

        if (cliccata.SenzaNome && altra.SenzaNome)
        {
            await DaiNomeAsync(new PersonaUi
            {
                Nome = "Senza nome",
                Dettaglio = "",
                Volti = [.. cliccata.Volti, .. altra.Volti],
                Proposte = [],
                Foto = cliccata.Foto.Union(altra.Foto).ToHashSet(),
                Impronte = [.. cliccata.Impronte, .. altra.Impronte],
                Avatar = cliccata.Avatar,
                AvatarJpeg = cliccata.AvatarJpeg,
            }, null);
            return;
        }

        if (cliccata.SenzaNome || altra.SenzaNome)
        {
            var (senzaNome, conNome) = cliccata.SenzaNome ? (cliccata, altra) : (altra, cliccata);
            var id = conNome.PersonaId!.Value;
            var campioniPrima = Archivio.Elenco().FirstOrDefault(p => p.Id == id)?.Campioni.ToList() ?? [];
            if (_selectionManager is not null) await _selectionManager.SvuotaAsync();
            var voltiPrima = await Task.Run(() => FotografaVolti(db));

            AggiungiCampioni(id, senzaNome.Impronte);
            await AssegnaEAggiornaAsync(senzaNome.Volti, id, StatoPersona.Confermato);
            ImpostaAnnulla(db, $"Gruppo senza nome unito a «{conNome.Nome}»", async () =>
            {
                await Task.Run(() => RipristinaVolti(db, voltiPrima));
                Archivio.ImpostaCampioni(id, campioniPrima);
            });
            return;
        }

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Unisci le persone",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, MaxWidth = 380,
                Text = $"«{cliccata.Nome}» e «{altra.Nome}» sono la stessa persona? Scegli il nome da tenere: " +
                       "l'unione vale in tutte le cartelle e puoi annullarla subito dopo.",
            },
            PrimaryButtonText = $"Tieni «{cliccata.Nome}»",
            SecondaryButtonText = $"Tieni «{altra.Nome}»",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
        };
        var scelta = await dialogo.ShowAsync();
        if (scelta == ContentDialogResult.None) return;
        var (tieni, assorbita) = scelta == ContentDialogResult.Primary ? (cliccata, altra) : (altra, cliccata);
        var tieniId = tieni.PersonaId!.Value;
        var assorbitaId = assorbita.PersonaId!.Value;

        var elenco = Archivio.Elenco();
        var campioniTieni = elenco.FirstOrDefault(p => p.Id == tieniId)?.Campioni.ToList() ?? [];
        var campioniAssorbita = elenco.FirstOrDefault(p => p.Id == assorbitaId)?.Campioni.Select(Impronte.DaByte).ToList() ?? [];
        var uniti = RiconoscimentoPersone.AggiornaCampioni(
            campioniTieni.Select(Impronte.DaByte).ToList(), campioniAssorbita.Concat(assorbita.Impronte));
        Archivio.ImpostaCampioni(tieniId, uniti.Select(Impronte.InByte).ToList());
        Archivio.Unisci(assorbitaId, tieniId);

        ImpostaAnnulla(db, $"«{assorbita.Nome}» unita a «{tieni.Nome}»", () =>
        {
            Archivio.AnnullaUnione(assorbitaId);
            Archivio.ImpostaCampioni(tieniId, campioniTieni);
            return Task.CompletedTask;
        });
        AggiornaPannelloPersone();
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
            RiproponiPersone(db, persone); // con un nome nuovo (o più campioni) altri volti possono ora essere riconosciuti
        });

        AggiornaPannelloPersone();
    }

    /// <summary>Propone la persona nota più somigliante ai volti ancora senza nome (da confermare a mano).</summary>
    private static void RiproponiPersone(LibrarySidecarDatabase db, IReadOnlyList<PersonaNota> persone)
    {
        foreach (var proposta in db.GetVoltiPersone()
                     .Where(v => v.PersonaId is null && v.Stato == StatoPersona.Nessuno && v.Impronta is not null)
                     .Select(v => (v.Id, Persona: RiconoscimentoPersone.Riconosci(Impronte.DaByte(v.Impronta!), persone)?.Persona))
                     .Where(x => x.Persona is not null)
                     .GroupBy(x => x.Persona!.Id))
        {
            db.AssegnaPersona(proposta.Select(x => x.Id).ToList(), proposta.Key, StatoPersona.Proposto);
        }
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
