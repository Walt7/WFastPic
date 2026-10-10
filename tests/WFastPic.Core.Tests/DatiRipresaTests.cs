using WFastPic.Core.Metadata;
using Xunit;

namespace WFastPic.Core.Tests;

public class DatiRipresaTests
{
    [Fact]
    public void Drone_DJI_altezza_dal_decollo_inclinazione_e_direzione_dall_XMP()
    {
        var xmp = new Dictionary<string, string>
        {
            ["drone-dji:RelativeAltitude"] = "+87.30",
            ["drone-dji:AbsoluteAltitude"] = "+152.10",
            ["drone-dji:GimbalPitchDegree"] = "-90.00",
            ["drone-dji:GimbalYawDegree"] = "-45.50",
        };

        var dati = DatiRipresa.Componi(null, null, xmp, "DJI", "FC8482");

        Assert.Equal(87.3, dati.AltezzaDalDecolloMetri);
        Assert.Equal(152.1, dati.AltitudineMetri);
        Assert.Equal(-90.0, dati.InclinazioneGradi);
        Assert.Equal(314.5, dati.DirezioneGradi); // -45.5 riportato su bussola 0..360
        Assert.Equal(TipoDispositivo.Drone, dati.Tipo);
        Assert.Equal("DJI FC8482", dati.Dispositivo);
    }

    [Fact]
    public void Telefono_usa_altitudine_e_direzione_EXIF()
    {
        var dati = DatiRipresa.Componi(12.4, 271.0, new Dictionary<string, string>(), "Apple", "iPhone 15 Pro");

        Assert.Equal(12.4, dati.AltitudineMetri);
        Assert.Equal(271.0, dati.DirezioneGradi);
        Assert.Null(dati.AltezzaDalDecolloMetri);
        Assert.Equal(TipoDispositivo.Telefono, dati.Tipo);
        Assert.Equal("Apple iPhone 15 Pro", dati.Dispositivo);
    }

    [Theory]
    [InlineData("samsung", "SM-S918B", "samsung SM-S918B", TipoDispositivo.Telefono)]
    [InlineData("Google", "Pixel 8", "Google Pixel 8", TipoDispositivo.Telefono)]
    [InlineData("Canon", "Canon EOS R6", "Canon EOS R6", TipoDispositivo.Fotocamera)]
    [InlineData(null, null, null, TipoDispositivo.Sconosciuto)]
    public void Dispositivo_nome_e_tipo(string? marca, string? modello, string? nome, TipoDispositivo tipo)
    {
        Assert.Equal(nome, DatiRipresa.NomeDispositivo(marca, modello));
        Assert.Equal(tipo, DatiRipresa.ClassificaDispositivo(marca, modello, haXmpDrone: false));
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(44, "NE")]
    [InlineData(180, "S")]
    [InlineData(250, "O")]
    [InlineData(350, "N")]
    public void Punto_cardinale(double gradi, string atteso) => Assert.Equal(atteso, DatiRipresa.PuntoCardinale(gradi));

    [Fact]
    public void Nome_luogo_da_Nominatim_punto_di_interesse_localita_e_regione()
    {
        const string json = """
            {"display_name":"Lama Monachile, Polignano a Mare, Bari, Puglia, Italia",
             "address":{"beach":"Lama Monachile","town":"Polignano a Mare","county":"Bari","state":"Puglia","country":"Italia"}}
            """;

        Assert.Equal("Lama Monachile, Polignano a Mare, Puglia", NomeLuogo.DaNominatim(json));
    }

    [Fact]
    public void Chiave_luogo_raggruppa_punti_entro_circa_100_metri()
    {
        Assert.Equal(NomeLuogo.Chiave(40.99601, 17.21862), NomeLuogo.Chiave(40.99641, 17.21889));
        Assert.NotEqual(NomeLuogo.Chiave(40.996, 17.218), NomeLuogo.Chiave(40.998, 17.218));
    }

    [Fact]
    public async Task Risolutore_usa_la_cache_e_chiama_il_servizio_una_volta_sola()
    {
        var chiamate = 0;
        var http = new HttpClient(new RispostaFissa(() => chiamate++)) { BaseAddress = new Uri("https://nominatim.test/") };
        var cache = new Dictionary<string, string>();
        var risolutore = new RisolutoreLuoghi(http, k => cache.GetValueOrDefault(k), (k, v) => cache[k] = v);

        var primo = await risolutore.NomeAsync(40.9960, 17.2186);
        var secondo = await risolutore.NomeAsync(40.9961, 17.2187);

        Assert.Equal("Polignano a Mare, Puglia", primo);
        Assert.Equal(primo, secondo);
        Assert.Equal(1, chiamate);
        Assert.Single(cache);

        // Nuova istanza (riapertura): basta la cache persistente, nessuna chiamata.
        var dopo = new RisolutoreLuoghi(http, k => cache.GetValueOrDefault(k), (k, v) => cache[k] = v);
        Assert.Equal(primo, await dopo.NomeAsync(40.9960, 17.2186));
        Assert.Equal(1, chiamate);
    }

    private sealed class RispostaFissa(Action conta) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            conta();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"address":{"town":"Polignano a Mare","state":"Puglia"}}"""),
            });
        }
    }
}
