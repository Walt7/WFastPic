using WFastPic.Core.Library;
using Xunit;

namespace WFastPic.Core.Tests;

public class FolderScannerSottocartelleTests
{
    [Fact]
    public void Scansione_include_sottocartelle_ed_esclude_scarti_export_e_nascoste()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-sub-" + Guid.NewGuid());
        try
        {
            foreach (var sotto in new[] { "", "Giorno 1", Path.Combine("Giorno 1", "Cerimonia"), "Scarti", "Export", ".wfastpic" })
            {
                Directory.CreateDirectory(Path.Combine(cartella, sotto));
                File.WriteAllBytes(Path.Combine(cartella, sotto, "IMG_1.jpg"), [1, 2, 3]);
            }

            var percorsi = FolderScanner.Scan(cartella).Select(s => s.Identity.PathRelativo).Order().ToList();

            Assert.Equal(
                [Path.Combine("Giorno 1", "Cerimonia", "IMG_1.jpg"), Path.Combine("Giorno 1", "IMG_1.jpg"), "IMG_1.jpg"],
                percorsi);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }
}
