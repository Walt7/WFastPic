using WFastPic.Core.Persistence;

namespace WFastPic.Core.Persone;

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
        var proposti = 0;
        foreach (var proposta in db.GetImpronteSenzaNome()
                     .Select(v => (v.Id, Persona: RiconoscimentoPersone.Riconosci(Impronte.DaByte(v.Impronta), persone)?.Persona))
                     .Where(x => x.Persona is not null)
                     .GroupBy(x => x.Persona!.Id))
        {
            var volti = proposta.Select(x => x.Id).ToList();
            db.ProponiPersona(volti, proposta.Key);
            proposti += volti.Count;
        }
        return proposti;
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
