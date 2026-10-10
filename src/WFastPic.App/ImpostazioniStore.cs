using System.Text.Json;

namespace WFastPic.App;

public sealed record Impostazioni(int DimensioneCellaGriglia = 160)
{
    public ImpostazioniExport Export { get; init; } = new();

    /// <summary>Nomi dei luoghi chiesti a OpenStreetMap (le coordinate escono dal PC); spento = solo cache.</summary>
    public bool NomiLuoghiOnline { get; init; } = true;

    /// <summary>Griglia divisa per giorno di scatto.</summary>
    public bool DividiPerGiorni { get; init; }
}

/// <summary>
/// Come Picasa, il dialog di export ricorda le ultime scelte. <see cref="Posizione"/> null = la
/// cartella delle foto aperta (cambia con la cartella); un percorso scelto con Sfoglia resta fisso.
/// I tag no: sono quasi sempre specifici del viaggio.
/// </summary>
public sealed record ImpostazioniExport
{
    public string? Posizione { get; init; }
    public string NomeCartella { get; init; } = "Export";
    public bool DimensioniOriginali { get; init; }
    public int LatoLungo { get; init; } = 2048;
    public int QualitaPreset { get; init; }
    public int QualitaPersonalizzata { get; init; } = 90;
    public bool Filigrana { get; init; }
    public string TestoFiligrana { get; init; } = "";
    public double OpacitaFiligrana { get; init; } = 50;
    public int PosizioneFiligrana { get; init; } = 3;
    public bool RimuoviGps { get; init; }
    public bool SostituisciEsistenti { get; init; } = true;
}

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
