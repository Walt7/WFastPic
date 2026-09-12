namespace WFastPic.Core.Library;

/// <summary>
/// Rileva se una foto ha un video "gemello" accanto: Apple Live Photo (stesso nome file + .mov)
/// o Motion Photo/video esportati separatamente (stesso nome + .mp4). Solo un controllo di
/// esistenza file — nessun parsing del contenuto, quindi resta rapido anche su centinaia di foto.
/// La riproduzione resta fuori scope per questo giro: qui serve solo a segnalarne la presenza.
/// </summary>
public static class CompanionVideoDetector
{
    private static readonly string[] EstensioniVideo = [".mov", ".mp4"];

    public static string? TrovaVideoAbbinato(string percorsoFoto)
    {
        var baseSenzaEstensione = Path.Combine(
            Path.GetDirectoryName(percorsoFoto) ?? "",
            Path.GetFileNameWithoutExtension(percorsoFoto));

        foreach (var estensione in EstensioniVideo)
        {
            var candidato = baseSenzaEstensione + estensione;
            if (File.Exists(candidato))
            {
                return candidato;
            }
        }

        return null;
    }
}
