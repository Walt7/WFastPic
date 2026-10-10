namespace WFastPic.Core.Processing;

/// <summary>Zona da non tagliare mai (es. un volto), normalizzata (0..1) sulla tela raddrizzata.</summary>
public sealed record ZonaImportante(double X0, double Y0, double X1, double Y1);

/// <summary>Un ritaglio proposto, in coordinate normalizzate (0..1) sulla tela raddrizzata.</summary>
public sealed record RitaglioSuggerito(string Etichetta, double Rapporto, double X0, double Y0, double X1, double Y1);

/// <summary>
/// Ritagli suggeriti come in Picasa: per alcune proporzioni propone il riquadro che tiene la parte
/// "interessante" della foto e taglia i bordi vuoti. Interesse = dettagli/bordi (gradiente) +
/// contrasto di colore rispetto alla media della foto, calcolato su una copia ridotta (~128 px) con
/// immagini integrali, così resta istantaneo anche su una preview da 2048 px. Un piccolo premio
/// porta il soggetto vicino ai punti dei terzi. Con foto raddrizzata i ritagli stanno sempre dentro
/// l'immagine ruotata (niente angoli vuoti).
/// </summary>
public static class SuggerimentiRitaglio
{
    private const int LatoAnalisi = 128;
    private const double AreaMinima = 0.35;
    private static readonly double[] Scale = [1.0, 0.9, 0.8, 0.7, 0.6];

    /// <param name="bgra">Tela raddrizzata (stessa dimensione dell'originale).</param>
    /// <param name="angoloGradi">Raddrizzamento applicato: le zone vuote agli angoli non contano e non si ritagliano.</param>
    /// <param name="volti">Volti rilevati: pesano molto nell'interesse, restano sempre interi (con aria
    /// attorno e sopra la testa) e la composizione li porta verso il terzo superiore.</param>
    public static IReadOnlyList<RitaglioSuggerito> Calcola(
        byte[] bgra, int larghezza, int altezza, double angoloGradi = 0, IReadOnlyList<ZonaImportante>? volti = null)
    {
        if (larghezza < 8 || altezza < 8 || bgra.Length < larghezza * altezza * 4) return [];

        var mappa = MappaInteresse.Crea(bgra, larghezza, altezza, angoloGradi, volti ?? []);
        var proposte = new List<RitaglioSuggerito>();

        foreach (var (etichetta, rapporto) in Proporzioni(larghezza, altezza))
        {
            if (Migliore(mappa, rapporto, angoloGradi) is not { } r) continue;
            var ritaglio = new RitaglioSuggerito(etichetta, rapporto,
                r.X0 / mappa.W, r.Y0 / mappa.H, r.X1 / mappa.W, r.Y1 / mappa.H);

            // Due proposte quasi uguali non aiutano a scegliere.
            if (proposte.All(p => Sovrapposizione(p, ritaglio) < 0.9)) proposte.Add(ritaglio);
        }

        return proposte;
    }

    /// <summary>Come la foto, poi le classiche: orientate come la foto (16:9 diventa 9:16 su una verticale).</summary>
    private static IEnumerable<(string Etichetta, double Rapporto)> Proporzioni(int larghezza, int altezza)
    {
        var verticale = altezza > larghezza;
        var foto = (double)larghezza / altezza;
        double Orienta(double r) => verticale ? 1 / r : r;
        string Nome(int a, int b) => verticale ? $"{b}:{a}" : $"{a}:{b}";

        var elenco = new List<(string, double)> { ("Come la foto", foto) };
        var classica = Math.Abs(Math.Max(foto, 1 / foto) - 1.5) < 0.05 ? (Nome(4, 3), Orienta(4.0 / 3)) : (Nome(3, 2), Orienta(1.5));
        elenco.Add(classica);
        elenco.Add((Nome(16, 9), Orienta(16.0 / 9)));
        elenco.Add(("1:1", 1.0));
        return elenco;
    }

