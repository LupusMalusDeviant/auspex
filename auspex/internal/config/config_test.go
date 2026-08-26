package config

import (
	"encoding/json"
	"strings"
	"testing"

	"gopkg.in/yaml.v3"
)

func TestAddressesAcceptsScalarAndList(t *testing.T) {
	cases := []struct {
		name string
		in   string
		want []string
	}{
		{"a single address", "udp: \"127.0.0.1:53\"\n", []string{"127.0.0.1:53"}},
		{"list", "udp:\n  - \"192.168.1.61:53\"\n  - \"100.64.0.5:53\"\n",
			[]string{"192.168.1.61:53", "100.64.0.5:53"}},
		{"empty", "udp: \"\"\n", nil},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			var l Listen
			if err := yaml.Unmarshal([]byte(c.in), &l); err != nil {
				t.Fatal(err)
			}
			if len(l.UDP) != len(c.want) {
				t.Fatalf("UDP = %v, expected %v", l.UDP, c.want)
			}
			for i := range c.want {
				if l.UDP[i].Addr != c.want[i] {
					t.Errorf("UDP[%d] = %q, expected %q", i, l.UDP[i].Addr, c.want[i])
				}
				if l.UDP[i].Optional {
					t.Errorf("UDP[%d] became optional by itself", i)
				}
			}
		})
	}
}

func TestValidateRejectsAddressWithoutPort(t *testing.T) {
	cfg := Default()
	cfg.Listen.UDP = Bind("192.168.1.61")

	if err := cfg.Validate(); err == nil {
		t.Error("an address without a port has to be rejected - otherwise the listener is the first to fail")
	}
}

func TestValidateAcceptsSeveralAddresses(t *testing.T) {
	cfg := Default()
	cfg.Listen.UDP = Bind("192.168.1.61:53", "[::1]:53")
	cfg.Listen.TCP = Bind("192.168.1.61:53")

	if err := cfg.Validate(); err != nil {
		t.Errorf("valid addresses rejected: %v", err)
	}
}

// The default must not listen on every address: on a host with a global
// IPv6 address the result would be an open resolver.
func TestDefaultDoesNotBindToWildcard(t *testing.T) {
	for _, addr := range Default().Listen.UDP {
		if addr.Addr == "0.0.0.0:53" || addr.Addr == ":53" || addr.Addr == "[::]:53" {
			t.Errorf("the default binds to %q", addr.Addr)
		}
	}
}

// The control plane sends device profiles as JSON. If a field is missing its
// JSON name the decoder rejects it - with a message nobody connects to the
// cause ("unknown field block_rules"). That is exactly what happened when
// the browser extension tried to write its first exception. The fault had
// been in there longer: the devices page in the dashboard would have
// triggered it too, as soon as somebody set a rule there.
func TestClientTakesTheNamesItIsSent(t *testing.T) {
	roh := `{
		"name": "Arbeitsrechner",
		"match": ["192.168.1.43"],
		"macs": ["00:00:5e:00:53:0e"],
		"policy": "open",
		"filtering": true,
		"block_rules": ["||nasty.example^"],
		"allow_rules": ["@@||good.example^"],
		"block_services": ["tiktok"],
		"schedules": []
	}`

	dec := json.NewDecoder(strings.NewReader(roh))
	dec.DisallowUnknownFields()

	var c Client
	if err := dec.Decode(&c); err != nil {
		t.Fatalf("profile not readable: %v", err)
	}

	if c.Name != "Arbeitsrechner" {
		t.Errorf("Name: %q", c.Name)
	}
	if len(c.Macs) != 1 || c.Macs[0] != "00:00:5e:00:53:0e" {
		t.Errorf("Macs: %v", c.Macs)
	}
	if len(c.BlockRules) != 1 || c.BlockRules[0] != "||nasty.example^" {
		t.Errorf("BlockRules: %v", c.BlockRules)
	}
	if len(c.AllowRules) != 1 || c.AllowRules[0] != "@@||good.example^" {
		t.Errorf("AllowRules: %v", c.AllowRules)
	}
	if c.Filtering == nil || !*c.Filtering {
		t.Errorf("Filtering: %v", c.Filtering)
	}
}

// The long form is what marks an address as optional. Both spellings have to
// work side by side in one list — a configuration in which the ordinary case
// suddenly needs ceremony is one nobody keeps tidy.
func TestAnAddressCanBeMarkedOptional(t *testing.T) {
	in := `
udp:
  - "192.168.1.61:53"
  - address: "100.64.0.5:53"
    optional: true
`
	var l Listen
	if err := yaml.Unmarshal([]byte(in), &l); err != nil {
		t.Fatal(err)
	}
	if len(l.UDP) != 2 {
		t.Fatalf("%d addresses, expected 2: %+v", len(l.UDP), l.UDP)
	}
	if l.UDP[0].Addr != "192.168.1.61:53" || l.UDP[0].Optional {
		t.Errorf("the short form became %+v", l.UDP[0])
	}
	if l.UDP[1].Addr != "100.64.0.5:53" || !l.UDP[1].Optional {
		t.Errorf("the long form became %+v", l.UDP[1])
	}
}

// An optional address is still an address: a typo in it has to be caught at
// startup, exactly like everywhere else.
func TestAnOptionalAddressIsValidatedToo(t *testing.T) {
	cfg := Default()
	cfg.Listen.UDP = append(Bind("127.0.0.1:53"), Address{Addr: "100.64.0.5", Optional: true})

	if err := cfg.Validate(); err == nil {
		t.Error("an optional address without a port was accepted")
	}
}

// The trap the fatal listener exists to prevent: if every address may fail,
// Auspex can come up, bind nothing, report itself healthy and answer not a
// single query.
func TestAConfigurationOfNothingButOptionalAddressesIsRefused(t *testing.T) {
	cfg := Default()
	cfg.Listen.UDP = Addresses{{Addr: "127.0.0.1:53", Optional: true}}
	cfg.Listen.TCP = Addresses{{Addr: "127.0.0.1:53", Optional: true}}

	err := cfg.Validate()
	if err == nil {
		t.Fatal("a configuration in which every listener may fail was accepted")
	}
	if !strings.Contains(err.Error(), "optional") {
		t.Errorf("the message does not name the cause: %v", err)
	}
}

// And the case that has to keep working: one required, one optional.
func TestOneRequiredAddressIsEnough(t *testing.T) {
	cfg := Default()
	cfg.Listen.UDP = append(Bind("192.168.1.61:53"), Address{Addr: "100.64.0.5:53", Optional: true})
	cfg.Listen.TCP = Bind("192.168.1.61:53")

	if err := cfg.Validate(); err != nil {
		t.Errorf("a valid mixture was rejected: %v", err)
	}
}
