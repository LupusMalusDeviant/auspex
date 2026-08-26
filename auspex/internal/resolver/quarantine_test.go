package resolver

import (
	"testing"

	"github.com/miekg/dns"

	"auspex/internal/config"
)

func quarantined(rules ...string) config.Config {
	cfg := config.Default()
	cfg.Clients = []config.Client{{
		Name:       "verdaechtig",
		Match:      []string{"10.0.5.0/24"},
		Policy:     "quarantine",
		AllowRules: rules,
	}}
	return cfg
}

// Quarantine means quarantine: everything, whatever the device may have
// learned before.
func TestAQuarantinedDeviceReachesNothing(t *testing.T) {
	res := resolverWithUpstream(t, quarantined(), &fakeUpstream{})

	for _, name := range []string{"example.com", "update.microsoft.com", "harmlos.example"} {
		msg := query(t, res, "10.0.5.20", name)
		if msg.Rcode != dns.RcodeNameError {
			t.Errorf("%s: rcode = %s, expected NXDOMAIN", name, dns.RcodeToString[msg.Rcode])
		}
	}

	entry := res.QueryLog().Recent(1)[0]
	// Its own list name: "quarantined" and "not learned" mean very different
	// things to whoever reads the log.
	if entry.List != QuarantineListName {
		t.Errorf("list = %q, expected %q", entry.List, QuarantineListName)
	}
}

// Without an escape hatch a quarantined device cannot even reach what it
// needs to be repaired.
func TestAnExplicitAllowRuleStillLifts(t *testing.T) {
	res := resolverWithUpstream(t, quarantined("@@||update.microsoft.com^"), &fakeUpstream{})

	if msg := query(t, res, "10.0.5.20", "update.microsoft.com"); msg.Rcode != dns.RcodeSuccess {
		t.Errorf("the allow rule did not lift the quarantine: %s", dns.RcodeToString[msg.Rcode])
	}
	if msg := query(t, res, "10.0.5.20", "example.com"); msg.Rcode != dns.RcodeNameError {
		t.Error("everything else has to stay blocked")
	}
}

// Other devices must not notice a thing.
func TestQuarantineAppliesOnlyToItsOwnProfile(t *testing.T) {
	res := resolverWithUpstream(t, quarantined(), &fakeUpstream{})

	if msg := query(t, res, "10.0.9.20", "example.com"); msg.Rcode != dns.RcodeSuccess {
		t.Error("a device outside the profile was blocked too")
	}
}

// A quarantine must not need a learning directory. Otherwise the one setting
// meant for an emergency would be the one that refuses to start.
func TestQuarantineNeedsNoLearnStore(t *testing.T) {
	cfg := quarantined()
	profiles, err := compileProfiles(cfg.Clients, cfg.Learning, nil)
	if err != nil {
		t.Fatalf("quarantine without a learning manager failed: %v", err)
	}
	if profiles[0].Learn != nil {
		t.Error("a learn store was created for a quarantine")
	}
}

// The configuration has to accept it, or it cannot be set from the browser.
func TestQuarantineIsAValidPolicy(t *testing.T) {
	cfg := config.Default()
	err := cfg.ValidateClient(config.Client{
		Name: "verdaechtig", Match: []string{"10.0.5.20"}, Policy: "quarantine",
	})
	if err != nil {
		t.Errorf("quarantine was refused: %v", err)
	}
}
