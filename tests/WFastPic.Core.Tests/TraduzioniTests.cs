using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WFastPic.Core.Library;
using WFastPic.Core.Localizzazione;
using Xunit;

// La lingua è uno stato globale (Testi): niente test in parallelo, altrimenti uno in inglese
// fa fallire quelli che si aspettano l'italiano.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WFastPic.Core.Tests;

public class TraduzioniTests
{
    /// <summary>Esegue l'azione in inglese e torna sempre all'italiano (lo stato è globale).</summary>
    private static void InInglese(Action azione)
    {
        Testi.Imposta(Lingua.Inglese);
        try { azione(); }
        finally { Testi.Imposta(Lingua.Italiano); }
    }

    // ---------------------------------------------------------------- comportamento

    [Fact]
    public void Italiano_restituisce_il_testo_originale()
    {
        Testi.Imposta(Lingua.Italiano);
        Assert.Equal("Annulla", Testi.T("Annulla"));
        Assert.Equal("3 di 10", Testi.T("{0} di {1}", 3, 10));
    }

    [Fact]
    public void Inglese_traduce_e_senza_voce_resta_italiano()
    {
        InInglese(() =>
        {
            Assert.Equal("Cancel", Testi.T("Annulla"));
            Assert.Equal("3 of 10", Testi.T("{0} di {1}", 3, 10));
            Assert.Equal("Testo mai tradotto", Testi.T("Testo mai tradotto"));
        });
    }

    [Theory]
    [InlineData("auto", "it-IT", Lingua.Italiano)]
    [InlineData("auto", "en-US", Lingua.Inglese)]
    [InlineData("auto", "de-DE", Lingua.Inglese)]
    [InlineData(null, "it-IT", Lingua.Italiano)]
    [InlineData("it", "en-US", Lingua.Italiano)]
    [InlineData("en", "it-IT", Lingua.Inglese)]
    [InlineData("EN", "it-IT", Lingua.Inglese)]
    public void Risolvi_lingua(string? preferenza, string cultura, Lingua atteso) =>
        Assert.Equal(atteso, Testi.Risolvi(preferenza, CultureInfo.GetCultureInfo(cultura)));

    [Fact]
    public void Foto_ha_il_plurale_giusto()
    {
        Testi.Imposta(Lingua.Italiano);
        Assert.Equal("1 foto", Testi.Foto(1));
        Assert.Equal("1.300 foto", Testi.Foto(1300));
        InInglese(() =>
        {
            Assert.Equal("1 photo", Testi.Foto(1));
            Assert.Equal("1,300 photos", Testi.Foto(1300));
        });
    }

    [Fact]
    public void Segnaposti_html_si_traducono()
    {
        const string html = "L.control.layers({ '@@Mappa@@': a, '@@Satellite@@': b })";
        Testi.Imposta(Lingua.Italiano);
        Assert.Equal("L.control.layers({ 'Mappa': a, 'Satellite': b })", Testi.Segnaposti(html));
        InInglese(() => Assert.Equal("L.control.layers({ 'Map': a, 'Satellite': b })", Testi.Segnaposti(html)));
    }

    [Fact]
    public void Testi_della_libreria_seguono_la_lingua()
    {
        var progresso = StimaAvanzamento.Descrivi(340, 1300, TimeSpan.FromSeconds(10));
        Assert.Equal("340 di 1300 · circa 29 s rimanenti", progresso);
        InInglese(() => Assert.Equal("340 of 1300 · about 29 s remaining", StimaAvanzamento.Descrivi(340, 1300, TimeSpan.FromSeconds(10))));

        var cartella = new CartellaArchivio(@"E:\Foto\Puglia", 1300, new DateTime(2026, 7, 12), new DateTime(2026, 7, 20), true, 37);
        Assert.Equal("1.300 foto · 37 selezionate", ArchivioCartelle.Dettaglio(cartella));
        Assert.Equal("12–20 lug 2026", ArchivioCartelle.PeriodoCartella(cartella));
        InInglese(() =>
        {
            Assert.Equal("1,300 photos · 37 selected", ArchivioCartelle.Dettaglio(cartella));
            Assert.Equal("12–20 Jul 2026", ArchivioCartelle.PeriodoCartella(cartella));
        });

        var giorno = new DateOnly(2026, 7, 15);
        Assert.Equal("Mercoledì 15 luglio 2026 · 37 foto", GiorniScatto.Titolo(giorno, 37));
        InInglese(() =>
        {
            Assert.Equal("Wednesday 15 July 2026 · 37 photos", GiorniScatto.Titolo(giorno, 37));
            Assert.Equal("No date · 1 photo", GiorniScatto.Titolo(null, 1));
        });
    }

    // ---------------------------------------------------------------- completezza

    private static readonly Regex Segnaposto = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    private static string[] Segnaposti(string testo) =>
        Segnaposto.Matches(testo).Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Traduzioni_hanno_gli_stessi_segnaposti_dell_italiano()
    {
        var errori = new List<string>();
        foreach (var (it, en) in Testi.TraduzioniInglesi)
        {
            if (!Segnaposti(it).SequenceEqual(Segnaposti(en)))
                errori.Add($"{it}  →  {en}");
        }
        Assert.True(errori.Count == 0, "Segnaposti diversi tra italiano e inglese:\n" + string.Join("\n", errori));
    }

    [Fact]
    public void Traduzioni_non_sono_vuote()
    {
        var vuote = Testi.TraduzioniInglesi.Where(v => string.IsNullOrWhiteSpace(v.Value)).Select(v => v.Key).ToList();
        Assert.True(vuote.Count == 0, "Traduzioni vuote:\n" + string.Join("\n", vuote));
    }

