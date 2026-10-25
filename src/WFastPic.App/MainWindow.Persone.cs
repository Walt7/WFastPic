using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;
using WFastPic.Core.Processing;
using static WFastPic.Core.Localizzazione.Testi;

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

    /// <summary>Io, famiglia, amico, conoscente (<see cref="CerchiaPersona"/>) e la stella: pesano nel voto automatico.</summary>
    public int Cerchia { get; init; }
    public bool Preferita { get; init; }
    public string Soprannome { get; init; } = "";

    public ImportanzaPersone Importanza => SenzaNome
        ? ImportanzaPersone.Nessuna
        : Cerchia switch
        {
            CerchiaPersona.Io => ImportanzaPersone.Io,
            CerchiaPersona.Famiglia => ImportanzaPersone.Preferita,
            _ when Preferita => ImportanzaPersone.Preferita,
            CerchiaPersona.Amico => ImportanzaPersone.Amico,
            CerchiaPersona.Conoscente => ImportanzaPersone.Conoscente,
            _ => ImportanzaPersone.ConNome,
        };

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

/// <summary>Un gruppo dell'elenco Persone (Io, Famiglia, Amici, Conoscenti, Altre persone, Sconosciuti).</summary>
public sealed class GruppoPersone : List<PersonaUi>
{
    public required string Titolo { get; init; }
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
    private long? _filtroPersonaId; // null = gruppo senza nome
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
            PersoneStatoText.Text = _riconoscimentoPersone == true ? "" : T("Attiva il riconoscimento per raggruppare i volti per persona.");
            AggiornaImportanzaPersone([]);
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
        PersoneList.ItemsSource = new CollectionViewSource { IsSourceGrouped = true, Source = RaggruppaPersone(_personeCorrenti) }.View;
        AggiornaImportanzaPersone(_personeCorrenti);
        Viewer.AggiornaVoltiFotoCorrente();
        RiallineaFiltroPersona();
        var analizzate = _tutte.Count(f => f.PersoneAnalizzate);
        var quiQualcuno = elenco.Any(e => e.Persona.Foto.Count > 0);
        PersoneStatoText.Text = analizzate < _tutte.Count
            ? (quiQualcuno ? T("Analisi in corso: {0} di {1}", analizzate, Foto(_tutte.Count)) : T("Analisi delle foto in corso…"))
            : quiQualcuno ? "" : T("Nessun gruppo di volti trovato in questa cartella.");

