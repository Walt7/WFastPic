using System.Text.Json;
using System.Text.Json.Nodes;
using WFastPic.Core.Decoding;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;
using WFastPic.Mcp;
using Xunit;

namespace WFastPic.Core.Tests;

/// <summary>
/// Server MCP, strumenti sui volti: gruppi senza nome tra cartelle diverse, nome dato e proposto altrove, conferma,
/// annulla, persone in un giorno, immagini dei volti e analisi a lotti riprendibile (con un analizzatore finto).
/// </summary>
public class McpVoltiTests : IDisposable
{
    private readonly string _radice = Path.Combine(Path.GetTempPath(), "wfastpic-mcp-volti-" + Guid.NewGuid());
    private readonly string? _datiPrima = Environment.GetEnvironmentVariable("WFASTPIC_DATA");
    private readonly string _dati, _mare, _montagna;
    private static readonly DateTime Giorno = new(2024, 8, 15, 10, 0, 0);

    public McpVoltiTests()
    {
        _dati = Path.Combine(_radice, "dati");
        _mare = Path.Combine(_radice, "Foto", "Mare");
        _montagna = Path.Combine(_radice, "Foto", "Montagna");
        Directory.CreateDirectory(_dati);
        Directory.CreateDirectory(_mare);
        Directory.CreateDirectory(_montagna);
        Environment.SetEnvironmentVariable("WFASTPIC_DATA", _dati);
    }

    public void Dispose()
    {
        AnalisiCartella.AnalizzatoreDiProva = null;
        Environment.SetEnvironmentVariable("WFASTPIC_DATA", _datiPrima);
        try { Directory.Delete(_radice, recursive: true); } catch (IOException) { }
    }

    /// <summary>Impronta normalizzata vicina all'asse indicato: stesso asse = stessa persona.</summary>
    private static float[] Impronta(int asse, int seme)
    {
        var caso = new Random(seme);
        var v = new float[Impronte.Dimensione];
        for (var i = 0; i < v.Length; i++) v[i] = (float)(caso.NextDouble() - 0.5) * 0.05f;
        v[asse] = 1;
        return Impronte.Normalizza(v);
    }

    /// <summary>Una foto per volto: (nome file, asse della persona). Il volto prende la data <see cref="Giorno"/>.</summary>
    private static void Catalogo(string cartella, params (string File, int Asse)[] foto)
    {
        foreach (var (file, _) in foto) File.WriteAllBytes(Path.Combine(cartella, file), [1, 2, 3]);
        using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
        db.SyncScan(foto.Select(f =>
        {
            var info = new FileInfo(Path.Combine(cartella, f.File));
            return new ScannedPhoto(new PhotoIdentity(f.File, info.Length, info.LastWriteTimeUtc), Giorno);
        }).ToList());
        var perNome = db.GetAllPhotos().ToDictionary(p => p.PathRelativo);
        var seme = cartella.Length;
        foreach (var (file, asse) in foto)
            db.SalvaVoltiPersone(perNome[file].Id,
                [new VoltoDaSalvare(0.3, 0.3, 0.6, 0.6, 0.9f, Impronte.InByte(Impronta(asse, seme++)), [1, 2], null, StatoPersona.Nessuno)]);
    }

    private void Prepara()
    {
        Catalogo(_mare, ("A1.jpg", 0), ("A2.jpg", 0), ("A3.jpg", 0), ("A4.jpg", 1));
        Catalogo(_montagna, ("B1.jpg", 0), ("B2.jpg", 1), ("B3.jpg", 1));
        File.WriteAllText(Path.Combine(_dati, "recent-folders.json"), JsonSerializer.Serialize(new[] { _mare, _montagna }));
    }

    private static string Rif(string file) => Volti.Leggi().Single(v => Path.GetFileName(v.Foto) == file).Rif;

    private static JsonNode Chiama(string strumento, string argomenti) =>
        JsonNode.Parse(Strumenti.Chiama(strumento, JsonNode.Parse(argomenti)!.AsObject()))!;

