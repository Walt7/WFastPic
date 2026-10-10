using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;
using Xunit;

namespace WFastPic.Core.Tests;

public class PersoneTests
{
    // Impronta finta: direzione base + piccolo rumore, normalizzata (come quelle vere).
    private static float[] Impronta(int persona, int variante)
    {
        var rnd = new Random(persona * 1000 + variante);
        var v = new float[Impronte.Dimensione];
        var base_ = new Random(persona);
        for (var i = 0; i < v.Length; i++) v[i] = (float)(base_.NextDouble() - 0.5) + (float)(rnd.NextDouble() - 0.5) * 0.35f;
        return Impronte.Normalizza(v);
    }

    [Fact]
    public void Raggruppa_mette_insieme_i_volti_della_stessa_persona()
    {
        var impronte = new[] { Impronta(1, 0), Impronta(2, 0), Impronta(1, 1), Impronta(3, 0), Impronta(2, 1), Impronta(1, 2) };

        var gruppi = RiconoscimentoPersone.Raggruppa(impronte);

        Assert.Equal(gruppi[0], gruppi[2]);
        Assert.Equal(gruppi[0], gruppi[5]);
        Assert.Equal(gruppi[1], gruppi[4]);
        Assert.NotEqual(gruppi[0], gruppi[1]);
        Assert.NotEqual(gruppi[3], gruppi[0]);
        Assert.Equal(0, gruppi[0]); // il gruppo più numeroso per primo
    }

    [Fact]
    public void Raggruppa_tanti_volti_resta_veloce_e_corretto()
    {
        // 6 persone × 100 volti: oltre la soglia del legame medio si usano i centroidi.
        var impronte = Enumerable.Range(0, 600).Select(i => Impronta(i % 6, i)).ToList();

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var gruppi = RiconoscimentoPersone.Raggruppa(impronte);
        cronometro.Stop();

        Assert.True(cronometro.ElapsedMilliseconds < 2000, $"{cronometro.ElapsedMilliseconds} ms");
        for (var p = 0; p < 6; p++)
        {
            var etichette = Enumerable.Range(0, 600).Where(i => i % 6 == p).Select(i => gruppi[i]).Distinct().ToList();
            Assert.Single(etichette);
        }
        Assert.Equal(6, gruppi.Distinct().Count());
    }

    [Fact]
    public void Riconosci_propone_la_persona_giusta_o_nessuna()
    {
        var persone = new[]
        {
            new PersonaNota(10, "Marco", [Impronta(1, 0), Impronta(1, 1)]),
            new PersonaNota(20, "Anna", [Impronta(2, 0)]),
        };

        Assert.Equal("Marco", RiconoscimentoPersone.Riconosci(Impronta(1, 5), persone)?.Persona.Nome);
        Assert.Equal("Anna", RiconoscimentoPersone.Riconosci(Impronta(2, 7), persone)?.Persona.Nome);
        Assert.Null(RiconoscimentoPersone.Riconosci(Impronta(9, 0), persone));
    }

    [Fact]
    public void Campioni_senza_doppioni_e_con_un_massimo()
    {
        var esistenti = new List<float[]> { Impronta(1, 0) };
        var aggiornati = RiconoscimentoPersone.AggiornaCampioni(esistenti, [Impronta(1, 0), Impronta(1, 1)]);
        Assert.Equal(2, aggiornati.Count);

        var tanti = RiconoscimentoPersone.AggiornaCampioni([], Enumerable.Range(0, 100).Select(i => Impronta(i, 0)));
        Assert.Equal(RiconoscimentoPersone.CampioniMassimi, tanti.Count);
    }

    [Fact]
    public void Similitudine_ritrova_rotazione_scala_e_traslazione()
    {
        // Punti del modello ruotati di 20°, scalati ×2 e spostati: la stima deve riportarli al modello.
        var rad = 20 * MathF.PI / 180;
        var sorgente = AllineamentoVolto.Modello
            .Select(p => (X: 2 * (p.X * MathF.Cos(rad) - p.Y * MathF.Sin(rad)) + 300, Y: 2 * (p.X * MathF.Sin(rad) + p.Y * MathF.Cos(rad)) + 150))
            .ToArray();

        var (a, b, tx, ty) = AllineamentoVolto.StimaSimilitudine(sorgente, AllineamentoVolto.Modello);

        for (var i = 0; i < sorgente.Length; i++)
        {
            Assert.Equal(AllineamentoVolto.Modello[i].X, a * sorgente[i].X - b * sorgente[i].Y + tx, 2);
            Assert.Equal(AllineamentoVolto.Modello[i].Y, b * sorgente[i].X + a * sorgente[i].Y + ty, 2);
        }
    }

    [Fact]
    public void Impronte_sopravvivono_al_salvataggio_in_byte()
    {
        var impronta = Impronta(4, 2);
        Assert.Equal(impronta, Impronte.DaByte(Impronte.InByte(impronta)));
    }

    [Fact]
    public void Archivio_persone_trova_o_crea_senza_distinguere_maiuscole()
    {
        var percorso = Path.Combine(Path.GetTempPath(), "wfastpic-persone-" + Guid.NewGuid() + ".db");
        try
        {
            using (var archivio = ArchivioPersone.Apri(percorso))
            {
                var marco = archivio.TrovaOCrea("Marco", [1, 2, 3]);
                Assert.Equal(marco, archivio.TrovaOCrea(" marco ", null));
                archivio.ImpostaCampioni(marco, [Impronte.InByte(Impronta(1, 0)), Impronte.InByte(Impronta(1, 1))]);
            }

            using var riaperto = ArchivioPersone.Apri(percorso);
            var persona = Assert.Single(riaperto.Elenco());
            Assert.Equal("Marco", persona.Nome);
            Assert.Equal(2, persona.Campioni.Count);
            Assert.Equal([1, 2, 3], persona.Avatar);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(percorso) + "*")) File.Delete(f);
        }
    }

    [Fact]
    public void Sidecar_salva_volti_con_impronta_e_nomi()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-voltipersone-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        File.WriteAllBytes(Path.Combine(cartella, "a.jpg"), [1, 2, 3]);
        try
        {
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));
            var foto = db.GetAllPhotos().Single();
            Assert.False(foto.PersoneAnalizzate);

            db.SalvaVoltiPersone(foto.Id,
            [
                new VoltoDaSalvare(0.1, 0.1, 0.2, 0.2, 0.9f, Impronte.InByte(Impronta(1, 0)), [9], null, StatoPersona.Nessuno),
                new VoltoDaSalvare(0.5, 0.1, 0.6, 0.2, 0.9f, Impronte.InByte(Impronta(2, 0)), [9], 7, StatoPersona.Proposto),
            ]);

            var riletta = db.GetAllPhotos().Single();
            Assert.True(riletta.PersoneAnalizzate);
            Assert.Equal(2, riletta.NumeroVolti);

            var volti = db.GetVoltiPersone();
            Assert.Equal(2, volti.Count);
            db.AssegnaPersona([volti[0].Id], 7, StatoPersona.Confermato);
            Assert.All(db.GetVoltiPersone(), v => Assert.Equal(7, v.PersonaId));
            Assert.Equal(2, db.GetVolti(foto.Id)!.Count); // i ritagli suggeriti continuano a vederli
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }
}
