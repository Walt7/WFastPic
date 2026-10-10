using WFastPic.Core.Library;
using Xunit;

namespace WFastPic.Core.Tests;

public class ControlloVersioneTests
{
    private static string Release(string tag, bool draft = false, bool prerelease = false, string url = "https://github.com/Walt7/WFastPic/releases/tag/v1.1.0") =>
        $$"""{"tag_name":"{{tag}}","html_url":"{{url}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"name":"WFastPic"}""";

    [Fact]
    public void Segnala_una_versione_piu_nuova()
    {
        var nuova = ControlloVersione.Confronta(Release("v1.1.0"), new Version(1, 0, 0, 0));

        Assert.NotNull(nuova);
        Assert.Equal(new Version(1, 1, 0), nuova.Versione);
        Assert.Equal("https://github.com/Walt7/WFastPic/releases/tag/v1.1.0", nuova.Indirizzo);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("1.0")]
    [InlineData("v0.9.2")]
    public void Niente_avviso_se_e_la_stessa_o_una_vecchia(string tag)
    {
        // La versione dell'assembly ha 4 numeri (1.0.0.0): conta come 1.0.0.
        Assert.Null(ControlloVersione.Confronta(Release(tag), new Version(1, 0, 0, 0)));
    }

    [Fact]
    public void Ignora_bozze_e_anteprime()
    {
        Assert.Null(ControlloVersione.Confronta(Release("v2.0.0", draft: true), new Version(1, 0, 0)));
        Assert.Null(ControlloVersione.Confronta(Release("v2.0.0", prerelease: true), new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("{\"message\":\"API rate limit exceeded\"}")]
    [InlineData("<html>proxy</html>")]
    [InlineData("{\"tag_name\":\"ultima\"}")]
    public void Risposta_inattesa_nessun_avviso(string json)
    {
        Assert.Null(ControlloVersione.Confronta(json, new Version(1, 0, 0)));
    }

    [Fact]
    public void Apre_solo_pagine_di_github()
    {
        var nuova = ControlloVersione.Confronta(Release("v1.2.0", url: "https://esempio.invalid/scarica"), new Version(1, 0, 0));

        Assert.Equal("https://github.com/Walt7/WFastPic/releases/latest", nuova!.Indirizzo);
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V2.0", 2, 0, 0)]
    [InlineData("1.3.0-beta.1", 1, 3, 0)]
    public void Legge_i_tag(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), ControlloVersione.Leggi(tag));
    }
}