    [Fact]
    public void Gruppi_tra_cartelle_nome_proposto_altrove_conferma_e_annulla()
    {
        Prepara();

        var elenco = Chiama("volti_da_nominare", "{}");
        Assert.Equal(2, elenco["gruppi_totali"]!.GetValue<int>());
        var primo = elenco["gruppi"]![0]!;
        Assert.Equal(4, primo["foto"]!.GetValue<int>()); // A1-A3 e B1, in due cartelle
        Assert.Equal(2, primo["cartelle"]!.AsArray().Count);
        Assert.Equal("2024-08-15", primo["dal"]!.GetValue<string>());

        // Nome a due volti: gli altri due somiglianti (anche nell'altra cartella) vengono proposti.
        var nome = Chiama("assegna_nome", JsonSerializer.Serialize(new { nome = "Laura", volti = new[] { Rif("A1.jpg"), Rif("A2.jpg") }, soprannome = "Lalla", chi_e = "amico" }));
        Assert.Equal(2, nome["volti_nominati"]!.GetValue<int>());
        Assert.Equal(2, nome["proposte_nuove"]!.GetValue<int>());
        Assert.Equal(2, Ricerca.Cerca(new FiltriFoto(Persone: ["Lalla"])).Count);
        Assert.Equal(4, Ricerca.Cerca(new FiltriFoto(Persone: ["Laura"], AncheProposte: true)).Count);
        Assert.Equal("amico", Ricerca.ElencoPersone().Single().Cerchia);

        var proposte = Chiama("volti_da_nominare", "{}")["proposte_da_confermare"]![0]!;
        Assert.Equal("Laura", proposte["persona"]!.GetValue<string>());
        Assert.Equal(2, proposte["volti"]!.GetValue<int>());

        Assert.Throws<ArgumentException>(() => Chiama("conferma_volti", """{"persona":"Laura"}"""));
        var conferma = Chiama("conferma_volti", """{"persona":"lalla","tutte":true}""");
        Assert.Equal(2, conferma["volti_confermati"]!.GetValue<int>());
        Assert.Equal(4, Ricerca.Cerca(new FiltriFoto(Persone: ["Laura"])).Count);

        var giorno = Chiama("persone_nel_giorno", """{"giorno":"2024-08-15"}""")[0]!;
        Assert.Equal(7, giorno["foto_con_volti"]!.GetValue<int>());
        Assert.Equal("Laura", giorno["persone"]![0]!["nome"]!.GetValue<string>());
        Assert.Equal(4, giorno["persone"]![0]!["foto"]!.GetValue<int>());
        Assert.Equal(3, giorno["volti_senza_nome"]!.GetValue<int>());
        Assert.Empty(Chiama("persone_nel_giorno", """{"dal":"2024-08-16","al":"2024-08-20"}""").AsArray());

        // Annulla la conferma (le proposte tornano), poi il nome (la persona creata sparisce).
        Chiama("annulla_volti", "{\"elenco\":true}");
        Assert.Contains("Annullata l'operazione 2", Strumenti.Chiama("annulla_volti", new JsonObject()));
        Assert.Equal(2, Ricerca.Cerca(new FiltriFoto(Persone: ["Laura"])).Count);
        Assert.Contains("persona creata è stata tolta", Strumenti.Chiama("annulla_volti", new JsonObject()));
        Assert.Empty(Ricerca.ElencoPersone());
        Assert.Equal(2, Chiama("volti_da_nominare", "{}")["gruppi_totali"]!.GetValue<int>());
        Assert.Throws<InvalidOperationException>(() => Strumenti.Chiama("annulla_volti", new JsonObject()));

        var registro = Strumenti.Chiama("ultime_attivita", JsonNode.Parse("""{"tipo":"mcp.volti"}""")!.AsObject());
        Assert.Contains("nome «Laura»", registro);
        Assert.Contains("annulla", registro);
    }

