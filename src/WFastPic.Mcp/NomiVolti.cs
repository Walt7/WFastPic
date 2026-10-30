using System.Text.Json;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;

namespace WFastPic.Mcp;

/// <summary>Un volto cambiato da un'operazione: com'era prima e com'è diventato.</summary>
public sealed record VoltoCambiato(string Sidecar, long Id, long? PersonaPrima, int StatoPrima, long? PersonaDopo, int StatoDopo);

/// <summary>Quanto serve per tornare indietro: volti cambiati e com'era la persona (campioni, soprannome, cerchia).</summary>
public sealed record OperazioneVolti(
    int Numero, DateTime Quando, string Descrizione, long? PersonaId, bool PersonaCreata,
    List<string>? CampioniPrima, string? SoprannomePrima, int? CerchiaPrima,
    List<VoltoCambiato> Volti, bool Annullata = false);

public sealed record EsitoNomi(int Operazione, string? Persona, int Volti, int ProposteNuove);

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
        var esisteva = archivio.EsisteNome(nome);
        var avatar = esisteva ? null : volti.OrderByDescending(v => (v.Miniatura?.Length ?? 0) * Math.Sqrt(Math.Max(1e-6, (v.X1 - v.X0) * (v.Y1 - v.Y0)))).First().Miniatura;
        var id = archivio.TrovaOCrea(nome, avatar);
        var prima = archivio.Elenco().First(p => p.Id == id);

        var esito = Esegui(archivio, $"nome «{nome}» a {volti.Count} volti", id, !esisteva, prima, sidecar =>
        {
            foreach (var g in volti.GroupBy(v => v.Sidecar, StringComparer.OrdinalIgnoreCase))
                sidecar(g.Key).AssegnaPersona(g.Select(v => v.Id).ToList(), id, StatoPersona.Confermato);
        }, () =>
        {
            GestionePersone.AggiungiCampioni(archivio, id, volti.Select(v => v.Impronta));
            if (!string.IsNullOrWhiteSpace(soprannome)) archivio.ImpostaSoprannome(id, soprannome.Trim());
            if (cerchia >= 0) archivio.ImpostaCerchia(id, cerchia);
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

        var esito = Esegui(archivio, $"conferma {scelti.Count} volti di «{trovata.Nome}»", trovata.Id, false, trovata, sidecar =>
        {
            foreach (var g in scelti.GroupBy(v => v.Sidecar, StringComparer.OrdinalIgnoreCase))
                sidecar(g.Key).AssegnaPersona(g.Select(v => v.Id).ToList(), trovata.Id, StatoPersona.Confermato);
        }, () => GestionePersone.AggiungiCampioni(archivio, trovata.Id, scelti.Select(v => v.Impronta)));
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
        return Esegui(archivio, $"ignora {volti.Count} volti", null, false, null, sidecar =>
        {
            foreach (var g in volti.GroupBy(v => v.Sidecar, StringComparer.OrdinalIgnoreCase))
                sidecar(g.Key).AssegnaPersona(g.Select(v => v.Id).ToList(), null, StatoPersona.Rifiutato);
        }, null);
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

            if (op.PersonaId is long id)
            {
                using var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone);
                if (op.PersonaCreata) archivio.Elimina(id);
                else if (archivio.Elenco().Any(p => p.Id == id))
                {
                    if (op.CampioniPrima is not null) archivio.ImpostaCampioni(id, op.CampioniPrima.Select(Convert.FromBase64String).ToList());
                    if (op.SoprannomePrima is not null) archivio.ImpostaSoprannome(id, op.SoprannomePrima);
                    if (op.CerchiaPrima is int c) archivio.ImpostaCerchia(id, c);
                }
            }

            operazioni[operazioni.IndexOf(op)] = op with { Annullata = true };
            ScriviOperazioni(operazioni);
            Volti.Invalida();
            RegistroAttivita.Scrivi("mcp.volti.annulla", $"operazione {op.Numero} ({op.Descrizione}): {rimessi} volti rimessi com'erano" +
                (cambiatiDopo > 0 ? $", {cambiatiDopo} lasciati perché cambiati dopo" : ""));
            return $"Annullata l'operazione {op.Numero} ({op.Descrizione}): {rimessi} volti tornati com'erano" +
                   (cambiatiDopo > 0 ? $"; {cambiatiDopo} lasciati com'erano perché cambiati dopo." : ".") +
                   (op.PersonaCreata ? " La persona creata è stata tolta." : "");
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
    /// Scrive (<paramref name="assegna"/> sui sidecar, <paramref name="aggiornaPersona"/> su persone.db), poi
    /// ripropone la persona su tutte le cartelle catalogate e salva cosa è cambiato per poterlo annullare.
    /// </summary>
    private static EsitoNomi Esegui(
        ArchivioPersone archivio, string descrizione, long? personaId, bool personaCreata, PersonaArchivio? prima,
        Action<Func<string, LibrarySidecarDatabase>> assegna, Action? aggiornaPersona)
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
                if (personaId is not null)
                    foreach (var cartella in Volti.CartelleNote()) Sidecar(cartella);

                assegna(Sidecar);
                aggiornaPersona?.Invoke();

                if (personaId is long id && GestionePersone.PersoneNote(archivio).FirstOrDefault(p => p.Id == id) is { } nota)
                    foreach (var db in aperti.Values) GestionePersone.Riproponi(db, [nota]);

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

                var operazioni = LeggiOperazioni();
                var numero = operazioni.Count > 0 ? operazioni.Max(o => o.Numero) + 1 : 1;
                operazioni.Add(new OperazioneVolti(
                    numero, DateTime.Now, descrizione, personaId, personaCreata,
                    personaCreata ? null : prima?.Campioni.Select(Convert.ToBase64String).ToList(),
                    personaCreata ? null : prima?.Soprannome, personaCreata ? null : prima?.Cerchia,
                    cambiati));
                ScriviOperazioni(operazioni.Count > OperazioniTenute ? operazioni.Skip(operazioni.Count - OperazioniTenute).ToList() : operazioni);
                Volti.Invalida();

                var nuoveProposte = cambiati.Where(c => c.StatoDopo == StatoPersona.Proposto && c.StatoPrima != StatoPersona.Proposto).ToList();
                var scelti = cambiati.Count - nuoveProposte.Count;
                RegistroAttivita.Scrivi("mcp.volti", $"operazione {numero}: {descrizione} (persona {personaId?.ToString() ?? "-"}), " +
                    $"{scelti} volti cambiati, {nuoveProposte.Count} proposte nuove");
                return new EsitoNomi(numero, null, scelti, nuoveProposte.Count);
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
