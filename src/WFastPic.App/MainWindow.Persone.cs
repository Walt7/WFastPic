using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;

namespace WFastPic.App;

/// <summary>Una persona (o un gruppo di volti ancora senza nome) nel pannello Persone.</summary>
public sealed class PersonaUi
{
    private static Brush? _bianco, _grigio;

    public required string Nome { get; init; }
    public required string Dettaglio { get; init; }
    public ImageSource? Avatar { get; set; }
    public long? PersonaId { get; init; }
    public bool SenzaNome => PersonaId is null;

    /// <summary>Volti, proposte, foto e impronte nella cartella aperta (vuoti se la persona è solo altrove).</summary>
    public required IReadOnlyList<long> Volti { get; init; }
    public required IReadOnlyList<long> Proposte { get; init; }
    public required HashSet<long> Foto { get; init; }
    public required IReadOnlyList<float[]> Impronte { get; init; }
    public byte[]? AvatarJpeg { get; init; }

    /// <summary>Miniature dei volti nella cartella aperta, per "Cambia immagine".</summary>
    public IReadOnlyList<byte[]> MiniatureVolti { get; init; } = [];

    /// <summary>Le altre cartelle note in cui compare, con quante foto (più numerose prima).</summary>
    public IReadOnlyList<(string Cartella, int Foto)> AltreCartelle { get; init; } = [];

    // Chi non compare nella cartella aperta è attenuato, come le voci "altrove" di Picasa.
    public double Opacita => Foto.Count > 0 ? 1.0 : 0.5;

    // Pennelli creati al primo binding (thread UI): le PersonaUi nascono in background.
    public Brush ColoreNome => SenzaNome
        ? _grigio ??= new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xAA, 0xAA, 0xAA))
        : _bianco ??= new SolidColorBrush(Microsoft.UI.Colors.White);
}

/// <summary>
/// Persone stile Picasa nel pannello di sinistra: tutte le persone con nome (archivio globale, con le
/// foto contate nei sidecar di tutte le cartelle note) e i gruppi di volti simili ancora senza nome
/// della cartella aperta. Clic = filtra la griglia su quella persona (o apre la sua cartella se qui
/// non c'è); tasto destro = dai un nome / conferma le proposte / rinomina / "è la stessa persona di…" /
/// cambia immagine / togli il nome / elimina. Dare un nome aggiunge i volti come campioni della persona
/// e ripropone subito i volti somiglianti. Nomi e unioni si possono annullare subito dopo.
/// </summary>
public partial class MainWindow
{
    private ArchivioPersone? _archivioPersone;
    private (string Nome, HashSet<long> Foto)? _filtroPersona;
    private int _richiestaPersone;
    private List<PersonaUi> _personeCorrenti = [];
    private (LibrarySidecarDatabase Db, Func<Task> Azione)? _annullaPersone;

    // Conteggi delle altre cartelle: si rileggono dai dischi solo quando cambia la cartella aperta.
    private (string Corrente, List<(string Cartella, IReadOnlyDictionary<long, int> Conteggi)> Elenco)? _personeAltreCartelle;

    // Clic su una persona che compare solo altrove: a cartella caricata la griglia si filtra su di lei.
    private (string Cartella, long PersonaId)? _personaDaFiltrare;

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

    /// <summary>Ricostruisce l'elenco dal sidecar (dopo aver scritto quanto in coda), dalle altre cartelle e dall'archivio persone.</summary>
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

        var corrente = db.CartellaFoto;
        var altre = _personeAltreCartelle is { } cache && StessoPercorso(cache.Corrente, corrente) ? cache.Elenco : null;
        if (altre is null)
        {
            var percorsi = _cartelleRecenti.Where(p => !StessoPercorso(p, corrente)).ToList();
            altre = await Task.Run(() => percorsi
                .Where(Directory.Exists)
                .Select(p => (Cartella: p, Conteggi: LibrarySidecarDatabase.LeggiFotoPerPersona(p)))
                .Where(c => c.Conteggi.Count > 0)
                .ToList());
            if (richiesta != _richiestaPersone) return;
            _personeAltreCartelle = (corrente, altre);
        }

