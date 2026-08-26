package lists

import (
	"path/filepath"
	"testing"
)

func TestAddAndPersistence(t *testing.T) {
	dir := t.TempDir()
	store, err := OpenStore(dir)
	if err != nil {
		t.Fatal(err)
	}
	if err := store.Add(Managed{Name: "oisd", URL: "https://big.oisd.nl/", Enabled: true}); err != nil {
		t.Fatal(err)
	}

	wieder, err := OpenStore(dir)
	if err != nil {
		t.Fatal(err)
	}
	all := wieder.All()
	if len(all) != 1 || all[0].Name != "oisd" || !all[0].Enabled {
		t.Fatalf("nach Neustart: %+v", all)
	}
	if _, err := filepath.Abs(dir); err != nil {
		t.Fatal(err)
	}
}

// A path instead of a URL would let the control plane write into the
// resolver's file system - that belongs in the configuration.
func TestOnlyHttpUrls(t *testing.T) {
	store, _ := OpenStore(t.TempDir())

	if err := store.Add(Managed{Name: "lokal", URL: "/etc/passwd"}); err == nil {
		t.Error("a file path has to be rejected")
	}
	if err := store.Add(Managed{Name: "empty", URL: ""}); err == nil {
		t.Error("an empty URL has to be rejected")
	}
	if err := store.Add(Managed{Name: "", URL: "https://example.com/l.txt"}); err == nil {
		t.Error("a missing name has to be rejected")
	}
}

func TestSwitchOffAndOnAgain(t *testing.T) {
	store, _ := OpenStore(t.TempDir())
	_ = store.Add(Managed{Name: "test", URL: "https://example.com/l.txt", Enabled: true})

	ok, err := store.SetEnabled("test", false)
	if err != nil || !ok {
		t.Fatalf("SetEnabled: ok=%v err=%v", ok, err)
	}
	if store.All()[0].Enabled {
		t.Error("the list should be switched off")
	}
	// Switching off must not lose the list.
	if len(store.All()) != 1 {
		t.Error("the list was removed rather than switched off")
	}

	if ok, _ := store.SetEnabled("gibtsnicht", true); ok {
		t.Error("an unknown list should return false")
	}
}

func TestRemove(t *testing.T) {
	store, _ := OpenStore(t.TempDir())
	_ = store.Add(Managed{Name: "gone", URL: "https://example.com/l.txt"})

	if ok, _ := store.Remove("gone"); !ok {
		t.Error("removing should return true")
	}
	if len(store.All()) != 0 {
		t.Error("list still there")
	}
	if ok, _ := store.Remove("gone"); ok {
		t.Error("removing a second time should return false")
	}
}

func TestAsConfigTranslatesForTheLoader(t *testing.T) {
	store, _ := OpenStore(t.TempDir())
	_ = store.Add(Managed{Name: "aus", URL: "https://example.com/a.txt", Enabled: false})
	_ = store.Add(Managed{Name: "an", URL: "https://example.com/b.txt", Enabled: true, Allow: true})

	byName := map[string]bool{}
	for _, c := range store.AsConfig() {
		byName[c.Name] = c.IsEnabled()
		if c.Name == "an" && !c.Allow {
			t.Error("Allow-Kennzeichen ging verloren")
		}
	}
	if byName["aus"] {
		t.Error("a switched-off list came through as active")
	}
	if !byName["an"] {
		t.Error("an active list came through as switched off")
	}
}

func TestTheCatalogueIsUsable(t *testing.T) {
	for _, k := range KnownLists() {
		if k.Name == "" || k.URL == "" || k.Description == "" {
			t.Errorf("incomplete catalogue entry: %+v", k)
		}
	}
}
