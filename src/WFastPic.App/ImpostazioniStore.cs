using System.Text.Json;

namespace WFastPic.App;

public sealed record Impostazioni(int DimensioneCellaGriglia = 160)
{
    public ImpostazioniExport Export { get; init; } = new();

    /// <summary>"auto" (lingua di Windows), "it" o "en": si applica al prossimo avvio.</summary>
    public string Lingua { get; init; } = "auto";

    /// <summary>Nomi dei luoghi chiesti a OpenStreetMap (le coordinate escono dal PC); spento = solo cache.</summary>
    public bool NomiLuoghiOnline { get; init; } = true;

    /// <summary>All'avvio chiede a GitHub qual è l'ultima release e avvisa (in piccolo, nella barra in basso) se ce n'è una nuova.</summary>
    public bool ControllaAggiornamenti { get; init; } = true;

    /// <summary>Griglia divisa per giorno di scatto.</summary>
    public bool DividiPerGiorni { get; init; }

    /// <summary>Riconoscimento persone (pesante: modelli da 38 MB, analisi in background); null = da chiedere.</summary>
    public bool? RiconoscimentoPersone { get; init; }

    /// <summary>Pannello cartelle a sinistra: aperto di default, come in Picasa.</summary>
    public bool PannelloCartelleAperto { get; init; } = true;

    /// <summary>Colonne del viewer: modifiche (sinistra) e dettagli (destra).</summary>
    public bool ViewerPannelloModifiche { get; init; } = true;

    public bool ViewerPannelloDettagli { get; init; } = true;

    /// <summary>
    /// Dove si era rimasti: all'avvio si riapre l'ultima cartella (la prima delle recenti) e, se è la
    /// stessa, si torna su questa foto, nel viewer se era aperto.
    /// </summary>
    public string? UltimaCartella { get; init; }

    public string? UltimaFoto { get; init; }

    public bool UltimaFotoNelViewer { get; init; }
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
