using System.Globalization;
using WFastPic.Core.Library;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WFastPic.Mcp;

/// <summary>Gli strumenti MCP: definizioni (nome, descrizione, schema) e chiamate.</summary>
public static class Strumenti
{
    private const int LimiteElenco = 100, LimiteMassimoElenco = 1000, LimitePacchetto = 5000;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static JsonObject Filtri() => new()
    {
        ["cartella"] = Prop("string", "Percorso completo di una cartella (include le sue sottocartelle) oppure parte del nome. Vuoto = tutte le cartelle note."),
        ["persone"] = new JsonObject
        {
            ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" },
            ["description"] = "Nomi o soprannomi (anche parziali) delle persone: la foto deve contenerle tutte.",
        },
        ["anche_proposte"] = Prop("boolean", "Conta anche i volti solo proposti dal riconoscimento (non confermati). Default false."),
        ["dal"] = Prop("string", "Data di scatto minima, AAAA-MM-GG."),
        ["al"] = Prop("string", "Data di scatto massima inclusa, AAAA-MM-GG."),
        ["luogo"] = Prop("string", "Testo contenuto nel nome del luogo (es. «Polignano»). Servono i nomi dei luoghi già calcolati dall'app."),
        ["solo_selezionate"] = Prop("boolean", "Solo le foto selezionate dall'utente."),
        ["voto_minimo"] = Prop("integer", "Voto minimo 0-5 dato dall'utente."),
        ["includi_scarti"] = Prop("boolean", "Include le foto scartate (default false)."),
        ["selezione"] = Prop("string", "Nome di una selezione salvata (vedi salva_selezione): limita la ricerca a quelle foto."),
    };

    private static JsonObject Prop(string tipo, string descrizione) => new() { ["type"] = tipo, ["description"] = descrizione };

    private static JsonObject Schema(JsonObject proprieta) => new() { ["type"] = "object", ["properties"] = proprieta };

    public static JsonArray Elenco()
    {
        var cerca = Filtri();
        cerca["limite"] = Prop("integer", $"Quante foto mostrare (default {LimiteElenco}, massimo {LimiteMassimoElenco}); il totale è sempre indicato.");

        var salva = Filtri();
        salva.Remove("selezione");
        salva["nome"] = Prop("string", "Nome della selezione (es. «Matrimonio Alby e Eri»).");
        salva["percorsi"] = new JsonObject
        {
            ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" },
            ["description"] = "Percorsi completi delle foto (da cerca_foto). Se presenti, i filtri sono ignorati.",
        };
        salva["sovrascrivi"] = Prop("boolean", "Sostituisce una selezione con lo stesso nome (default false).");

        var pacchetto = Filtri();
        pacchetto["percorsi"] = new JsonObject
        {
            ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" },
            ["description"] = "Percorsi completi delle foto da includere (da cerca_foto). Se presenti, i filtri sono ignorati.",
        };
        pacchetto["nome"] = Prop("string", "Nome della cartella o dello zip (default «Foto»).");
        pacchetto["destinazione"] = Prop("string", "Cartella dove creare il pacchetto (default Documenti\\WFastPic condivisione). Non può stare dentro una cartella di foto.");
        pacchetto["zip"] = Prop("boolean", "true = un solo file .zip, false = una cartella (default false).");
        pacchetto["includi_video"] = Prop("boolean", "Include il video abbinato (Live Photo). Default true.");

        return new JsonArray
        {
            Strumento("elenco_cartelle", "Cartelle di foto già aperte in WFastPic, con numero di foto, periodo e quante selezionate.", Schema([])),
            Strumento("elenco_persone", "Persone con nome conosciute da WFastPic (nome, soprannome, chi è: io/famiglia/amico/conoscente).", Schema([])),
            Strumento("cerca_foto",
                "Cerca foto già catalogate per cartella (sottocartelle comprese), persone, date e luogo. Sola lettura. " +
                "Restituisce percorso, data, voto, luogo e persone di ogni foto.",
                Schema(cerca)),
            Strumento("salva_selezione",
                "Salva un elenco di foto con un nome, anche di cartelle diverse, per riusarlo (filtro «selezione» di cerca_foto e prepara_pacchetto). " +
                "Si indicano i filtri oppure i percorsi. Scrive solo selezioni.json: le foto non vengono toccate.",
                Schema(salva)),
            Strumento("elenco_selezioni", "Selezioni salvate: nome, quante foto, quando.", Schema([])),
            Strumento("elimina_selezione", "Toglie una selezione salvata dall'elenco (le foto restano dove sono).",
                Schema(new JsonObject { ["nome"] = Prop("string", "Nome della selezione.") })),
            Strumento("prepara_pacchetto",
                "Prepara una cartella o uno zip con COPIE degli originali (video abbinato compreso) da dare a qualcuno. " +
                "Usa gli stessi filtri di cerca_foto oppure un elenco esplicito di percorsi. Non invia nulla, non sposta né cancella foto. " +
                "Prima controlla con cerca_foto quali foto verranno incluse.",
                Schema(pacchetto)),
        };
    }

    private static JsonObject Strumento(string nome, string descrizione, JsonObject schema) =>
        new() { ["name"] = nome, ["description"] = descrizione, ["inputSchema"] = schema };

