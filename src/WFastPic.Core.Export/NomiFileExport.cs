using WFastPic.Core.Library;
using WFastPic.Core.Persistence;

namespace WFastPic.Core.Export;

/// <summary>
/// Nome del JPEG di output. Dentro lo stesso export due foto con lo stesso nome base (IMG_1.HEIC e
/// IMG_1.JPG) non si sovrascrivono mai a vicenda; un file rimasto da un export precedente viene
/// sostituito solo se richiesto e mai se potrebbe essere una foto originale: cartella delle foto,
/// sue sottocartelle, altre cartelle catalogate (con sidecar), ovunque una scansione lo vedrebbe.
/// </summary>
public static class NomiFileExport
{
    public static string PercorsoOutput(
        string cartellaOutput, string cartellaFoto, string nomeBase, bool sostituisciEsistenti,
        ISet<string> usati, Func<string, bool> esiste, bool? cartellaConOriginali = null)
    {
        var puoSostituire = sostituisciEsistenti
            && !(cartellaConOriginali ?? ContieneOriginali(cartellaOutput, cartellaFoto));

        var candidato = Path.Combine(cartellaOutput, nomeBase + ".jpg");
        var contatore = 2;
        while (usati.Contains(candidato) || (!puoSostituire && esiste(candidato)))
        {
            candidato = Path.Combine(cartellaOutput, $"{nomeBase}_{contatore}.jpg");
            contatore++;
        }

        usati.Add(candidato);
        return candidato;
    }

    /// <summary>
    /// Vero se i JPEG di <paramref name="cartellaOutput"/> possono essere originali: la cartella delle foto
    /// o una cartella con sidecar (anche più in alto) la catalogherebbe con la scansione ricorsiva.
    /// </summary>
    public static bool ContieneOriginali(string cartellaOutput, string cartellaFoto)
    {
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartellaOutput));
        for (var antenato = output; !string.IsNullOrEmpty(antenato); antenato = Path.GetDirectoryName(antenato))
        {
            var catalogata = StessaCartella(antenato, cartellaFoto)
                || File.Exists(Path.Combine(antenato, LibrarySidecarDatabase.CartellaSidecar, LibrarySidecarDatabase.NomeFileDatabase));
            if (catalogata && FolderScanner.ScansioneVedrebbe(antenato, output)) return true;
        }
        return false;
    }

    public static bool StessaCartella(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
