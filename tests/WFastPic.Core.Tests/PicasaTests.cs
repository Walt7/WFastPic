using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;
using Xunit;

namespace WFastPic.Core.Tests;

/// <summary>Volti di Picasa: lettura dei Picasa.ini, rect64, abbinamento ai volti di WFastPic, nomi da unire.</summary>
public class PicasaTests : IDisposable
{
    private readonly string _radice = Path.Combine(Path.GetTempPath(), "wfastpic-picasa-" + Guid.NewGuid());

    public PicasaTests() => Directory.CreateDirectory(_radice);

    public void Dispose()
    {
        try { Directory.Delete(_radice, recursive: true); } catch (IOException) { }
    }

    public static string Rect64(double x0, double y0, double x1, double y1)
    {
        ulong Parte(double v) => (ulong)Math.Round(v * 65535);
        return (Parte(x0) << 48 | Parte(y0) << 32 | Parte(x1) << 16 | Parte(y1)).ToString("x16");
    }

    [Fact]
    public void Rect64_quattro_valori_a_16_bit_su_65535()
    {
        var r = VoltiPicasa.Rect64("62be555570786b4b");
        Assert.Equal(0x62be / 65535.0, r.X0, 6);
        Assert.Equal(0x5555 / 65535.0, r.Y0, 6);
        Assert.Equal(0x7078 / 65535.0, r.X1, 6);
        Assert.Equal(0x6b4b / 65535.0, r.Y1, 6);
        var corto = VoltiPicasa.Rect64("7fff"); // cifre iniziali omesse = zeri
        Assert.Equal((0.0, 0.0, 0.0), (corto.X0, corto.Y0, corto.X1));
        Assert.Equal(0x7fff / 65535.0, corto.Y1, 6);
    }

    [Fact]
    public void Leggi_volti_e_contatti_saltando_righe_rovinate()
    {
        var ini = Path.Combine(_radice, ".picasa.ini");
        File.WriteAllText(ini, $"""
            [IMG_1.JPG]
            filters=enhance=1;
            faces=rect64({Rect64(0.1, 0.1, 0.3, 0.4)}),AA11;rect64({Rect64(0.5, 0.5, 0.6, 0.7)}),ffffffffffffffff
            [IMG_2.JPG]
            faces=rect64(zzzz),bb22;rect64(0000000000000000),bb22
            [Contacts2]
            aa11=Laura Villa;laura@example.com;
            bb22=;solo@email;
            """);

        var (volti, contatti) = VoltiPicasa.Leggi(ini);
        Assert.Equal(2, volti.Count);
        Assert.Equal(("IMG_1.JPG", "aa11"), (volti[0].File, volti[0].Contatto));
        Assert.Equal(0.3, volti[0].X1, 3);
        Assert.Equal("Laura Villa", contatti["AA11"]);
        Assert.False(contatti.ContainsKey("bb22")); // nome vuoto
        Assert.Equal([ini], VoltiPicasa.TrovaIni(_radice));
    }

    [Fact]
    public void Abbina_volti_vicini_uno_a_uno_e_niente_sotto_soglia()
    {
        Riquadro[] picasa = [new(0.10, 0.10, 0.20, 0.25), new(0.22, 0.10, 0.32, 0.25), new(0.8, 0.8, 0.9, 0.9)];
        Riquadro[] nostri = [new(0.23, 0.09, 0.33, 0.26), new(0.09, 0.11, 0.21, 0.26), new(0.5, 0.5, 0.55, 0.55)];
        Assert.Equal([(0, 1), (1, 0)], VoltiPicasa.Abbina(picasa, nostri).OrderBy(c => c.Picasa));
    }

