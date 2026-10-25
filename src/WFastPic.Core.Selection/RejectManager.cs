using Microsoft.VisualBasic.FileIO;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;

namespace WFastPic.Core.Selection;

/// <summary>
/// Unico punto dove uno scarto (stato Reject) può lasciare la cartella. Mai invocato dal ciclo di
/// selezione principale (Delete = solo stato Reject in DB). Nessuna cancellazione irreversibile:
/// l'eliminazione passa sempre dal Cestino di Windows.
/// Il video abbinato (Live Photo .mov / Motion Photo .mp4) segue sempre la foto: un video orfano
/// nella cartella non avrebbe più senso, e in Scarti/ deve restare riconoscibile come coppia.
/// Le eccezioni di I/O (file bloccato, permessi) arrivano al chiamante; in quel caso la riga DB
/// resta, così la foto non sparisce dalla libreria senza essere stata davvero spostata.
/// </summary>
public sealed class RejectManager
{
    private readonly LibrarySidecarDatabase _db;

    public RejectManager(LibrarySidecarDatabase db)
    {
        _db = db;
    }

    public IReadOnlyList<PhotoRecord> GetScarti() => _db.GetByStato(StatoSelezione.Reject);

    public void SpostaNelCestino(PhotoRecord foto)
    {
        var percorso = Path.Combine(_db.CartellaFoto, foto.PathRelativo);
        var video = TrovaVideo(percorso);

        // Dal Cestino non si può annullare a metà: prima si verifica che entrambi i file siano liberi,
        // così un video bloccato non lascia la foto cestinata e il video orfano in cartella.
        VerificaNonBloccato(percorso);
        VerificaNonBloccato(video);

        if (File.Exists(percorso)) Cestina(percorso);
        if (video is not null) Cestina(video);

        _db.RemovePhoto(foto.Id);
    }

    /// <summary>
    /// Sposta foto (ed eventuale video) in Scarti/. Se il nome è già occupato usa "nome (1).ext"
    /// per entrambi, così la coppia resta abbinata. Restituisce il percorso finale della foto.
    /// </summary>
    public string SpostaInScarti(PhotoRecord foto)
    {
        var origine = Path.Combine(_db.CartellaFoto, foto.PathRelativo);
        var video = TrovaVideo(origine);
        var cartellaScarti = Path.Combine(_db.CartellaFoto, "Scarti");
        Directory.CreateDirectory(cartellaScarti);
        var (destinazioneFoto, destinazioneVideo) = DestinazioniLibere(
            cartellaScarti, Path.GetFileName(foto.PathRelativo), video is null ? null : Path.GetExtension(video));

        VerificaNonBloccato(origine);
        VerificaNonBloccato(video);

        if (video is not null) File.Move(video, destinazioneVideo!, overwrite: false);
        try
        {
            if (File.Exists(origine)) File.Move(origine, destinazioneFoto, overwrite: false);
        }
        catch
        {
            // Foto non spostata: il video torna al suo posto, la coppia non si separa.
            if (video is not null)
            {
                try { File.Move(destinazioneVideo!, video, overwrite: false); }
                catch (IOException) { }
            }
            throw;
        }

        _db.RemovePhoto(foto.Id);
        return destinazioneFoto;
    }

    public void Lascia(PhotoRecord foto) => _db.UpdateStato(foto.Id, StatoSelezione.None);

    /// <summary>
    /// Primo nome libero per la foto e, se presente, per il suo video con lo stesso nome base:
    /// "IMG_1.HEIC"/"IMG_1.MOV", poi "IMG_1 (1).HEIC"/"IMG_1 (1).MOV", …
    /// </summary>
    public static (string Foto, string? Video) DestinazioniLibere(string cartella, string nomeFoto, string? estensioneVideo)
    {
        var nome = Path.GetFileNameWithoutExtension(nomeFoto);
        var estensioneFoto = Path.GetExtension(nomeFoto);
        for (var i = 0; ; i++)
        {
            var baseNome = i == 0 ? nome : $"{nome} ({i})";
            var foto = Path.Combine(cartella, baseNome + estensioneFoto);
            var video = estensioneVideo is null ? null : Path.Combine(cartella, baseNome + estensioneVideo);
            if (!File.Exists(foto) && (video is null || !File.Exists(video))) return (foto, video);
        }
    }

    /// <summary>Video abbinato con il nome reale su disco (maiuscole comprese, es. "IMG_1.MOV").</summary>
    private static string? TrovaVideo(string percorsoFoto)
    {
        var video = CompanionVideoDetector.TrovaVideoAbbinato(percorsoFoto);
        if (video is null) return null;
        var cartella = Path.GetDirectoryName(video) ?? "";
        return Directory.EnumerateFiles(cartella, Path.GetFileName(video)).FirstOrDefault() ?? video;
    }

    private static void VerificaNonBloccato(string? percorso)
    {
        if (percorso is null || !File.Exists(percorso)) return;
        // Lancia IOException se un altro programma ha il file aperto (es. video in riproduzione).
        using var _ = File.Open(percorso, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void Cestina(string percorso) =>
        FileSystem.DeleteFile(percorso, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}
