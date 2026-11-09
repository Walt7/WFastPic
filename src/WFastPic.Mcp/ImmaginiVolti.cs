using WFastPic.Core.Decoding;

namespace WFastPic.Mcp;

/// <summary>Un volto da mostrare: JPEG piccolo e da quale foto viene.</summary>
public sealed record ImmagineVolto(string Rif, string Foto, byte[] Jpeg, bool DallaFoto, string? Posizione = null)
{
    /// <summary>
    /// Dove sta il volto nella foto, per controllarlo a occhio: a parole («in alto, a destra») e in percentuale
    /// (da sinistra / dall'alto, 0 % = bordo sinistro o superiore). Coordinate normalizzate 0..1.
    /// </summary>
    public static string DescriviPosizione(double x0, double y0, double x1, double y1)
    {
        var cx = (x0 + x1) / 2; var cy = (y0 + y1) / 2;
        var orizzontale = cx < 1.0 / 3 ? "a sinistra" : cx > 2.0 / 3 ? "a destra" : "al centro";
        var verticale = cy < 1.0 / 3 ? "in alto" : cy > 2.0 / 3 ? "in basso" : "a metà altezza";
        var parole = orizzontale == "al centro" && verticale == "a metà altezza" ? "al centro della foto" : $"{verticale}, {orizzontale}";
        string P(double v) => $"{Math.Round(Math.Clamp(v, 0, 1) * 100)} %";
        return $"{parole} (da sinistra {P(x0)}–{P(x1)}, dall'alto {P(y0)}–{P(y1)})";
    }
}

/// <summary>
/// I volti come immagini piccole per chi deve dare il nome: ritagliati dalla foto con un po' di contesto attorno
/// (capelli, spalle: si riconosce meglio), oppure la miniatura salvata se la foto non è leggibile (disco scollegato).
/// Più volti insieme diventano un foglio unico con il numero su ognuno, così si risponde «2 = Anna».
/// </summary>
public static class ImmaginiVolti
{
    public const int Lato = 256;
    private const int Colonne = 4, Bordo = 4;

    public static async Task<List<ImmagineVolto>> PreparaAsync(IEnumerable<string> rifs, bool dallaFoto)
    {
        var risultato = new List<ImmagineVolto>();
        foreach (var rif in rifs)
        {
            if (Volti.Dettaglio(rif) is not { } v) continue;
            byte[]? jpeg = null;
            if (dallaFoto && File.Exists(v.Foto))
            {
                try { jpeg = await RitaglioAsync(v.Foto, v.X0, v.Y0, v.X1, v.Y1); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException) { }
            }
            var posizione = ImmagineVolto.DescriviPosizione(v.X0, v.Y0, v.X1, v.Y1);
            if (jpeg is not null) risultato.Add(new ImmagineVolto(rif, v.Foto, jpeg, true, posizione));
            else if (v.Miniatura is not null) risultato.Add(new ImmagineVolto(rif, v.Foto, v.Miniatura, false, posizione));
        }
        return risultato;
    }

    private static async Task<byte[]> RitaglioAsync(string foto, double x0, double y0, double x1, double y1)
    {
        // Decode alla scala giusta perché il ritaglio venga di circa 256 px: mai la foto intera a piena risoluzione.
        var (w, h) = await PreviewDecoder.LeggiDimensioniAsync(foto);
        var (_, _, latoPieno) = JpegBgra.RiquadroConContesto(w, h, x0, y0, x1, y1);
        var lato = (int)Math.Clamp(Math.Ceiling((double)Lato * Math.Max(w, h) / Math.Max(1, latoPieno)), Lato, Math.Max(w, h));
        var (bgra, lw, lh) = await PreviewDecoder.DecodePreviewBgraAsync(foto, Math.Min(lato, 4096));
        var (x, y, l) = JpegBgra.RiquadroConContesto(lw, lh, x0, y0, x1, y1);
        return await JpegBgra.CodificaAsync(JpegBgra.Ritaglia(bgra, lw, x, y, l, l), l, l, Lato, 0.85);
    }

    /// <summary>Un foglio con i volti in griglia (4 per riga), ognuno con il suo numero (1, 2, …) in alto a sinistra.</summary>
    public static async Task<byte[]> FoglioAsync(IReadOnlyList<ImmagineVolto> volti)
    {
        var colonne = Math.Min(Colonne, volti.Count);
        var righe = (volti.Count + colonne - 1) / colonne;
        var (larghezza, altezza) = (colonne * (Lato + Bordo) + Bordo, righe * (Lato + Bordo) + Bordo);
        var foglio = new byte[larghezza * altezza * 4];
        Array.Fill(foglio, (byte)40);

        for (var i = 0; i < volti.Count; i++)
        {
            var (bgra, w, h) = await JpegBgra.DecodificaAsync(volti[i].Jpeg);
            var (ox, oy) = (Bordo + i % colonne * (Lato + Bordo), Bordo + i / colonne * (Lato + Bordo));
            // Le miniature salvate sono 112 px: si ingrandiscono (pixel più grossi, ma stesso posto nel foglio).
            for (var y = 0; y < Lato; y++)
                for (var x = 0; x < Lato; x++)
                    Array.Copy(bgra, ((y * h / Lato) * w + x * w / Lato) * 4, foglio, ((oy + y) * larghezza + ox + x) * 4, 4);
            ScriviNumero(foglio, larghezza, ox, oy, i + 1);
        }
        return await JpegBgra.CodificaAsync(foglio, larghezza, altezza, qualita: 0.85);
    }

    // Cifre 3×5 (una riga = 3 bit), ingrandite: niente librerie di testo per un numero.
    private static readonly int[][] Cifre =
    [
        [7, 5, 5, 5, 7], [2, 6, 2, 2, 7], [7, 1, 7, 4, 7], [7, 1, 7, 1, 7], [5, 5, 7, 1, 1],
        [7, 4, 7, 1, 7], [7, 4, 7, 5, 7], [7, 1, 1, 1, 1], [7, 5, 7, 5, 7], [7, 5, 7, 1, 7],
    ];

    private static void ScriviNumero(byte[] bgra, int larghezza, int ox, int oy, int numero)
    {
        const int punto = 6, margine = 6;
        var testo = numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (w, h) = (testo.Length * 4 * punto - punto + 2 * margine, 5 * punto + 2 * margine);
        Rettangolo(bgra, larghezza, ox, oy, w, h, 0, 0, 0);
        for (var c = 0; c < testo.Length; c++)
        {
            var cifra = Cifre[testo[c] - '0'];
            for (var r = 0; r < 5; r++)
                for (var b = 0; b < 3; b++)
                    if ((cifra[r] >> (2 - b) & 1) == 1)
                        Rettangolo(bgra, larghezza, ox + margine + (c * 4 + b) * punto, oy + margine + r * punto, punto, punto, 255, 255, 255);
        }
    }

    private static void Rettangolo(byte[] bgra, int larghezza, int x0, int y0, int w, int h, byte r, byte g, byte b)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                var i = (y * larghezza + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (b, g, r, 255);
            }
    }
}
