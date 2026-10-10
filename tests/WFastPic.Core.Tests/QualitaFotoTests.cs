using WFastPic.Core.Processing;
using Xunit;

namespace WFastPic.Core.Tests;

public class QualitaFotoTests
{
    private const int W = MisuraQualita.LatoAnalisi, H = 384;

    // Modello "dead leaves": dischi sovrapposti di dimensioni distribuite come nelle foto vere
    // (tanti piccoli, pochi grandi), con bordi netti.
    private static float[] Scena(int seme)
    {
        var r = new Random(seme);
        var g = new float[W * H];
        var fatto = new bool[W * H];
        var mancanti = W * H;
        for (var k = 0; k < 50_000 && mancanti > 0; k++)
        {
            var raggio = Math.Pow(r.NextDouble() * (Math.Pow(100, -2) - Math.Pow(2, -2)) + Math.Pow(2, -2), -0.5);
            int cx = r.Next(W), cy = r.Next(H);
            var v = (float)(r.NextDouble() * 200 + 25);
            for (var y = Math.Max(0, (int)(cy - raggio)); y <= Math.Min(H - 1, (int)(cy + raggio)); y++)
            for (var x = Math.Max(0, (int)(cx - raggio)); x <= Math.Min(W - 1, (int)(cx + raggio)); x++)
            {
                var i = y * W + x;
                if (fatto[i] || (x - cx) * (x - cx) + (y - cy) * (y - cy) > raggio * raggio) continue;
                g[i] = v;
                fatto[i] = true;
                mancanti--;
            }
        }
        return g;
    }

    private static float[] Sfoca(float[] g, double sigma, int x0 = 0, int y0 = 0, int x1 = W, int y1 = H)
    {
        var raggio = (int)Math.Ceiling(3 * sigma);
        var k = Enumerable.Range(-raggio, 2 * raggio + 1).Select(i => Math.Exp(-i * i / (2 * sigma * sigma))).ToArray();
        var somma = k.Sum();
        var orizzontale = (float[])g.Clone();
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
            orizzontale[y * W + x] = (float)(Enumerable.Range(-raggio, 2 * raggio + 1).Sum(i => k[i + raggio] * g[y * W + Math.Clamp(x + i, 0, W - 1)]) / somma);
        var risultato = (float[])orizzontale.Clone();
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
            risultato[y * W + x] = (float)(Enumerable.Range(-raggio, 2 * raggio + 1).Sum(i => k[i + raggio] * orizzontale[Math.Clamp(y + i, 0, H - 1) * W + x]) / somma);
        return risultato;
    }

