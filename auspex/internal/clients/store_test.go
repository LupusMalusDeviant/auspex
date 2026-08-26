package clients

import (
	"testing"

	"auspex/internal/config"
)

func TestPutAndPersistence(t *testing.T) {
	dir := t.TempDir()
	store, err := Open(dir)
	if err != nil {
		t.Fatal(err)
	}
	if err := store.Put(config.Client{
		Name:          "kinder-tablet",
		Match:         []string{"192.168.1.50"},
		BlockServices: []string{"tiktok"},
	}); err != nil {
		t.Fatal(err)
	}

	wieder, err := Open(dir)
	if err != nil {
		t.Fatal(err)
	}
	all := wieder.All()
	if len(all) != 1 || all[0].Name != "kinder-tablet" || len(all[0].BlockServices) != 1 {
		t.Fatalf("nach Neustart: %+v", all)
	}
}

func TestIncompleteProfilesAreRejected(t *testing.T) {
	store, _ := Open(t.TempDir())

	if err := store.Put(config.Client{Match: []string{"10.0.0.1"}}); err == nil {
		t.Error("a profile without a name has to be rejected")
	}
	if err := store.Put(config.Client{Name: "no-address"}); err == nil {
		t.Error("a profile without an address has to be rejected")
	}
}

// The same check as at startup: a profile from the browser must not be
// treated more leniently than one from the file.
func TestBadInputIsRejected(t *testing.T) {
	store, _ := Open(t.TempDir())

	err := store.Put(config.Client{
		Name: "tippfehler", Match: []string{"10.0.0.1"}, BlockServices: []string{"tikttok"},
	})
	if err == nil {
		t.Error("an unknown service has to be rejected")
	}

	err = store.Put(config.Client{
		Name: "falschepolicy", Match: []string{"10.0.0.1"}, Policy: "irgendwas",
	})
	if err == nil {
		t.Error("an unknown policy has to be rejected")
	}
}

// The configuration file belongs to the operator: a click in the browser
// must not be able to override a line in it.
func TestConfigurationWinsOnANameClash(t *testing.T) {
	store, _ := Open(t.TempDir())
	_ = store.Put(config.Client{Name: "arbeit", Match: []string{"10.0.0.99"}})
	_ = store.Put(config.Client{Name: "managed-only", Match: []string{"10.0.0.50"}})

	mixed := store.Merge([]config.Client{
		{Name: "arbeit", Match: []string{"192.168.1.20"}},
	})

	if len(mixed) != 2 {
		t.Fatalf("Ergebnis: %+v", mixed)
	}
	if mixed[0].Name != "arbeit" || mixed[0].Match[0] != "192.168.1.20" {
		t.Error("the profile from the configuration should have won")
	}
}

func TestRemove(t *testing.T) {
	store, _ := Open(t.TempDir())
	_ = store.Put(config.Client{Name: "gone", Match: []string{"10.0.0.1"}})

	if ok, _ := store.Remove("gone"); !ok {
		t.Error("removing should return true")
	}
	if ok, _ := store.Remove("gone"); ok {
		t.Error("removing a second time should return false")
	}
	if len(store.All()) != 0 {
		t.Error("profile still there")
	}
}