    private static (double X0, double Y0, double X1, double Y1)? Migliore(MappaInteresse m, double rapporto, double angolo)
    {
        double wMax, hMax;
        if ((double)m.W / m.H > rapporto) { hMax = m.H; wMax = m.H * rapporto; }
        else { wMax = m.W; hMax = m.W / rapporto; }

        (double X0, double Y0, double X1, double Y1)? migliore = null;
        var punteggioMigliore = double.NegativeInfinity;

        void Cerca(bool volti)
        {
            foreach (var scala in Scale)
            {
                var w = wMax * scala;
                var h = hMax * scala;
                var frazioneArea = w * h / (m.W * m.H);
                if (frazioneArea < AreaMinima) break;

                var passoX = Math.Max(1.0, (m.W - w) / 24);
                var passoY = Math.Max(1.0, (m.H - h) / 24);
                for (var y = 0.0; y + h <= m.H + 1e-6; y += passoY)
                {
                    for (var x = 0.0; x + w <= m.W + 1e-6; x += passoX)
                    {
                        // Un volto tagliato a metà non è mai una proposta valida.
                        if (volti && m.ZonaVolti is { } z && (x > z.X0 || y > z.Y0 || x + w < z.X1 || y + h < z.Y1)) continue;
                        if (angolo != 0 && !StraightenGeometry.ContainsRect(m.W, m.H, angolo, x, y, x + w, y + h)) continue;

                        var punteggio = m.Punteggio(x, y, x + w, y + h, frazioneArea);
                        if (punteggio > punteggioMigliore)
                        {
                            punteggioMigliore = punteggio;
                            migliore = (x, y, x + w, y + h);
                        }
                    }
                }
            }
        }

        Cerca(volti: true);
        // Volti troppo distanti tra loro per questa proporzione: si propone comunque il ritaglio migliore.
        if (migliore is null && m.ZonaVolti is not null) Cerca(volti: false);

        // Nessuna posizione valida (angolo forte + proporzione lontana): il più grande che entra, al centro.
        if (migliore is null && angolo != 0)
        {
            var cx = m.W / 2.0; var cy = m.H / 2.0;
            var (x0, y0, x1, y1) = StraightenGeometry.FitRect(m.W, m.H, angolo, cx - wMax / 2, cy - hMax / 2, cx + wMax / 2, cy + hMax / 2);
            if (x1 - x0 > 1 && y1 - y0 > 1) migliore = (x0, y0, x1, y1);
        }

        return migliore;
    }

