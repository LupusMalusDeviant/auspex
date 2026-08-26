package config

import (
	"path/filepath"
	"strings"
	"testing"
)

// The example configuration is the file every installation starts from — and
// until now nothing checked it. It could name a field the code no longer
// reads, or a service that has left the catalogue, and the only person to
// find out would be somebody starting Auspex for the first time.
func TestTheExampleConfigurationLoadsAndValidates(t *testing.T) {
	path := filepath.Join("..", "..", "config.example.yaml")

	cfg, err := Load(path)
	if err != nil {
		t.Fatalf("the example configuration does not load: %v", err)
	}
	if err := cfg.Validate(); err != nil {
		t.Fatalf("the example configuration does not validate: %v", err)
	}
	for _, client := range cfg.Clients {
		if err := cfg.ValidateClient(client); err != nil {
			t.Errorf("client %q: %v", client.Name, err)
		}
	}

	// A file that loaded but is empty would pass everything above.
	if len(cfg.Clients) == 0 {
		t.Fatal("no profiles in the example configuration")
	}
}

// And the part that would be embarrassing rather than merely broken: the
// repository is public. Test data uses 192.168.1.x; the household this was
// built in does not.
func TestTheExampleConfigurationCarriesNoRealAddresses(t *testing.T) {
	cfg, err := Load(filepath.Join("..", "..", "config.example.yaml"))
	if err != nil {
		t.Fatal(err)
	}
	for _, client := range cfg.Clients {
		for _, m := range client.Match {
			if strings.HasPrefix(m, "192.168.178.") {
				t.Errorf("client %q carries a real address: %s", client.Name, m)
			}
		}
	}
}

// The example shows SafeSearch off. If the providers named there ever leave
// the catalogue, the file would be teaching a setting that makes the start
// fail — which is exactly what the validation above catches, but this says
// out loud what is being relied on.
func TestTheExampleShowsSafeSearch(t *testing.T) {
	cfg, err := Load(filepath.Join("..", "..", "config.example.yaml"))
	if err != nil {
		t.Fatal(err)
	}
	for _, client := range cfg.Clients {
		if len(client.SafeSearch) > 0 {
			return
		}
	}
	t.Error("no profile in the example configuration shows safe_search")
}