    [Fact]
    public void Nome_di_una_persona_nota_unisce_e_ignora_toglie_un_gruppo()
    {
        Prepara();
        var gruppi = Volti.GruppiSenzaNome();
        var (grande, piccolo) = (gruppi[0], gruppi[1]);

        // Prima un volto solo, poi il gruppo intero allo stesso nome (scritto diverso): una persona sola.
        Chiama("assegna_nome", JsonSerializer.Serialize(new { nome = "Marco", volti = new[] { piccolo.Volti[0].Rif } }));
        var esito = Chiama("assegna_nome", JsonSerializer.Serialize(new { nome = "marco", gruppo = piccolo.Id }));
        Assert.Equal("Marco", esito["persona"]!.GetValue<string>());
        Assert.Single(Ricerca.ElencoPersone());

        // "Non è lui": escluso dal gruppo, resta senza nome; il resto si ignora.
        var escluso = grande.Volti[0].Rif;
        var ignorati = Chiama("ignora_volti", JsonSerializer.Serialize(new { gruppo = grande.Id, escludi = new[] { escluso } }));
        Assert.Equal(grande.Volti.Count - 1, ignorati["volti_ignorati"]!.GetValue<int>());
        Assert.Empty(Volti.GruppiSenzaNome());
        Assert.Equal([escluso], Volti.GruppiSenzaNome(minimoVolti: 1).SelectMany(g => g.Volti).Select(v => v.Rif));

        Assert.Throws<ArgumentException>(() => Chiama("assegna_nome", """{"nome":"X","volti":["zzzzzzzz-1"]}"""));
        Assert.Throws<ArgumentException>(() => Chiama("assegna_nome", """{"nome":"X","gruppo":"gnon-esiste"}"""));
        Assert.Throws<ArgumentException>(() => Chiama("assegna_nome", JsonSerializer.Serialize(new { nome = "X", volti = new[] { escluso }, chi_e = "zio" })));
    }