    private static string RadiceRepository()
    {
        var cartella = new DirectoryInfo(AppContext.BaseDirectory);
        while (cartella is not null && !File.Exists(Path.Combine(cartella.FullName, "WFastPic.slnx"))) cartella = cartella.Parent;
        return cartella?.FullName ?? throw new InvalidOperationException("WFastPic.slnx non trovato risalendo da " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> FileSorgente(string estensione)
    {
        var src = Path.Combine(RadiceRepository(), "src");
        return Directory.EnumerateFiles(src, "*" + estensione, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains("WFastPic.Core.Localizzazione"));
    }

    /// <summary>Legge una stringa C# da s[i] == '"' (con gli escape comuni); ritorna il valore e l'indice dopo la chiusura.</summary>
    private static (string Valore, int Fine) LeggiStringa(string s, int i)
    {
        var sb = new StringBuilder();
        i++;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\')
            {
                var n = s[i + 1];
                switch (n)
                {
                    case 'n': sb.Append('\n'); i += 2; continue;
                    case 't': sb.Append('\t'); i += 2; continue;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 2, 4), 16)); i += 6; continue;
                    default: sb.Append(n); i += 2; continue;
                }
            }
            if (c == '"') return (sb.ToString(), i + 1);
            sb.Append(c);
            i++;
        }
        throw new FormatException("stringa non chiusa");
    }

    /// <summary>Tutti i testi passati a <c>T("…")</c> (anche concatenati con +) nel codice dell'app e delle librerie.</summary>
    private static IEnumerable<(string File, string Chiave)> ChiaviNelCodice()
    {
        var inizio = new Regex(@"(?<![A-Za-z0-9_.])(?:Testi\.)?T\(\s*""", RegexOptions.Compiled);
        var piu = new Regex(@"\G\s*\+\s*""", RegexOptions.Compiled);
        foreach (var file in FileSorgente(".cs"))
        {
            var testo = File.ReadAllText(file);
            foreach (Match m in inizio.Matches(testo))
            {
                var (valore, fine) = LeggiStringa(testo, m.Index + m.Length - 1);
                var sb = new StringBuilder(valore);
                while (piu.Match(testo, fine) is { Success: true } p)
                {
                    var (altro, dopo) = LeggiStringa(testo, p.Index + p.Length - 1);
                    sb.Append(altro);
                    fine = dopo;
                }
                yield return (Path.GetFileName(file), sb.ToString());
            }
        }
    }

    [Fact]
    public void Ogni_testo_T_del_codice_ha_la_traduzione_inglese()
    {
        var mancanti = ChiaviNelCodice()
            .Where(c => !Testi.TraduzioniInglesi.ContainsKey(c.Chiave))
            .Select(c => $"{c.File}: {c.Chiave}")
            .Distinct()
            .ToList();
        Assert.True(mancanti.Count == 0, "Testi senza traduzione inglese:\n" + string.Join("\n", mancanti));
        Assert.NotEmpty(ChiaviNelCodice()); // il lettore deve trovare davvero i testi
    }

    [Fact]
    public void Nessun_T_usa_stringhe_interpolate()
    {
        // T($"…") non si può tradurre: la chiave sarebbe diversa a ogni valore. Va T("… {0}", valore).
        var errori = new List<string>();
        var interpolata = new Regex(@"(?<![A-Za-z0-9_.])(?:Testi\.)?T\(\s*\$""", RegexOptions.Compiled);
        foreach (var file in FileSorgente(".cs"))
        {
            if (interpolata.IsMatch(File.ReadAllText(file))) errori.Add(Path.GetFileName(file));
        }
        Assert.True(errori.Count == 0, "T con stringa interpolata in: " + string.Join(", ", errori));
    }

    /// <summary>Testi dello XAML uguali in italiano e inglese (tasti, simboli, nomi propri): non servono voci.</summary>
    private static readonly HashSet<string> NeutriXaml = new(StringComparer.Ordinal)
    {
        "Ctrl+A", "Shift+Space", "Space", "Tab", "WFastPic", "L", "U", "X", "Zoom 100%",
    };

    [Fact]
    public void Ogni_testo_dello_XAML_ha_la_traduzione_inglese()
    {
        var proprieta = new HashSet<string>
        {
            "Text", "Content", "Header", "PlaceholderText", "ToolTipService.ToolTip", "AutomationProperties.Name", "Title",
            "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "Label", "OnContent", "OffContent", "Description", "Message",
        };
        var mancanti = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in FileSorgente(".xaml"))
        {
            var doc = XDocument.Load(file);
            foreach (var attributo in doc.Descendants().SelectMany(e => e.Attributes()))
            {
                var nome = attributo.Name.LocalName;
                var qualificato = attributo.Name.Namespace == XNamespace.None ? nome : nome; // ToolTipService.ToolTip ha il punto nel nome locale
                if (!proprieta.Contains(qualificato)) continue;

                var valore = attributo.Value;
                if (valore.StartsWith('{')) continue; // binding o risorsa
                if (!valore.Any(char.IsLetter)) continue; // solo simboli o numeri ("✕", "0 – 5", "0.0°")
                if (NeutriXaml.Contains(valore)) continue;
                if (!Testi.TraduzioniInglesi.ContainsKey(valore)) mancanti.Add($"{Path.GetFileName(file)}: {valore}");
            }
        }
        Assert.True(mancanti.Count == 0, "Testi XAML senza traduzione inglese:\n" + string.Join("\n", mancanti));
    }
}
