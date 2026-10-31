namespace WFastPic.Mcp;

/// <summary>
/// Analisi dei volti dal terminale, per archivi da ore di lavoro: non dipende da Claude aperto, Ctrl+C la ferma e
/// rilanciandola riprende dalla foto dove si era fermata. Lo stato si legge anche con stato_analisi.
/// </summary>
public static class RigaDiComando
{
    public static async Task<int> AnalizzaAsync(string cartella, int? massimoFoto)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.Error.WriteLine("Mi fermo dopo questa foto…");
        };

        var ultimo = DateTime.MinValue;
        var faseScritta = "";
        try
        {
            await AnalisiCartella.EseguiAsync(cartella, massimoFoto, cts.Token, s =>
            {
                if (s.Fase == faseScritta && !s.Finita && DateTime.Now - ultimo < TimeSpan.FromSeconds(5)) return;
                (ultimo, faseScritta) = (DateTime.Now, s.Fase);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {s.Fase}: {s.Avanzamento ?? $"{s.Fatte} di {s.Totale}"} · {s.Volti} volti · {s.Errori} non lette");
            });
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// «WFastPic.Mcp importa-picasa &lt;cartella&gt; [esegui] [nomi.json]»: come lo strumento importa_picasa, dal terminale.
    /// Senza «esegui» è solo l'anteprima. nomi.json = {"nome in Picasa": "nome da usare", "x@gmail.com": ""}.
    /// Va lanciato da un terminale normale (non da dentro Claude Desktop) perché scriva nel persone.db vero.
    /// </summary>
    public static int ImportaPicasa(string[] args)
    {
        try
        {
            var argomenti = new System.Text.Json.Nodes.JsonObject
            {
                ["cartella"] = args[1],
                ["esegui"] = args.Skip(2).Any(a => a.Equals("esegui", StringComparison.OrdinalIgnoreCase)),
                ["limite"] = 1000,
            };
            if (args.Skip(2).FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) is { } fileNomi)
                argomenti["nomi"] = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fileNomi));
            Console.WriteLine(Strumenti.Chiama("importa_picasa", argomenti));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
