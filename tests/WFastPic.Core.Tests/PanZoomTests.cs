using WFastPic.Core.Processing;
using Xunit;

namespace WFastPic.Core.Tests;

public class PanZoomTests
{
    [Fact]
    public void Limita_a_zoom_1_tiene_la_foto_centrata()
    {
        Assert.Equal((0.0, 0.0), PanZoomGeometry.Limita(150, -80, 800, 600, 1, 1000, 700));
    }

    [Fact]
    public void Limita_consente_di_scorrere_solo_fino_al_bordo_della_foto()
    {
        // Foto 800x600 a zoom 2 = 1600x1200 in un'area 1000x700: nascosti 300px per lato in X, 250 in Y.
        Assert.Equal((300.0, -250.0), PanZoomGeometry.Limita(5000, -5000, 800, 600, 2, 1000, 700));
        Assert.Equal((120.0, 40.0), PanZoomGeometry.Limita(120, 40, 800, 600, 2, 1000, 700));
    }

    [Fact]
    public void Limita_blocca_l_asse_che_ci_sta_tutto()
    {
        // Foto verticale 400x600 a zoom 1.5 = 600x900: in larghezza ci sta, in altezza no.
        Assert.Equal((0.0, 100.0), PanZoomGeometry.Limita(200, 300, 400, 600, 1.5, 1000, 700));
    }

    [Fact]
    public void ZoomVersoPunto_lascia_fermo_il_punto_sotto_il_mouse()
    {
        // Punto immagine sotto il mouse prima e dopo deve coincidere: (p - t) / z costante.
        var (tx, ty) = PanZoomGeometry.ZoomVersoPunto(tx: 20, ty: -10, px: 200, py: 100, zoomPrima: 1.5, zoomDopo: 3);

        Assert.Equal((200 - 20) / 1.5, (200 - tx) / 3, precision: 9);
        Assert.Equal((100 - -10) / 1.5, (100 - ty) / 3, precision: 9);
    }

    [Fact]
    public void ZoomVersoPunto_al_centro_non_sposta()
    {
        Assert.Equal((0.0, 0.0), PanZoomGeometry.ZoomVersoPunto(0, 0, 0, 0, 1, 2));
    }
}
