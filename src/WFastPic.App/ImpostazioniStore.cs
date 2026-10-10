using System.Text.Json;

namespace WFastPic.App;

public sealed record Impostazioni(int DimensioneCellaGriglia = 160);

/// <summary>
/// Preferenze UI locali all'installazione (come <see cref="RecentFoldersStore"/>): file JSON in LocalAppData.
/// </summary>
public static class ImpostazioniStore
{
    private static readonly string PercorsoFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WFastPic", "settings.json");

    public static Impostazioni Carica()
    {
        try
        {
            if (!File.Exists(PercorsoFile)) return new Impostazioni();
            return JsonSerializer.Deserialize<Impostazioni>(File.ReadAllText(PercorsoFile)) ?? new Impostazioni();
        }
        catch
        {
            return new Impostazioni();
        }
    }

    public static void Salva(Impostazioni impostazioni)
    {
        try
        {
            var cartellaFile = Path.GetDirectoryName(PercorsoFile);
            if (cartellaFile is not null) Directory.CreateDirectory(cartellaFile);
            File.WriteAllText(PercorsoFile, JsonSerializer.Serialize(impostazioni));
        }
        catch
        {
            // Preferenza persa: si riparte dal default, nessun impatto sul flusso.
        }
    }
}
