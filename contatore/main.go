// Contatore di visite del sito di WFastPic.
//
// Riceve un ping dalle pagine (POST /api/visita) e lo scrive in MySQL; mostra i numeri su
// /statistiche/ protetta da password. Niente cookie e niente dati personali: l'IP serve solo a
// calcolare, insieme allo user agent e a un sale casuale che cambia ogni giorno, un hash anonimo
// che distingue i visitatori di quel giorno e non si può ricollegare né a una persona né ai giorni
// successivi (come Plausible).
//
// Configurazione da variabili d'ambiente (in produzione da un EnvironmentFile fuori dal repo):
//
//	CONTATORE_DSN       utente:password@tcp(127.0.0.1:3306)/wfastpic   (obbligatoria)
//	CONTATORE_PASSWORD  password della pagina /statistiche/             (obbligatoria)
//	CONTATORE_ADDR      indirizzo di ascolto, predefinito 127.0.0.1:8090
package main

import (
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"html/template"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"strings"
	"sync"
	"time"
	_ "time/tzdata"

	_ "github.com/go-sql-driver/mysql"
)

// Le sole pagine contate: tutto il resto viene ignorato, così nessuno riempie la tabella di spazzatura.
var pagineValide = func() map[string]bool {
	m := map[string]bool{}
	for _, p := range []string{"/", "/it/", "/heic-photos-windows/", "/it/foto-heic-windows/",
		"/picasa-alternative/", "/it/alternativa-picasa/"} {
		m[p], m[p+"index.html"] = true, true
	}
	return m
}()

const (
	nostroSito         = "wfastpic.walterdev.eu"
	maxEventiAlMinuto  = 30
	giorniStatistiche  = 30
	dimensioneMaxCorpo = 2048
)

var fusoOrario = func() *time.Location {
	l, err := time.LoadLocation("Europe/Rome")
	if err != nil {
		return time.UTC
	}
	return l
}()

// ------------------------------------------------------------------ classificazione (pura, testata)

// PaginaNormalizzata riporta "/index.html" a "/" e simili; "" se la pagina non è del sito.
func PaginaNormalizzata(p string) string {
	if !pagineValide[p] {
		return ""
	}
	return strings.TrimSuffix(p, "index.html")
}

// Provenienza tiene solo il dominio del referrer, senza "www."; "" per visite dirette o interne.
func Provenienza(referrer string) string {
	if referrer == "" {
		return ""
	}
	u, err := url.Parse(referrer)
	if err != nil || u.Hostname() == "" {
		return ""
	}
	h := strings.TrimPrefix(strings.ToLower(u.Hostname()), "www.")
	if h == nostroSito {
		return ""
	}
	if len(h) > 100 {
		h = h[:100]
	}
	return h
}

// Lingua riduce "it-IT" a "it"; "" se non sembra un codice di lingua.
func Lingua(l string) string {
	l = strings.ToLower(strings.TrimSpace(l))
	if i := strings.IndexAny(l, "-_"); i >= 0 {
		l = l[:i]
	}
	if len(l) < 2 || len(l) > 3 {
		return ""
	}
	for _, c := range l {
		if c < 'a' || c > 'z' {
			return ""
		}
	}
	return l
}

// SembraBot riconosce crawler e generatori di anteprime (WhatsApp, Facebook, ...): non sono visite.
func SembraBot(ua string) bool {
	u := strings.ToLower(ua)
	if u == "" {
		return true
	}
	for _, s := range []string{"bot", "crawl", "spider", "slurp", "preview", "whatsapp", "facebookexternalhit",
		"headless", "lighthouse", "curl", "wget", "python", "go-http-client", "java/", "httpclient"} {
		if strings.Contains(u, s) {
			return true
		}
	}
	return false
}

// Dispositivo e sistema operativo dallo user agent, a grandi linee.
func Dispositivo(ua string) (dispositivo, sistema string) {
	u := strings.ToLower(ua)
	switch {
	case strings.Contains(u, "ipad") || strings.Contains(u, "tablet"):
		dispositivo = "tablet"
	case strings.Contains(u, "mobi") || strings.Contains(u, "iphone") || strings.Contains(u, "android"):
		dispositivo = "telefono"
	default:
		dispositivo = "computer"
	}
	switch {
	case strings.Contains(u, "windows"):
		sistema = "Windows"
	case strings.Contains(u, "iphone") || strings.Contains(u, "ipad"):
		sistema = "iOS"
	case strings.Contains(u, "android"):
		sistema = "Android"
	case strings.Contains(u, "mac os"):
		sistema = "macOS"
	case strings.Contains(u, "cros"):
		sistema = "ChromeOS"
	case strings.Contains(u, "linux"):
		sistema = "Linux"
	default:
		sistema = "altro"
	}
	return
}

