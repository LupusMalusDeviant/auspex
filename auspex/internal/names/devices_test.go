package names

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

// The MAC-to-name file comes from the control plane and can be half
// written, broken or absent at any moment. None of these cases may disturb
// the resolver - at worst a device stays nameless, and that harms nobody.

func write(t *testing.T, content string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "devices.json")
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestNamesAreRead(t *testing.T) {
	p := write(t, `{"00:00:5e:00:53:0e":"Arbeitsrechner","00:00:5e:00:53:c1":"FireTV"}`)
	d := NewDeviceNames(p, time.Minute)

	if got := d.Name("00:00:5e:00:53:0e"); got != "Arbeitsrechner" {
		t.Errorf("expected Arbeitsrechner, got %q", got)
	}
	if d.Len() != 2 {
		t.Errorf("expected 2 names, got %d", d.Len())
	}
}

func TestMacCaseDoesNotMatter(t *testing.T) {
	// The router reports upper case, the kernel's neighbour table lower.
	// Both have to hit the same row.
	p := write(t, `{"00:00:5E:00:53:0E":"Arbeitsrechner"}`)
	d := NewDeviceNames(p, time.Minute)

	for _, mac := range []string{"00:00:5e:00:53:0e", "00:00:5E:00:53:0E", "00:00:5E:00:53:0e"} {
		if got := d.Name(mac); got != "Arbeitsrechner" {
			t.Errorf("%s: expected Arbeitsrechner, got %q", mac, got)
		}
	}
}

func TestAnUnknownMacReturnsEmpty(t *testing.T) {
	p := write(t, `{"00:00:5e:00:53:0e":"Arbeitsrechner"}`)
	d := NewDeviceNames(p, time.Minute)

	if got := d.Name("aa:bb:cc:dd:ee:ff"); got != "" {
		t.Errorf("expected empty, got %q", got)
	}
}

func TestAMissingFileIsHarmless(t *testing.T) {
	d := NewDeviceNames(filepath.Join(t.TempDir(), "gibtsnicht.json"), time.Minute)
	if got := d.Name("00:00:5e:00:53:0e"); got != "" {
		t.Errorf("expected empty, got %q", got)
	}
	if d.Len() != 0 {
		t.Errorf("expected 0, got %d", d.Len())
	}
}

func TestABrokenFileDoesNotDropTheState(t *testing.T) {
	// The case from real life: the control plane is writing, the resolver
	// is reading. What it sees then is half a JSON document. Emptying what
	// we have because of that would be the worst of all reactions.
	p := write(t, `{"00:00:5e:00:53:0e":"Arbeitsrechner"}`)
	d := NewDeviceNames(p, time.Millisecond)

	if d.Name("00:00:5e:00:53:0e") != "Arbeitsrechner" {
		t.Fatal("precondition: the name has to be there first")
	}

	if err := os.WriteFile(p, []byte(`{"00:00:5e:00:53:0e":"Lup`), 0o644); err != nil {
		t.Fatal(err)
	}
	time.Sleep(5 * time.Millisecond)
	d.load()

	if got := d.Name("00:00:5e:00:53:0e"); got != "Arbeitsrechner" {
		t.Errorf("after a broken file, expected Arbeitsrechner, got %q", got)
	}
}

func TestEmptyNamesAreSkipped(t *testing.T) {
	p := write(t, `{"00:00:5e:00:53:0e":"","aa:bb:cc:dd:ee:ff":"  ","11:22:33:44:55:66":"Echo"}`)
	d := NewDeviceNames(p, time.Minute)

	if d.Len() != 1 {
		t.Errorf("expected 1 usable name, got %d", d.Len())
	}
	if d.Name("11:22:33:44:55:66") != "Echo" {
		t.Error("the one real name is missing")
	}
}

func TestWithoutAFileEverythingIsEmpty(t *testing.T) {
	var d *DeviceNames
	if got := d.Name("00:00:5e:00:53:0e"); got != "" {
		t.Errorf("a store that was never set up has to answer empty, got %q", got)
	}
	if d.Len() != 0 {
		t.Error("Len on nil has to be 0")
	}
}
