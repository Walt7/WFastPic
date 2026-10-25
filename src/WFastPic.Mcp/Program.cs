using System.Text.Json;
using System.Text.Json.Nodes;
using WFastPic.Mcp;

// Server MCP su stdio: un messaggio JSON-RPC per riga. Su stdout solo risposte; i log vanno su stderr.
Console.InputEncoding = new System.Text.UTF8Encoding(false);
Console.OutputEncoding = new System.Text.UTF8Encoding(false);

var uscita = Console.Out;
string? riga;
while ((riga = Console.In.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(riga)) continue;

    JsonNode? id = null;
    try
    {
        var richiesta = JsonNode.Parse(riga)?.AsObject();
        if (richiesta is null) continue;
        id = richiesta["id"]?.DeepClone();
        var metodo = richiesta["method"]?.GetValue<string>();
        if (id is null) continue; // notifiche (initialized, cancelled…): nessuna risposta

        JsonNode? risultato = metodo switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = richiesta["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "wfastpic", ["version"] = "1.0.0" },
                ["instructions"] = "Cerca nelle foto catalogate con WFastPic (cartelle, persone, date, luoghi) e prepara cartelle o zip di copie da condividere. Sola lettura sulle foto: prepara_pacchetto copia soltanto.",
            },
            "ping" => new JsonObject(),
            "tools/list" => new JsonObject { ["tools"] = Strumenti.Elenco() },
            "tools/call" => ChiamaStrumento(richiesta["params"]?.AsObject()),
            _ => null,
        };

        if (risultato is null)
            Scrivi(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Metodo non supportato: {metodo}" } });
        else
            Scrivi(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = risultato });
    }
    catch (JsonException)
    {
        Scrivi(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "JSON non valido" } });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        Scrivi(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32603, ["message"] = ex.Message } });
    }
}

void Scrivi(JsonObject messaggio)
{
    uscita.WriteLine(messaggio.ToJsonString());
    uscita.Flush();
}

static JsonObject ChiamaStrumento(JsonObject? parametri)
{
    var nome = parametri?["name"]?.GetValue<string>() ?? "";
    try
    {
        var testo = Strumenti.Chiama(nome, parametri?["arguments"]?.AsObject());
        return new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = testo }) };
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
    {
        // Errore dello strumento (non del protocollo): Claude lo legge e può correggere la chiamata.
        return new JsonObject
        {
            ["isError"] = true,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = ex.Message }),
        };
    }
}
