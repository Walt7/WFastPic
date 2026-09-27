using WFastPic.Core.Decoding;
using WFastPic.Core.Persistence;
using WFastPic.Core.Processing;
using WFastPic.Core.Selection;

namespace WFastPic.Core.Export;

public sealed record RisultatoExport(long PhotoId, string PercorsoOutput, bool Successo, string? Errore);
public sealed record ProgressoExport(int Completate, int Totale, string FileCorrente);

/// <summary>
/// Orchestrazione dell'export batch: decodifica full/preset, applica la ricetta salvata
/// (raddrizza+crop+auto), watermark opzionale, poi codifica JPEG con tag personalizzati.
/// Esporta solo foto Selected di default, mai le foto originali sono toccate.
/// </summary>
public static class Exporter
{
    public static async Task<IReadOnlyList<RisultatoExport>> EseguiAsync(
        LibrarySidecarDatabase db,
        ExportOptions opzioni,
        IProgress<ProgressoExport>? progresso = null,
        CancellationToken ct = default)
    {
        var foto = opzioni.SoloSelezionate
            ? db.GetByStato(StatoSelezione.Selected)
            : db.GetAllPhotos();

        Directory.CreateDirectory(opzioni.CartellaOutput);

        var risultati = new List<RisultatoExport>();
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var latoLungo = opzioni.LatoLungo.LatoLungoPixel() ?? int.MaxValue;

        for (var i = 0; i < foto.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var record = foto[i];
            var percorsoOriginale = Path.Combine(db.CartellaFoto, record.PathRelativo);
            progresso?.Report(new ProgressoExport(i, foto.Count, record.PathRelativo));

            try
            {
                var ricettaJson = db.GetRecipeJson(record.Id);
                var ricetta = ricettaJson is not null ? EditRecipeSerializer.FromJson(ricettaJson) : EditRecipe.Default;

                var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(percorsoOriginale, latoLungo);
                var (elaborato, ew, eh) = RecipeApplier.Applica(bgra, w, h, ricetta);

                if (!string.IsNullOrWhiteSpace(opzioni.TestoWatermark))
                    WatermarkRenderer.Applica(elaborato, ew, eh, opzioni.TestoWatermark, opzioni.OpacitaWatermark, opzioni.PosizioneWatermark);

                var nomeBase = Path.GetFileNameWithoutExtension(record.PathRelativo);
                var percorsoOutput = PercorsoOutputUnivoco(opzioni.CartellaOutput, nomeBase, usati);

                await JpegEncoder.SalvaAsync(percorsoOutput, elaborato, ew, eh, opzioni.QualitaJpeg, percorsoOriginale, opzioni.Tag);

                risultati.Add(new RisultatoExport(record.Id, percorsoOutput, true, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                risultati.Add(new RisultatoExport(record.Id, "", false, ex.Message));
            }
        }

        progresso?.Report(new ProgressoExport(foto.Count, foto.Count, ""));
        return risultati;
    }

    private static string PercorsoOutputUnivoco(string cartella, string nomeBase, HashSet<string> usati)
    {
        var candidato = Path.Combine(cartella, nomeBase + ".jpg");
        var contatore = 2;
        while (usati.Contains(candidato) || File.Exists(candidato))
        {
            candidato = Path.Combine(cartella, $"{nomeBase}_{contatore}.jpg");
            contatore++;
        }

        usati.Add(candidato);
        return candidato;
    }
}