    [Fact]
    public void Nomi_forse_uguali_ma_non_uniti_da_soli()
    {
        var coppie = VoltiPicasa.ForseUguali(["walter", "Walter Rodeghiero", "Rodeghiero Pierluigi", "Pierluigi Rodeghiero", "Anna Bellini", "Erika Pastori"]);
        Assert.Contains(("walter", "Walter Rodeghiero"), coppie);
        Assert.Contains(("Rodeghiero Pierluigi", "Pierluigi Rodeghiero"), coppie);
        Assert.Equal(2, coppie.Count);
        Assert.True(VoltiPicasa.EEmail("fortebraccio@gmail.com"));
        Assert.False(VoltiPicasa.EEmail("Anna Bellini"));
        Assert.Equal(VoltiPicasa.Chiave(" Walter  rodeghiero"), VoltiPicasa.Chiave("walter Rodeghiero"));
    }

    [Fact]
    public void Pianifica_con_contatti_di_altre_cartelle_e_grafie_unite()
    {
        var a = Directory.CreateDirectory(Path.Combine(_radice, "A")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_radice, "B")).FullName;
        // In A il contatto c1 non ha nome: lo dà l'ini di B (gli id di Picasa valgono ovunque).
        File.WriteAllText(Path.Combine(a, "Picasa.ini"), $"""
            [f1.jpg]
            faces=rect64({Rect64(0.1, 0.1, 0.3, 0.3)}),c1;rect64({Rect64(0.6, 0.6, 0.8, 0.8)}),c2
            [f2.jpg]
            faces=rect64({Rect64(0.1, 0.1, 0.3, 0.3)}),c1
            """);
        File.WriteAllText(Path.Combine(b, ".picasa.ini"), """
            [Contacts2]
            c1=Walter Rodeghiero;;
            c2=walter rodeghiero;;
            """);
        var volti = new List<(int, string, Riquadro)>
        {
            (1, Path.Combine(a, "f1.jpg"), new Riquadro(0.11, 0.1, 0.31, 0.31)),
            (2, Path.Combine(a, "f1.jpg"), new Riquadro(0.6, 0.62, 0.79, 0.8)),
        };

        var piano = VoltiPicasa.Pianifica(_radice, volti);
        Assert.Equal(3, piano.VoltiPicasa);
        Assert.Equal(1, piano.FileIni); // l'ini di B ha solo contatti
        Assert.Equal([(1, "Walter Rodeghiero"), (2, "Walter Rodeghiero")], piano.Abbinati.OrderBy(x => x.Volto));
        Assert.Equal(1, piano.InFotoNonAnalizzate); // f2 non ha volti di WFastPic
        Assert.Equal(1, piano.CartelleNonAnalizzate[a]);
    }

    [Fact]
    public void Proposte_con_tante_persone_trovano_ancora_quella_giusta()
    {
        float[] Vettore(int asse, int seme)
        {
            var caso = new Random(seme);
            var v = new float[Impronte.Dimensione];
            for (var i = 0; i < v.Length; i++) v[i] = (float)(caso.NextDouble() - 0.5) * 0.05f;
            v[asse] = 1;
            return Impronte.Normalizza(v);
        }

        var cartella = Directory.CreateDirectory(Path.Combine(_radice, "Tante")).FullName;
        File.WriteAllBytes(Path.Combine(cartella, "x.jpg"), [1]);
        using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
        db.SyncScan(FolderScanner.Scan(cartella));
        db.SalvaVoltiPersone(db.GetAllPhotos().Single().Id,
            [new VoltoDaSalvare(0, 0, 1, 1, 1, Impronte.InByte(Vettore(77, 1)), [1], null, StatoPersona.Nessuno)]);

        var persone = Enumerable.Range(0, 60).Select(i => new PersonaNota(i, $"P{i}", [Vettore(i, i + 100), Vettore(i, i + 200)])).ToList();
        Assert.Equal(0, GestionePersone.Riproponi(db, persone)); // nessuno somiglia all'asse 77
        persone.Add(new PersonaNota(999, "Giusta", [Vettore(77, 5), Vettore(77, 6)]));
        Assert.Equal(1, GestionePersone.Riproponi(db, persone));
        Assert.Equal(999, db.GetStatiVolti().Single().PersonaId);
    }
}
