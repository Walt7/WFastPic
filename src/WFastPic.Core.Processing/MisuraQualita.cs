namespace WFastPic.Core.Processing;

/// <summary>
/// Misure tecniche di una foto, calcolate una volta sulla preview e salvate nel sidecar.
/// Nitidezza = quanto dettaglio fine resta rispetto a quello a scala doppia, nelle zone più a fuoco e
/// nella direzione peggiore: ~1 per una foto nitida, sotto ~0,5 per una mossa o sfocata. È un rapporto,
/// quindi non dipende da quanto è "ricca" la scena (un cielo azzurro nitido non sembra sfocato).
/// </summary>
public sealed record MisureQualita(
    double Nitidezza,
    double? NitidezzaVolti,
    double Bruciati,
    double Chiusi,
    double Luminosita);

/// <summary>
/// Calcolo delle <see cref="MisureQualita"/> da un buffer BGRA. Puro e deterministico: nessuna rete,
/// nessuna dipendenza esterna. La foto va decodificata con il lato lungo di <see cref="LatoAnalisi"/>
/// (le misure di nitidezza dipendono dalla scala).
/// </summary>
public static class MisuraQualita
{
    /// <summary>Lato lungo della preview su cui si misura: la scala su cui sono tarate le soglie di <see cref="VotoAutomatico"/>.</summary>
    public const int LatoAnalisi = 512;

    /// <summary>Cambia quando cambia il calcolo: le misure salvate con un'altra versione si rifanno.</summary>
    public const int Versione = 1;

    // Tessere a metà scala (64 px della preview da 512).
    private const int LatoTessera = 32;

    // Sotto questa energia (luma²) a un quarto di scala una tessera è "piatta" (cielo, muro, buio):
    // non dice nulla sulla messa a fuoco.
    private const double EnergiaMinimaStruttura = 9;

    // Volti più piccoli di così (in pixel a metà scala) sono troppo pochi pixel per giudicarne il fuoco.
    private const int LatoMinimoVolto = 12;

    private const int Direzioni = 4;

    public static MisureQualita Misura(byte[] bgra, int larghezza, int altezza,
        IReadOnlyList<(double X0, double Y0, double X1, double Y1)>? volti = null)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (larghezza < 16 || altezza < 16 || bgra.Length < 4L * larghezza * altezza)
            throw new ArgumentException("Immagine troppo piccola o buffer incompleto.");

        var luma = new float[larghezza * altezza];
        int bruciati = 0, chiusi = 0;
        double somma = 0;
        for (int i = 0, p = 0; i < luma.Length; i++, p += 4)
        {
            var y = 0.114f * bgra[p] + 0.587f * bgra[p + 1] + 0.299f * bgra[p + 2];
            luma[i] = y;
            somma += y;
            if (y >= 250) bruciati++;
            else if (y <= 5) chiusi++;
        }

        // Si confronta metà scala con un quarto di scala, non la piena: il rumore dei pixel (ISO alti,
        // foto al buio) sparisce già a metà scala e altrimenti farebbe sembrare nitida una foto sfocata.
        var fine = Dimezza(new Immagine(luma, larghezza, altezza));
        var grossa = Dimezza(fine);

        var tessere = new List<Energie>();
        for (var ty = 0; ty + LatoTessera <= fine.Altezza; ty += LatoTessera)
        for (var tx = 0; tx + LatoTessera <= fine.Larghezza; tx += LatoTessera)
        {
            var e = MisuraArea(fine, grossa, tx, ty, tx + LatoTessera, ty + LatoTessera);
            if (e.GrossaTotale >= EnergiaMinimaStruttura) tessere.Add(e);
        }

        var nitidezza = NitidezzaZonePiuNitide(tessere);

        double? nitidezzaVolti = null;
        if (volti is not null)
        {
            foreach (var (vx0, vy0, vx1, vy1) in volti)
            {
                var x0 = (int)Math.Clamp(vx0 * fine.Larghezza, 0, fine.Larghezza);
                var y0 = (int)Math.Clamp(vy0 * fine.Altezza, 0, fine.Altezza);
                var x1 = (int)Math.Clamp(vx1 * fine.Larghezza, 0, fine.Larghezza);
                var y1 = (int)Math.Clamp(vy1 * fine.Altezza, 0, fine.Altezza);
                if (x1 - x0 < LatoMinimoVolto || y1 - y0 < LatoMinimoVolto) continue;

                var e = MisuraArea(fine, grossa, x0, y0, x1, y1);
                if (e.GrossaTotale < EnergiaMinimaStruttura / 3) continue; // volto quasi uniforme (controluce)
                // Il volto più nitido conta: in un gruppo basta che sia a fuoco il soggetto.
                var n = e.Rapporto();
                nitidezzaVolti = nitidezzaVolti is null ? n : Math.Max(nitidezzaVolti.Value, n);
            }
        }