        if (_personaDaFiltrare is { } daFiltrare && StessoPercorso(daFiltrare.Cartella, corrente))
        {
            _personaDaFiltrare = null;
            var persona = _personeCorrenti.FirstOrDefault(p => p.PersonaId == daFiltrare.PersonaId && p.Foto.Count > 0);
            if (persona is not null) FiltraPersona(persona);
        }
    }

    /// <summary>Cartelle di persone come in Picasa: tu, famiglia, amici, conoscenti, le altre con nome, gli sconosciuti.</summary>
    private static List<GruppoPersone> RaggruppaPersone(IEnumerable<PersonaUi> persone)
    {
        var gruppi = new (string Titolo, Func<PersonaUi, bool> Contiene)[]
        {
            (T("Io"), p => !p.SenzaNome && p.Cerchia == CerchiaPersona.Io),
            (T("Famiglia"), p => !p.SenzaNome && p.Cerchia == CerchiaPersona.Famiglia),
            (T("Amici"), p => !p.SenzaNome && p.Cerchia == CerchiaPersona.Amico),
            (T("Conoscenti"), p => !p.SenzaNome && p.Cerchia == CerchiaPersona.Conoscente),
            (T("Altre persone"), p => !p.SenzaNome && p.Cerchia is not (CerchiaPersona.Io or CerchiaPersona.Famiglia or CerchiaPersona.Amico or CerchiaPersona.Conoscente)),
            (T("Sconosciuti"), p => p.SenzaNome),
        };
        var elenco = persone.ToList();
        var risultato = new List<GruppoPersone>();
        foreach (var (titolo, contiene) in gruppi)
        {
            var gruppo = new GruppoPersone { Titolo = titolo };
            gruppo.AddRange(elenco.Where(contiene).OrderByDescending(p => p.Preferita)); // stabile: poi l'ordine alfabetico
            if (gruppo.Count > 0) risultato.Add(gruppo);
        }
        return risultato;
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
            var parti = new List<string>();
            if (persona.Soprannome.Length > 0) parti.Add($"«{persona.Soprannome}»");
            if (EtichettaCerchia(persona.Cerchia, persona.Preferita) is { } etichetta) parti.Add(etichetta);
            parti.Add(Foto(totale));
            if (foto.Count == 0) parti.Add(altrove.Count == 1 ? ArchivioCartelle.Nome(altrove[0].Cartella) : T("{0} cartelle", altrove.Count));
            else if (altrove.Count > 0) parti.Add(T("{0} qui", foto.Count));
            if (proposte.Count > 0) parti.Add(T("{0} da confermare", proposte.Count));

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
                Cerchia = persona.Cerchia,
                Preferita = persona.Preferita,
                Soprannome = persona.Soprannome,
            }, persona.Avatar ?? MiglioreMiniatura(qui)));
        }

        // In cima tu, poi le persone con la stella e la famiglia; le altre in ordine alfabetico.
        risultato = risultato.OrderBy(r => r.Item1.Importanza switch
        {
            ImportanzaPersone.Io => 0,
            ImportanzaPersone.Preferita => 1,
            _ => 2,
        }).ToList();

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
                Nome = T("Senza nome"),
                Dettaglio = Foto(foto.Count),
                PersonaId = null,
                Volti = gruppo.Select(v => v.Id).ToList(),
                Proposte = [],
                Foto = foto,
                Impronte = gruppo.Select(v => Impronte.DaByte(v.Impronta!)).ToList(),
                AvatarJpeg = MiglioreMiniatura(gruppo),
            }, MiglioreMiniatura(gruppo)));
        }

        return risultato;
    }

    /// <summary>
    /// Il volto da mostrare come immagine di una persona o di un gruppo: il più nitido, non il primo che capita
    /// (un volto piccolo o mosso ingrandito nella miniatura sembra una macchia). Una miniatura JPEG con più
    /// dettaglio pesa di più, e un volto grande nella foto ha più pixel: si premia il prodotto dei due.
    /// </summary>
    private static byte[]? MiglioreMiniatura(IEnumerable<VoltoSalvato> volti) =>
        volti.Where(v => v.Miniatura is not null)
            .OrderByDescending(v => v.Miniatura!.Length * Math.Sqrt(Math.Max(1e-6, (v.X1 - v.X0) * (v.Y1 - v.Y0))))
            .FirstOrDefault()?.Miniatura;

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
        _filtroPersona = (persona.SenzaNome ? T("Senza nome") : persona.Nome, persona.Foto);
        _filtroPersonaId = persona.PersonaId;
        FiltroPersonaText.Text = _filtroPersona.Value.Nome;
        FiltroPersonaButton.Visibility = Visibility.Visible;
        ApplicaFiltro();
        Griglia.Focus(FocusState.Programmatic);
    }

    private void FiltroPersonaButton_Click(object sender, RoutedEventArgs e) => TogliFiltroPersona();

    private void TogliFiltroPersona()
    {
        _filtroPersona = null;
        _filtroPersonaId = null;
        FiltroPersonaButton.Visibility = Visibility.Collapsed;
        ApplicaFiltro();
    }

    /// <summary>
    /// Dopo una riassegnazione (unione, nome dato, annulla) le foto sotto restano quelle della persona che
    /// si stava guardando, con l'elenco aggiornato: il filtro segue la persona, non l'insieme di foto di prima.
    /// Se la persona non esiste più il filtro si toglie.
    /// </summary>
    private void RiallineaFiltroPersona()
    {
        if (_filtroPersona is null || _filtroPersonaId is not long id) return;

        var persona = _personeCorrenti.FirstOrDefault(p => p.PersonaId == id);
        if (persona is null) { TogliFiltroPersona(); return; }

        _filtroPersona = (persona.Nome, persona.Foto);
        FiltroPersonaText.Text = persona.Nome;
        ApplicaFiltro();
    }

    /// <summary>Il filtro mostrava questo gruppo (stesse foto, o stessa persona): da ora segue la persona che lo ha assorbito.</summary>
    private void SeguiFiltroSe(PersonaUi gruppo, long nuovaPersonaId, string nuovoNome)
    {
        if (_filtroPersona is not { } f) return;
        var eraQuesto = _filtroPersonaId is null ? gruppo.SenzaNome && f.Foto.SetEquals(gruppo.Foto) : _filtroPersonaId == gruppo.PersonaId;
        if (!eraQuesto) return;
        _filtroPersonaId = nuovaPersonaId;
        _filtroPersona = (nuovoNome, f.Foto);
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
            Voce(menu.Items, T("Dai un nome…"), "", () => _ = DaiNomeAsync(persona, null));
            Voce(menu.Items, T("Ignora questi volti"), "", () => _ = AssegnaEAggiornaAsync(persona.Volti, null, StatoPersona.Rifiutato));
        }
        else
        {
            if (persona.Proposte.Count > 0)
                Voce(menu.Items, T("Conferma le proposte ({0})", persona.Proposte.Count), "", () => _ = ConfermaProposteAsync(persona));
            Voce(menu.Items, T("Rinomina…"), "", () => _ = DaiNomeAsync(persona, persona.Nome));
            Voce(menu.Items, T("Soprannome…"), "", () => _ = DaiSoprannomeAsync(persona));
            if (persona.MiniatureVolti.Count > 1)
                Voce(menu.Items, T("Cambia immagine…"), "", () => _ = CambiaImmagineAsync(persona));

            menu.Items.Add(new MenuFlyoutSeparator());
            var preferita = new ToggleMenuFlyoutItem { Text = T("Preferita"), IsChecked = persona.Preferita, Icon = new FontIcon { Glyph = "" } };
            preferita.Click += (_, _) => ImpostaImportanza(persona, () => Archivio.ImpostaPreferita(persona.PersonaId!.Value, !persona.Preferita));
            menu.Items.Add(preferita);

            var chiE = new MenuFlyoutSubItem { Text = T("Chi è"), Icon = new FontIcon { Glyph = "" } };
            foreach (var cerchia in new[] { CerchiaPersona.Io, CerchiaPersona.Famiglia, CerchiaPersona.Amico, CerchiaPersona.Conoscente, CerchiaPersona.Nessuna })
            {
                var voce = new RadioMenuFlyoutItem
                {
                    Text = cerchia == CerchiaPersona.Nessuna ? T("Non specificato") : EtichettaCerchia(cerchia, false)!,
                    GroupName = "cerchia",
                    IsChecked = persona.Cerchia == cerchia,
                };
                voce.Click += (_, _) => ImpostaImportanza(persona, () => Archivio.ImpostaCerchia(persona.PersonaId!.Value, cerchia));
                chiE.Items.Add(voce);
            }
            menu.Items.Add(chiE);
        }

        var altre = _personeCorrenti.Where(p => p != persona).ToList();
        if (altre.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            // Posizione relativa alla lista (l'elemento cliccato può essere riciclato a menu chiuso).
            var posizione = e.GetPosition(PersoneList);
            Voce(menu.Items, T("È la stessa persona di…"), "\uE71B", () => MostraSelettoreUnione(persona, altre, posizione));
        }

        menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
        e.Handled = true;
    }

    /// <summary>"Sono io", "Famiglia", "★ Amico"… sotto il nome nel pannello; null se non è stato detto nulla.</summary>
    private static string? EtichettaCerchia(int cerchia, bool preferita)
    {
        var nome = cerchia switch
        {
            CerchiaPersona.Io => T("Sono io"),
            CerchiaPersona.Famiglia => T("Famiglia"),
            CerchiaPersona.Amico => T("Amico"),
            CerchiaPersona.Conoscente => T("Conoscente"),
            _ => null,
        };
        if (!preferita) return nome;
        return nome is null ? "★" : "★ " + nome;
    }

    /// <summary>Stella o cerchia cambiata: l'elenco si riordina e le stelline automatiche si ricalcolano.</summary>
    private void ImpostaImportanza(PersonaUi persona, Action modifica)
    {
        if (persona.PersonaId is null) return;
        modifica();
        AggiornaPannelloPersone();
    }

    /// <summary>
    /// Elenco con le faccine (avatar tondo, nome, dettaglio) per scegliere con chi unire: nel sottomenu
    /// la voce può avere solo un'icona 16 px, troppo piccola per riconoscere un volto.
    /// </summary>
    private void MostraSelettoreUnione(PersonaUi persona, List<PersonaUi> altre, Windows.Foundation.Point posizione)
    {
        var flyout = new Flyout();
        var elenco = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxHeight = 380,
            MinWidth = 280,
        };

        foreach (var altra in altre)
        {
            var avatar = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x2A, 0x2A, 0x2A)),
                Child = new Image { Source = altra.Avatar, Stretch = Stretch.UniformToFill },
            };
            var testi = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            testi.Children.Add(new TextBlock { Text = altra.Nome, FontSize = 14 });
            testi.Children.Add(new TextBlock
            {
                Text = altra.Dettaglio, FontSize = 12,
                Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x99, 0x99, 0x99)),
            });

            var riga = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(0, 2, 0, 2), Tag = altra };
            riga.Children.Add(avatar);
            riga.Children.Add(testi);
            elenco.Items.Add(riga);
        }

        elenco.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is not FrameworkElement { Tag: PersonaUi scelta }) return;
            flyout.Hide();
            _ = UnisciPersoneAsync(persona, scelta);
        };

        var contenuto = new StackPanel { Spacing = 8 };
        contenuto.Children.Add(new TextBlock
        {
            Text = persona.SenzaNome ? T("Questo gruppo è la stessa persona di…") : T("«{0}» è la stessa persona di…", persona.Nome),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        contenuto.Children.Add(elenco);
        flyout.Content = contenuto;
        flyout.ShowAt(PersoneList, new FlyoutShowOptions { Position = posizione });
    }

    private async Task DaiSoprannomeAsync(PersonaUi persona)
    {
        if (persona.PersonaId is not long id) return;
        var casella = new TextBox { PlaceholderText = T("Soprannome"), Text = persona.Soprannome, Width = 300 };
        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = T("Soprannome di {0}", persona.Nome),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    casella,
                    new TextBlock
                    {
                        Text = T("Compare sotto il nome nell'elenco Persone. Lascia vuoto per toglierlo."),
                        TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray), MaxWidth = 320,
                    },
                },
            },
            PrimaryButtonText = T("Salva"),
            CloseButtonText = T("Annulla"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        Archivio.ImpostaSoprannome(id, casella.Text);
        AggiornaPannelloPersone();
    }

    /// <param name="suggerito">Nome già proposto o dato al volto (dal viewer): scritto nella casella.</param>
    private async Task DaiNomeAsync(PersonaUi persona, string? nomeAttuale, string? suggerito = null)
    {
        var nomiNoti = Archivio.Elenco().Select(p => p.Nome).ToList();
        var casella = new AutoSuggestBox { PlaceholderText = T("Nome della persona"), Text = nomeAttuale ?? suggerito ?? "", Width = 300 };
        casella.TextChanged += (s, a) =>
        {
            if (a.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            s.ItemsSource = nomiNoti.Where(n => n.Contains(s.Text, StringComparison.CurrentCultureIgnoreCase)).Take(8).ToList();
        };

        var contenuto = new StackPanel { Spacing = 12 };
        if (persona.Avatar is not null)
            contenuto.Children.Add(new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(48), Child = new Image { Source = persona.Avatar, Stretch = Stretch.UniformToFill } });
        contenuto.Children.Add(casella);
        var casellaSoprannome = new TextBox { PlaceholderText = T("Soprannome (facoltativo)"), Text = persona.Soprannome, Width = 300 };
        contenuto.Children.Add(casellaSoprannome);
        contenuto.Children.Add(new TextBlock
        {
            Text = nomeAttuale is null && persona.Volti.Count == 1
                ? T("Il volto prende questo nome (anche di una persona già nota); WFastPic lo proporrà anche sui volti somiglianti, in tutte le cartelle.")
                : nomeAttuale is null
                ? T("I {0} volti del gruppo prendono questo nome (anche di una persona già nota); WFastPic lo proporrà anche sui volti somiglianti, in tutte le cartelle.", persona.Volti.Count)
                : T("Rinomina la persona in tutte le cartelle. Con il nome di un'altra persona, le due si uniscono."),
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray), MaxWidth = 320,
        });

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = nomeAttuale is null ? T("Chi è?") : T("Rinomina"),
            Content = contenuto,
            PrimaryButtonText = T("Salva"),
            CloseButtonText = T("Annulla"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        var nome = casella.Text.Trim();
        if (nome.Length == 0) return;
        var soprannome = casellaSoprannome.Text.Trim();

        if (persona.PersonaId is long id)
        {
            if (nome == nomeAttuale)
            {
                // Solo il soprannome è cambiato.
                if (soprannome != persona.Soprannome)
                {
                    Archivio.ImpostaSoprannome(id, soprannome);
                    AggiornaPannelloPersone();
                }
                return;
            }

            // Il nome di un'altra persona: è la stessa, confluisce in lei (come trascinare un volto in Picasa).
            var omonima = Archivio.Elenco().FirstOrDefault(p => p.Id != id && string.Equals(p.Nome, nome, StringComparison.CurrentCultureIgnoreCase));
            if (omonima is not null)
            {
                var db = _db;
                if (db is not null) await UnisciInAsync(db, persona, omonima.Id, omonima.Nome);
                if (soprannome.Length > 0) Archivio.ImpostaSoprannome(omonima.Id, soprannome);
                return;
            }

            try { Archivio.Rinomina(id, nome); Archivio.ImpostaSoprannome(id, soprannome); }
            catch (Microsoft.Data.Sqlite.SqliteException) { StatusText.Text = T("Esiste già una persona chiamata «{0}».", nome); return; }
            if (_filtroPersona is { } f && f.Nome == nomeAttuale) FiltroPersonaText.Text = nome;
            AggiornaPannelloPersone();
            return;
        }

        // Volto che ha già questo nome (confermato, non solo proposto): niente da cambiare.
        if (string.Equals(nome, suggerito, StringComparison.CurrentCultureIgnoreCase) && persona.Proposte.Count == 0) return;

        var dbCorrente = _db;
        if (dbCorrente is null) return;
        var esisteva = Archivio.EsisteNome(nome);
        var nuovaId = Archivio.TrovaOCrea(nome, persona.AvatarJpeg);
        if (soprannome.Length > 0) Archivio.ImpostaSoprannome(nuovaId, soprannome);
        var campioniPrima = Archivio.Elenco().FirstOrDefault(p => p.Id == nuovaId)?.Campioni.ToList() ?? [];
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();
        var voltiPrima = await Task.Run(() => FotografaVolti(dbCorrente));
        AggiungiCampioni(nuovaId, persona.Impronte);
        SeguiFiltroSe(persona, nuovaId, nome);
        await AssegnaEAggiornaAsync(persona.Volti, nuovaId, StatoPersona.Confermato);

        var annulla = persona.Volti.Count == 1 ? T("Nome «{0}» dato a un volto", nome) : T("Nome «{0}» dato a {1} volti", nome, persona.Volti.Count);
        ImpostaAnnulla(dbCorrente, annulla, async () =>
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
            CloseButtonText = T("Annulla"),
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
                Nome = T("Senza nome"),
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
            SeguiFiltroSe(senzaNome, id, conNome.Nome);
            await AssegnaEAggiornaAsync(senzaNome.Volti, id, StatoPersona.Confermato);
            ImpostaAnnulla(db, T("Gruppo senza nome unito a «{0}»", conNome.Nome), async () =>
            {
                await Task.Run(() => RipristinaVolti(db, voltiPrima));
                Archivio.ImpostaCampioni(id, campioniPrima);
            });
            return;
        }

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = T("Unisci le persone"),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, MaxWidth = 380,
                Text = T("«{0}» e «{1}» sono la stessa persona? Scegli il nome da tenere: " +
                       "l'unione vale in tutte le cartelle e puoi annullarla subito dopo.", cliccata.Nome, altra.Nome),
            },
            PrimaryButtonText = T("Tieni «{0}»", cliccata.Nome),
            SecondaryButtonText = T("Tieni «{0}»", altra.Nome),
            CloseButtonText = T("Annulla"),
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
        SeguiFiltroSe(assorbita, tieniId, tieniNome);

        ImpostaAnnulla(db, T("«{0}» unita a «{1}»", assorbita.Nome, tieniNome), () =>
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
                T("Eliminare «{0}»?", persona.Nome),
                T("Il nome viene tolto da tutte le foto, in tutte le cartelle: i volti tornano senza nome. Le foto non vengono toccate."),
                T("Elimina")))
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
            Title = T("Immagine di {0}", persona.Nome),
            Content = scelte,
            PrimaryButtonText = T("Usa questa"),
            CloseButtonText = T("Annulla"),
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
                T("Rianalizzare le persone di questa cartella?"),
                T("Volti e impronte di tutte le foto della cartella vengono ricalcolati da zero, in background. " +
                "Le persone con un nome restano: i loro volti verranno riproposti in automatico, da confermare."),
                T("Rianalizza")))
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

    // ---------------------------------------------------------------- volti nei dettagli del viewer

    async Task<IReadOnlyList<VoltoDellaFoto>?> IViewerHost.VoltiDellaFotoAsync(PhotoGridItem foto)
    {
        var db = _db;
        if (db is null || _riconoscimentoPersone != true || !foto.PersoneAnalizzate) return null;
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();

        // Nomi dal pannello (già con le unioni risolte); un id unito si porta sulla persona che resta.
        var perId = _personeCorrenti.Where(p => p.PersonaId is not null).ToDictionary(p => p.PersonaId!.Value);
        var archivio = Archivio;
        try
        {
            return await Task.Run(() =>
            {
                var unioni = archivio.Unioni();
                return db.GetVoltiPersone(foto.Id)
                    .Where(v => v.Impronta is not null && !(v.PersonaId is null && v.Stato == StatoPersona.Rifiutato))
                    .Select(v =>
                    {
                        var id = v.PersonaId is long p && unioni.TryGetValue(p, out var resta) ? resta : v.PersonaId;
                        var persona = id is long pid && v.Stato is StatoPersona.Proposto or StatoPersona.Confermato
                            ? perId.GetValueOrDefault(pid) : null;
                        return new VoltoDellaFoto(v.Id, v.PhotoId, v.Miniatura, Impronte.DaByte(v.Impronta!),
                            persona?.Nome, v.Stato == StatoPersona.Proposto, persona?.Cerchia ?? CerchiaPersona.Nessuna,
                            persona?.Preferita ?? false);
                    })
                    .ToList();
            });
        }
        catch (ObjectDisposedException)
        {
            return null; // cartella cambiata nel frattempo
        }
    }

    /// <summary>Come scrivere il nome sotto un volto in Picasa: stesso "Chi è?" del pannello, per un volto solo.</summary>
    async Task IViewerHost.DaiNomeAlVoltoAsync(VoltoDellaFoto volto)
    {
        var persona = new PersonaUi
        {
            Nome = T("Senza nome"),
            Dettaglio = "",
            PersonaId = null,
            Volti = [volto.Id],
            Proposte = volto.Proposto ? [volto.Id] : [],
            Foto = [volto.PhotoId],
            Impronte = [volto.Impronta],
            AvatarJpeg = volto.Miniatura,
        };
        if (volto.Miniatura is not null) persona.Avatar = await CreaImmagineAsync(volto.Miniatura);

        try
        {
            await DaiNomeAsync(persona, null, volto.Nome);
        }
        catch (Exception)
        {
            // Un altro dialogo è già aperto.
        }
    }
}
