using System.Globalization;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WFastPic.Mcp;

/// <summary>Gli strumenti MCP: definizioni (nome, descrizione, schema) e chiamate.</summary>
public static class Strumenti
{
    private const int LimiteElenco = 100, LimiteMassimoElenco = 1000, LimitePacchetto = 5000;
    private const int LimiteGruppi = 20, VoltiMostrati = 6, MassimoVoltiMostrati = 12;

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
            Strumento("ultime_attivita",
                "Mini registro di quello che ha fatto l'app (nomi dati, unioni, import, esportazioni, errori…), dal più recente. " +
                "Serve a capire cosa è successo. Sola lettura.",
                Schema(new JsonObject
                {
                    ["limite"] = Prop("integer", "Quanti eventi (default 50, massimo 500)."),
                    ["tipo"] = Prop("string", "Solo i tipi che iniziano così, es. «persona», «cartella», «export», «errore»."),
                })),
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

            Strumento("analizza_cartella",
                "Cerca e raggruppa i volti delle foto di una cartella (sottocartelle comprese, anche tutto l'archivio) senza aprire l'app. " +
                "Gira in background e torna subito: l'avanzamento si legge con stato_analisi. Cataloga la cartella come l'app (.wfastpic) " +
                "se non lo è già, salta le foto già analizzate (si può fermare e riprendere) e ripropone le persone note sui volti trovati. " +
                "Scrive solo nei cataloghi .wfastpic: le foto non vengono toccate. Per archivi molto grandi è meglio lanciarla dal terminale " +
                "(WFastPic.Mcp.exe analizza <cartella>), così non si ferma chiudendo Claude.",
                Schema(new JsonObject
                {
                    ["cartella"] = Prop("string", "Percorso completo della cartella, es. E:\\Fotocamera\\2017."),
                    ["massimo_foto"] = Prop("integer", "Analizza al massimo queste foto in questo giro (lotto); le altre al prossimo. Vuoto = tutte."),
                })),
            Strumento("stato_analisi",
                "A che punto è l'analisi dei volti (anche quella lanciata dal terminale): fase, foto fatte su quante, volti trovati, tempo rimanente.",
                Schema(new JsonObject { ["ferma"] = Prop("boolean", "true = ferma l'analisi in corso in questo server (riprende dalla stessa foto alla prossima).") })),
            Strumento("volti_da_nominare",
                "Gruppi di volti ancora senza nome, che si somigliano, dal più frequente: id del gruppo, quante foto, periodo, cartelle principali, " +
                "volti di esempio e, se c'è, la persona nota più somigliante (solo un indizio da verificare). Elenca anche le proposte automatiche " +
                "da confermare per ogni persona. Non riconoscere tu le persone dalla faccia: proponi nomi dal contesto (date, cartelle, chi c'era) " +
                "e fai confermare all'utente guardando i volti con mostra_volto.",
                Schema(new JsonObject
                {
                    ["cartella"] = Prop("string", "Solo le foto sotto questa cartella (percorso completo o parte del nome). Vuoto = tutto l'archivio catalogato."),
                    ["limite"] = Prop("integer", $"Quanti gruppi mostrare (default {LimiteGruppi}, massimo 200); il totale è sempre indicato."),
                    ["minimo_volti"] = Prop("integer", "Solo gruppi con almeno questi volti (default 2; 1 = anche volti isolati)."),
                })),
            Strumento("mostra_volto",
                $"Mostra all'utente alcuni volti come piccole immagini ({ImmaginiVolti.Lato} px), ritagliati dalla foto con un po' di contesto: " +
                "quelli indicati, alcuni di un gruppo, o le proposte di una persona. Servono all'utente per dire chi è: non usarle per identificare tu le persone.",
                Schema(new JsonObject
                {
                    ["volti"] = ListaStringhe("Riferimenti dei volti (es. «a1b2c3d4-123»)."),
                    ["gruppo"] = Prop("string", "Id di un gruppo da volti_da_nominare (es. «ga1b2c3d4-123»)."),
                    ["persona"] = Prop("string", "Nome di una persona: mostra i suoi volti proposti da confermare (o confermati, con proposte=false)."),
                    ["proposte"] = Prop("boolean", "Con persona: true = volti proposti (default), false = volti già confermati."),
                    ["massimo"] = Prop("integer", $"Quanti volti (default {VoltiMostrati}, massimo {MassimoVoltiMostrati})."),
                    ["dalla_foto"] = Prop("boolean", "true = ritaglio dalla foto con contesto (default); false = miniatura salvata, più veloce."),
                    ["foglio"] = Prop("boolean", "Con più volti: true = un'immagine sola con il numero su ogni volto (default), false = un'immagine per volto."),
                })),
            Strumento("assegna_nome",
                "Dà un nome, confermato dall'utente, a un gruppo di volti o a singoli volti. Con il nome di una persona già nota i volti vanno a lei " +
                "(è anche il modo di unire un gruppo a una persona esistente). Poi il riconoscimento propone lo stesso nome sui volti somiglianti di " +
                "tutte le cartelle (da confermare con conferma_volti). Scrive nei cataloghi .wfastpic e in persone.db, mai nelle foto; si annulla con annulla_volti.",
                Schema(new JsonObject
                {
                    ["nome"] = Prop("string", "Nome della persona (es. «Alberto Rossi»), detto o confermato dall'utente."),
                    ["gruppo"] = Prop("string", "Id del gruppo da volti_da_nominare."),
                    ["volti"] = ListaStringhe("Riferimenti di singoli volti (in alternativa o in aggiunta al gruppo)."),
                    ["escludi"] = ListaStringhe("Volti del gruppo che NON sono questa persona: restano senza nome."),
                    ["soprannome"] = Prop("string", "Come la chiama l'utente («Alby», «la nonna»): solo un'etichetta, cercabile."),
                    ["chi_e"] = Prop("string", "io, famiglia, amico, conoscente (pesa nel voto automatico delle foto). «io» è una persona sola."),
                })),
            Strumento("conferma_volti",
                "Conferma i volti che il riconoscimento ha proposto per una persona (dopo averli mostrati all'utente con mostra_volto): quelli indicati, " +
                "oppure tutti con tutte=true. I volti confermati diventano campioni e migliorano le proposte successive. Si annulla con annulla_volti.",
                Schema(new JsonObject
                {
                    ["persona"] = Prop("string", "Nome (o soprannome) della persona."),
                    ["volti"] = ListaStringhe("Riferimenti dei volti proposti da confermare."),
                    ["tutte"] = Prop("boolean", "true = conferma tutte le proposte di questa persona."),
                })),
            Strumento("ignora_volti",
                "Come «Ignora questi volti» nell'app: passanti, volti su un poster, errori del rilevatore. Spariscono dai gruppi da nominare e dalle " +
                "proposte (non dalle foto). Si annulla con annulla_volti.",
                Schema(new JsonObject
                {
                    ["gruppo"] = Prop("string", "Id del gruppo da volti_da_nominare."),
                    ["volti"] = ListaStringhe("Riferimenti di singoli volti."),
                    ["escludi"] = ListaStringhe("Volti del gruppo da non ignorare."),
                })),
            Strumento("annulla_volti",
                "Annulla un'operazione sui volti fatta con assegna_nome, conferma_volti o ignora_volti: i volti tornano com'erano (se nel frattempo " +
                "non sono stati cambiati di nuovo), una persona appena creata viene tolta. Senza numero annulla l'ultima; elenco con elenco=true.",
                Schema(new JsonObject
                {
                    ["operazione"] = Prop("integer", "Numero dell'operazione (lo restituiscono assegna_nome, conferma_volti, ignora_volti)."),
                    ["elenco"] = Prop("boolean", "true = elenca le ultime operazioni invece di annullare."),
                })),
            Strumento("importa_picasa",
                "Prende i nomi dei volti che Picasa aveva marcato (file Picasa.ini nelle cartelle) e li dà ai volti trovati da WFastPic nelle " +
                "stesse foto, come confermati: diventano campioni e il riconoscimento propone quelle persone nel resto dell'archivio. " +
                "Prima chiamala senza esegui: è un'anteprima con i nomi, quelli che sono email e quelli forse uguali («walter» / «Walter " +
                "Rodeghiero»), da far decidere all'utente; poi con esegui=true e, se serve, nomi per rinominare, unire o saltare. " +
                "Vale solo per le foto già analizzate (analizza_cartella). Non tocca i Picasa.ini né le foto; si annulla con annulla_volti.",
                Schema(new JsonObject
                {
                    ["cartella"] = Prop("string", "Percorso completo della cartella (sottocartelle comprese), es. E:\\Fotocamera\\2012."),
                    ["esegui"] = Prop("boolean", "false = anteprima (default), true = scrive i nomi."),
                    ["nomi"] = new JsonObject
                    {
                        ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "string" },
                        ["description"] = "Nome in Picasa → nome da usare in WFastPic (anche di una persona già nota, per unirli); \"\" = non importarlo. " +
                                          "Es. {\"walter\": \"Walter Rodeghiero\", \"Rodeghiero Pierluigi\": \"Gigi\", \"x@gmail.com\": \"\"}.",
                    },
                    ["limite"] = Prop("integer", "Quanti nomi elencare nell'anteprima (default 60)."),
                })),
            Strumento("luoghi_frequenti",
                "I posti dove sono state fatte più foto, in più occasioni: raggruppa le foto con GPS per vicinanza e le ordina per numero di anni " +
                "diversi e di giorni diversi. Serve per scoprire i luoghi che contano (montagna, mare, posti dove si torna). Con casa_lat/casa_lon ed " +
                "escludi_km si tolgono i posti vicino a casa. Il nome del luogo c'è solo se l'app l'ha già calcolato; altrimenti ci sono le coordinate. Sola lettura.",
                Schema(new JsonObject
                {
                    ["cartella"] = Prop("string", "Solo le foto sotto questa cartella (percorso completo o parte del nome). Vuoto = tutto l'archivio."),
                    ["dal"] = Prop("string", "Data minima, AAAA-MM-GG."),
                    ["al"] = Prop("string", "Data massima inclusa, AAAA-MM-GG."),
                    ["raggio_m"] = Prop("integer", "Raggio del luogo in metri (default 300): foto più vicine di così sono lo stesso posto."),
                    ["minimo_anni"] = Prop("integer", "Almeno quanti anni diversi (default 2)."),
                    ["minimo_giorni"] = Prop("integer", "Almeno quanti giorni diversi (default 2)."),
                    ["casa_lat"] = Prop("number", "Latitudine di casa, per escludere i posti vicini."),
                    ["casa_lon"] = Prop("number", "Longitudine di casa."),
                    ["escludi_km"] = Prop("number", "Raggio attorno a casa da escludere, in km (default 0 = niente)."),
                    ["limite"] = Prop("integer", "Quanti luoghi elencare (default 30)."),
                })),
            Strumento("persone_nel_giorno",
                "Chi compare nelle foto di un giorno o di un intervallo: per ogni giorno le persone con nome e le loro foto, e quanti volti sono ancora " +
                "senza nome (con esempi da mostrare). Serve per il diario: sapere chi c'era quando. Sola lettura.",
                Schema(new JsonObject
                {
                    ["giorno"] = Prop("string", "Un giorno, AAAA-MM-GG."),
                    ["dal"] = Prop("string", "Inizio dell'intervallo, AAAA-MM-GG (in alternativa a giorno)."),
                    ["al"] = Prop("string", "Fine dell'intervallo inclusa, AAAA-MM-GG."),
                    ["cartella"] = Prop("string", "Solo le foto sotto questa cartella (percorso completo o parte del nome)."),
                    ["anche_proposte"] = Prop("boolean", "Conta anche i volti proposti e non ancora confermati (indicati come tali). Default false."),
                    ["foto_per_persona"] = Prop("integer", "Quanti percorsi di foto elencare per persona (default 5, 0 = nessuno)."),
                })),
        };
    }

    private static JsonObject ListaStringhe(string descrizione) => new()
    {
        ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = descrizione,
    };

    /// <summary>Il contenuto MCP della risposta: di norma un testo JSON; mostra_volto aggiunge le immagini dei volti.</summary>
    public static JsonArray ChiamaContenuto(string nome, JsonObject? args)
    {
        if (nome != "mostra_volto") return new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Chiama(nome, args) });

        args ??= new JsonObject();
        var massimo = Math.Clamp(Int(args, "massimo") ?? VoltiMostrati, 1, MassimoVoltiMostrati);
        var rifs = new List<string>(Lista(args, "volti"));
        if (Testo(args, "gruppo") is { } gruppo)
        {
            var delGruppo = Volti.VoltiDelGruppo(gruppo).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rifs.AddRange(Volti.Esempi(Volti.Leggi().Where(v => delGruppo.Contains(v.Rif)), massimo));
        }
        if (Testo(args, "persona") is { } persona)
        {
            var proposte = Bool(args, "proposte") ?? true;
            var stato = proposte ? StatoPersona.Proposto : StatoPersona.Confermato;
            var suoi = Volti.Leggi()
                .Where(v => v.Stato == stato && string.Equals(v.Persona, persona.Trim(), StringComparison.CurrentCultureIgnoreCase))
                .ToList();
            if (suoi.Count == 0)
                throw new InvalidOperationException($"Nessun volto {(proposte ? "proposto" : "confermato")} per «{persona}» (scrivi il nome completo, come in elenco_persone).");
            rifs.AddRange(Volti.Esempi(suoi, massimo));
        }
        rifs = rifs.Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(massimo).ToList();
        if (rifs.Count == 0) throw new InvalidOperationException("Indica volti, gruppo o persona.");

        var immagini = ImmaginiVolti.PreparaAsync(rifs, Bool(args, "dalla_foto") ?? true).GetAwaiter().GetResult();
        var contenuto = new JsonArray(new JsonObject
        {
            ["type"] = "text",
            ["text"] = Serializza(new
            {
                nota = "Volti nell'ordine delle immagini. Chiedi all'utente chi è: il nome lo dice o lo conferma lui.",
                volti = immagini.Select((i, n) => new { n = n + 1, volto = i.Rif, foto = i.Foto, posizione = i.Posizione, ritaglio = i.DallaFoto ? "dalla foto" : "miniatura salvata" }),
                non_trovati = rifs.Count > immagini.Count ? rifs.Except(immagini.Select(i => i.Rif), StringComparer.OrdinalIgnoreCase) : null,
            }),
        });
        // Più volti: un foglio unico con i numeri sopra, così l'utente risponde «2 = Anna» senza contare le immagini.
        var immaginiDaInviare = immagini.Count > 1 && (Bool(args, "foglio") ?? true)
            ? [ImmaginiVolti.FoglioAsync(immagini).GetAwaiter().GetResult()]
            : immagini.Select(i => i.Jpeg).ToList();
        foreach (var jpeg in immaginiDaInviare)
            contenuto.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(jpeg), ["mimeType"] = "image/jpeg" });
        return contenuto;
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

            case "ultime_attivita":
                return Serializza(Ricerca.UltimeAttivita(Int(args, "limite") ?? 50, Testo(args, "tipo")).Select(e => new
                {
                    quando = e.Quando.ToString("yyyy-MM-dd HH:mm:ss"), tipo = e.Tipo, dettaglio = e.Dettaglio, errore = e.Errore,
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

            case "analizza_cartella":
            {
                var stato = AnalisiCartella.Avvia(Testo(args, "cartella") ?? "", Int(args, "massimo_foto"));
                return Serializza(new
                {
                    avviata = stato.Cartella, catalogo = stato.Catalogo,
                    nota = "L'analisi gira in background: controlla con stato_analisi. Le foto già analizzate vengono saltate.",
                });
            }

            case "stato_analisi":
            {
                var fermata = Bool(args, "ferma") == true && AnalisiCartella.Ferma();
                if (AnalisiCartella.Stato() is not { } s) return Serializza(new { nota = "Nessuna analisi avviata." });
                var abbandonata = !s.Finita && s.Processo != Environment.ProcessId && DateTime.Now - s.Aggiornato > TimeSpan.FromMinutes(5);
                return Serializza(new
                {
                    cartella = s.Cartella, catalogo = s.Catalogo, fase = s.Fase, fatte = s.Fatte, totale = s.Totale,
                    volti_trovati = s.Volti, non_lette = s.Errori, avanzamento = s.Avanzamento, finita = s.Finita, errore = s.Errore,
                    iniziata = s.Inizio.ToString("yyyy-MM-dd HH:mm"), aggiornata = s.Aggiornato.ToString("yyyy-MM-dd HH:mm:ss"),
                    altro_processo = s.Processo != Environment.ProcessId ? true : (bool?)null,
                    jpeg_anomali = s.JpegAnomali > 0 ? s.JpegAnomali : (int?)null,
                    elenco_jpeg_anomali = s.JpegAnomali > 0 ? AnalisiCartella.FileJpegAnomali : null,
                    nota = fermata ? "Fermata: riprende dalla stessa foto con analizza_cartella."
                        : abbandonata ? "Nessun aggiornamento da più di 5 minuti: il processo che la faceva si è probabilmente chiuso. Rilancia analizza_cartella per riprendere."
                        : s.JpegAnomali > 0 ? $"Attenzione: {s.JpegAnomali} JPEG hanno un'intestazione anomala (Windows li legge lo stesso ma segnala «Invalid SOS parameters for sequential JPEG»). Elenco in {AnalisiCartella.FileJpegAnomali}. Chiedi all'utente se vuole risolverli."
                        : null,
                });
            }

            case "volti_da_nominare":
            {
                var cartella = Testo(args, "cartella");
                var gruppi = Volti.GruppiSenzaNome(cartella, Int(args, "minimo_volti") ?? 2);
                var limite = Math.Clamp(Int(args, "limite") ?? LimiteGruppi, 1, 200);
                return Serializza(new
                {
                    gruppi_totali = gruppi.Count,
                    volti_nei_gruppi = gruppi.Sum(g => g.Volti.Count),
                    gruppi = gruppi.Take(limite).Select(g => new
                    {
                        gruppo = g.Id, foto = g.Foto, volti = g.Volti.Count,
                        dal = g.Dal?.ToString("yyyy-MM-dd"), al = g.Al?.ToString("yyyy-MM-dd"),
                        cartelle = g.Cartelle.Select(c => new { cartella = c.Cartella, foto = c.Foto }),
                        esempi = g.Esempi,
                        forse = g.SimileA is { } p ? new { persona = p.Nome, somiglianza = p.Somiglianza } : null,
                    }),
                    proposte_da_confermare = Volti.Proposte(cartella).Select(p => new { persona = p.Persona, volti = p.Volti, foto = p.Foto, esempi = p.Esempi }),
                    nota = "«forse» è solo la persona nota più somigliante, sotto la soglia delle proposte: va verificata guardando i volti.",
                });
            }

            case "assegna_nome":
            {
                var esito = NomiVolti.AssegnaNome(Testo(args, "nome") ?? "", VoltiScelti(args), Testo(args, "soprannome"), Testo(args, "chi_e"));
                return Serializza(new
                {
                    operazione = esito.Operazione, persona = esito.Persona, volti_nominati = esito.Volti, proposte_nuove = esito.ProposteNuove,
                    nota = esito.ProposteNuove > 0
                        ? $"Il riconoscimento propone «{esito.Persona}» su altri {esito.ProposteNuove} volti: guardali con mostra_volto (persona) e conferma con conferma_volti."
                        : "Nessun altro volto somigliante da proporre per ora.",
                });
            }

            case "conferma_volti":
            {
                var esito = NomiVolti.Conferma(Testo(args, "persona") ?? "", Lista(args, "volti"), Bool(args, "tutte") ?? false);
                return Serializza(new { operazione = esito.Operazione, persona = esito.Persona, volti_confermati = esito.Volti, proposte_nuove = esito.ProposteNuove });
            }

            case "ignora_volti":
            {
                var esito = NomiVolti.Ignora(VoltiScelti(args));
                return Serializza(new { operazione = esito.Operazione, volti_ignorati = esito.Volti });
            }

            case "annulla_volti":
                if (Bool(args, "elenco") == true)
                    return Serializza(NomiVolti.Operazioni().AsEnumerable().Reverse().Select(o => new
                    {
                        operazione = o.Numero, quando = o.Quando.ToString("yyyy-MM-dd HH:mm"), descrizione = o.Descrizione,
                        volti = o.Volti.Count, annullata = o.Annullata,
                    }));
                return NomiVolti.Annulla(Int(args, "operazione"));

            case "importa_picasa":
            {
                var nomi = args["nomi"] is JsonObject o
                    ? o.Where(kv => kv.Value is JsonValue).ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>())
                    : [];
                var (a, esito) = NomiVolti.ImportaPicasa(Testo(args, "cartella") ?? "", nomi, Bool(args, "esegui") ?? false);
                var limite = Math.Clamp(Int(args, "limite") ?? 60, 1, 1000);
                return Serializza(new
                {
                    file_picasa = a.Piano.FileIni, volti_picasa = a.Piano.VoltiPicasa,
                    abbinati = a.Piano.Abbinati.Count, da_nominare = a.DaNominare, gia_confermati = a.GiaConfermati,
                    senza_nome_in_picasa = a.Piano.SenzaNome, non_trovati_da_wfastpic = a.Piano.NonTrovati,
                    in_foto_non_analizzate = a.Piano.InFotoNonAnalizzate,
                    cartelle_da_analizzare = a.Piano.CartelleNonAnalizzate.OrderByDescending(c => c.Value).Take(20)
                        .Select(c => new { cartella = c.Key, volti_picasa = c.Value }),
                    persone = a.Nomi.Count,
                    nomi = a.Nomi.Take(limite).Select(n => new { picasa = n.Picasa, diventa = n.DaUsare == n.Picasa ? null : n.DaUsare ?? "(saltato)", volti = n.Volti }),
                    email_da_decidere = a.Email.Count > 0 ? a.Email : null,
                    forse_la_stessa_persona = a.ForseUguali.Count > 0 ? a.ForseUguali.Select(f => new[] { f.A, f.B }) : null,
                    operazione = esito?.Operazione, volti_nominati = esito?.Volti, proposte_nuove = esito?.ProposteNuove,
                    nota = esito is not null
                        ? $"Fatto: {esito.Volti} volti cambiati, {esito.ProposteNuove} proposte nuove nel resto dell'archivio. Si annulla con annulla_volti operazione={esito.Operazione}."
                        : "Anteprima: niente è stato scritto. Fai decidere all'utente email e nomi forse uguali, poi richiama con esegui=true (e nomi).",
                });
            }

            case "luoghi_frequenti":
            {
                double? Num(string chiave) => args[chiave] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
                var foto = Ricerca.Cerca(new FiltriFoto(Cartella: Testo(args, "cartella"), Dal: Data(args, "dal"), Al: Data(args, "al")));
                (double, double)? casa = Num("casa_lat") is { } cla && Num("casa_lon") is { } clo ? (cla, clo) : null;
                var luoghi = LuoghiFrequenti.Calcola(
                    foto, Math.Max(50, Int(args, "raggio_m") ?? 300), Math.Max(1, Int(args, "minimo_anni") ?? 2),
                    Math.Max(1, Int(args, "minimo_giorni") ?? 2), casa, Num("escludi_km") ?? 0);
                var limite = Math.Clamp(Int(args, "limite") ?? 30, 1, 200);
                return Serializza(new
                {
                    foto_con_gps = foto.Count(f => f.Latitudine is not null),
                    luoghi_trovati = luoghi.Count,
                    luoghi = luoghi.Take(limite).Select(l => new
                    {
                        lat = l.Latitudine, lon = l.Longitudine, nome = l.Nome, foto = l.Foto, anni = l.Anni, giorni = l.Giorni,
                        primo = l.Primo.ToString("yyyy-MM-dd"), ultimo = l.Ultimo.ToString("yyyy-MM-dd"), esempio = l.Esempio,
                    }),
                });
            }

            case "persone_nel_giorno":
            {
                var giorno = Data(args, "giorno");
                var dal = giorno ?? Data(args, "dal") ?? throw new InvalidOperationException("Indica giorno oppure dal (e al).");
                var al = giorno ?? Data(args, "al") ?? dal;
                if (al < dal) throw new InvalidOperationException("«al» viene prima di «dal».");
                var perPersona = Math.Max(0, Int(args, "foto_per_persona") ?? 5);
                return Serializza(Volti.PersoneNeiGiorni(dal, al, Testo(args, "cartella"), Bool(args, "anche_proposte") ?? false).Select(g => new
                {
                    giorno = g.Giorno.ToString("yyyy-MM-dd"), foto_con_volti = g.FotoConVolti,
                    persone = g.Persone.Select(p => new
                    {
                        nome = p.Nome, foto = p.Foto.Count, solo_proposta = p.SoloProposta ? true : (bool?)null,
                        percorsi = perPersona > 0 ? p.Foto.Take(perPersona) : null,
                    }),
                    volti_senza_nome = g.VoltiSenzaNome,
                    esempi_senza_nome = g.EsempiSenzaNome.Count > 0 ? g.EsempiSenzaNome : null,
                }));
            }

            default:
                throw new InvalidOperationException($"Strumento sconosciuto: {nome}");
        }
    }

    /// <summary>I volti del gruppo (meno quelli da escludere) più quelli indicati uno per uno.</summary>
    private static List<string> VoltiScelti(JsonObject args)
    {
        var escludi = Lista(args, "escludi").Select(r => r.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rifs = new List<string>();
        if (Testo(args, "gruppo") is { } gruppo) rifs.AddRange(Volti.VoltiDelGruppo(gruppo));
        rifs.AddRange(Lista(args, "volti").Select(r => r.Trim()));
        return rifs.Where(r => !escludi.Contains(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