    /// <summary>Mosso orizzontale: media su <paramref name="lunghezza"/> pixel lungo x.</summary>
    private static float[] Muovi(float[] g, int lunghezza)
    {
        var risultato = new float[g.Length];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            double v = 0;
            for (var i = 0; i < lunghezza; i++) v += g[y * W + Math.Clamp(x + i - lunghezza / 2, 0, W - 1)];
            risultato[y * W + x] = (float)(v / lunghezza);
        }
        return risultato;
    }

    private static float[] Rumore(float[] g, double sigma)
    {
        var r = new Random(99);
        return g.Select(v =>
        {
            var u = 1 - r.NextDouble();
            return (float)Math.Clamp(v + sigma * Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * r.NextDouble()), 0, 255);
        }).ToArray();
    }

    private static float[] Scala(float[] g, double fattore, double scostamento = 0) =>
        g.Select(v => (float)Math.Clamp(v * fattore + scostamento, 0, 255)).ToArray();

    private static byte[] Bgra(float[] g)
    {
        var b = new byte[g.Length * 4];
        for (var i = 0; i < g.Length; i++)
        {
            var v = (byte)Math.Clamp(Math.Round(g[i]), 0, 255);
            (b[4 * i], b[4 * i + 1], b[4 * i + 2], b[4 * i + 3]) = (v, v, v, 255);
        }
        return b;
    }

    private static MisureQualita Misura(float[] g, IReadOnlyList<(double, double, double, double)>? volti = null) =>
        MisuraQualita.Misura(Bgra(g), W, H, volti);

    [Fact]
    public void Foto_nitida_supera_la_soglia_sfocata_e_mossa_no()
    {
        var scena = Scena(1);

        Assert.True(Misura(scena).Nitidezza >= VotoAutomatico.SogliaMorbida);
        Assert.True(Misura(Sfoca(scena, 2)).Nitidezza < VotoAutomatico.SogliaMossa);
        Assert.True(Misura(Muovi(scena, 9)).Nitidezza < VotoAutomatico.SogliaMossa);
    }

    [Fact]
    public void Nitidezza_scende_con_la_sfocatura()
    {
        var scena = Scena(2);
        var valori = new[] { 0.0, 0.8, 1.5, 3 }.Select(s => Misura(s == 0 ? scena : Sfoca(scena, s)).Nitidezza).ToArray();

        for (var i = 1; i < valori.Length; i++) Assert.True(valori[i] < valori[i - 1], string.Join(" ", valori));
    }

    [Fact]
    public void Il_rumore_non_fa_sembrare_nitida_una_foto_sfocata()
    {
        var sfocataConRumore = Rumore(Sfoca(Scena(3), 2), 6);

        Assert.True(Misura(sfocataConRumore).Nitidezza < VotoAutomatico.SogliaMossa);
    }

    [Fact]
    public void Non_dipende_dal_contrasto_della_scena()
    {
        var scena = Scena(4);
        var piena = Misura(scena).Nitidezza;
        var tenue = Misura(Scala(scena, 0.4, 70)).Nitidezza;

        Assert.InRange(tenue, piena - 0.08, piena + 0.08);
    }

    [Fact]
    public void Ritratto_con_sfondo_sfocato_resta_nitido()
    {
        // Solo il quarto centrale è a fuoco, come un soggetto davanti a uno sfondo sfocato.
        var scena = Scena(5);
        var sfondoSfocato = Sfoca(scena, 3);
        for (var y = H / 3; y < 2 * H / 3; y++)
        for (var x = W / 3; x < 2 * W / 3; x++)
            sfondoSfocato[y * W + x] = scena[y * W + x];

        Assert.True(Misura(sfondoSfocato).Nitidezza >= VotoAutomatico.SogliaMorbida);
    }

    [Fact]
    public void Misura_il_volto_a_parte()
    {
        var scena = Scena(6);
        var voltoMosso = Sfoca(scena, 2.5, 200, 120, 300, 240);
        (double, double, double, double)[] volti = [(200.0 / W, 120.0 / H, 300.0 / W, 240.0 / H)];

        var nitido = Misura(scena, volti);
        var mosso = Misura(voltoMosso, volti);

        Assert.NotNull(nitido.NitidezzaVolti);
        Assert.True(mosso.NitidezzaVolti < VotoAutomatico.SogliaVoltoSfocato);
        Assert.True(nitido.NitidezzaVolti > mosso.NitidezzaVolti + 0.2);
    }

    [Fact]
    public void Volti_troppo_piccoli_non_contano()
    {
        (double, double, double, double)[] volti = [(0.5, 0.5, 0.51, 0.51)];

        Assert.Null(Misura(Scena(7), volti).NitidezzaVolti);
    }

    [Fact]
    public void Misura_esposizione_e_scena_vuota()
    {
        var nera = Misura(new float[W * H]);
        Assert.Equal(1, nera.Chiusi, 3);
        Assert.True(nera.Luminosita < VotoAutomatico.LuminositaMinima);
        Assert.Equal(1, nera.Nitidezza); // niente struttura: niente da giudicare sul fuoco

        var bianca = Misura(Enumerable.Repeat(255f, W * H).ToArray());
        Assert.Equal(1, bianca.Bruciati, 3);
    }

    // ---------------------------------------------------------------- voto

    private static MisureQualita M(double nitidezza, double? volti = null, double bruciati = 0, double luminosita = 120) =>
        new(nitidezza, volti, bruciati, 0, luminosita);

    [Fact]
    public void Mossa_vale_una_stella_ed_e_da_scartare()
    {
        var voti = VotoAutomatico.Calcola([new(1, M(0.5), null), new(2, M(0.9), null)]);

        Assert.Equal(1, voti[1].Stelle);
        Assert.True(voti[1].DaScartare);
        Assert.True(voti[1].Motivi.HasFlag(MotiviVoto.MossaOSfocata));
        Assert.False(voti[2].DaScartare);
    }

    [Fact]
    public void Solo_esposizioni_estreme_sono_da_scartare()
    {
        var voti = VotoAutomatico.Calcola([
            new(1, M(0.9, luminosita: 5), null),     // in tasca
            new(2, M(0.9, luminosita: 25), null),    // notturna
            new(3, M(0.9, bruciati: 0.8), null),     // tutta bianca
            new(4, M(0.9, bruciati: 0.35), null),    // cielo chiaro
        ]);

        Assert.True(voti[1].Motivi.HasFlag(MotiviVoto.TroppoScura) && voti[1].DaScartare);
        Assert.False(voti[2].DaScartare);
        Assert.True(voti[3].Motivi.HasFlag(MotiviVoto.Bruciata) && voti[3].DaScartare);
        Assert.False(voti[4].DaScartare);
    }

    [Fact]
    public void Stelle_relative_alla_cartella_per_le_foto_buone()
    {
        var foto = Enumerable.Range(1, 20).Select(i => new FotoDaValutare(i, M(0.70 + i * 0.02), null)).ToList();

        var voti = VotoAutomatico.Calcola(foto);

        Assert.Equal(3, voti[1].Stelle);
        Assert.Equal(5, voti[20].Stelle);
        Assert.All(voti.Values, v => Assert.InRange(v.Stelle, 3, 5));
        Assert.True(voti.Values.Select(v => v.Stelle).Distinct().Count() == 3);
    }

    [Fact]
    public void Morbida_o_volto_sfocato_valgono_due_stelle_senza_scarto()
    {
        var voti = VotoAutomatico.Calcola([new(1, M(0.65), null), new(2, M(0.95, volti: 0.4), null)]);

        Assert.Equal(2, voti[1].Stelle);
        Assert.Equal(2, voti[2].Stelle);
        Assert.True(voti[2].Motivi.HasFlag(MotiviVoto.VoltoSfocato));
        Assert.False(voti[1].DaScartare || voti[2].DaScartare);
    }

    [Fact]
    public void Raffica_premia_la_piu_nitida_senza_suggerire_lo_scarto_delle_altre()
    {
        var voti = VotoAutomatico.Calcola([
            new(1, M(0.75), 7), new(2, M(1.0), 7), new(3, M(0.98), 7),
            new(4, M(0.8), null), new(5, M(0.9), null),
        ]);

        Assert.True(voti[2].MiglioreRaffica);
        Assert.False(voti[1].MiglioreRaffica);
        Assert.True(voti[1].Motivi.HasFlag(MotiviVoto.RafficaPiuNitida));
        Assert.False(voti[3].Motivi.HasFlag(MotiviVoto.RafficaPiuNitida)); // quasi uguale alla migliore
        Assert.DoesNotContain(voti.Values, v => v.DaScartare);
        Assert.True(voti[2].Stelle > voti[1].Stelle);
    }

    [Fact]
    public void Nella_raffica_vince_una_foto_ben_esposta()
    {
        var voti = VotoAutomatico.Calcola([new(1, M(1.0, luminosita: 3), 1), new(2, M(0.8), 1)]);

        Assert.True(voti[2].MiglioreRaffica);
    }

    [Fact]
    public void Le_persone_importanti_alzano_il_voto_in_ordine()
    {
        // Stessa nitidezza (la meno nitida delle buone): conta solo chi c'è nella foto.
        var persone = new[]
        {
            ImportanzaPersone.Nessuna, ImportanzaPersone.Conoscente, ImportanzaPersone.ConNome,
            ImportanzaPersone.Amico, ImportanzaPersone.Preferita, ImportanzaPersone.Io,
        };
        var foto = persone.Select((p, i) => new FotoDaValutare(i + 1, M(0.75), null, p))
            .Append(new FotoDaValutare(100, M(1.0), null)).ToList();

        var voti = VotoAutomatico.Calcola(foto);

        var stelle = persone.Select((_, i) => voti[i + 1].Stelle).ToArray();
        for (var i = 1; i < stelle.Length; i++) Assert.True(stelle[i] >= stelle[i - 1], string.Join(" ", stelle));
        Assert.Equal(3, voti[1].Stelle);
        Assert.Equal(5, voti[6].Stelle);
        Assert.True(voti[6].CiSeiTu);
        Assert.True(voti[5].Motivi.HasFlag(MotiviVoto.PersoneImportanti));
    }

    [Fact]
    public void Una_tua_foto_mossa_resta_da_scartare_ma_in_evidenza()
    {
        var voti = VotoAutomatico.Calcola([new(1, M(0.5), null, ImportanzaPersone.Io)]);

        Assert.True(voti[1].DaScartare);
        Assert.Equal(1, voti[1].Stelle);
        Assert.True(voti[1].CiSeiTu);
    }

    [Fact]
    public void Foto_non_ancora_misurate_non_hanno_voto()
    {
        var voti = VotoAutomatico.Calcola([new(1, null, null), new(2, M(0.9), null)]);

        Assert.False(voti.ContainsKey(1));
        Assert.True(voti.ContainsKey(2));
    }
}
