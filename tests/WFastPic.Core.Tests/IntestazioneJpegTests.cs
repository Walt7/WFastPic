using Xunit;
using WFastPic.Core.Decoding;

namespace WFastPic.Core.Tests;

public class IntestazioneJpegTests
{
    // JPEG minimo: SOI, SOF0 1×1 a un componente, SOS (Ss, Se, AhAl), due byte di dati, EOI.
    private static byte[] Jpeg(byte ss, byte se, byte ahAl, byte sofMarcatore = 0xC0) =>
    [
        0xFF, 0xD8,
        0xFF, sofMarcatore, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, ss, se, ahAl,
        0x12, 0x34,
        0xFF, 0xD9,
    ];

    [Fact]
    public void Intestazione_regolare_non_ha_problemi() =>
        Assert.Null(IntestazioneJpeg.Problema(Jpeg(0, 63, 0)));

    [Theory]
    [InlineData(1, 63, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(0, 63, 0x01)]
    public void Parametri_SOS_non_standard_in_un_sequenziale_sono_segnalati(byte ss, byte se, byte ahAl) =>
        Assert.Contains("SOS", IntestazioneJpeg.Problema(Jpeg(ss, se, ahAl)));

    [Fact]
    public void Ripara_cambia_solo_i_byte_del_SOS()
    {
        var originale = Jpeg(0, 0, 0);
        var riparato = IntestazioneJpeg.Ripara(originale);

        Assert.NotNull(riparato);
        Assert.Equal(originale.Length, riparato.Length);
        Assert.Equal(1, originale.Zip(riparato).Count(c => c.First != c.Second)); // qui differisce il solo Se (0 -> 63)
        Assert.Null(IntestazioneJpeg.Problema(riparato));
        Assert.Null(IntestazioneJpeg.Ripara(riparato)); // gia regolare: niente da fare
    }

    [Fact]
    public void Progressivo_con_parametri_diversi_e_normale() =>
        Assert.Null(IntestazioneJpeg.Problema(Jpeg(1, 5, 0x10, sofMarcatore: 0xC2)));

    [Fact]
    public void File_non_jpeg_o_troncato_non_lancia()
    {
        Assert.Null(IntestazioneJpeg.Problema([]));
        Assert.Null(IntestazioneJpeg.Problema([0x89, 0x50, 0x4E, 0x47]));
        Assert.Null(IntestazioneJpeg.Problema(Jpeg(0, 63, 0).AsSpan(0, 20)));
    }
}