    /// <summary>Esegue lo strumento e restituisce il testo della risposta; le eccezioni diventano errori dello strumento.</summary>
    public static string Chiama(string nome, JsonObject? args)
    {
        args ??= new JsonObject();
        switch (nome)
        {
            case "elenco_cartelle":
                return Serializza(Ricerca.ElencoCartelle().Select(c => new
                {
                    cartella = c.Percorso, foto = c.Foto, selezionate = c.Selezionate,
                    dal = c.Primo?.ToString("yyyy-MM-dd"), al = c.Ultimo?.ToString("yyyy-MM-dd"),
                }));

            case "elenco_persone":
                return Serializza(Ricerca.ElencoPersone().Select(p => new
                {
                    nome = p.Nome, soprannome = p.Soprannome.Length > 0 ? p.Soprannome : null,
                    chi_e = p.Cerchia.Length > 0 ? p.Cerchia : null, preferita = p.Preferita,
                }));

            case "cerca_foto":
            {
                var trovate = Ricerca.Cerca(LeggiFiltri(args));
                var limite = Math.Clamp(Int(args, "limite") ?? LimiteElenco, 1, LimiteMassimoElenco);
                return Serializza(new
                {
                    totale = trovate.Count,
                    mostrate = Math.Min(limite, trovate.Count),
                    foto = trovate.Take(limite).Select(f => new
                    {
                        percorso = f.Percorso, data = f.Data?.ToString("yyyy-MM-dd HH:mm"), voto = f.Voto,
                        stato = f.Stato, luogo = f.Luogo, persone = f.Persone.Count > 0 ? f.Persone : null,
                    }),
                });
            }

            case "salva_selezione":
            {
                var salvata = Selezioni.Salva(Testo(args, "nome") ?? "", RisolviPercorsi(args), Bool(args, "sovrascrivi") ?? false);
                return Serializza(new { selezione = salvata.Nome, foto = salvata.Percorsi.Count, nota = "Salvata in selezioni.json: nessuna foto è stata toccata." });
            }

            case "elenco_selezioni":
                return Serializza(Selezioni.Elenco().Select(s => new { nome = s.Nome, foto = s.Percorsi.Count, creata = s.Creata.ToString("yyyy-MM-dd HH:mm") }));

            case "elimina_selezione":
                return Selezioni.Elimina(Testo(args, "nome") ?? "") ? "Selezione tolta dall'elenco." : throw new InvalidOperationException("Selezione non trovata.");

            case "prepara_pacchetto":
            {
                var percorsi = RisolviPercorsi(args);
                var esito = Pacchetto.Prepara(percorsi, Testo(args, "nome") ?? "Foto", Testo(args, "destinazione"),
                    Bool(args, "zip") ?? false, Bool(args, "includi_video") ?? true);
                return Serializza(new
                {
                    percorso = esito.Percorso, tipo = esito.Zip ? "zip" : "cartella", foto = esito.Foto, video = esito.Video,
                    megabyte = Math.Round(esito.Byte / 1048576.0, 1),
                    non_trovate = esito.Mancanti.Count > 0 ? esito.Mancanti : null,
                    nota = "Copie degli originali: niente è stato spostato o cancellato, e niente è stato inviato.",
                });
            }

            default:
                throw new InvalidOperationException($"Strumento sconosciuto: {nome}");
        }
    }

    /// <summary>I percorsi indicati, oppure le foto che corrispondono ai filtri (almeno uno: mai «tutte le foto»).</summary>
    private static List<string> RisolviPercorsi(JsonObject args)
    {
        var percorsi = Lista(args, "percorsi");
        if (percorsi.Count > 0) return percorsi;

        var filtri = LeggiFiltri(args);
        if (string.IsNullOrWhiteSpace(filtri.Cartella) && filtri.Persone is not { Count: > 0 } && filtri.Dal is null
            && filtri.Al is null && string.IsNullOrWhiteSpace(filtri.Luogo) && !filtri.SoloSelezionate
            && filtri.VotoMinimo == 0 && filtri.Percorsi is null)
            throw new InvalidOperationException("Indica almeno un filtro (cartella, persone, date, luogo, selezione…) o i percorsi: senza, sarebbero tutte le foto.");

        var trovate = Ricerca.Cerca(filtri);
        if (trovate.Count > LimitePacchetto)
            throw new InvalidOperationException($"Le foto trovate sono {trovate.Count}, più del massimo di {LimitePacchetto}: restringi i filtri.");
        return trovate.Select(f => f.Percorso).ToList();
    }

    private static FiltriFoto LeggiFiltri(JsonObject args) => new(
        Cartella: Testo(args, "cartella"),
        Persone: Lista(args, "persone"),
        AncheProposte: Bool(args, "anche_proposte") ?? false,
        Dal: Data(args, "dal"),
        Al: Data(args, "al"),
        Luogo: Testo(args, "luogo"),
        SoloSelezionate: Bool(args, "solo_selezionate") ?? false,
        VotoMinimo: Int(args, "voto_minimo") ?? 0,
        IncludiScarti: Bool(args, "includi_scarti") ?? false,
        Percorsi: Testo(args, "selezione") is { } nomeSelezione
            ? (Selezioni.Trova(nomeSelezione) ?? throw new InvalidOperationException($"Selezione «{nomeSelezione}» non trovata."))
                .Percorsi.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null);

    private static string Serializza(object valore) => JsonSerializer.Serialize(valore, Json);

    private static string? Testo(JsonObject a, string chiave) => a[chiave] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
    private static bool? Bool(JsonObject a, string chiave) => a[chiave] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    private static int? Int(JsonObject a, string chiave) => a[chiave] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;

    private static List<string> Lista(JsonObject a, string chiave) =>
        a[chiave] is JsonArray arr ? arr.Select(x => x?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() : [];

    private static DateTime? Data(JsonObject a, string chiave)
    {
        var testo = Testo(a, chiave);
        if (testo is null) return null;
        if (!DateTime.TryParse(testo, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new InvalidOperationException($"Data non valida «{testo}» (usa AAAA-MM-GG).");
        return d;
    }
}
