package main

import (
	"testing"
	"time"
)

func TestPaginaNormalizzata(t *testing.T) {
	casi := map[string]string{"/": "/", "/index.html": "/", "/it/": "/it/", "/it/index.html": "/it/",
		"/statistiche/": "", "/qualcosa": "", "": "", "/it": "",
		"/heic-photos-windows/": "/heic-photos-windows/", "/it/alternativa-picasa/index.html": "/it/alternativa-picasa/"}
	for in, atteso := range casi {
		if got := PaginaNormalizzata(in); got != atteso {
			t.Errorf("PaginaNormalizzata(%q) = %q, atteso %q", in, got, atteso)
		}
	}
}

func TestProvenienza(t *testing.T) {
	casi := map[string]string{
		"":                                      "",
		"https://www.google.com/":               "google.com",
		"https://www.reddit.com/r/software/abc": "reddit.com",
		"https://wfastpic.walterdev.eu/it/":     "",
		"android-app://com.google.android.gm/":  "com.google.android.gm",
		"non un url":                            "",
	}
	for in, atteso := range casi {
		if got := Provenienza(in); got != atteso {
			t.Errorf("Provenienza(%q) = %q, atteso %q", in, got, atteso)
		}
	}
}

func TestLingua(t *testing.T) {
	casi := map[string]string{"it-IT": "it", "en": "en", "EN_gb": "en", "": "", "x": "", "it<script>": "", "fil-PH": "fil"}
	for in, atteso := range casi {
		if got := Lingua(in); got != atteso {
			t.Errorf("Lingua(%q) = %q, atteso %q", in, got, atteso)
		}
	}
}

func TestSembraBot(t *testing.T) {
	bot := []string{"", "WhatsApp/2.23.20.0 A", "facebookexternalhit/1.1", "Mozilla/5.0 (compatible; Googlebot/2.1)",
		"curl/8.5.0", "Mozilla/5.0 (X11; Linux x86_64) HeadlessChrome/120.0"}
	persone := []string{
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0",
		"Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1",
	}
	for _, ua := range bot {
		if !SembraBot(ua) {
			t.Errorf("SembraBot(%q) = false", ua)
		}
	}
	for _, ua := range persone {
		if SembraBot(ua) {
			t.Errorf("SembraBot(%q) = true", ua)
		}
	}
}

func TestDispositivo(t *testing.T) {
	casi := []struct{ ua, disp, sis string }{
		{"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0", "computer", "Windows"},
		{"Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148", "telefono", "iOS"},
		{"Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)", "tablet", "iOS"},
		{"Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/130.0 Mobile Safari/537.36", "telefono", "Android"},
		{"Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) Safari/605.1.15", "computer", "macOS"},
	}
	for _, c := range casi {
		d, s := Dispositivo(c.ua)
		if d != c.disp || s != c.sis {
			t.Errorf("Dispositivo(%q) = %q, %q; atteso %q, %q", c.ua, d, s, c.disp, c.sis)
		}
	}
}

func TestVisitatoreAnonimo(t *testing.T) {
	var s sale
	a := s.hash("2026-10-11", "1.2.3.4", "ua")
	if a != s.hash("2026-10-11", "1.2.3.4", "ua") {
		t.Error("lo stesso visitatore nello stesso giorno deve avere lo stesso hash")
	}
	if a == s.hash("2026-10-11", "1.2.3.5", "ua") {
		t.Error("visitatori diversi devono avere hash diversi")
	}
	if a == s.hash("2026-10-12", "1.2.3.4", "ua") {
		t.Error("il giorno dopo il sale cambia: lo stesso visitatore non deve essere ricollegabile")
	}
	if len(a) != 16 {
		t.Errorf("hash lungo %d, attesi 16 caratteri (colonna CHAR(16))", len(a))
	}
}

func TestLimite(t *testing.T) {
	var l limite
	ora := time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)
	for i := 0; i < maxEventiAlMinuto; i++ {
		if !l.consenti("v", ora) {
			t.Fatalf("evento %d bloccato troppo presto", i+1)
		}
	}
	if l.consenti("v", ora) {
		t.Error("oltre il limite doveva bloccare")
	}
	if !l.consenti("altro", ora) {
		t.Error("il limite è per visitatore")
	}
	if !l.consenti("v", ora.Add(time.Minute)) {
		t.Error("al minuto dopo si riparte da zero")
	}
}
