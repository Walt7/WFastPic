using WFastPic.Core.Persistence;

namespace WFastPic.Core.Persone;

/// <summary>Esito di <see cref="GestionePersone.AssegnaNomi"/>: persone toccate e create, volti cambiati, volti lasciati com'erano.</summary>
public sealed record EsitoNomiImportati(IReadOnlyList<long> Persone, IReadOnlyList<long> Create, int Assegnati, int InConflitto);

/// <summary>
/// I passi comuni tra l'app e il server MCP quando si danno nomi ai volti: persone note con i loro campioni,
/// campioni aggiunti dopo un nome o una conferma, proposte automatiche sui volti ancora senza nome e volti
/// appena analizzati pronti per il sidecar. Solo dati: nessuna interfaccia.
/// </summary>
public static class GestionePersone
{
    public static List<PersonaNota> PersoneNote(ArchivioPersone archivio) =>
        archivio.Elenco()
            .Select(p => new PersonaNota(p.Id, p.Nome, p.Campioni.Select(Impronte.DaByte).ToList()))
            .ToList();

    /// <summary>Aggiunge ai campioni della persona le impronte dei volti a cui l'utente ha dato il suo nome.</summary>
    public static void AggiungiCampioni(ArchivioPersone archivio, long personaId, IEnumerable<float[]> nuovi)
    {
        var esistenti = archivio.Elenco().FirstOrDefault(p => p.Id == personaId)?.Campioni.Select(Impronte.DaByte).ToList() ?? [];
        var campioni = RiconoscimentoPersone.AggiornaCampioni(esistenti, nuovi);
        archivio.ImpostaCampioni(personaId, campioni.Select(Impronte.InByte).ToList());
    }

    /// <summary>
    /// Propone la persona nota più somigliante ai volti ancora senza nome (da confermare a mano); restituisce
    /// quanti volti hanno ricevuto una proposta. Con le sole persone appena nominate basta confrontare loro.
    /// </summary>
    public static int Riproponi(LibrarySidecarDatabase db, IReadOnlyList<PersonaNota> persone)
    {
        if (persone.All(p => p.Campioni.Count == 0)) return 0;
        // Con centinaia di persone (es. dopo l'import da Picasa) confrontare ogni volto con tutti i campioni costa troppo:
        // prima si scelgono le persone più vicine per media dei campioni, poi il confronto vero solo con quelle.
        var centroidi = persone.Count > CandidatiPerVolto * 4 ? persone.Select(Centroide).ToList() : null;
        IReadOnlyList<PersonaNota> Candidati(float[] impronta) => centroidi is null
            ? persone
            : persone.Select((p, i) => (p, s: Impronte.Somiglianza(impronta, centroidi[i])))
                .OrderByDescending(x => x.s).Take(CandidatiPerVolto).Select(x => x.p).ToList();

        var proposti = 0;
        foreach (var proposta in db.GetImpronteSenzaNome()
                     .Select(v => (v.Id, Impronta: Impronte.DaByte(v.Impronta)))
                     .Select(v => (v.Id, Persona: RiconoscimentoPersone.Riconosci(v.Impronta, Candidati(v.Impronta))?.Persona))
                     .Where(x => x.Persona is not null)
                     .GroupBy(x => x.Persona!.Id))
        {
            var volti = proposta.Select(x => x.Id).ToList();
            db.ProponiPersona(volti, proposta.Key);
            proposti += volti.Count;
        }
        return proposti;
    }

    private const int CandidatiPerVolto = 5;

    private static float[] Centroide(PersonaNota p)
    {
        var somma = new float[Impronte.Dimensione];
        foreach (var c in p.Campioni) for (var k = 0; k < somma.Length && k < c.Length; k++) somma[k] += c[k];
        return Impronte.Normalizza(somma);
    }

    /// <summary>
    /// Dà i nomi ai volti di un catalogo come confermati (es. presi da Picasa), creando le persone che mancano. Non tocca i
    /// volti che l'utente ha già confermato come un'altra persona o ha ignorato. Restituisce le persone toccate, quelle
    /// create e quanti volti sono cambiati; le proposte sugli altri volti le fa poi il chiamante.
    /// </summary>
    public static EsitoNomiImportati AssegnaNomi(LibrarySidecarDatabase db, ArchivioPersone archivio, IEnumerable<(long Volto, string Nome)> nomi)
    {
        var stati = db.GetStatiVolti().ToDictionary(s => s.Id);
        var volti = db.GetVoltiPersone().ToDictionary(v => v.Id);
        var (persone, create) = (new List<long>(), new List<long>());
        var (assegnati, inConflitto) = (0, 0);
        foreach (var gruppo in nomi.Where(n => !string.IsNullOrWhiteSpace(n.Nome)).GroupBy(n => n.Nome.Trim(), StringComparer.CurrentCultureIgnoreCase))
        {
            var esisteva = archivio.EsisteNome(gruppo.Key);
            var id = archivio.TrovaOCrea(gruppo.Key, null);
            var daAssegnare = new List<long>();
            foreach (var (volto, _) in gruppo)
            {
                if (!stati.TryGetValue(volto, out var s)) continue;
                if (s.Stato == StatoPersona.Confermato && s.PersonaId == id) continue;
                if (s.Stato == StatoPersona.Rifiutato || (s.Stato == StatoPersona.Confermato && s.PersonaId != id)) { inConflitto++; continue; }
                daAssegnare.Add(volto);
            }
            if (daAssegnare.Count == 0)
            {
                if (!esisteva) archivio.Elimina(id);
                continue;
            }

            db.AssegnaPersona(daAssegnare, id, StatoPersona.Confermato);
            var suoi = daAssegnare.Select(v => volti.GetValueOrDefault(v)).Where(v => v?.Impronta is not null).ToList();
            AggiungiCampioni(archivio, id, suoi.Select(v => Impronte.DaByte(v!.Impronta!)));
            if (!esisteva)
            {
                create.Add(id);
                if (suoi.Where(v => v!.Miniatura is not null)
                        .OrderByDescending(v => v!.Miniatura!.Length * Math.Sqrt(Math.Max(1e-6, (v.X1 - v.X0) * (v.Y1 - v.Y0))))
                        .FirstOrDefault() is { } migliore)
                    archivio.ImpostaAvatar(id, migliore.Miniatura!);
            }
            persone.Add(id);
            assegnati += daAssegnare.Count;
        }
        return new EsitoNomiImportati(persone, create, assegnati, inConflitto);
    }

    /// <summary>Volti di una foto appena analizzata, con la persona proposta se somiglia a una già nota.</summary>
    public static async Task<List<VoltoDaSalvare>> DaSalvareAsync(
        IReadOnlyList<VoltoAnalizzato> volti, IReadOnlyList<PersonaNota> persone,
        Func<byte[], int, int, Task<byte[]>> codificaJpeg)
    {
        var daSalvare = new List<VoltoDaSalvare>(volti.Count);
        foreach (var v in volti)
        {
            var proposta = RiconoscimentoPersone.Riconosci(v.Impronta, persone);
            daSalvare.Add(new VoltoDaSalvare(
                v.X0, v.Y0, v.X1, v.Y1, v.Punteggio,
                Impronte.InByte(v.Impronta),
                await codificaJpeg(v.MiniaturaBgra, AllineamentoVolto.Lato, AllineamentoVolto.Lato).ConfigureAwait(false),
                proposta?.Persona.Id,
                proposta is null ? StatoPersona.Nessuno : StatoPersona.Proposto));
        }
        return daSalvare;
    }
}