        List<(PersonaUi Persona, byte[]? Avatar)> elenco;
        try
        {
            var archivio = Archivio;
            elenco = await Task.Run(() => CostruisciElencoPersone(db, archivio, altre));
        }
        catch (ObjectDisposedException)
        {
            return; // cartella cambiata nel frattempo
        }
        if (richiesta != _richiestaPersone) return;

        foreach (var (persona, avatar) in elenco)
        {
            if (avatar is not null) persona.Avatar = await CreaImmagineAsync(avatar);
        }
        if (richiesta != _richiestaPersone) return;

        _personeCorrenti = elenco.Select(e => e.Persona).ToList();
        PersoneList.ItemsSource = _personeCorrenti;
        var analizzate = _tutte.Count(f => f.PersoneAnalizzate);
        var quiQualcuno = elenco.Any(e => e.Persona.Foto.Count > 0);
        PersoneStatoText.Text = analizzate < _tutte.Count
            ? (quiQualcuno ? $"Analisi in corso: {analizzate} di {_tutte.Count} foto" : "Analisi delle foto in corso…")
            : quiQualcuno ? "" : "Nessun gruppo di volti trovato in questa cartella.";

        if (_personaDaFiltrare is { } daFiltrare && StessoPercorso(daFiltrare.Cartella, corrente))
        {
            _personaDaFiltrare = null;
            var persona = _personeCorrenti.FirstOrDefault(p => p.PersonaId == daFiltrare.PersonaId && p.Foto.Count > 0);
            if (persona is not null) FiltraPersona(persona);
        }
    }

    private static async Task<ImageSource> CreaImmagineAsync(byte[] jpeg)
    {
        var immagine = new BitmapImage();
        using var stream = new MemoryStream(jpeg).AsRandomAccessStream();
        await immagine.SetSourceAsync(stream);
        return immagine;
    }

    /// <summary>
    /// Persone con nome in ordine alfabetico, come Picasa (foto della cartella aperta, proposte
    /// comprese, più quelle delle altre cartelle note), poi i gruppi senza nome della cartella da
    /// almeno 2 volti (un volto isolato si confonderebbe con mille passanti: comparirà quando ne
    /// arriva un altro). Le persone unite si risolvono qui, in lettura (i sidecar restano come sono e
    /// l'unione si può annullare); i volti di persone eliminate tornano senza nome nel sidecar.
    /// </summary>
    private static List<(PersonaUi, byte[]?)> CostruisciElencoPersone(
        LibrarySidecarDatabase db, ArchivioPersone archivio, List<(string Cartella, IReadOnlyDictionary<long, int> Conteggi)> altre)
    {
        var persone = archivio.Elenco();
        var unioni = archivio.Unioni();
        var note = persone.Select(p => p.Id).ToHashSet();
        long? Risolvi(long id) =>
            note.Contains(id) ? id : unioni.TryGetValue(id, out var resta) && note.Contains(resta) ? resta : null;

        var volti = db.GetVoltiPersone().Where(v => v.Impronta is not null).ToList();
        var eliminate = volti
            .Where(v => v.PersonaId is long id && !note.Contains(id) && !unioni.ContainsKey(id))
            .Select(v => v.PersonaId!.Value).Distinct().ToList();
        if (eliminate.Count > 0)
        {
            foreach (var vecchia in eliminate) db.SostituisciPersona(vecchia, null);
            volti = db.GetVoltiPersone().Where(v => v.Impronta is not null).ToList();
        }
        volti = volti
            .Select(v => v.PersonaId is long id && unioni.ContainsKey(id) ? v with { PersonaId = Risolvi(id) } : v)
            .ToList();

        var quiPerPersona = volti
            .Where(v => v.PersonaId is not null && v.Stato is StatoPersona.Proposto or StatoPersona.Confermato)
            .GroupBy(v => v.PersonaId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var risultato = new List<(PersonaUi, byte[]?)>();
        foreach (var persona in persone)
        {
            var qui = quiPerPersona.GetValueOrDefault(persona.Id) ?? [];
            var altrove = altre
                .Select(c => (c.Cartella, Foto: c.Conteggi.Where(kv => Risolvi(kv.Key) == persona.Id).Sum(kv => kv.Value)))
                .Where(c => c.Foto > 0)
                .OrderByDescending(c => c.Foto)
                .ToList();
            var foto = qui.Select(v => v.PhotoId).ToHashSet();
            var totale = foto.Count + altrove.Sum(c => c.Foto);
            if (totale == 0) continue; // nome rimasto senza volti in nessuna cartella nota

            var proposte = qui.Where(v => v.Stato == StatoPersona.Proposto).Select(v => v.Id).ToList();
            var parti = new List<string> { $"{totale} foto" };
            if (foto.Count == 0) parti.Add(altrove.Count == 1 ? ArchivioCartelle.Nome(altrove[0].Cartella) : $"{altrove.Count} cartelle");
            else if (altrove.Count > 0) parti.Add($"{foto.Count} qui");
            if (proposte.Count > 0) parti.Add($"{proposte.Count} da confermare");

            risultato.Add((new PersonaUi
            {
                Nome = persona.Nome,
                Dettaglio = string.Join(" · ", parti),
                PersonaId = persona.Id,
                Volti = qui.Select(v => v.Id).ToList(),
                Proposte = proposte,
                Foto = foto,
                Impronte = qui.Select(v => Impronte.DaByte(v.Impronta!)).ToList(),
                MiniatureVolti = qui.Where(v => v.Miniatura is not null).Select(v => v.Miniatura!).Take(60).ToList(),
                AltreCartelle = altrove,
            }, persona.Avatar ?? qui.FirstOrDefault()?.Miniatura));
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

    // ---------------------------------------------------------------- filtro griglia, apertura cartella

    private void PersoneList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PersonaUi persona) return;
        if (persona.Foto.Count > 0)
        {
            FiltraPersona(persona);
            return;
        }

        if (persona.AltreCartelle.Count == 1)
        {
            _ = ApriCartellaDellaPersonaAsync(persona, persona.AltreCartelle[0].Cartella);
            return;
        }

        // Compare in più cartelle: si sceglie quale aprire.
        var menu = new MenuFlyout();
        foreach (var voce in VociCartelleDellaPersona(persona)) menu.Items.Add(voce);
        if (PersoneList.ContainerFromItem(persona) is FrameworkElement riga) menu.ShowAt(riga);
    }

    private IEnumerable<MenuFlyoutItem> VociCartelleDellaPersona(PersonaUi persona) =>
        persona.AltreCartelle.Select(c =>
        {
            var voce = new MenuFlyoutItem { Text = $"{ArchivioCartelle.Nome(c.Cartella)} · {c.Foto} foto", Icon = new FontIcon { Glyph = "" } };
            ToolTipService.SetToolTip(voce, c.Cartella);
            voce.Click += (_, _) => _ = ApriCartellaDellaPersonaAsync(persona, c.Cartella);
            return voce;
        });

    private async Task ApriCartellaDellaPersonaAsync(PersonaUi persona, string cartella)
    {
        if (persona.PersonaId is not long id) return;
        _personaDaFiltrare = (cartella, id);
        await CaricaCartellaAsync(cartella);
    }

    private void FiltraPersona(PersonaUi persona)
    {
        _filtroPersona = (persona.SenzaNome ? "Senza nome" : persona.Nome, persona.Foto);
        FiltroPersonaText.Text = _filtroPersona.Value.Nome;
        FiltroPersonaButton.Visibility = Visibility.Visible;
        ApplicaFiltro();
        Griglia.Focus(FocusState.Programmatic);
    }

    private void FiltroPersonaButton_Click(object sender, RoutedEventArgs e) => TogliFiltroPersona();

    private void TogliFiltroPersona()
    {
        _filtroPersona = null;
        FiltroPersonaButton.Visibility = Visibility.Collapsed;
        ApplicaFiltro();
    }

    private bool CorrispondeAllaPersona(PhotoGridItem foto) => _filtroPersona is not { } f || f.Foto.Contains(foto.Id);

    // ---------------------------------------------------------------- menu: gestione dei nomi

    private void PersoneList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not PersonaUi persona) return;

        var menu = new MenuFlyout();
        void Voce(IList<MenuFlyoutItemBase> destinazione, string testo, string glifo, Action azione)
        {
            var voce = new MenuFlyoutItem { Text = testo, Icon = new FontIcon { Glyph = glifo } };
            voce.Click += (_, _) => azione();
            destinazione.Add(voce);
        }

        if (persona.SenzaNome)
        {
            Voce(menu.Items, "Dai un nome…", "", () => _ = DaiNomeAsync(persona, null));
            Voce(menu.Items, "Ignora questi volti", "", () => _ = AssegnaEAggiornaAsync(persona.Volti, null, StatoPersona.Rifiutato));
        }
        else
        {
            if (persona.Proposte.Count > 0)
                Voce(menu.Items, $"Conferma le proposte ({persona.Proposte.Count})", "", () => _ = ConfermaProposteAsync(persona));
            Voce(menu.Items, "Rinomina…", "", () => _ = DaiNomeAsync(persona, persona.Nome));
            if (persona.MiniatureVolti.Count > 1)
                Voce(menu.Items, "Cambia immagine…", "", () => _ = CambiaImmagineAsync(persona));
        }

        var altre = _personeCorrenti.Where(p => p != persona).ToList();
        if (altre.Count > 0)
        {
            var unisci = new MenuFlyoutSubItem { Text = "È la stessa persona di…", Icon = new FontIcon { Glyph = "" } };
            foreach (var altra in altre) Voce(unisci.Items, $"{altra.Nome} · {altra.Dettaglio}", "", () => _ = UnisciPersoneAsync(persona, altra));
            menu.Items.Add(unisci);
        }

        if (!persona.SenzaNome)
        {
            if (persona.AltreCartelle.Count > 0)
            {
                var apri = new MenuFlyoutSubItem { Text = "Apri la cartella", Icon = new FontIcon { Glyph = "" } };
                foreach (var voce in VociCartelleDellaPersona(persona)) apri.Items.Add(voce);
                menu.Items.Add(apri);
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            if (persona.Volti.Count > 0)
                Voce(menu.Items, "Togli il nome da questi volti", "", () => _ = AssegnaEAggiornaAsync(persona.Volti, null, StatoPersona.Nessuno));
            Voce(menu.Items, "Elimina la persona…", "", () => _ = EliminaPersonaAsync(persona));
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
                ? $"I {persona.Volti.Count} volti del gruppo prendono questo nome (anche di una persona già nota); WFastPic lo proporrà anche sui volti somiglianti, in tutte le cartelle."
                : "Rinomina la persona in tutte le cartelle. Con il nome di un'altra persona, le due si uniscono.",
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
            if (nome == nomeAttuale) return;

            // Il nome di un'altra persona: è la stessa, confluisce in lei (come trascinare un volto in Picasa).
            var omonima = Archivio.Elenco().FirstOrDefault(p => p.Id != id && string.Equals(p.Nome, nome, StringComparison.CurrentCultureIgnoreCase));
            if (omonima is not null)
            {
                var db = _db;
                if (db is not null) await UnisciInAsync(db, persona, omonima.Id, omonima.Nome);
                return;
            }

            try { Archivio.Rinomina(id, nome); }
            catch (Microsoft.Data.Sqlite.SqliteException) { StatusText.Text = $"Esiste già una persona chiamata «{nome}»."; return; }
            if (_filtroPersona is { } f && f.Nome == nomeAttuale) FiltroPersonaText.Text = nome;
            AggiornaPannelloPersone();
            return;
        }

        var dbCorrente = _db;
        if (dbCorrente is null) return;
        var esisteva = Archivio.EsisteNome(nome);
        var nuovaId = Archivio.TrovaOCrea(nome, persona.AvatarJpeg);
        var campioniPrima = Archivio.Elenco().FirstOrDefault(p => p.Id == nuovaId)?.Campioni.ToList() ?? [];
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();
        var voltiPrima = await Task.Run(() => FotografaVolti(dbCorrente));
        AggiungiCampioni(nuovaId, persona.Impronte);
        await AssegnaEAggiornaAsync(persona.Volti, nuovaId, StatoPersona.Confermato);

        ImpostaAnnulla(dbCorrente, $"Nome «{nome}» dato a {persona.Volti.Count} volti", async () =>
        {
            await Task.Run(() => RipristinaVolti(dbCorrente, voltiPrima));
            if (esisteva) Archivio.ImpostaCampioni(nuovaId, campioniPrima); else Archivio.Elimina(nuovaId);
        });
    }

    private async Task<bool> ChiediConfermaAsync(string titolo, string testo, string pulsante)
    {
        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = titolo,
            Content = new TextBlock { Text = testo, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = pulsante,
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialogo.ShowAsync() == ContentDialogResult.Primary;
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
        await UnisciInAsync(db, assorbita, tieni.PersonaId!.Value, tieni.Nome);
    }

    /// <summary>
    /// <paramref name="assorbita"/> confluisce nella persona <paramref name="tieniId"/>, in tutte le
    /// cartelle: campioni uniti, nessuna cancellazione, Annulla le separa di nuovo.
    /// </summary>
    private async Task UnisciInAsync(LibrarySidecarDatabase db, PersonaUi assorbita, long tieniId, string tieniNome)
    {
        if (assorbita.PersonaId is not long assorbitaId || assorbitaId == tieniId) return;

        var elenco = Archivio.Elenco();
        var campioniTieni = elenco.FirstOrDefault(p => p.Id == tieniId)?.Campioni.ToList() ?? [];
        var campioniAssorbita = elenco.FirstOrDefault(p => p.Id == assorbitaId)?.Campioni.Select(Impronte.DaByte).ToList() ?? [];
        var uniti = RiconoscimentoPersone.AggiornaCampioni(
            campioniTieni.Select(Impronte.DaByte).ToList(), campioniAssorbita.Concat(assorbita.Impronte));
        Archivio.ImpostaCampioni(tieniId, uniti.Select(Impronte.InByte).ToList());
        Archivio.Unisci(assorbitaId, tieniId);
        if (_filtroPersona is { } f && f.Nome == assorbita.Nome) TogliFiltroPersona();

        ImpostaAnnulla(db, $"«{assorbita.Nome}» unita a «{tieniNome}»", () =>
        {
            Archivio.AnnullaUnione(assorbitaId);
            Archivio.ImpostaCampioni(tieniId, campioniTieni);
            return Task.CompletedTask;
        });
        // Con i campioni uniti altri volti senza nome possono ora essere riconosciuti.
        await AssegnaEAggiornaAsync([], null, StatoPersona.Nessuno);
    }

    private async Task EliminaPersonaAsync(PersonaUi persona)
    {
        if (persona.PersonaId is not long id) return;
        if (!await ChiediConfermaAsync(
                $"Eliminare «{persona.Nome}»?",
                "Il nome viene tolto da tutte le foto, in tutte le cartelle: i volti tornano senza nome. Le foto non vengono toccate.",
                "Elimina"))
            return;

        NascondiAnnullaPersone(); // un annulla precedente potrebbe riferirsi a questa persona
        Archivio.Elimina(id);
        if (_filtroPersona is { } f && f.Nome == persona.Nome) TogliFiltroPersona();
        await AssegnaEAggiornaAsync(persona.Volti, null, StatoPersona.Nessuno);
    }

    /// <summary>Sceglie quale volto (della cartella aperta) rappresenta la persona nel pannello.</summary>
    private async Task CambiaImmagineAsync(PersonaUi persona)
    {
        if (persona.PersonaId is not long id) return;

        var scelte = new GridView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360, Width = 380 };
        foreach (var jpeg in persona.MiniatureVolti)
        {
            scelte.Items.Add(new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(4), Tag = jpeg,
                Child = new Image { Source = await CreaImmagineAsync(jpeg), Stretch = Stretch.UniformToFill },
            });
        }

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Immagine di {persona.Nome}",
            Content = scelte,
            PrimaryButtonText = "Usa questa",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        if (scelte.SelectedItem is not Border { Tag: byte[] scelta }) return;

        Archivio.ImpostaAvatar(id, scelta);
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

        if (!await ChiediConfermaAsync(
                "Rianalizzare le persone di questa cartella?",
                "Volti e impronte di tutte le foto della cartella vengono ricalcolati da zero, in background. " +
                "Le persone con un nome restano: i loro volti verranno riproposti in automatico, da confermare.",
                "Rianalizza"))
            return;

        FermaRicercaVolti();
        NascondiAnnullaPersone(); // i volti salvati per l'annulla non esistono più
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();
        await Task.Run(db.AzzeraAnalisiVolti);
        foreach (var foto in _tutte)
        {
            foto.NumeroVolti = null;
            foto.PersoneAnalizzate = false;
        }

        TogliFiltroPersona();
        AggiornaPannelloPersone();
        AvviaRicercaVolti();
    }
}
