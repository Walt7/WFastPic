using System.IO.Compression;

namespace WFastPic.Mcp;

public sealed record EsitoPacchetto(string Percorso, bool Zip, int Foto, int Video, long Byte, IReadOnlyList<string> Mancanti);

/// <summary>
/// Prepara le foto da dare a qualcuno: una cartella (o uno zip) con COPIE degli originali, video abbinato
/// compreso (Live Photo). Non sposta, non cancella e non modifica nulla delle cartelle foto; non invia niente.
/// </summary>
public static class Pacchetto
{
    private static readonly string[] EstensioniFoto = [".heic", ".heif", ".jpg", ".jpeg"];
    private static readonly string[] EstensioniVideo = [".mov", ".mp4"];

    public static string CartellaPredefinita => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WFastPic condivisione");

    public static EsitoPacchetto Prepara(
        IReadOnlyList<string> percorsi, string nome, string? destinazione, bool zip, bool includiVideo)
    {
        var base_ = Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(destinazione) ? CartellaPredefinita : destinazione));
        VerificaDestinazione(base_);

        nome = NomeSicuro(nome);
        var mancanti = new List<string>();
        var origini = new List<string>();
        var catalogate = Ricerca.Catalogate(percorsi);
        foreach (var p in percorsi.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var completo = Path.GetFullPath(p);
            // Solo foto catalogate: lo strumento non deve poter copiare file qualsiasi dal disco.
            if (!EstensioniFoto.Contains(Path.GetExtension(completo), StringComparer.OrdinalIgnoreCase)) { mancanti.Add(completo + " (non è una foto)"); continue; }
            if (!File.Exists(completo)) { mancanti.Add(completo); continue; }
            if (!catalogate.Contains(completo)) { mancanti.Add(completo + " (non è in una cartella catalogata da WFastPic)"); continue; }
            origini.Add(completo);
        }
        if (origini.Count == 0) throw new InvalidOperationException("Nessuna foto da preparare.");
        VerificaFuoriDalleSorgenti(base_, origini);

        Directory.CreateDirectory(base_);
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Foto e video condividono il nome base (Live Photo): si rinumera finché sono liberi entrambi,
        // così IMG_1.HEIC e IMG_1.JPG con lo stesso IMG_1.MOV accanto non si contendono il video.
        var coppie = origini.Select(o =>
        {
            string? video = null;
            if (includiVideo)
            {
                var senzaExt = Path.Combine(Path.GetDirectoryName(o)!, Path.GetFileNameWithoutExtension(o));
                video = EstensioniVideo.Select(e => senzaExt + e).FirstOrDefault(File.Exists);
            }
            var radice = Path.GetFileNameWithoutExtension(o);
            var extFoto = Path.GetExtension(o);
            var extVideo = video is null ? null : Path.GetExtension(video);
            for (var i = 0; ; i++)
            {
                var nomeBase = i == 0 ? radice : $"{radice} ({i})";
                var nomeFoto = nomeBase + extFoto;
                var nomeVideo = extVideo is null ? null : nomeBase + extVideo;
                if (usati.Contains(nomeFoto) || (nomeVideo is not null && usati.Contains(nomeVideo))) continue;
                usati.Add(nomeFoto);
                if (nomeVideo is not null) usati.Add(nomeVideo);
                return (Foto: o, NomeFoto: nomeFoto, Video: video, NomeVideo: nomeVideo);
            }
        }).ToList();

        long totale = 0;
        var video = coppie.Count(c => c.Video is not null);
        string percorsoFinale;

        percorsoFinale = zip
            ? PercorsoLibero(Path.Combine(base_, nome + ".zip"), cartella: false)
            : PercorsoLibero(Path.Combine(base_, nome), cartella: true);
        try
        {
            if (zip)
            {
                using var archivio = ZipFile.Open(percorsoFinale, ZipArchiveMode.Create);
                foreach (var c in coppie)
                {
                    // Foto e video sono già compressi: NoCompression è molto più veloce e quasi uguale in dimensioni.
                    archivio.CreateEntryFromFile(c.Foto, c.NomeFoto, CompressionLevel.NoCompression);
                    totale += new FileInfo(c.Foto).Length;
                    if (c.Video is not null)
                    {
                        archivio.CreateEntryFromFile(c.Video, c.NomeVideo!, CompressionLevel.NoCompression);
                        totale += new FileInfo(c.Video).Length;
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(percorsoFinale);
                foreach (var c in coppie)
                {
                    File.Copy(c.Foto, Path.Combine(percorsoFinale, c.NomeFoto), overwrite: false);
                    totale += new FileInfo(c.Foto).Length;
                    if (c.Video is not null)
                    {
                        File.Copy(c.Video, Path.Combine(percorsoFinale, c.NomeVideo!), overwrite: false);
                        totale += new FileInfo(c.Video).Length;
                    }
                }
            }
        }
        catch
        {
            // Niente pacchetti a metà: si toglie solo quello appena creato (copie, mai gli originali).
            try
            {
                if (zip) File.Delete(percorsoFinale);
                else if (Directory.Exists(percorsoFinale)) Directory.Delete(percorsoFinale, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }

        return new EsitoPacchetto(percorsoFinale, zip, coppie.Count, video, totale, mancanti);
    }

    /// <summary>
    /// Mai dentro una cartella di foto catalogata (nota all'app o con sidecar, anche più in alto): il pacchetto
    /// non deve sporcare l'archivio, e una scansione ricorsiva lo riprenderebbe come doppioni.
    /// </summary>
    private static void VerificaDestinazione(string destinazione)
    {
        foreach (var cartella in Ricerca.CartelleRecenti())
        {
            if (Dentro(destinazione, cartella))
                throw new InvalidOperationException($"La destinazione è dentro la cartella di foto «{cartella}»: scegline un'altra.");
        }
        for (var su = destinazione; !string.IsNullOrEmpty(su); su = Path.GetDirectoryName(su))
        {
            if (Ricerca.HaSidecar(su))
                throw new InvalidOperationException($"La destinazione è dentro la cartella di foto «{su}»: scegline un'altra.");
        }
    }

    private static void VerificaFuoriDalleSorgenti(string destinazione, IEnumerable<string> origini)
    {
        foreach (var cartella in origini.Select(o => Path.GetDirectoryName(o)!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Dentro(destinazione, cartella))
                throw new InvalidOperationException($"La destinazione è dentro la cartella delle foto «{cartella}»: scegline un'altra.");
        }
    }

    private static bool Dentro(string percorso, string cartella)
    {
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella));
        return string.Equals(percorso, c, StringComparison.OrdinalIgnoreCase)
               || percorso.StartsWith(c + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string NomeSicuro(string nome)
    {
        var pulito = string.Concat((string.IsNullOrWhiteSpace(nome) ? "Foto" : nome.Trim())
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim('.', ' ');
        return pulito.Length == 0 ? "Foto" : pulito;
    }

    // Come per gli export: un pacchetto già fatto non si sovrascrive, quello nuovo prende " (2)".
    private static string PercorsoLibero(string percorso, bool cartella)
    {
        bool Esiste(string p) => cartella ? Directory.Exists(p) : File.Exists(p);
        if (!Esiste(percorso)) return percorso;
        var radice = cartella ? percorso : Path.Combine(Path.GetDirectoryName(percorso)!, Path.GetFileNameWithoutExtension(percorso));
        var ext = cartella ? "" : Path.GetExtension(percorso);
        for (var i = 2; ; i++)
        {
            var candidato = $"{radice} ({i}){ext}";
            if (!Esiste(candidato)) return candidato;
        }
    }
}
