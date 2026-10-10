namespace WFastPic.Core.Export;

/// <summary>
/// Nome del JPEG di output. Dentro lo stesso export due foto con lo stesso nome base (IMG_1.HEIC e
/// IMG_1.JPG) non si sovrascrivono mai a vicenda; un file rimasto da un export precedente viene
/// sostituito solo se richiesto e mai se l'output è la cartella delle foto originali.
/// </summary>
public static class NomiFileExport
{
    public static string PercorsoOutput(
        string cartellaOutput, string cartellaFoto, string nomeBase, bool sostituisciEsistenti,
        ISet<string> usati, Func<string, bool> esiste)
    {
        var puoSostituire = sostituisciEsistenti && !StessaCartella(cartellaOutput, cartellaFoto);

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

    public static bool StessaCartella(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
