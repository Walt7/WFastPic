using System.Text.Json;

namespace WFastPic.App;

/// <summary>
/// Elenco cartelle recenti, preferenza puramente locale all'installazione (non è dominio foto,
/// quindi non vive nel sidecar SQLite per-cartella). File JSON semplice in LocalAppData.
/// </summary>
public static class RecentFoldersStore
{
    private static readonly string PercorsoFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WFastPic", "recent-folders.json");

    public static List<string> Carica()
    {
        try
        {
            if (!File.Exists(PercorsoFile)) return [];
            var json = File.ReadAllText(PercorsoFile);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Salva(List<string> cartelle)
    {
        try
        {
            var cartellaFile = Path.GetDirectoryName(PercorsoFile);
            if (cartellaFile is not null) Directory.CreateDirectory(cartellaFile);
            File.WriteAllText(PercorsoFile, JsonSerializer.Serialize(cartelle));
        }
        catch
        {
            // Preferenza persa non deve mai impedire l'apertura di una cartella.
        }
    }

    // È l'elenco "le mie cartelle" del pannello (come le cartelle di Picasa), non solo gli ultimi accessi.
    public static List<string> AggiungiRecente(List<string> attuali, string nuovaCartella, int max = 200)
    {
        var lista = attuali.Where(c => !string.Equals(c, nuovaCartella, StringComparison.OrdinalIgnoreCase)).ToList();
        lista.Insert(0, nuovaCartella);
        return lista.Count > max ? lista.Take(max).ToList() : lista;
    }
}
