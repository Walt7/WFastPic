using System.Text.Encodings.Web;
using System.Text.Json;

namespace WFastPic.Core.Library;

public sealed record SelezioneSalvata(string Nome, DateTime Creata, List<string> Percorsi);

/// <summary>
/// Selezioni con nome ("Matrimonio Alby e Eri"): elenchi di foto anche di cartelle diverse, da riusare
/// per cercare o preparare pacchetti. Vivono in <c>selezioni.json</c> accanto a persone.db, non nei
/// sidecar delle cartelle (una selezione può attraversarne più d'una); le foto non vengono mai toccate.
/// Condivise tra l'app (menu della griglia) e il server MCP.
/// </summary>
public static class Selezioni
{
    private static readonly JsonSerializerOptions Opzioni = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Cartella dati dell'app (LocalAppData\WFastPic); <c>WFASTPIC_DATA</c> la sposta, per le prove.</summary>
    public static string CartellaDati =>
        Environment.GetEnvironmentVariable("WFASTPIC_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic");

    private static string File_ => Path.Combine(CartellaDati, "selezioni.json");

    /// <summary>Ultima modifica del file (anche dal server MCP): chi le mostra può accorgersi che sono cambiate.</summary>
    public static DateTime UltimaModifica()
    {
        try { return File.Exists(File_) ? File.GetLastWriteTimeUtc(File_) : DateTime.MinValue; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    public static List<SelezioneSalvata> Elenco()
    {
        try { return Leggi(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static SelezioneSalvata? Trova(string nome) =>
        Elenco().FirstOrDefault(s => string.Equals(s.Nome, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public static SelezioneSalvata Salva(string nome, IEnumerable<string> percorsi, bool sovrascrivi)
    {
        nome = nome.Trim();
        if (nome.Length == 0) throw new InvalidOperationException("Serve un nome per la selezione.");
        var nuova = new SelezioneSalvata(nome, DateTime.Now,
            percorsi.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        if (nuova.Percorsi.Count == 0) throw new InvalidOperationException("Nessuna foto da salvare.");

        return Modifica(elenco =>
        {
            var esistente = elenco.FindIndex(s => string.Equals(s.Nome, nome, StringComparison.CurrentCultureIgnoreCase));
            if (esistente >= 0 && !sovrascrivi)
                throw new InvalidOperationException($"Esiste già una selezione «{elenco[esistente].Nome}»: scegli un altro nome o usa sovrascrivi.");
            if (esistente >= 0) elenco[esistente] = nuova; else elenco.Add(nuova);
            return (nuova, true);
        });
    }

    public static bool Elimina(string nome) =>
        Modifica(elenco =>
        {
            var tolte = elenco.RemoveAll(s => string.Equals(s.Nome, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));
            return (tolte > 0, tolte > 0);
        });

    private static List<SelezioneSalvata> Leggi() =>
        File.Exists(File_)
            ? JsonSerializer.Deserialize<List<SelezioneSalvata>>(File.ReadAllText(File_)) ?? []
            : [];

    /// <summary>
    /// Legge, cambia e riscrive sotto un mutex condiviso da app e server MCP, così due salvataggi insieme
    /// non si perdono a vicenda. Un file illeggibile non viene mai sovrascritto: resta da parte come .bak.
    /// </summary>
    private static T Modifica<T>(Func<List<SelezioneSalvata>, (T Esito, bool Scrivi)> cambia)
    {
        using var mutex = new Mutex(false, @"Local\WFastPic.Selezioni");
        try { mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { /* l'altro processo è morto a metà: il file resta valido (scrittura atomica) */ }
        try
        {
            List<SelezioneSalvata> elenco;
            try { elenco = Leggi(); }
            catch (JsonException)
            {
                var copia = File_ + ".bak";
                File.Copy(File_, copia, overwrite: true);
                throw new InvalidOperationException($"Il file delle selezioni è rovinato (copia in {copia}): non lo sovrascrivo.");
            }

            var (esito, scrivi) = cambia(elenco);
            if (scrivi) Scrivi(elenco);
            return esito;
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { /* WaitOne scaduto: non era nostro */ }
        }
    }

    // Scrittura atomica: un'interruzione a metà non deve lasciare un file a metà.
    private static void Scrivi(List<SelezioneSalvata> elenco)
    {
        Directory.CreateDirectory(CartellaDati);
        var temp = $"{File_}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(elenco, Opzioni));
        File.Move(temp, File_, overwrite: true);
    }
}
