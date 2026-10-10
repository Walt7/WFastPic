using WFastPic.Core.Library;
using Xunit;

namespace WFastPic.Core.Tests;

public class ReportErroreTests
{
    [Theory]
    [InlineData(@"Could not find file 'E:\Fotocamera\2026\Vacanze in Puglia\IMG_0001.HEIC'.", "Could not find file '<percorso>.heic'.")]
    [InlineData(@"Impossibile trovare il file ""C:\Users\walter\Pictures\Matrimonio Anna\foto 1.jpg"".", @"Impossibile trovare il file ""<percorso>.jpg"".")]
    [InlineData(@"Access to the path '\\WALTLEOPARD\trasp\.wfastpic\library.db' is denied.", "Access to the path '<percorso>.db' is denied.")]
    [InlineData(@"Cartella non trovata: D:\Foto di famiglia", "Cartella non trovata: <percorso>")]
    public void Toglie_i_percorsi_e_tiene_il_tipo_di_file(string messaggio, string atteso)
    {
        Assert.Equal(atteso, ReportErrore.Anonimizza(messaggio));
    }

    [Fact]
    public void Toglie_i_percorsi_dalle_tracce_in_inglese_e_in_italiano()
    {
        const string traccia =
            "   at WFastPic.App.MainWindow.Carica() in C:\\Users\\walter\\src\\WFastPic\\MainWindow.cs:line 42\r\n" +
            "   in WFastPic.App.MainWindow.Apri() in C:\\Users\\walter\\src\\WFastPic\\Altro File.cs:riga 7\r\n";

        var pulita = ReportErrore.Anonimizza(traccia);

        Assert.DoesNotContain("walter", pulita);
        Assert.Contains("MainWindow.Carica() in <percorso>.cs:line 42", pulita);
        Assert.Contains("MainWindow.Apri() in <percorso>.cs:riga 7", pulita);
    }

    [Fact]
    public void Toglie_nome_utente_e_nome_del_pc_anche_fuori_dai_percorsi()
    {
        var pulita = ReportErrore.Anonimizza("Utente DESKTOP-WALT\\Walter senza permessi su DESKTOP-WALT", "walter", "DESKTOP-WALT");

        Assert.Equal("Utente <pc>\\<utente> senza permessi su <pc>", pulita);
    }

    [Fact]
    public void Non_tocca_parole_che_contengono_il_nome_utente()
    {
        // "ann" dentro "annullato" non è il nome utente "ann".
        Assert.Equal("Export annullato da <utente>", ReportErrore.Anonimizza("Export annullato da Ann", "ann"));
    }

    [Fact]
    public void Lascia_intatto_il_resto_del_report()
    {
        const string report = "WFastPic — report errore\nVersione:  0.9.2.0\nSystem.InvalidOperationException: Sequence contains no elements";

        Assert.Equal(report, ReportErrore.Anonimizza(report, "walter", "PC1"));
    }

    [Fact]
    public void Segnalazione_su_github_compilata_e_mai_troppo_lunga()
    {
        var corta = ReportErrore.IndirizzoSegnalazione("Errore: è successo", "Cosa stavi facendo?", "riga 1\nriga 2");
        Assert.StartsWith(ReportErrore.IndirizzoNuovaSegnalazione + "?title=", corta);
        Assert.Contains(Uri.EscapeDataString("riga 2"), corta);

        var lunghissimo = string.Join("\n", Enumerable.Range(1, 2000).Select(i => $"   at Metodo{i}() àèìòù"));
        var lunga = ReportErrore.IndirizzoSegnalazione("Errore", "Cosa stavi facendo?", lunghissimo);
        Assert.True(lunga.Length <= ReportErrore.LunghezzaMassimaUrl, lunga.Length.ToString());
        Assert.Contains(Uri.EscapeDataString("Metodo1()"), lunga); // l'inizio, dove c'è l'errore, resta
        Assert.Contains(Uri.EscapeDataString("…"), lunga);
    }
}