    private static double Sovrapposizione(RitaglioSuggerito a, RitaglioSuggerito b)
    {
        var ix = Math.Max(0, Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0));
        var iy = Math.Max(0, Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0));
        var intersezione = ix * iy;
        var unione = (a.X1 - a.X0) * (a.Y1 - a.Y0) + (b.X1 - b.X0) * (b.Y1 - b.Y0) - intersezione;
        return unione <= 0 ? 0 : intersezione / unione;
    }

    /// <summary>Miniatura BGRA del ritaglio (media a blocchi), per le anteprime cliccabili.</summary>
    public static (byte[] Bgra, int Larghezza, int Altezza) Miniatura(
        byte[] bgra, int larghezza, int altezza, RitaglioSuggerito ritaglio, int latoMassimo)
    {
        var x0 = (int)Math.Round(ritaglio.X0 * larghezza);
        var y0 = (int)Math.Round(ritaglio.Y0 * altezza);
        var cw = Math.Max(1, (int)Math.Round((ritaglio.X1 - ritaglio.X0) * larghezza));
        var ch = Math.Max(1, (int)Math.Round((ritaglio.Y1 - ritaglio.Y0) * altezza));
        cw = Math.Min(cw, larghezza - x0);
        ch = Math.Min(ch, altezza - y0);

        var scala = Math.Min(1.0, (double)latoMassimo / Math.Max(cw, ch));
        var tw = Math.Max(1, (int)Math.Round(cw * scala));
        var th = Math.Max(1, (int)Math.Round(ch * scala));
        var risultato = new byte[tw * th * 4];

        for (var ty = 0; ty < th; ty++)
        {
            var sy0 = y0 + ty * ch / th;
            var sy1 = Math.Max(sy0 + 1, y0 + (ty + 1) * ch / th);
            for (var tx = 0; tx < tw; tx++)
            {
                var sx0 = x0 + tx * cw / tw;
                var sx1 = Math.Max(sx0 + 1, x0 + (tx + 1) * cw / tw);
                long b = 0, g = 0, r = 0, n = 0;
                for (var sy = sy0; sy < sy1; sy++)
                {
                    var riga = sy * larghezza * 4;
                    for (var sx = sx0; sx < sx1; sx++)
                    {
                        var o = riga + sx * 4;
                        b += bgra[o]; g += bgra[o + 1]; r += bgra[o + 2]; n++;
                    }
                }
                var d = (ty * tw + tx) * 4;
                risultato[d] = (byte)(b / n);
                risultato[d + 1] = (byte)(g / n);
                risultato[d + 2] = (byte)(r / n);
                risultato[d + 3] = 255;
            }
        }

        return (risultato, tw, th);
    }

    /// <summary>Mappa di interesse ridotta con immagini integrali (somma, momenti x e y).</summary>
    private sealed class MappaInteresse
    {
        public int W { get; private init; }
        public int H { get; private init; }
        private double[] _somma = [];
        private double[] _sommaX = [];
        private double[] _sommaY = [];
        private double _totale;

        /// <summary>Riquadro (coordinate mappa) che deve restare intero: tutti i volti con margine e aria sopra.</summary>
        public (double X0, double Y0, double X1, double Y1)? ZonaVolti { get; private set; }

        /// <summary>Baricentro dei volti (coordinate mappa): con dei volti la composizione segue loro.</summary>
        public (double X, double Y)? CentroVolti { get; private set; }

        public static MappaInteresse Crea(byte[] bgra, int larghezza, int altezza, double angolo, IReadOnlyList<ZonaImportante> volti)
        {
            var fattore = Math.Max(1, (int)Math.Ceiling((double)Math.Max(larghezza, altezza) / LatoAnalisi));
            var w = Math.Max(2, larghezza / fattore);
            var h = Math.Max(2, altezza / fattore);

            // Riduzione a blocchi: luminanza e colore medi.
            var luma = new double[w * h];
            var rr = new double[w * h]; var gg = new double[w * h]; var bb = new double[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    double b = 0, g = 0, r = 0; var n = 0;
                    for (var sy = y * fattore; sy < Math.Min(altezza, (y + 1) * fattore); sy++)
                    {
                        for (var sx = x * fattore; sx < Math.Min(larghezza, (x + 1) * fattore); sx++)
                        {
                            var o = (sy * larghezza + sx) * 4;
                            b += bgra[o]; g += bgra[o + 1]; r += bgra[o + 2]; n++;
                        }
                    }
                    var i = y * w + x;
                    bb[i] = b / n; gg[i] = g / n; rr[i] = r / n;
                    luma[i] = 0.299 * rr[i] + 0.587 * gg[i] + 0.114 * bb[i];
                }
            }

            // Zone vuote della foto raddrizzata: fuori dall'analisi.
            var valido = new bool[w * h];
            double mR = 0, mG = 0, mB = 0; var nValidi = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    valido[i] = angolo == 0 || StraightenGeometry.ContainsPoint(w, h, angolo, x + 0.5, y + 0.5);
                    if (!valido[i]) continue;
                    mR += rr[i]; mG += gg[i]; mB += bb[i]; nValidi++;
                }
            }
            if (nValidi > 0) { mR /= nValidi; mG /= nValidi; mB /= nValidi; }

            // Gradiente (Sobel) + distanza dal colore medio, ciascuno normalizzato al proprio massimo.
            var gradiente = new double[w * h];
            var colore = new double[w * h];
            double maxG = 1e-9, maxC = 1e-9;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    if (!valido[i]) continue;
                    double L(int xx, int yy) => luma[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)];
                    var gx = L(x + 1, y - 1) + 2 * L(x + 1, y) + L(x + 1, y + 1) - L(x - 1, y - 1) - 2 * L(x - 1, y) - L(x - 1, y + 1);
                    var gy = L(x - 1, y + 1) + 2 * L(x, y + 1) + L(x + 1, y + 1) - L(x - 1, y - 1) - 2 * L(x, y - 1) - L(x + 1, y - 1);
                    gradiente[i] = Math.Sqrt(gx * gx + gy * gy);
                    var dr = rr[i] - mR; var dg = gg[i] - mG; var db = bb[i] - mB;
                    colore[i] = Math.Sqrt(dr * dr + dg * dg + db * db);
                    maxG = Math.Max(maxG, gradiente[i]);
                    maxC = Math.Max(maxC, colore[i]);
                }
            }

            var mappa = new MappaInteresse { W = w, H = h };
            var interesse = new double[w * h];
            for (var i = 0; i < interesse.Length; i++)
                interesse[i] = valido[i] ? 0.5 * gradiente[i] / maxG + 0.5 * colore[i] / maxC : 0;

            mappa.AggiungiVolti(interesse, volti);
            mappa.CostruisciIntegrali(interesse);
            return mappa;
        }

        private void AggiungiVolti(double[] interesse, IReadOnlyList<ZonaImportante> volti)
        {
            if (volti.Count == 0) return;

            double zx0 = double.MaxValue, zy0 = double.MaxValue, zx1 = double.MinValue, zy1 = double.MinValue;
            double sommaX = 0, sommaY = 0, pesi = 0;
            foreach (var v in volti)
            {
                double x0 = v.X0 * W, y0 = v.Y0 * H, x1 = v.X1 * W, y1 = v.Y1 * H;
                var lato = Math.Max(x1 - x0, y1 - y0);

                // Margine attorno al volto e aria sopra la testa (il rilevatore inquadra fronte-mento).
                zx0 = Math.Min(zx0, x0 - 0.25 * lato);
                zx1 = Math.Max(zx1, x1 + 0.25 * lato);
                zy0 = Math.Min(zy0, y0 - 0.45 * lato);
                zy1 = Math.Max(zy1, y1 + 0.25 * lato);

                var area = (x1 - x0) * (y1 - y0);
                sommaX += (x0 + x1) / 2 * area; sommaY += (y0 + y1) / 2 * area; pesi += area;

                // Un volto vale più di qualunque dettaglio dello sfondo.
                for (var y = Math.Max(0, (int)y0); y < Math.Min(H, (int)Math.Ceiling(y1)); y++)
                    for (var x = Math.Max(0, (int)x0); x < Math.Min(W, (int)Math.Ceiling(x1)); x++)
                        interesse[y * W + x] += 3.0;
            }

            ZonaVolti = (Math.Max(0, zx0), Math.Max(0, zy0), Math.Min(W, zx1), Math.Min(H, zy1));
            if (pesi > 0) CentroVolti = (sommaX / pesi, sommaY / pesi);
        }

        private void CostruisciIntegrali(double[] valori)
        {
            var w1 = W + 1;
            _somma = new double[w1 * (H + 1)];
            _sommaX = new double[w1 * (H + 1)];
            _sommaY = new double[w1 * (H + 1)];
            for (var y = 0; y < H; y++)
            {
                for (var x = 0; x < W; x++)
                {
                    var v = valori[y * W + x];
                    var i = (y + 1) * w1 + x + 1;
                    _somma[i] = v + _somma[i - 1] + _somma[i - w1] - _somma[i - w1 - 1];
                    _sommaX[i] = v * (x + 0.5) + _sommaX[i - 1] + _sommaX[i - w1] - _sommaX[i - w1 - 1];
                    _sommaY[i] = v * (y + 0.5) + _sommaY[i - 1] + _sommaY[i - w1] - _sommaY[i - w1 - 1];
                }
            }
            _totale = _somma[^1];
        }

        private static double Area(double[] integrale, int w1, int x0, int y0, int x1, int y1) =>
            integrale[y1 * w1 + x1] - integrale[y0 * w1 + x1] - integrale[y1 * w1 + x0] + integrale[y0 * w1 + x0];

        /// <summary>
        /// Quanta parte interessante tiene, meno quanto è grande (un ritaglio più stretto vince se lascia
        /// fuori solo zone vuote), più un premio se il "baricentro" dell'interesse cade sui terzi.
        /// </summary>
        public double Punteggio(double x0, double y0, double x1, double y1, double frazioneArea)
        {
            var w1 = W + 1;
            int ix0 = (int)Math.Round(x0), iy0 = (int)Math.Round(y0);
            int ix1 = Math.Min(W, (int)Math.Round(x1)), iy1 = Math.Min(H, (int)Math.Round(y1));
            if (ix1 <= ix0 || iy1 <= iy0) return double.NegativeInfinity;

            var dentro = Area(_somma, w1, ix0, iy0, ix1, iy1);
            if (_totale <= 0) return -frazioneArea; // foto uniforme: vince il ritaglio più ampio
            var copertura = dentro / _totale;

            var premioTerzi = 0.0;
            if (CentroVolti is { } volto)
            {
                // Ritratti: volti in orizzontale su un terzo o al centro, in verticale sul terzo superiore.
                var fx = (volto.X - x0) / (x1 - x0);
                var fy = (volto.Y - y0) / (y1 - y0);
                double[] puntiX = [1.0 / 3, 0.5, 2.0 / 3];
                var dx = puntiX.Min(p => Math.Abs(fx - p));
                var dy = Math.Abs(fy - 1.0 / 3);
                premioTerzi = 2 * (1 - Math.Min(1, Math.Sqrt(dx * dx + dy * dy) * 3));
            }
            else if (dentro > 0)
            {
                var cx = Area(_sommaX, w1, ix0, iy0, ix1, iy1) / dentro;
                var cy = Area(_sommaY, w1, ix0, iy0, ix1, iy1) / dentro;
                var fx = (cx - x0) / (x1 - x0);
                var fy = (cy - y0) / (y1 - y0);
                // Distanza dal punto "forte" più vicino (terzi), con il centro come alternativa accettabile.
                double[] punti = [1.0 / 3, 0.5, 2.0 / 3];
                var dx = punti.Min(p => Math.Abs(fx - p));
                var dy = punti.Min(p => Math.Abs(fy - p));
                premioTerzi = 1 - Math.Min(1, Math.Sqrt(dx * dx + dy * dy) * 3);
            }

            return copertura - 0.55 * frazioneArea + 0.08 * premioTerzi;
        }
    }
}
