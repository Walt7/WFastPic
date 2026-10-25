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
        var base_ = Path.GetFullPath(string.IsNullOrWhiteSpace(destinazione) ? CartellaPredefinita : destinazione);
        VerificaDestinazione(base_);

        nome = NomeSicuro(nome);
        var mancanti = new List<string>();
        var origini = new List<string>();
        foreach (var p in percorsi.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var completo = Path.GetFullPath(p);
            // Solo foto: lo strumento non deve poter copiare file qualsiasi dal disco.
            if (!EstensioniFoto.Contains(Path.GetExtension(completo), StringComparer.OrdinalIgnoreCase)) { mancanti.Add(completo + " (non è una foto)"); continue; }
            if (!File.Exists(completo)) { mancanti.Add(completo); continue; }
            origini.Add(completo);
        }
        if (origini.Count == 0) throw new InvalidOperationException("Nessuna foto da preparare.");

        Directory.CreateDirectory(base_);
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Unico(string nomeFile)
        {
            var radice = Path.GetFileNameWithoutExtension(nomeFile);
            var ext = Path.GetExtension(nomeFile);
            var candidato = nomeFile;
            for (var i = 1; !usati.Add(candidato); i++) candidato = $"{radice} ({i}){ext}";
            return candidato;
        }

        // Video con lo stesso nome base del file foto: segue la foto con lo stesso nome nel pacchetto.
        var coppie = origini.Select(o =>
        {
            string? video = null;
            if (includiVideo)
            {
                var senzaExt = Path.Combine(Path.GetDirectoryName(o)!, Path.GetFileNameWithoutExtension(o));
                video = EstensioniVideo.Select(e => senzaExt + e).FirstOrDefault(File.Exists);
            }
            var nomeFoto = Unico(Path.GetFileName(o));
            // Il video riprende il nome (eventualmente numerato) della foto, così restano abbinati.
            var nomeVideo = video is null ? null : Path.GetFileNameWithoutExtension(nomeFoto) + Path.GetExtension(video);
            if (nomeVideo is not null) usati.Add(nomeVideo);
            return (Foto: o, NomeFoto: nomeFoto, Video: video, NomeVideo: nomeVideo);
        }).ToList();

        long totale = 0;
        var video = coppie.Count(c => c.Video is not null);
        string percorsoFinale;

        if (zip)
        {
            percorsoFinale = PercorsoLibero(Path.Combine(base_, nome + ".zip"), cartella: false);
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
            percorsoFinale = PercorsoLibero(Path.Combine(base_, nome), cartella: true);
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

        return new EsitoPacchetto(percorsoFinale, zip, coppie.Count, video, totale, mancanti);
    }

    /// <summary>Mai dentro una cartella di foto catalogata: il pacchetto non deve sporcare l'archivio.</summary>
    private static void VerificaDestinazione(string destinazione)
    {
        foreach (var cartella in Ricerca.CartelleRecenti())
        {
            var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella));
            if (string.Equals(destinazione, c, StringComparison.OrdinalIgnoreCase)
                || destinazione.StartsWith(c + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"La destinazione è dentro la cartella di foto «{cartella}»: scegline un'altra.");
        }
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
