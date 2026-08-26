package names

import (
	"encoding/json"
	"os"
	"strings"
	"sync"
	"time"
)

// DeviceNames reads the MAC-to-name mapping that the control plane writes
// out of the router's device list.
//
// The split is deliberate: the control plane talks to the router, the
// resolver does not. Its path through a query must not depend on somebody
// else's device - a Fritz!Box in the middle of a reboot should not stall the
// house's name resolution. The same split as with the rules: discovery and
// writing there, reading only here.
type DeviceNames struct {
	path string
	ttl  time.Duration

	mu      sync.RWMutex
	names   map[string]string
	checked time.Time
	state   time.Time
}

func NewDeviceNames(path string, ttl time.Duration) *DeviceNames {
	if ttl <= 0 {
		ttl = time.Minute
	}
	d := &DeviceNames{path: path, ttl: ttl, names: map[string]string{}}
	d.load()
	return d
}

// Name returns the device name for a MAC, or "" if none is known.
func (d *DeviceNames) Name(mac string) string {
	if d == nil || d.path == "" {
		return ""
	}

	d.mu.RLock()
	name := d.names[strings.ToLower(mac)]
	due := time.Since(d.checked) > d.ttl
	d.mu.RUnlock()

	if due {
		go d.load()
	}
	return name
}

// Len returns the number of known names, for the status display.
func (d *DeviceNames) Len() int {
	if d == nil {
		return 0
	}
	d.mu.RLock()
	defer d.mu.RUnlock()
	return len(d.names)
}

func (d *DeviceNames) load() {
	if d.path == "" {
		return
	}

	info, err := os.Stat(d.path)
	if err != nil {
		// File not there yet: the control plane only writes it once a router
		// account is stored. Not an error, just nothing to do.
		d.mu.Lock()
		d.checked = time.Now()
		d.mu.Unlock()
		return
	}

	d.mu.RLock()
	unchanged := info.ModTime().Equal(d.state)
	d.mu.RUnlock()
	if unchanged {
		d.mu.Lock()
		d.checked = time.Now()
		d.mu.Unlock()
		return
	}

	roh, err := os.ReadFile(d.path)
	if err != nil {
		d.mu.Lock()
		d.checked = time.Now()
		d.mu.Unlock()
		return
	}

	var read map[string]string
	if err := json.Unmarshal(roh, &read); err != nil {
		// A half-written or broken file must not throw away what we have.
		d.mu.Lock()
		d.checked = time.Now()
		d.mu.Unlock()
		return
	}

	lowered := make(map[string]string, len(read))
	for mac, name := range read {
		if name = strings.TrimSpace(name); name != "" {
			lowered[strings.ToLower(strings.TrimSpace(mac))] = name
		}
	}

	d.mu.Lock()
	d.names = lowered
	d.state = info.ModTime()
	d.checked = time.Now()
	d.mu.Unlock()
}