// ------------------------------------------------------------------ visitatore anonimo

// sale casuale del giorno: cambia a mezzanotte e non viene mai salvato.
type sale struct {
	mu     sync.Mutex
	giorno string
	valore []byte
}

func (s *sale) hash(giorno, ip, ua string) string {
	s.mu.Lock()
	if s.giorno != giorno {
		s.giorno = giorno
		s.valore = make([]byte, 32)
		if _, err := rand.Read(s.valore); err != nil {
			panic(err)
		}
	}
	v := s.valore
	s.mu.Unlock()
	h := sha256.New()
	h.Write(v)
	h.Write([]byte(ip + "\x00" + ua))
	return hex.EncodeToString(h.Sum(nil)[:8])
}

// limite: al massimo maxEventiAlMinuto eventi al minuto per visitatore.
type limite struct {
	mu     sync.Mutex
	minuto int64
	conti  map[string]int
}

func (l *limite) consenti(visitatore string, ora time.Time) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	if m := ora.Unix() / 60; m != l.minuto || l.conti == nil {
		l.minuto, l.conti = m, map[string]int{}
	}
	l.conti[visitatore]++
	return l.conti[visitatore] <= maxEventiAlMinuto
}

// ------------------------------------------------------------------ server

type server struct {
	db       *sql.DB
	password string
	sale     sale
	limite   limite
}

const schema = `CREATE TABLE IF NOT EXISTS eventi (
	id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
	istante     DATETIME     NOT NULL,
	giorno      DATE         NOT NULL,
	tipo        VARCHAR(16)  NOT NULL,
	pagina      VARCHAR(32)  NOT NULL,
	provenienza VARCHAR(100) NOT NULL DEFAULT '',
	lingua      VARCHAR(3)   NOT NULL DEFAULT '',
	dispositivo VARCHAR(16)  NOT NULL DEFAULT '',
	sistema     VARCHAR(16)  NOT NULL DEFAULT '',
	visitatore  CHAR(16)     NOT NULL,
	KEY per_giorno (giorno, tipo)
) DEFAULT CHARSET=utf8mb4`

type ping struct {
	Pagina   string `json:"p"`
	Referrer string `json:"r"`
	Lingua   string `json:"l"`
	Tipo     string `json:"e"`
}

func ipCliente(r *http.Request) string {
	// Ascoltiamo solo su localhost dietro nginx, che mette l'IP vero in X-Real-IP.
	if ip := r.Header.Get("X-Real-IP"); ip != "" {
		return ip
	}
	host, _, _ := net.SplitHostPort(r.RemoteAddr)
	return host
}

