namespace WFastPic.Core.Persone;

/// <summary>Una persona con nome e alcune impronte confermate (dai volti a cui l'utente ha dato quel nome).</summary>
public sealed record PersonaNota(long Id, string Nome, IReadOnlyList<float[]> Campioni);

/// <summary>
/// Raggruppamento e riconoscimento, logica pura (testabile senza modelli):
/// - i volti senza nome di una cartella si raggruppano per somiglianza (legame medio: un gruppo resta
///   unito solo se in media i suoi volti si somigliano, così un sosia non "contagia" tutto il gruppo);
/// - un volto nuovo viene proposto come una persona già nominata se somiglia abbastanza ai suoi
///   campioni (media dei 3 più simili, più stabile del solo migliore).
/// </summary>
public static class RiconoscimentoPersone
{
    /// <summary>Più prudente della soglia OpenCV (0,363): una proposta sbagliata infastidisce più di una mancata.</summary>
    public const float SogliaProposta = 0.42f;

    /// <summary>Soglia per mettere insieme volti ancora senza nome.</summary>
    public const float SogliaGruppo = 0.40f;

    public const int CampioniMassimi = 40;

    /// <summary>Persona più probabile per un'impronta, o null se nessuna supera la soglia.</summary>
    public static (PersonaNota Persona, float Somiglianza)? Riconosci(float[] impronta, IReadOnlyList<PersonaNota> persone, float soglia = SogliaProposta)
    {
        (PersonaNota, float)? migliore = null;
        foreach (var persona in persone)
        {
            if (persona.Campioni.Count == 0) continue;
            var punteggio = persona.Campioni
                .Select(c => Impronte.Somiglianza(impronta, c))
                .OrderByDescending(s => s)
                .Take(3)
                .Average();
            if (punteggio >= soglia && (migliore is null || punteggio > migliore.Value.Item2))
                migliore = (persona, punteggio);
        }
        return migliore;
    }

    /// <summary>Oltre questi volti il legame medio (costo cubico) lascia il posto ai centroidi (quasi lineare).</summary>
    public const int MassimoLegameMedio = 300;

    /// <summary>Gruppo (0..n-1) per ogni volto; gruppi ordinati dal più numeroso.</summary>
    public static int[] Raggruppa(IReadOnlyList<float[]> impronte, float soglia = SogliaGruppo)
    {
        var n = impronte.Count;
        if (n == 0) return [];
        if (n > MassimoLegameMedio) return RaggruppaPerCentroidi(impronte, soglia);

        var somiglianza = new float[n, n];
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                somiglianza[i, j] = somiglianza[j, i] = Impronte.Somiglianza(impronte[i], impronte[j]);

        var gruppi = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList();
        while (true)
        {
            var (miglioreA, miglioreB, migliore) = (-1, -1, float.MinValue);
            for (var a = 0; a < gruppi.Count; a++)
            {
                for (var b = a + 1; b < gruppi.Count; b++)
                {
                    float somma = 0;
                    foreach (var i in gruppi[a]) foreach (var j in gruppi[b]) somma += somiglianza[i, j];
                    var media = somma / (gruppi[a].Count * gruppi[b].Count);
                    if (media > migliore) (miglioreA, miglioreB, migliore) = (a, b, media);
                }
            }

            if (miglioreA < 0 || migliore < soglia) break;
            gruppi[miglioreA].AddRange(gruppi[miglioreB]);
            gruppi.RemoveAt(miglioreB);
        }

        return Etichetta(gruppi, n);
    }

    /// <summary>
    /// Cartelle con tanti volti: ogni volto va nel gruppo col centroide più simile (sopra soglia) o ne
    /// apre uno nuovo; poi un secondo giro riassegna ogni volto al centroide finale migliore, così
    /// l'ordine delle foto conta poco.
    /// </summary>
    private static int[] RaggruppaPerCentroidi(IReadOnlyList<float[]> impronte, float soglia)
    {
        var somme = new List<float[]>();
        var centroidi = new List<float[]>();
        foreach (var impronta in impronte)
        {
            var (migliore, valore) = Vicino(centroidi, impronta);
            if (migliore >= 0 && valore >= soglia)
            {
                for (var k = 0; k < impronta.Length; k++) somme[migliore][k] += impronta[k];
                centroidi[migliore] = Impronte.Normalizza(somme[migliore]);
            }
            else
            {
                somme.Add((float[])impronta.Clone());
                centroidi.Add(impronta);
            }
        }

        var gruppi = centroidi.Select(_ => new List<int>()).ToList();
        for (var i = 0; i < impronte.Count; i++)
        {
            var (migliore, _) = Vicino(centroidi, impronte[i]);
            gruppi[migliore].Add(i);
        }
        return Etichetta(gruppi.Where(g => g.Count > 0).ToList(), impronte.Count);
    }

    private static (int Indice, float Somiglianza) Vicino(List<float[]> centroidi, float[] impronta)
    {
        var (indice, valore) = (-1, float.MinValue);
        for (var c = 0; c < centroidi.Count; c++)
        {
            var s = Impronte.Somiglianza(impronta, centroidi[c]);
            if (s > valore) (indice, valore) = (c, s);
        }
        return (indice, valore);
    }

    private static int[] Etichetta(List<List<int>> gruppi, int n)
    {
        var etichette = new int[n];
        var ordinati = gruppi.OrderByDescending(g => g.Count).ToList();
        for (var g = 0; g < ordinati.Count; g++)
            foreach (var i in ordinati[g]) etichette[i] = g;
        return etichette;
    }

    /// <summary>Campioni da tenere per una persona: i più recenti, fino al massimo, senza doppioni quasi identici.</summary>
    public static List<float[]> AggiornaCampioni(IReadOnlyList<float[]> esistenti, IEnumerable<float[]> nuovi)
    {
        var tutti = esistenti.ToList();
        foreach (var n in nuovi)
        {
            if (tutti.Any(e => Impronte.Somiglianza(e, n) > 0.97f)) continue;
            tutti.Add(n);
        }
        return tutti.Count > CampioniMassimi ? tutti.Skip(tutti.Count - CampioniMassimi).ToList() : tutti;
    }
}
