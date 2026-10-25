using System.Text.Encodings.Web;
using System.Text.Json;

namespace WFastPic.Mcp;

public sealed record SelezioneSalvata(string Nome, DateTime Creata, List<string> Percorsi);

/// <summary>
/// Selezioni con nome ("Matrimonio Alby e Eri"): elenchi di foto anche di cartelle diverse, da riusare
/// per cercare o preparare pacchetti. Vivono in <c>selezioni.json</c> accanto a persone.db, non nei
/// sidecar delle cartelle (una selezione può attraversarne più d'una); le foto non vengono mai toccate.
/// </summary>
public static class Selezioni
{
    private static readonly JsonSerializerOptions Opzioni = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string File_ => Path.Combine(Ricerca.DatiApp, "selezioni.json");

    public static List<SelezioneSalvata> Elenco()
    {
        try
        {
            return File.Exists(File_)
                ? JsonSerializer.Deserialize<List<SelezioneSalvata>>(File.ReadAllText(File_)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static SelezioneSalvata? Trova(string nome) =>
        Elenco().FirstOrDefault(s => string.Equals(s.Nome, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public static SelezioneSalvata Salva(string nome, IEnumerable<string> percorsi, bool sovrascrivi)
    {
        nome = nome.Trim();
        if (nome.Length == 0) throw new InvalidOperationException("Serve un nome per la selezione.");
        var elenco = Elenco();
        var esistente = elenco.FindIndex(s => string.Equals(s.Nome, nome, StringComparison.CurrentCultureIgnoreCase));
        if (esistente >= 0 && !sovrascrivi)
            throw new InvalidOperationException($"Esiste già una selezione «{elenco[esistente].Nome}»: scegli un altro nome o usa sovrascrivi.");

        var nuova = new SelezioneSalvata(nome, DateTime.Now,
            percorsi.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        if (nuova.Percorsi.Count == 0) throw new InvalidOperationException("Nessuna foto da salvare.");

        if (esistente >= 0) elenco[esistente] = nuova; else elenco.Add(nuova);
        Scrivi(elenco);
        return nuova;
    }

    public static bool Elimina(string nome)
    {
        var elenco = Elenco();
        var tolte = elenco.RemoveAll(s => string.Equals(s.Nome, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (tolte > 0) Scrivi(elenco);
        return tolte > 0;
    }

    // Scrittura atomica: un'interruzione a metà non deve lasciare un file a metà.
    private static void Scrivi(List<SelezioneSalvata> elenco)
    {
        Directory.CreateDirectory(Ricerca.DatiApp);
        var temp = File_ + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(elenco, Opzioni));
        File.Move(temp, File_, overwrite: true);
    }
}
