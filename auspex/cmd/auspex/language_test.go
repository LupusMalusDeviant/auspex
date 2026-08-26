package main

import (
	"io/fs"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// The control plane has had a test like this since 0.9.0. The Go side did
// not, and it showed: sixteen German log messages, four API error texts and
// two half-translated explanation strings survived every word-list sweep of
// that release, and were only found by starting the binary and reading what
// it printed.
//
// Umlauts and eszett are a crude but reliable indicator; the word list
// underneath catches the German that happens to be spelt without one.
// Neither is complete, and neither has to be — this is a net, not a proof.

var germanMarkers = []string{"ä", "ö", "ü", "ß", "Ä", "Ö", "Ü"}

// Words that are German and are not also English or a proper noun. Kept
// short deliberately: a long list produces false alarms, and a test that
// cries wolf gets switched off.
var germanWords = []string{
	"fehlt", "fehler", "fehlgeschlagen", "haengt", "ungueltig", "unbekannt",
	"lauscht", "beende", "gesperrt", "erlaubt", "geraet", "anfrage",
	"antwort", "aufloesung", "meldung", "regelsatz", "zeichen", "loeschen",
	"speichern", "abbrechen", "ersatzbank", "fehlversuch", "konflikte",
	"beispiele", "adresse", "netz", "protokoll", "nachbarn", "ziel",
	"lernspeicher", "deaktiviert", "aktiviert",
	// German function words. Unambiguous: none of these is also an
	// English word, so they can be matched without producing noise.
	"fuer", "ueber", "nicht", "oder", "und", "wenn", "wird", "sind",
	"eine", "einen", "einem", "diese", "dieser", "dass", "auch", "noch",
	"schon", "aber", "alle", "allen", "ohne", "gegen", "zwischen",
	"sowie", "daher", "damit", "weil", "beim", "vom", "zum", "zur",
	// Verb and noun stems that turned up as identifiers.
	"liefert", "macht", "holt", "baut", "prueft", "schickt", "laeuft",
	"steht", "zeigt", "kennt", "vorhanden", "warme", "uebernommen",
	"geraete", "geraetenamen", "bekannten", "fasst", "bildet",
}

// Files where German is the point rather than an oversight. Each one is in
// the table in docs/codemap.md, with its reason.
var germanIsAllowedIn = []string{
	// The list descriptions are the German fallback; the control plane
	// translates them through Strings.ListDescription.
	filepath.Join("internal", "lists", "catalog.go"),
	// "täglich", "werktags", "wochenende" are accepted configuration words,
	// next to their English equivalents.
	filepath.Join("internal", "resolver", "policy.go"),
	// This file names the German it is looking for.
	filepath.Join("cmd", "auspex", "language_test.go"),
}

func TestNoGermanLeftInTheGoSource(t *testing.T) {
	root := filepath.Join("..", "..")

	err := filepath.WalkDir(root, func(path string, d fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if d.IsDir() {
			// path, not d.Name(): the walk starts at "..", whose name begins
			// with a dot. Checking the name skipped the root and therefore
			// the entire tree — and a test that walks nothing passes.
			if path == root {
				return nil
			}
			if d.Name() == "var" || d.Name() == "vendor" || strings.HasPrefix(d.Name(), ".") {
				return fs.SkipDir
			}
			return nil
		}
		if filepath.Ext(path) != ".go" {
			return nil
		}

		rel, relErr := filepath.Rel(root, path)
		if relErr != nil {
			return relErr
		}
		for _, allowed := range germanIsAllowedIn {
			if rel == allowed {
				return nil
			}
		}

		raw, readErr := readFile(path)
		if readErr != nil {
			return readErr
		}
		for i, line := range strings.Split(raw, "\n") {
			lower := strings.ToLower(line)
			for _, marker := range germanMarkers {
				if strings.Contains(line, marker) {
					t.Errorf("%s:%d contains %q: %s", rel, i+1, marker, strings.TrimSpace(line))
				}
			}
			for _, word := range germanWords {
				if containsWord(lower, word) {
					t.Errorf("%s:%d contains the German word %q: %s",
						rel, i+1, word, strings.TrimSpace(line))
				}
			}
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
}

// containsWord matches whole words only. Without this "netz" fires on
// "Netzwerk" but also on nothing at all useful, and "ziel" fires inside
// every English word that happens to contain those four letters.
func containsWord(line, word string) bool {
	for i := 0; i+len(word) <= len(line); i++ {
		if line[i:i+len(word)] != word {
			continue
		}
		if i > 0 && isWordByte(line[i-1]) {
			continue
		}
		if i+len(word) < len(line) && isWordByte(line[i+len(word)]) {
			continue
		}
		return true
	}
	return false
}

func isWordByte(b byte) bool {
	return b >= 'a' && b <= 'z' || b >= 'A' && b <= 'Z' || b >= '0' && b <= '9' || b == '_'
}

func readFile(path string) (string, error) {
	b, err := os.ReadFile(path)
	return string(b), err
}
