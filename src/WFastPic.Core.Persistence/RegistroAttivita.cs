using Microsoft.Data.Sqlite;

namespace WFastPic.Core.Persistence;

public sealed record EventoRegistro(long Id, DateTime Quando, string Tipo, string Dettaglio, string? Errore);

/// <summary>
/// Mini registro di quello che fa l'app (nomi dati, unioni, import, esportazioni, errori…), per capire dopo cosa è
/// successo: <c>attivita.db</c> accanto a persone.db. Solo testo breve, mai foto né impronte; si tengono gli ultimi
/// <see cref="MassimoEventi"/> eventi. Non deve mai disturbare chi lo usa: ogni errore di scrittura è ignorato.
/// </summary>
public static class RegistroAttivita
{
    public const int MassimoEventi = 20000;

    private static readonly Lock Lucchetto = new();
    private static SqliteConnection? _connessione;
    private static string? _percorsoAperto;

    public static string PercorsoPredefinito => Path.Combine(
        Environment.GetEnvironmentVariable("WFASTPIC_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic"),
        "attivita.db");

    /// <summary>Un evento: tipo breve ("persona.nome", "cartella.apri"…) e dettaglio leggibile.</summary>
    public static void Scrivi(string tipo, string dettaglio = "", string? errore = null)
    {
        try
        {
            using var scope = Lucchetto.EnterScope();
            var connessione = Apri();
            using var cmd = connessione.CreateCommand();
            cmd.CommandText = "INSERT INTO eventi (quando, tipo, dettaglio, errore) VALUES ($q, $t, $d, $e);";
            cmd.Parameters.AddWithValue("$q", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            cmd.Parameters.AddWithValue("$t", tipo);
            cmd.Parameters.AddWithValue("$d", dettaglio);
            cmd.Parameters.AddWithValue("$e", (object?)errore ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            // Il registro è un aiuto: se non si riesce a scrivere si va avanti senza.
        }
    }

    public static void Errore(string tipo, Exception ex, string dettaglio = "") => Scrivi(tipo, dettaglio, ex.ToString());

    /// <summary>Gli ultimi eventi, dal più recente (sola lettura: funziona anche con l'app aperta).</summary>
    public static IReadOnlyList<EventoRegistro> Ultimi(int quanti = 100, string? percorso = null)
    {
        var risultato = new List<EventoRegistro>();
        percorso ??= PercorsoPredefinito;
        if (!File.Exists(percorso)) return risultato;
        try
        {
            using var connessione = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = percorso, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            }.ToString());
            connessione.Open();
            using var cmd = connessione.CreateCommand();
            cmd.CommandText = "SELECT id, quando, tipo, dettaglio, errore FROM eventi ORDER BY id DESC LIMIT $n;";
            cmd.Parameters.AddWithValue("$n", Math.Clamp(quanti, 1, 2000));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                risultato.Add(new EventoRegistro(r.GetInt64(0), DateTime.Parse(r.GetString(1)), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or FormatException) { }
        return risultato;
    }

    private static SqliteConnection Apri()
    {
        var percorso = PercorsoPredefinito;
        if (_connessione is not null && _percorsoAperto == percorso) return _connessione;

        _connessione?.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        var connessione = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = percorso, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false,
        }.ToString());
        connessione.Open();
        using (var cmd = connessione.CreateCommand())
        {
            cmd.CommandText = $"""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS eventi (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    quando TEXT NOT NULL,
                    tipo TEXT NOT NULL,
                    dettaglio TEXT NOT NULL,
                    errore TEXT NULL
                );
                DELETE FROM eventi WHERE id <= (SELECT COALESCE(MAX(id), 0) FROM eventi) - {MassimoEventi};
                """;
            cmd.ExecuteNonQuery();
        }
        _percorsoAperto = percorso;
        return _connessione = connessione;
    }
}
