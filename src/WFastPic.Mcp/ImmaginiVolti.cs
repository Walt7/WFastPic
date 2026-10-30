using WFastPic.Core.Decoding;

namespace WFastPic.Mcp;

/// <summary>Un volto da mostrare: JPEG piccolo e da quale foto viene.</summary>
public sealed record ImmagineVolto(string Rif, string Foto, byte[] Jpeg, bool DallaFoto);

/// <summary>
/// I volti come immagini piccole per chi deve dare il nome: ritagliati dalla foto con un po' di contesto attorno
/// (capelli, spalle: si riconosce meglio), oppure la miniatura salvata se la foto non è leggibile (disco scollegato).
/// </summary>
public static class ImmaginiVolti
{
    public const int Lato = 256;

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
            if (jpeg is not null) risultato.Add(new ImmagineVolto(rif, v.Foto, jpeg, true));
            else if (v.Miniatura is not null) risultato.Add(new ImmagineVolto(rif, v.Foto, v.Miniatura, false));
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
}