        double totale = luma.Length;
        return new MisureQualita(nitidezza, nitidezzaVolti, bruciati / totale, chiusi / totale, somma / totale);
    }

    /// <summary>
    /// Rapporto nelle tessere col dettaglio fine più forte (il 10%): lì sta ciò che è a fuoco. Con lo
    /// sfondo sfocato di un ritratto il soggetto nitido basta a dire "nitida".
    /// </summary>
    private static double NitidezzaZonePiuNitide(List<Energie> tessere)
    {
        if (tessere.Count == 0) return 1; // scena senza struttura (nebbia, cielo): niente da giudicare

        tessere.Sort((a, b) => b.FineTotale.CompareTo(a.FineTotale));
        var somma = new Energie(new double[Direzioni], new double[Direzioni]);
        for (var i = 0; i < Math.Max(1, tessere.Count / 10); i++)
        for (var d = 0; d < Direzioni; d++)
        {
            somma.Fine[d] += tessere[i].Fine[d];
            somma.Grossa[d] += tessere[i].Grossa[d];
        }
        return somma.Rapporto();
    }

    private sealed record Immagine(float[] Pixel, int Larghezza, int Altezza);

    /// <summary>Energie della derivata seconda per direzione (orizzontale, verticale, due diagonali).</summary>
    private sealed record Energie(double[] Fine, double[] Grossa)
    {
        public double FineTotale => Fine.Sum();
        public double GrossaTotale => Grossa.Sum();

        /// <summary>
        /// Rapporto nella direzione peggiore: il mosso toglie dettaglio solo lungo il movimento e la
        /// media delle direzioni lo nasconderebbe.
        /// </summary>
        public double Rapporto()
        {
            var peggiore = double.MaxValue;
            for (var d = 0; d < Direzioni; d++)
            {
                if (Grossa[d] <= 0) continue;
                peggiore = Math.Min(peggiore, Math.Sqrt(Fine[d] / Grossa[d]));
            }
            return peggiore == double.MaxValue ? 1 : peggiore;
        }
    }

    private static Immagine Dimezza(Immagine img)
    {
        var (pixel, larghezza) = (img.Pixel, img.Larghezza);
        var lm = img.Larghezza / 2;
        var am = img.Altezza / 2;
        var mezza = new float[lm * am];
        for (var y = 0; y < am; y++)
        for (var x = 0; x < lm; x++)
        {
            var i = 2 * y * larghezza + 2 * x;
            mezza[y * lm + x] = (pixel[i] + pixel[i + 1] + pixel[i + larghezza] + pixel[i + larghezza + 1]) * 0.25f;
        }
        return new Immagine(mezza, lm, am);
    }

    /// <summary>La stessa area alle due scale (coordinate della scala fine).</summary>
    private static Energie MisuraArea(Immagine fine, Immagine grossa, int x0, int y0, int x1, int y1) =>
        new(EnergieDirezionali(fine, x0, y0, x1, y1), EnergieDirezionali(grossa, x0 / 2, y0 / 2, x1 / 2, y1 / 2));

    /// <summary>Energia media (luma²) della derivata seconda in 4 direzioni.</summary>
    private static double[] EnergieDirezionali(Immagine img, int x0, int y0, int x1, int y1)
    {
        var (p, w, h) = (img.Pixel, img.Larghezza, img.Altezza);
        var e = new double[Direzioni];
        var n = 0;
        for (var y = Math.Max(1, y0); y < Math.Min(h - 1, y1); y++)
        for (var x = Math.Max(1, x0); x < Math.Min(w - 1, x1); x++)
        {
            var i = y * w + x;
            var c = 2 * p[i];
            double o = p[i - 1] + p[i + 1] - c;
            double v = p[i - w] + p[i + w] - c;
            double d1 = p[i - w - 1] + p[i + w + 1] - c;
            double d2 = p[i - w + 1] + p[i + w - 1] - c;
            e[0] += o * o; e[1] += v * v; e[2] += d1 * d1; e[3] += d2 * d2;
            n++;
        }
        if (n > 0) for (var d = 0; d < Direzioni; d++) e[d] /= n;
        return e;
    }
}
