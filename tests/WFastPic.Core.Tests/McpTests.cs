using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
using WFastPic.Mcp;
using Xunit;

namespace WFastPic.Core.Tests;

/// <summary>Server MCP: ricerca per persona/data nei sidecar (madre + sottocartella) e pacchetto da condividere.</summary>
public class McpTests : IDisposable
{
    private readonly string _radice = Path.Combine(Path.GetTempPath(), "wfastpic-mcp-" + Guid.NewGuid());
    private readonly string? _datiPrima = Environment.GetEnvironmentVariable("WFASTPIC_DATA");
    private readonly string _foto, _dati, _uscita;

    public McpTests()
    {
        _foto = Path.Combine(_radice, "Foto");
        _dati = Path.Combine(_radice, "dati");
        _uscita = Path.Combine(_radice, "uscita");
        Directory.CreateDirectory(Path.Combine(_foto, "Matrimonio"));
        Directory.CreateDirectory(_dati);
        Environment.SetEnvironmentVariable("WFASTPIC_DATA", _dati);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("WFASTPIC_DATA", _datiPrima);
        try { Directory.Delete(_radice, recursive: true); } catch (IOException) { }
    }

    private void Prepara(out long idPersona)
    {
        File.WriteAllBytes(Path.Combine(_foto, "Matrimonio", "IMG_1.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_foto, "Matrimonio", "IMG_1.mov"), [9, 9]);
        File.WriteAllBytes(Path.Combine(_foto, "Matrimonio", "IMG_2.jpg"), [4, 5, 6]);
        File.WriteAllBytes(Path.Combine(_foto, "Matrimonio", "IMG_3.jpg"), [7, 8]);

        using (var archivio = ArchivioPersone.Apri(Path.Combine(_dati, "persone.db")))
        {
            idPersona = archivio.TrovaOCrea("Alberto Rossi", null);
            archivio.ImpostaSoprannome(idPersona, "Alby");
        }

        // Il sidecar vive nella sottocartella: la ricerca sulla madre la trova comunque.
        var sotto = Path.Combine(_foto, "Matrimonio");
        using (var db = LibrarySidecarDatabase.OpenOrCreate(sotto))
        {
            db.SyncScan(FolderScanner.Scan(sotto));
            var foto = db.GetAllPhotos().OrderBy(f => f.PathRelativo).ToList();
            db.SalvaVoltiPersone(foto[0].Id, [new VoltoDaSalvare(0, 0, 1, 1, 1f, [1], [1], idPersona, StatoPersona.Confermato)]);
            db.SalvaVoltiPersone(foto[1].Id, [new VoltoDaSalvare(0, 0, 1, 1, 1f, [1], [1], idPersona, StatoPersona.Proposto)]);
            db.UpdateStato(foto[2].Id, StatoSelezione.Reject);
        }

        File.WriteAllText(Path.Combine(_dati, "recent-folders.json"), JsonSerializer.Serialize(new[] { sotto }));
    }

    [Fact]
    public void Cerca_per_soprannome_trova_solo_i_volti_confermati_e_nasconde_gli_scarti()
    {
        Prepara(out _);

        var confermati = Ricerca.Cerca(new FiltriFoto(Cartella: _foto, Persone: ["alby"]));
        Assert.Equal(["IMG_1.jpg"], confermati.Select(f => Path.GetFileName(f.Percorso)));
        Assert.Equal(["Alberto Rossi"], confermati[0].Persone);

        var conProposte = Ricerca.Cerca(new FiltriFoto(Cartella: _foto, Persone: ["Rossi"], AncheProposte: true));
        Assert.Equal(2, conProposte.Count);

        Assert.Empty(Ricerca.Cerca(new FiltriFoto(Persone: ["Mario"])));
        Assert.Equal(2, Ricerca.Cerca(new FiltriFoto(Cartella: "Matrimonio")).Count); // IMG_3 è scartata
    }

    [Fact]
    public void Pacchetto_copia_foto_e_video_abbinato_senza_toccare_gli_originali()
    {
        Prepara(out _);
        var foto = Ricerca.Cerca(new FiltriFoto(Persone: ["Alberto"])).Select(f => f.Percorso).ToList();

        var esito = Pacchetto.Prepara(foto, "Matrimonio Alby", _uscita, zip: false, includiVideo: true);

        Assert.Equal(1, esito.Foto);
        Assert.Equal(1, esito.Video);
        Assert.True(File.Exists(Path.Combine(esito.Percorso, "IMG_1.jpg")));
        Assert.True(File.Exists(Path.Combine(esito.Percorso, "IMG_1.mov")));
        Assert.True(File.Exists(foto[0]));

        // Rifatto: non sovrascrive, prende " (2)".
        var secondo = Pacchetto.Prepara(foto, "Matrimonio Alby", _uscita, zip: false, includiVideo: true);
        Assert.EndsWith("Matrimonio Alby (2)", secondo.Percorso);
    }

    [Fact]
    public void Pacchetto_zip_e_destinazione_dentro_le_foto_rifiutata()
    {
        Prepara(out _);
        var foto = Ricerca.Cerca(new FiltriFoto(Persone: ["Alberto"])).Select(f => f.Percorso).ToList();

        var esito = Pacchetto.Prepara(foto, "Zip", _uscita, zip: true, includiVideo: true);
        using (var zip = ZipFile.OpenRead(esito.Percorso))
            Assert.Equal(["IMG_1.jpg", "IMG_1.mov"], zip.Entries.Select(e => e.Name).Order());

        Assert.Throws<InvalidOperationException>(() =>
            Pacchetto.Prepara(foto, "No", Path.Combine(_foto, "Matrimonio", "Export"), zip: false, includiVideo: true));
        Assert.Throws<InvalidOperationException>(() =>
            Pacchetto.Prepara(["C:\\Windows\\notepad.exe"], "No", _uscita, zip: false, includiVideo: true));
    }

    [Fact]
    public void Selezione_salvata_si_riusa_come_filtro_e_non_si_sovrascrive_per_sbaglio()
    {
        Prepara(out _);

        var risposta = Strumenti.Chiama("salva_selezione", JsonNode.Parse("""{"nome":"Matrimonio","persone":["Rossi"],"anche_proposte":true}""")!.AsObject());
        Assert.Contains("\"foto\": 2", risposta);
        Assert.Contains("Matrimonio", Strumenti.Chiama("elenco_selezioni", null));

        Assert.Throws<InvalidOperationException>(() =>
            Strumenti.Chiama("salva_selezione", JsonNode.Parse("""{"nome":"matrimonio","persone":["Rossi"]}""")!.AsObject()));

        var dallaSelezione = Ricerca.Cerca(new FiltriFoto(Percorsi: Selezioni.Trova("MATRIMONIO")!.Percorsi.ToHashSet(StringComparer.OrdinalIgnoreCase)));
        Assert.Equal(2, dallaSelezione.Count);

        var pacchetto = Strumenti.Chiama("prepara_pacchetto",
            JsonNode.Parse($$"""{"selezione":"Matrimonio","destinazione":{{JsonSerializer.Serialize(_uscita)}}}""")!.AsObject());
        Assert.Contains("\"foto\": 2", pacchetto);

        Assert.Equal("Selezione tolta dall'elenco.", Strumenti.Chiama("elimina_selezione", JsonNode.Parse("""{"nome":"Matrimonio"}""")!.AsObject()));
        Assert.Empty(Selezioni.Elenco());
    }

    [Fact]
    public void Strumenti_rispondono_in_json_e_rifiutano_un_pacchetto_senza_filtri()
    {
        Prepara(out _);
        var testo = Strumenti.Chiama("cerca_foto", JsonNode.Parse("""{"persone":["Alby"]}""")!.AsObject());
        Assert.Contains("\"totale\": 1", testo);
        Assert.Contains("IMG_1.jpg", testo);
        Assert.Throws<InvalidOperationException>(() => Strumenti.Chiama("prepara_pacchetto", new JsonObject()));
    }
}
