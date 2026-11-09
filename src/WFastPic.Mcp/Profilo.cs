using System.Diagnostics;
using WFastPic.Core.Decoding;
using WFastPic.Core.Persone;

namespace WFastPic.Mcp;

/// <summary>Temporaneo: cronometra le fasi dell'analisi su foto reali (una alla volta, niente salvataggio).</summary>
internal static class Profilo
{
    public static async Task<int> EseguiAsync(string cartella, int quante)
    {
        var estensioni = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".heic", ".png" };
        var tutte = Directory.EnumerateFiles(cartella, "*.*", SearchOption.AllDirectories).Where(f => estensioni.Contains(Path.GetExtension(f))).Take(quante * 20).ToList();
        var foto = tutte.Where((_, i) => i % 20 == 0).Take(quante).ToList();
        Console.WriteLine($"{foto.Count} foto (1 ogni 20 in {cartella})");

        await ModelliPersone.AssicuraAsync(new HttpClient(), ModelliPersone.CartellaPredefinita);
        using var rilevatore = new RilevatoreYuNet(ModelliPersone.Percorso(ModelliPersone.CartellaPredefinita, ModelliPersone.Rilevatore));
        using var estrattore = new EstrattoreImpronte(ModelliPersone.Percorso(ModelliPersone.CartellaPredefinita, ModelliPersone.Riconoscimento));

        foreach (var modo in new[] { Windows.Graphics.Imaging.BitmapInterpolationMode.Fant, Windows.Graphics.Imaging.BitmapInterpolationMode.Linear, Windows.Graphics.Imaging.BitmapInterpolationMode.NearestNeighbor })
        {
            var t = Stopwatch.StartNew();
            foreach (var f in foto) await PreviewDecoder.DecodePreviewBgraAsync(f, AnalizzatorePersone.LatoAnalisi, interpolazione: modo);
            Console.WriteLine($"decodifica {modo}: {t.Elapsed.TotalMilliseconds / foto.Count:F1} ms/foto");
        }

        double tIo = 0, tDec = 0, tRil = 0, tAll = 0, tEst = 0, tJpg = 0;
        long pixel = 0; var volti = 0;
        var sw = new Stopwatch();
        foreach (var f in foto)
        {
            sw.Restart(); File.ReadAllBytes(f); tIo += sw.Elapsed.TotalMilliseconds;
            sw.Restart(); var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(f, AnalizzatorePersone.LatoAnalisi); tDec += sw.Elapsed.TotalMilliseconds;
            pixel += (long)w * h;
            sw.Restart(); var trovati = rilevatore.Rileva(bgra, w, h); tRil += sw.Elapsed.TotalMilliseconds;
            foreach (var v in trovati)
            {
                if (Math.Min(v.Larghezza, v.Altezza) < AnalizzatorePersone.LatoMinimoVolto) continue;
                volti++;
                sw.Restart(); var al = AllineamentoVolto.Allinea(bgra, w, h, v.Punti); tAll += sw.Elapsed.TotalMilliseconds;
                sw.Restart(); estrattore.Calcola(al); tEst += sw.Elapsed.TotalMilliseconds;
                var mini = new byte[112 * 112 * 4];
                sw.Restart(); await JpegBgra.CodificaAsync(mini, 112, 112); tJpg += sw.Elapsed.TotalMilliseconds;
            }
        }

        var n = foto.Count;
        Console.WriteLine($"{volti} volti, {pixel / n / 1000} kpx medi");
        Console.WriteLine($"per foto (ms): lettura file {tIo / n:F1} | decodifica+scala {tDec / n:F1} | YuNet {tRil / n:F1}");
        Console.WriteLine($"per volto (ms): allineamento {tAll / Math.Max(1, volti):F2} | SFace {tEst / Math.Max(1, volti):F2} | miniatura JPEG {tJpg / Math.Max(1, volti):F2}");
        return 0;
    }
}
