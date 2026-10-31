using System.Text.Json;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;

namespace WFastPic.Mcp;

/// <summary>Un volto cambiato da un'operazione: com'era prima e com'è diventato.</summary>
public sealed record VoltoCambiato(string Sidecar, long Id, long? PersonaPrima, int StatoPrima, long? PersonaDopo, int StatoDopo);

/// <summary>Com'era una persona prima dell'operazione, o se l'operazione l'ha creata.</summary>
public sealed record PersonaPrima(long Id, bool Creata, List<string>? Campioni, string? Soprannome, int? Cerchia);

/// <summary>Quanto serve per tornare indietro: volti cambiati e com'erano le persone toccate.</summary>
public sealed record OperazioneVolti(
    int Numero, DateTime Quando, string Descrizione, List<PersonaPrima> Persone, List<VoltoCambiato> Volti, bool Annullata = false);

public sealed record EsitoNomi(int Operazione, string? Persona, int Volti, int ProposteNuove);

/// <summary>Cosa farebbe l'import da Picasa: piano, volti da nominare, nomi (Picasa → da usare), email e nomi forse uguali.</summary>
public sealed record AnteprimaPicasa(
    PianoPicasa<VoltoLetto> Piano, int DaNominare, int GiaConfermati,
    IReadOnlyList<(string Picasa, string? DaUsare, int Volti)> Nomi, IReadOnlyList<string> Email, IReadOnlyList<(string A, string B)> ForseUguali);

/// <summary>
/// Le scritture dell'assistente sui volti, come le fa l'app dal pannello Persone: dare un nome a un gruppo o a dei
/// volti (anche di una persona già nota), confermare le proposte, ignorare volti di passanti. Dopo ogni nome il
/// riconoscimento ripropone quella persona nei volti ancora senza nome di tutte le cartelle catalogate. Ogni
/// operazione finisce nel registro delle attività e si può annullare (<see cref="Annulla"/>): lo stato di prima è in
/// <c>annullamenti-volti.json</c> accanto a persone.db. Le foto non vengono mai toccate.
/// </summary>
public static class NomiVolti
{
    public const int OperazioniTenute = 50;

    private static readonly Lock Lucchetto = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static string FileAnnullamenti => Path.Combine(Ricerca.DatiApp, "annullamenti-volti.json");

    public static int Cerchia(string? chiE) => chiE?.Trim().ToLowerInvariant() switch
    {
        null or "" => -1,
        "io" => CerchiaPersona.Io,
        "famiglia" => CerchiaPersona.Famiglia,
        "amico" or "amica" or "amici" => CerchiaPersona.Amico,
        "conoscente" => CerchiaPersona.Conoscente,
        "nessuno" or "nessuna" => CerchiaPersona.Nessuna,
        _ => throw new ArgumentException($"«{chiE}» non va bene per chi_e: usa io, famiglia, amico, conoscente o nessuno."),
    };

