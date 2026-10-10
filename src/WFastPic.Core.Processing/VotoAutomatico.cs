namespace WFastPic.Core.Processing;

/// <summary>Perché una foto ha preso quel voto automatico (tradotti in testo dall'interfaccia).</summary>
[Flags]
public enum MotiviVoto
{
    Nessuno = 0,
    Nitida = 1,
    Morbida = 2,
    MossaOSfocata = 4,
    VoltoSfocato = 8,
    TroppoScura = 16,
    Bruciata = 32,
    MiglioreRaffica = 64,
    RafficaPiuNitida = 128,
    CiSeiTu = 256,
    PersoneImportanti = 512,
}

/// <summary>
/// Quanto conta la persona più importante della foto: tu, poi le persone con la stella e la famiglia,
/// gli amici, chi ha solo un nome, i conoscenti. Conta solo se il volto non è sfocato.
/// </summary>
public enum ImportanzaPersone
{
    Nessuna = 0,
    Conoscente = 1,
    ConNome = 2,
    Amico = 3,
    Preferita = 4,
    Io = 5,
}

public sealed record FotoDaValutare(long Id, MisureQualita? Misure, int? Raffica, ImportanzaPersone Persone = ImportanzaPersone.Nessuna);

/// <summary>Voto suggerito: sempre separato dal voto dell'utente, che non viene mai toccato.</summary>
public sealed record VotoSuggerito(int Stelle, bool DaScartare, MotiviVoto Motivi)
{
    public bool MiglioreRaffica => Motivi.HasFlag(MotiviVoto.MiglioreRaffica);
    public bool CiSeiTu => Motivi.HasFlag(MotiviVoto.CiSeiTu);
}

/// <summary>
/// Stelline automatiche (1-5) e suggerimenti di scarto dalle <see cref="MisureQualita"/>. Il voto è
/// relativo alla cartella (le più nitide di questa vacanza, non di tutte le foto del mondo), con limiti
/// assoluti: una foto mossa resta da 1 stella anche se tutte le altre sono peggio.
/// Soglie tarate su ~1300 foto reali di una vacanza confrontate con le selezioni fatte a mano: sotto
/// <see cref="SogliaMossa"/> nessuna foto era stata selezionata (e a vederle erano tutte mosse o sfocate).
/// </summary>
public static class VotoAutomatico
{
    public const double SogliaMossa = 0.60;
    public const double SogliaMorbida = 0.70;
    public const double SogliaVoltoSfocato = 0.55;

    // Solo i casi estremi: foto in tasca o con l'obiettivo coperto, non le notturne né i cieli chiari.
    public const double LuminositaMinima = 12;
    public const double BruciatiMassimi = 0.60;

    // In una raffica una sorella molto più nitida toglie una stella, ma non basta a suggerire lo scarto:
    // i gruppi di scatti a pochi secondi spesso hanno inquadrature diverse, tutte da tenere.
    public const double RapportoRafficaPeggiore = 0.85;

    public static IReadOnlyDictionary<long, VotoSuggerito> Calcola(IReadOnlyList<FotoDaValutare> foto)
    {
        var misurate = foto.Where(f => f.Misure is not null).ToList();
        var risultato = new Dictionary<long, VotoSuggerito>();
        if (misurate.Count == 0) return risultato;

        var nitidezza = misurate.ToDictionary(f => f.Id, f => f.Misure!.Nitidezza);

        // Percentile della nitidezza tra le foto tecnicamente buone della cartella.
        var buone = nitidezza.Values.Where(n => n >= SogliaMorbida).Order().ToArray();

        // Raffiche: la più nitida (tra quelle non da scartare per esposizione) è la migliore.
        var miglioreDellaRaffica = misurate
            .Where(f => f.Raffica is not null)
            .GroupBy(f => f.Raffica!.Value)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.MaxBy(f => (EsposizioneAccettabile(f.Misure!) ? 1 : 0, nitidezza[f.Id]))!);

        foreach (var f in misurate)
        {
            var m = f.Misure!;
            var n = nitidezza[f.Id];
            var motivi = MotiviVoto.Nessuno;
            var daScartare = false;
            int stelle;

            if (n < SogliaMossa) { motivi |= MotiviVoto.MossaOSfocata; daScartare = true; }
            else if (n < SogliaMorbida) motivi |= MotiviVoto.Morbida;
            else motivi |= MotiviVoto.Nitida;

            if (m.NitidezzaVolti is double nv && nv < SogliaVoltoSfocato) motivi |= MotiviVoto.VoltoSfocato;
            if (m.Luminosita < LuminositaMinima) { motivi |= MotiviVoto.TroppoScura; daScartare = true; }
            if (m.Bruciati > BruciatiMassimi) { motivi |= MotiviVoto.Bruciata; daScartare = true; }

            var migliore = false;
            if (f.Raffica is int r && miglioreDellaRaffica.TryGetValue(r, out var best))
            {
                if (best.Id == f.Id) { motivi |= MotiviVoto.MiglioreRaffica; migliore = true; }
                else if (n < nitidezza[best.Id] * RapportoRafficaPeggiore) motivi |= MotiviVoto.RafficaPiuNitida;
            }

            // In evidenza anche se mossa: si vede subito che è una tua foto, poi si decide.
            if (f.Persone == ImportanzaPersone.Io) motivi |= MotiviVoto.CiSeiTu;
            else if (f.Persone == ImportanzaPersone.Preferita) motivi |= MotiviVoto.PersoneImportanti;

            if (daScartare) stelle = 1;
            else if (motivi.HasFlag(MotiviVoto.Morbida) || motivi.HasFlag(MotiviVoto.VoltoSfocato)) stelle = 2;
            else
            {
                // Punteggio continuo 3..5 dal percentile, più i premi: con te quasi sempre 5 stelle, chi conta
                // di più sale di una, la migliore della raffica quasi di una, la sorella più mossa scende.
                var punteggio = 3 + 2 * Percentile(buone, n) + PremioPersone(f.Persone)
                    + (migliore ? 0.7 : 0) - (motivi.HasFlag(MotiviVoto.RafficaPiuNitida) ? 1 : 0);
                stelle = punteggio >= 4.7 ? 5 : punteggio >= 4.0 ? 4 : punteggio >= 3 ? 3 : 2;
            }

            risultato[f.Id] = new VotoSuggerito(stelle, daScartare, motivi);
        }

        return risultato;
    }

    private static double PremioPersone(ImportanzaPersone persone) => persone switch
    {
        ImportanzaPersone.Io => 2,
        ImportanzaPersone.Preferita => 1.2,
        ImportanzaPersone.Amico => 0.8,
        ImportanzaPersone.ConNome => 0.4,
        ImportanzaPersone.Conoscente => 0.1,
        _ => 0,
    };

    private static bool EsposizioneAccettabile(MisureQualita m) =>
        m.Luminosita >= LuminositaMinima && m.Bruciati <= BruciatiMassimi;

    /// <summary>Frazione delle altre foto buone meno nitide (0..1): a pari merito si sta in basso, non in alto.</summary>
    private static double Percentile(double[] ordinate, double valore)
    {
        if (ordinate.Length <= 1) return 1;
        var meno = 0;
        while (meno < ordinate.Length && ordinate[meno] < valore) meno++;
        return Math.Clamp((double)meno / (ordinate.Length - 1), 0, 1);
    }
}