func (s *server) visita(w http.ResponseWriter, r *http.Request) {
	w.WriteHeader(http.StatusNoContent) // il browser non aspetta niente: si risponde subito comunque
	corpo, err := io.ReadAll(io.LimitReader(r.Body, dimensioneMaxCorpo))
	if err != nil {
		return
	}
	var p ping
	if json.Unmarshal(corpo, &p) != nil {
		return
	}
	ua := r.UserAgent()
	pagina := PaginaNormalizzata(p.Pagina)
	if pagina == "" || SembraBot(ua) {
		return
	}
	tipo := "visita"
	if p.Tipo == "download" {
		tipo = "download"
	}
	ora := time.Now().In(fusoOrario)
	giorno := ora.Format("2006-01-02")
	visitatore := s.sale.hash(giorno, ipCliente(r), ua)
	if !s.limite.consenti(visitatore, ora) {
		return
	}
	dispositivo, sistema := Dispositivo(ua)
	_, err = s.db.Exec(`INSERT INTO eventi (istante, giorno, tipo, pagina, provenienza, lingua, dispositivo, sistema, visitatore)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		ora.Format("2006-01-02 15:04:05"), giorno, tipo, pagina, Provenienza(p.Referrer), Lingua(p.Lingua), dispositivo, sistema, visitatore)
	if err != nil {
		log.Printf("scrittura evento: %v", err)
	}
}

// ------------------------------------------------------------------ statistiche

type riga struct {
	Etichetta string
	Valore    int
	Percento  int
}

type giornata struct {
	Giorno     string
	Visitatori int
	Visite     int
	Download   int
	Percento   int
}

type pagina struct {
	Aggiornato                                        string
	Giorni                                            int
	Visitatori, Visite, Download                      int
	VisitatoriSempre, DownloadSempre                  int
	PerGiorno                                         []giornata
	Provenienze, Pagine, Lingue, Sistemi, Dispositivi []riga
}

func (s *server) classifica(colonna, dal string) ([]riga, error) {
	// colonna arriva solo dalle costanti qui sotto, mai dalla richiesta.
	rows, err := s.db.Query(`SELECT `+colonna+`, COUNT(DISTINCT giorno, visitatore) n FROM eventi
		WHERE tipo = 'visita' AND giorno >= ? GROUP BY `+colonna+` ORDER BY n DESC LIMIT 15`, dal)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []riga
	max := 0
	for rows.Next() {
		var r riga
		if err := rows.Scan(&r.Etichetta, &r.Valore); err != nil {
			return nil, err
		}
		if r.Etichetta == "" {
			r.Etichetta = "(diretta o sconosciuta)"
		}
		if r.Valore > max {
			max = r.Valore
		}
		out = append(out, r)
	}
	for i := range out {
		out[i].Percento = out[i].Valore * 100 / max
	}
	return out, rows.Err()
}

func (s *server) statistiche(w http.ResponseWriter, r *http.Request) {
	_, pw, ok := r.BasicAuth()
	if !ok || subtle.ConstantTimeCompare([]byte(pw), []byte(s.password)) != 1 {
		w.Header().Set("WWW-Authenticate", `Basic realm="Statistiche WFastPic", charset="UTF-8"`)
		http.Error(w, "Serve la password", http.StatusUnauthorized)
		return
	}
	ora := time.Now().In(fusoOrario)
	dal := ora.AddDate(0, 0, -(giorniStatistiche - 1)).Format("2006-01-02")
	p := pagina{Aggiornato: ora.Format("02/01/2006 15:04"), Giorni: giorniStatistiche}

	err := s.db.QueryRow(`SELECT COUNT(DISTINCT CASE WHEN tipo='visita' THEN CONCAT(giorno, visitatore) END),
		COALESCE(SUM(tipo='visita'), 0), COALESCE(SUM(tipo='download'), 0) FROM eventi WHERE giorno >= ?`, dal).
		Scan(&p.Visitatori, &p.Visite, &p.Download)
	if err == nil {
		err = s.db.QueryRow(`SELECT COUNT(DISTINCT CASE WHEN tipo='visita' THEN CONCAT(giorno, visitatore) END),
			COALESCE(SUM(tipo='download'), 0) FROM eventi`).Scan(&p.VisitatoriSempre, &p.DownloadSempre)
	}
	if err != nil {
		log.Printf("statistiche: %v", err)
		http.Error(w, "Errore del database", http.StatusInternalServerError)
		return
	}

	rows, err := s.db.Query(`SELECT DATE_FORMAT(giorno, '%Y-%m-%d'),
		COUNT(DISTINCT CASE WHEN tipo='visita' THEN visitatore END), SUM(tipo='visita'), SUM(tipo='download')
		FROM eventi WHERE giorno >= ? GROUP BY giorno`, dal)
	if err != nil {
		log.Printf("statistiche per giorno: %v", err)
		http.Error(w, "Errore del database", http.StatusInternalServerError)
		return
	}
	perGiorno := map[string]giornata{}
	for rows.Next() {
		var g giornata
		if rows.Scan(&g.Giorno, &g.Visitatori, &g.Visite, &g.Download) == nil {
			perGiorno[g.Giorno] = g
		}
	}
	rows.Close()
	max := 1
	for _, g := range perGiorno {
		if g.Visitatori > max {
			max = g.Visitatori
		}
	}
	for i := 0; i < giorniStatistiche; i++ { // anche i giorni a zero, dal più recente
		d := ora.AddDate(0, 0, -i)
		g := perGiorno[d.Format("2006-01-02")]
		g.Giorno = d.Format("Mon 02/01")
		g.Percento = g.Visitatori * 100 / max
		p.PerGiorno = append(p.PerGiorno, g)
	}

	for _, c := range []struct {
		colonna string
		dove    *[]riga
	}{{"provenienza", &p.Provenienze}, {"pagina", &p.Pagine}, {"lingua", &p.Lingue}, {"sistema", &p.Sistemi}, {"dispositivo", &p.Dispositivi}} {
		if *c.dove, err = s.classifica(c.colonna, dal); err != nil {
			log.Printf("classifica %s: %v", c.colonna, err)
		}
	}

	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("X-Robots-Tag", "noindex")
	if err := modello.Execute(w, p); err != nil {
		log.Printf("pagina statistiche: %v", err)
	}
}

var modello = template.Must(template.New("statistiche").Parse(`<!doctype html>
<html lang="it"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex"><title>Statistiche WFastPic</title>
<style>
:root { color-scheme: dark; }
body { margin: 0; padding: 24px 16px 48px; font: 15px/1.5 "Segoe UI", system-ui, sans-serif; background: #151515; color: #f2f2f2; }
main { max-width: 900px; margin: 0 auto; }
h1 { font-size: 26px; margin: 0 0 4px; } h2 { font-size: 18px; margin: 32px 0 10px; }
.nota { color: #8a8a8a; font-size: 13px; margin: 0; }
.numeri { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin-top: 20px; }
.numero { background: #1f1f1f; border: 1px solid #333; border-radius: 12px; padding: 14px 16px; }
.numero b { display: block; font-size: 30px; line-height: 1.1; }
.numero span { color: #b4b4b4; font-size: 13px; }
table { width: 100%; border-collapse: collapse; }
td, th { padding: 5px 8px; text-align: left; border-bottom: 1px solid #2a2a2a; white-space: nowrap; }
th { color: #8a8a8a; font-weight: 600; font-size: 13px; }
td.n { text-align: right; width: 70px; font-variant-numeric: tabular-nums; }
td.barra { width: 50%; } .barra div { height: 10px; border-radius: 5px; background: #4cc2ff; min-width: 2px; }
.zero { color: #555; }
.colonne { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 0 28px; }
</style></head><body><main>
<h1>Statistiche di wfastpic.walterdev.eu</h1>
<p class="nota">Aggiornate al {{.Aggiornato}} · visitatori anonimi, contati una volta al giorno · le tue visite sono escluse se hai aperto il sito con ?nonconta</p>
<div class="numeri">
  <div class="numero"><b>{{.Visitatori}}</b><span>visitatori negli ultimi {{.Giorni}} giorni</span></div>
  <div class="numero"><b>{{.Visite}}</b><span>pagine viste in {{.Giorni}} giorni</span></div>
  <div class="numero"><b>{{.Download}}</b><span>clic su "Scarica" in {{.Giorni}} giorni</span></div>
  <div class="numero"><b>{{.VisitatoriSempre}}</b><span>visitatori da sempre</span></div>
  <div class="numero"><b>{{.DownloadSempre}}</b><span>clic su "Scarica" da sempre</span></div>
</div>
<h2>Giorno per giorno</h2>
<table><tr><th>Giorno</th><th class="n">Visitatori</th><th class="n">Pagine</th><th class="n">Scarica</th><th></th></tr>
{{range .PerGiorno}}<tr{{if eq .Visitatori 0}} class="zero"{{end}}><td>{{.Giorno}}</td><td class="n">{{.Visitatori}}</td><td class="n">{{.Visite}}</td><td class="n">{{.Download}}</td><td class="barra">{{if .Visitatori}}<div style="width:{{.Percento}}%"></div>{{end}}</td></tr>
{{end}}</table>
<div class="colonne">
{{define "classifica"}}<table>{{range .}}<tr><td>{{.Etichetta}}</td><td class="n">{{.Valore}}</td><td class="barra"><div style="width:{{.Percento}}%"></div></td></tr>{{else}}<tr><td class="zero">ancora niente</td></tr>{{end}}</table>{{end}}
<section><h2>Da dove arrivano</h2>{{template "classifica" .Provenienze}}</section>
<section><h2>Pagine</h2>{{template "classifica" .Pagine}}</section>
<section><h2>Lingua del browser</h2>{{template "classifica" .Lingue}}</section>
<section><h2>Sistema</h2>{{template "classifica" .Sistemi}}</section>
<section><h2>Dispositivo</h2>{{template "classifica" .Dispositivi}}</section>
</div>
<p class="nota" style="margin-top:28px">WhatsApp, Telegram e le app in genere non passano la provenienza: quelle visite risultano "dirette".</p>
</main></body></html>`))

// ------------------------------------------------------------------ avvio

func main() {
	dsn, password := os.Getenv("CONTATORE_DSN"), os.Getenv("CONTATORE_PASSWORD")
	if dsn == "" || password == "" {
		log.Fatal("servono CONTATORE_DSN e CONTATORE_PASSWORD")
	}
	addr := os.Getenv("CONTATORE_ADDR")
	if addr == "" {
		addr = "127.0.0.1:8090"
	}
	db, err := sql.Open("mysql", dsn)
	if err != nil {
		log.Fatal(err)
	}
	db.SetMaxOpenConns(4)
	db.SetConnMaxLifetime(5 * time.Minute)
	if _, err := db.Exec(schema); err != nil {
		log.Fatalf("creazione tabella: %v", err)
	}
	s := &server{db: db, password: password}

	mux := http.NewServeMux()
	mux.HandleFunc("POST /api/visita", s.visita)
	mux.HandleFunc("GET /statistiche/", s.statistiche)
	srv := &http.Server{Addr: addr, Handler: mux, ReadTimeout: 10 * time.Second, WriteTimeout: 30 * time.Second}
	log.Printf("contatore in ascolto su %s", addr)
	log.Fatal(srv.ListenAndServe())
}