    /// <summary>
    /// Dà il nome ai volti (confermati). Se esiste già una persona con quel nome i volti vanno a lei, che così ha
    /// più campioni; altrimenti la persona si crea, con il volto più nitido come immagine.
    /// </summary>
    public static EsitoNomi AssegnaNome(string nome, IReadOnlyList<string> rifs, string? soprannome = null, string? chiE = null)
    {
        nome = nome.Trim();
        if (nome.Length == 0) throw new ArgumentException("Manca il nome.");
        if (rifs.Count == 0) throw new ArgumentException("Nessun volto indicato (gruppo o volti).");
        var cerchia = Cerchia(chiE);

        using var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone);
        var volti = LeggiVolti(rifs);
        var avatar = archivio.EsisteNome(nome) ? null : volti.OrderByDescending(v => (v.Miniatura?.Length ?? 0) * Math.Sqrt(Math.Max(1e-6, (v.X1 - v.X0) * (v.Y1 - v.Y0)))).First().Miniatura;
        long id = 0;
        var esito = Esegui(archivio, $"nome «{nome}» a {volti.Count} volti", riproponi: true, sidecar =>
        {
            id = archivio.TrovaOCrea(nome, avatar);
            foreach (var g in volti.GroupBy(v => v.Sidecar, StringComparer.OrdinalIgnoreCase))
                sidecar(g.Key).AssegnaPersona(g.Select(v => v.Id).ToList(), id, StatoPersona.Confermato);
            GestionePersone.AggiungiCampioni(archivio, id, volti.Select(v => v.Impronta));
            if (!string.IsNullOrWhiteSpace(soprannome)) archivio.ImpostaSoprannome(id, soprannome.Trim());
            if (cerchia >= 0) archivio.ImpostaCerchia(id, cerchia);
            return [id];
        });
        return esito with { Persona = archivio.Elenco().First(p => p.Id == id).Nome };
    }

    /// <summary>Conferma i volti proposti per una persona: tutti, o solo quelli indicati.</summary>
    public static EsitoNomi Conferma(string persona, IReadOnlyList<string>? rifs, bool tutte)
    {
        using var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone);
        var trovata = TrovaPersona(archivio, persona);
        var proposte = Volti.Leggi().Where(v => v.Stato == StatoPersona.Proposto && v.PersonaId == trovata.Id).ToList();
        List<VoltoLetto> scelti;
        if (rifs is { Count: > 0 })
        {
            var perRif = proposte.ToDictionary(v => v.Rif, StringComparer.OrdinalIgnoreCase);
            var mancanti = rifs.Where(r => !perRif.ContainsKey(r.Trim())).ToList();
            if (mancanti.Count > 0)
                throw new ArgumentException($"Questi volti non sono proposte di «{trovata.Nome}»: {string.Join(", ", mancanti)}.");
            scelti = rifs.Select(r => perRif[r.Trim()]).DistinctBy(v => v.Rif).ToList();
        }
        else if (tutte) scelti = proposte;
        else throw new ArgumentException("Indica i volti da confermare, oppure tutte=true per confermare tutte le proposte (meglio dopo averle guardate con mostra_volto).");
        if (scelti.Count == 0) throw new InvalidOperationException($"Nessuna proposta da confermare per «{trovata.Nome}».");

        var esito = Esegui(archivio, $"conferma {scelti.Count} volti di «{trovata.Nome}»", riproponi: true, sidecar =>
        {
            foreach (var g in scelti.GroupBy(v => v.Sidecar, StringComparer.OrdinalIgnoreCase))
                sidecar(g.Key).AssegnaPersona(g.Select(v => v.Id).ToList(), trovata.Id, StatoPersona.Confermato);
            GestionePersone.AggiungiCampioni(archivio, trovata.Id, scelti.Select(v => v.Impronta));
            return [trovata.Id];
        });
        return esito with { Persona = trovata.Nome };
    }

    /// <summary>
    /// Come «Ignora questi volti» nell'app: passanti, volti in un quadro, errori del rilevatore. Non compaiono più tra
    /// i gruppi da nominare né tra le proposte; si possono rimettere con <see cref="Annulla"/>.
    /// </summary>
    public static EsitoNomi Ignora(IReadOnlyList<string> rifs)
    {
        if (rifs.Count == 0) throw new ArgumentException("Nessun volto indicato (gruppo o volti).");
        using var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone);
        var volti = LeggiVolti(rifs);
        return Esegui(archivio, $"ignora {volti.Count} volti", riproponi: false, sidecar =>
        {
            foreach (var g in volti.GroupBy(v => v.Sidecar, StringComparer.OrdinalIgnoreCase))
                sidecar(g.Key).AssegnaPersona(g.Select(v => v.Id).ToList(), null, StatoPersona.Rifiutato);
            return [];
        });
    }

    /// <summary>
    /// Nomi dei volti marcati in Picasa (file Picasa.ini sotto <paramref name="cartella"/>) abbinati ai volti trovati da
    /// WFastPic. Senza <paramref name="esegui"/> è solo un'anteprima; con <paramref name="esegui"/> i nomi diventano
    /// confermati (una sola operazione da annullare) e il riconoscimento propone quelle persone nel resto dell'archivio.
    /// <paramref name="nomi"/> cambia il nome di Picasa in quello da usare (anche di una persona già nota); «» = non
    /// importarlo. I nomi che sono un indirizzo email si saltano se non hanno un nome qui.
    /// </summary>
    public static (AnteprimaPicasa Anteprima, EsitoNomi? Esito) ImportaPicasa(string cartella, IReadOnlyDictionary<string, string> nomi, bool esegui)
    {
        if (string.IsNullOrWhiteSpace(cartella) || !Path.IsPathRooted(cartella) || !Directory.Exists(cartella))
            throw new ArgumentException("Indica il percorso completo di una cartella esistente (es. E:\\Fotocamera\\2012).");
        var radice = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella));

        var volti = Volti.Leggi(radice);
        var piano = VoltiPicasa.Pianifica(radice,
            volti.Select(v => (v, v.Foto, new Riquadro(v.X0, v.Y0, v.X1, v.Y1))).ToList(),
            VoltiPicasa.LeggiContattiXml(VoltiPicasa.ContattiXmlPredefinito));

        var mappa = nomi.ToDictionary(kv => VoltiPicasa.Chiave(kv.Key), kv => kv.Value.Trim());
        string? NomeDaUsare(string nome) => mappa.TryGetValue(VoltiPicasa.Chiave(nome), out var scelto)
            ? (scelto.Length > 0 ? scelto : null)
            : VoltiPicasa.EEmail(nome) ? null : nome;

        var daScrivere = piano.Abbinati.Select(a => (a.Volto, Nome: NomeDaUsare(a.Nome))).Where(a => a.Nome is not null).ToList();
        var anteprima = new AnteprimaPicasa(
            piano, daScrivere.Count,
            daScrivere.Count(a => a.Volto.Stato == StatoPersona.Confermato),
            piano.PerNome.Select(n => (n.Nome, NomeDaUsare(n.Nome), n.Volti)).ToList(),
            piano.PerNome.Select(n => n.Nome).Where(n => VoltiPicasa.EEmail(n) && !mappa.ContainsKey(VoltiPicasa.Chiave(n))).ToList(),
            VoltiPicasa.ForseUguali(daScrivere.Select(a => a.Nome!).Distinct()));
        if (!esegui || daScrivere.Count == 0) return (anteprima, null);

        using var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone);
        var esito = Esegui(archivio, $"import da Picasa di {radice}: {daScrivere.Count} volti", riproponi: true, sidecar =>
        {
            var toccate = new HashSet<long>();
            foreach (var g in daScrivere.GroupBy(a => a.Volto.Sidecar, StringComparer.OrdinalIgnoreCase))
                toccate.UnionWith(GestionePersone.AssegnaNomi(sidecar(g.Key), archivio, g.Select(a => (a.Volto.Id, a.Nome!))).Persone);
            return toccate;
        });
        return (anteprima, esito);
    }

    /// <summary>Riporta i volti (e la persona) com'erano prima dell'operazione; senza numero, l'ultima non annullata.</summary>
    public static string Annulla(int? numero)
    {
        lock (Lucchetto)
        {
            var operazioni = LeggiOperazioni();
            var op = numero is int n
                ? operazioni.FirstOrDefault(o => o.Numero == n) ?? throw new ArgumentException($"Operazione {n} non trovata (si tengono le ultime {OperazioniTenute}).")
                : operazioni.LastOrDefault(o => !o.Annullata) ?? throw new InvalidOperationException("Nessuna operazione da annullare.");
            if (op.Annullata) throw new InvalidOperationException($"L'operazione {op.Numero} è già stata annullata.");

            var rimessi = 0;
            var cambiatiDopo = 0;
            foreach (var g in op.Volti.GroupBy(v => v.Sidecar, StringComparer.OrdinalIgnoreCase))
            {
                if (!Ricerca.HaSidecar(g.Key)) continue;
                using var db = LibrarySidecarDatabase.OpenOrCreate(g.Key);
                var ora = db.GetStatiVolti().ToDictionary(v => v.Id);
                // Un volto toccato di nuovo dopo (dall'app o da un'altra operazione) resta com'è.
                var daRimettere = g.Where(v => ora.TryGetValue(v.Id, out var s) && s.PersonaId == v.PersonaDopo && s.Stato == v.StatoDopo).ToList();
                cambiatiDopo += g.Count() - daRimettere.Count;
                foreach (var stato in daRimettere.GroupBy(v => (v.PersonaPrima, v.StatoPrima)))
                {
                    db.AssegnaPersona(stato.Select(v => v.Id).ToList(), stato.Key.PersonaPrima, stato.Key.StatoPrima);
                    rimessi += stato.Count();
                }
            }

            if (op.Persone.Count > 0)
            {
                using var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone);
                var esistenti = archivio.Elenco().Select(p => p.Id).ToHashSet();
                foreach (var p in op.Persone)
                {
                    if (p.Creata) archivio.Elimina(p.Id);
                    else if (esistenti.Contains(p.Id))
                    {
                        if (p.Campioni is not null) archivio.ImpostaCampioni(p.Id, p.Campioni.Select(Convert.FromBase64String).ToList());
                        if (p.Soprannome is not null) archivio.ImpostaSoprannome(p.Id, p.Soprannome);
                        if (p.Cerchia is int c) archivio.ImpostaCerchia(p.Id, c);
                    }
                }
            }

            operazioni[operazioni.IndexOf(op)] = op with { Annullata = true };
            ScriviOperazioni(operazioni);
            Volti.Invalida();
            RegistroAttivita.Scrivi("mcp.volti.annulla", $"operazione {op.Numero} ({op.Descrizione}): {rimessi} volti rimessi com'erano" +
                (cambiatiDopo > 0 ? $", {cambiatiDopo} lasciati perché cambiati dopo" : ""));
            return $"Annullata l'operazione {op.Numero} ({op.Descrizione}): {rimessi} volti tornati com'erano" +
                   (cambiatiDopo > 0 ? $"; {cambiatiDopo} lasciati com'erano perché cambiati dopo." : ".") +
                   (op.Persone.Count(p => p.Creata) switch { 0 => "", 1 => " La persona creata è stata tolta.", var quante => $" Le {quante} persone create sono state tolte." });
        }
    }

    public static List<OperazioneVolti> Operazioni()
    {
        lock (Lucchetto) return LeggiOperazioni();
    }

    // ---------------------------------------------------------------- interni

    private sealed record VoltoDaScrivere(string Sidecar, long Id, float[] Impronta, byte[]? Miniatura, double X0, double Y0, double X1, double Y1);

    private static PersonaArchivio TrovaPersona(ArchivioPersone archivio, string testo)
    {
        testo = testo.Trim();
        var elenco = archivio.Elenco();
        var esatta = elenco.Where(p => string.Equals(p.Nome, testo, StringComparison.CurrentCultureIgnoreCase)
                                       || string.Equals(p.Soprannome, testo, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (esatta.Count == 1) return esatta[0];
        var simili = elenco.Where(p => p.Nome.Contains(testo, StringComparison.CurrentCultureIgnoreCase)
                                       || p.Soprannome.Contains(testo, StringComparison.CurrentCultureIgnoreCase)).ToList();
        return simili.Count switch
        {
            1 => simili[0],
            0 => throw new ArgumentException($"Nessuna persona chiamata «{testo}»."),
            _ => throw new ArgumentException($"«{testo}» può essere: {string.Join(", ", simili.Select(p => p.Nome))}. Scrivi il nome completo."),
        };
    }

    private static List<VoltoDaScrivere> LeggiVolti(IReadOnlyList<string> rifs)
    {
        var risultato = new List<VoltoDaScrivere>();
        foreach (var g in rifs.Select(Volti.Risolvi).Distinct().GroupBy(r => r.Sidecar, StringComparer.OrdinalIgnoreCase))
        {
            using var c = Ricerca.ApriLettura(Path.Combine(g.Key, LibrarySidecarDatabase.CartellaSidecar, LibrarySidecarDatabase.NomeFileDatabase));
            foreach (var (_, id) in g)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT impronta, miniatura, x0, y0, x1, y1 FROM volti WHERE rowid = $id AND impronta IS NOT NULL;";
                cmd.Parameters.AddWithValue("$id", id);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new ArgumentException($"Volto «{Volti.Rif(g.Key, id)}» non trovato: forse la foto è stata rianalizzata. Richiama volti_da_nominare.");
                risultato.Add(new VoltoDaScrivere(g.Key, id, Impronte.DaByte((byte[])r[0]), r.IsDBNull(1) ? null : (byte[])r[1],
                    r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5)));
            }
        }
        return risultato;
    }

    /// <summary>
    /// Scrive (<paramref name="scrivi"/>, che restituisce le persone toccate), poi ripropone quelle persone su tutte le
    /// cartelle catalogate e salva cosa è cambiato (volti e persone) per poterlo annullare.
    /// </summary>
    private static EsitoNomi Esegui(
        ArchivioPersone archivio, string descrizione, bool riproponi,
        Func<Func<string, LibrarySidecarDatabase>, IReadOnlyCollection<long>> scrivi)
    {
        lock (Lucchetto)
        {
            var aperti = new Dictionary<string, LibrarySidecarDatabase>(StringComparer.OrdinalIgnoreCase);
            var statiPrima = new Dictionary<string, Dictionary<long, (long Id, long? PersonaId, int Stato)>>(StringComparer.OrdinalIgnoreCase);
            LibrarySidecarDatabase Sidecar(string cartella)
            {
                if (aperti.TryGetValue(cartella, out var db)) return db;
                if (!Ricerca.HaSidecar(cartella)) throw new InvalidOperationException($"La cartella «{cartella}» non ha più il suo catalogo (.wfastpic).");
                db = LibrarySidecarDatabase.OpenOrCreate(cartella);
                aperti[cartella] = db;
                statiPrima[cartella] = db.GetStatiVolti().ToDictionary(v => v.Id);
                return db;
            }

            try
            {
                // Tutte le cartelle prima di scrivere: anche le proposte nuove vanno ricordate per l'annulla.
                if (riproponi)
                    foreach (var cartella in Volti.CartelleNote()) Sidecar(cartella);
                var personePrima = archivio.Elenco().ToDictionary(p => p.Id);

                var toccate = scrivi(Sidecar);

                var note = GestionePersone.PersoneNote(archivio).Where(p => toccate.Contains(p.Id)).ToList();
                if (riproponi && note.Count > 0)
                    foreach (var db in aperti.Values) GestionePersone.Riproponi(db, note);

                var cambiati = new List<VoltoCambiato>();
                foreach (var (cartella, db) in aperti)
                {
                    var primaQui = statiPrima[cartella];
                    foreach (var dopo in db.GetStatiVolti())
                    {
                        if (primaQui.TryGetValue(dopo.Id, out var p) && (p.PersonaId != dopo.PersonaId || p.Stato != dopo.Stato))
                            cambiati.Add(new VoltoCambiato(cartella, dopo.Id, p.PersonaId, p.Stato, dopo.PersonaId, dopo.Stato));
                    }
                }

                var persone = toccate.Distinct().Select(id => personePrima.TryGetValue(id, out var p)
                    ? new PersonaPrima(id, false, p.Campioni.Select(Convert.ToBase64String).ToList(), p.Soprannome, p.Cerchia)
                    : new PersonaPrima(id, true, null, null, null)).ToList();
                var operazioni = LeggiOperazioni();
                var numero = operazioni.Count > 0 ? operazioni.Max(o => o.Numero) + 1 : 1;
                operazioni.Add(new OperazioneVolti(numero, DateTime.Now, descrizione, persone, cambiati));
                ScriviOperazioni(operazioni.Count > OperazioniTenute ? operazioni.Skip(operazioni.Count - OperazioniTenute).ToList() : operazioni);
                Volti.Invalida();

                var nuoveProposte = cambiati.Count(c => c.StatoDopo == StatoPersona.Proposto && c.StatoPrima != StatoPersona.Proposto);
                var scelti = cambiati.Count - nuoveProposte;
                RegistroAttivita.Scrivi("mcp.volti", $"operazione {numero}: {descrizione} ({persone.Count} persone, {persone.Count(p => p.Creata)} nuove), " +
                    $"{scelti} volti cambiati, {nuoveProposte} proposte nuove");
                return new EsitoNomi(numero, null, scelti, nuoveProposte);
            }
            catch (Exception ex)
            {
                RegistroAttivita.Errore("mcp.volti", ex, descrizione);
                throw;
            }
            finally
            {
                foreach (var db in aperti.Values) db.Dispose();
            }
        }
    }

    private static List<OperazioneVolti> LeggiOperazioni()
    {
        try
        {
            return File.Exists(FileAnnullamenti)
                ? JsonSerializer.Deserialize<List<OperazioneVolti>>(File.ReadAllText(FileAnnullamenti), Json) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }

    private static void ScriviOperazioni(List<OperazioneVolti> operazioni)
    {
        Directory.CreateDirectory(Ricerca.DatiApp);
        var temporaneo = FileAnnullamenti + ".tmp";
        File.WriteAllText(temporaneo, JsonSerializer.Serialize(operazioni, Json));
        File.Move(temporaneo, FileAnnullamenti, overwrite: true);
    }
}