    [Fact]
    public async Task Mostra_volto_ritaglia_dalla_foto_o_usa_la_miniatura()
    {
        // Una foto vera (JPEG 400×300) con il volto al centro.
        var bgra = new byte[400 * 300 * 4];
        Array.Fill(bgra, (byte)128);
        File.WriteAllBytes(Path.Combine(_mare, "vera.jpg"), await JpegBgra.CodificaAsync(bgra, 400, 300));
        var miniatura = await JpegBgra.CodificaAsync(new byte[112 * 112 * 4], 112, 112);
        using (var db = LibrarySidecarDatabase.OpenOrCreate(_mare))
        {
            db.SyncScan(FolderScanner.Scan(_mare));
            db.SalvaVoltiPersone(db.GetAllPhotos().Single().Id,
                [new VoltoDaSalvare(0.4, 0.4, 0.5, 0.55, 0.9f, Impronte.InByte(Impronta(0, 1)), miniatura, null, StatoPersona.Nessuno)]);
        }
        File.WriteAllText(Path.Combine(_dati, "recent-folders.json"), JsonSerializer.Serialize(new[] { _mare }));
        var rif = Rif("vera.jpg");

        var dallaFoto = Strumenti.ChiamaContenuto("mostra_volto", JsonNode.Parse($$"""{"volti":["{{rif}}"]}""")!.AsObject());
        Assert.Equal(2, dallaFoto.Count);
        Assert.Contains("dalla foto", dallaFoto[0]!["text"]!.GetValue<string>());
        Assert.Equal("image", dallaFoto[1]!["type"]!.GetValue<string>());
        var ritaglio = Convert.FromBase64String(dallaFoto[1]!["data"]!.GetValue<string>());
        var (_, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(await Salva(ritaglio));
        Assert.Equal(w, h); // quadrato, con il contesto attorno al volto
        Assert.InRange(w, 50, ImmaginiVolti.Lato);

        var salvata = Strumenti.ChiamaContenuto("mostra_volto", JsonNode.Parse($$"""{"volti":["{{rif}}"],"dalla_foto":false}""")!.AsObject());
        Assert.Equal(Convert.ToBase64String(miniatura), salvata[1]!["data"]!.GetValue<string>());

        File.Delete(Path.Combine(_mare, "vera.jpg")); // disco scollegato: resta la miniatura salvata
        var senzaFoto = Strumenti.ChiamaContenuto("mostra_volto", JsonNode.Parse($$"""{"volti":["{{rif}}"]}""")!.AsObject());
        Assert.Contains("miniatura salvata", senzaFoto[0]!["text"]!.GetValue<string>());
    }

    private async Task<string> Salva(byte[] jpeg)
    {
        var percorso = Path.Combine(_radice, Guid.NewGuid() + ".jpg");
        await File.WriteAllBytesAsync(percorso, jpeg);
        return percorso;
    }

    [Fact]
    public void Riquadro_con_contesto_resta_quadrato_e_dentro_la_foto()
    {
        Assert.Equal((120, 70, 160), JpegBgra.RiquadroConContesto(400, 300, 0.45, 0.45, 0.55, 0.55, margine: 1.5));
        var (x, y, lato) = JpegBgra.RiquadroConContesto(400, 300, 0.9, 0.0, 1.0, 0.2);
        Assert.True(x >= 0 && y >= 0 && x + lato <= 400 && y + lato <= 300);
        Assert.Equal((50, 0, 300), JpegBgra.RiquadroConContesto(400, 300, 0, 0, 1, 1)); // centrato sul volto
    }

    [Fact]
    public async Task Analisi_a_lotti_cataloga_salta_le_foto_fatte_e_propone_le_persone_note()
    {
        var cartella = Path.Combine(_radice, "Foto", "Nuova");
        Directory.CreateDirectory(Path.Combine(cartella, "Giorno 2"));
        foreach (var file in new[] { "F1.jpg", "F2.jpg", Path.Combine("Giorno 2", "F3.jpg") })
            File.WriteAllBytes(Path.Combine(cartella, file), [1, 2, 3]);
        using (var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone))
        {
            var id = archivio.TrovaOCrea("Giulia", null);
            archivio.ImpostaCampioni(id, [Impronte.InByte(Impronta(5, 99))]);
        }

        var analizzate = new List<string>();
        AnalisiCartella.AnalizzatoreDiProva = percorso =>
        {
            analizzate.Add(Path.GetFileName(percorso));
            var asse = Path.GetFileName(percorso) == "F3.jpg" ? 5 : 7;
            return Task.FromResult<IReadOnlyList<VoltoAnalizzato>>(
                [new VoltoAnalizzato(0.1, 0.1, 0.3, 0.3, 0.9f, Impronta(asse, analizzate.Count), new byte[112 * 112 * 4])]);
        };

        await AnalisiCartella.EseguiAsync(cartella, massimoFoto: 2, CancellationToken.None);
        Assert.Equal(["F1.jpg", "F2.jpg"], analizzate);
        Assert.True(Ricerca.HaSidecar(cartella));
        Assert.Contains(cartella, Ricerca.CartelleRecenti());

        // Secondo giro dallo strumento: solo la foto che mancava, che somiglia a Giulia.
        Chiama("analizza_cartella", JsonSerializer.Serialize(new { cartella }));
        await AnalisiCartella.AttendiAsync();
        Assert.Equal(["F1.jpg", "F2.jpg", "F3.jpg"], analizzate);
        var stato = Chiama("stato_analisi", "{}");
        Assert.True(stato["finita"]!.GetValue<bool>());
        Assert.Equal(1, stato["totale"]!.GetValue<int>());
        Assert.Equal(1, stato["volti_trovati"]!.GetValue<int>());

        Assert.Equal(["F3.jpg"], Ricerca.Cerca(new FiltriFoto(Persone: ["Giulia"], AncheProposte: true)).Select(f => Path.GetFileName(f.Percorso)));
        Assert.Single(Volti.GruppiSenzaNome(cartella)); // F1 e F2 si somigliano
        Assert.Contains("mcp.analisi.fine", Strumenti.Chiama("ultime_attivita", new JsonObject()));

        // Una sottocartella di una cartella già catalogata va nel catalogo della madre.
        Assert.Equal(cartella, AnalisiCartella.Catalogo(Path.Combine(cartella, "Giorno 2")));
        await AnalisiCartella.EseguiAsync(Path.Combine(cartella, "Giorno 2"), null, CancellationToken.None);
        Assert.Equal(3, analizzate.Count);
        Assert.False(Ricerca.HaSidecar(Path.Combine(cartella, "Giorno 2")));
    }
}
