using Microsoft.Data.Sqlite;

namespace WFastPic.Core.Persistence;

/// <summary>Una persona con nome: avatar (JPEG del volto) e campioni di impronta confermati.</summary>
public sealed record PersonaArchivio(long Id, string Nome, byte[]? Avatar, IReadOnlyList<byte[]> Campioni);

/// <summary>
/// Persone valide per tutte le cartelle, come le "Persone" di Picasa: un nome dato nelle foto della
/// Puglia viene riconosciuto anche altrove. Database a parte in LocalAppData (non nel sidecar di una
/// cartella): solo nomi, avatar e impronte; quali volti sono di chi resta nel sidecar di ogni cartella.
/// Thread-safe come il sidecar: ogni accesso è serializzato.
/// </summary>
public sealed class ArchivioPersone : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    public static string PercorsoPredefinito => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic", "persone.db");

    private ArchivioPersone(SqliteConnection connection) => _connection = connection;

    public static ArchivioPersone Apri(string percorso)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = percorso,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS persone (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                nome TEXT NOT NULL UNIQUE COLLATE NOCASE,
                avatar BLOB NULL
            );
            CREATE TABLE IF NOT EXISTS campioni (
                persona_id INTEGER NOT NULL REFERENCES persone(id) ON DELETE CASCADE,
                impronta BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_campioni_persona ON campioni(persona_id);
            """;
        cmd.ExecuteNonQuery();
        return new ArchivioPersone(connection);
    }

    public IReadOnlyList<PersonaArchivio> Elenco()
    {
        using var scope = _lock.EnterScope();
        var campioni = new Dictionary<long, List<byte[]>>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT persona_id, impronta FROM campioni;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!campioni.TryGetValue(id, out var lista)) campioni[id] = lista = [];
                lista.Add((byte[])reader[1]);
            }
        }

        using var persone = _connection.CreateCommand();
        persone.CommandText = "SELECT id, nome, avatar FROM persone ORDER BY nome COLLATE NOCASE;";
        using var r = persone.ExecuteReader();
        var elenco = new List<PersonaArchivio>();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            elenco.Add(new PersonaArchivio(id, r.GetString(1), r.IsDBNull(2) ? null : (byte[])r[2],
                campioni.TryGetValue(id, out var c) ? c : []));
        }
        return elenco;
    }

    /// <summary>La persona con quel nome (senza distinguere maiuscole), creata se non esiste.</summary>
    public long TrovaOCrea(string nome, byte[]? avatar)
    {
        nome = nome.Trim();
        using var scope = _lock.EnterScope();
        using (var cerca = _connection.CreateCommand())
        {
            cerca.CommandText = "SELECT id FROM persone WHERE nome = $nome COLLATE NOCASE;";
            cerca.Parameters.AddWithValue("$nome", nome);
            if (cerca.ExecuteScalar() is long esistente) return esistente;
        }

        using var crea = _connection.CreateCommand();
        crea.CommandText = "INSERT INTO persone (nome, avatar) VALUES ($nome, $avatar); SELECT last_insert_rowid();";
        crea.Parameters.AddWithValue("$nome", nome);
        crea.Parameters.AddWithValue("$avatar", (object?)avatar ?? DBNull.Value);
        return (long)crea.ExecuteScalar()!;
    }

    /// <summary>Sostituisce i campioni di impronta della persona (già filtrati e limitati dal chiamante).</summary>
    public void ImpostaCampioni(long personaId, IReadOnlyList<byte[]> campioni)
    {
        using var scope = _lock.EnterScope();
        using var transaction = _connection.BeginTransaction();
        using (var cancella = _connection.CreateCommand())
        {
            cancella.Transaction = transaction;
            cancella.CommandText = "DELETE FROM campioni WHERE persona_id = $id;";
            cancella.Parameters.AddWithValue("$id", personaId);
            cancella.ExecuteNonQuery();
        }

        using var inserisci = _connection.CreateCommand();
        inserisci.Transaction = transaction;
        inserisci.CommandText = "INSERT INTO campioni (persona_id, impronta) VALUES ($id, $impronta);";
        var pId = inserisci.Parameters.Add("$id", SqliteType.Integer);
        var pImpronta = inserisci.Parameters.Add("$impronta", SqliteType.Blob);
        foreach (var c in campioni)
        {
            pId.Value = personaId;
            pImpronta.Value = c;
            inserisci.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Rinomina(long personaId, string nuovoNome)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE persone SET nome = $nome WHERE id = $id;";
        cmd.Parameters.AddWithValue("$nome", nuovoNome.Trim());
        cmd.Parameters.AddWithValue("$id", personaId);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        using var scope = _lock.EnterScope();
        _connection.Dispose();
    }
}
