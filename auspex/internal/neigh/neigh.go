// Package neigh reads the kernel's neighbour table: which IP address on the
// local network currently belongs to which MAC.
//
// The reason is IPv6. Windows and Android use temporary addresses and rotate
// them regularly - daily by default. That dissolves any device identity
// hanging off the IP: client profiles stop applying, learn mode learns for
// an address that is gone tomorrow, and per-device analysis splinters into
// nothing but mayflies.
//
// The router cannot help with this. It answers no reverse lookup for
// temporary IPv6 addresses, TR-064 only takes IPv4 for its device query, and
// they do not appear in its home-network overview either. It cannot possibly
// know: the devices make these addresses up themselves and tell nobody.
//
// The host's kernel does know. It sits in the same network segment and keeps
// the IP -> MAC mapping for every neighbour it has spoken to. That is what
// gets read here.
package neigh

import (
	"net/netip"
	"sync"
	"time"
)

// Table holds the most recently read mapping.
//
// Deliberately with a cache and background refresh: the table is queried on
// the DNS path, and that must not stop for anything taking longer than a
// memory access.
type Table struct {
	ttl time.Duration

	mu        sync.RWMutex
	entry     map[netip.Addr]string // address -> MAC in lower case
	fetched   time.Time
	readTable func() (map[netip.Addr]string, error)
}

func New(ttl time.Duration) *Table {
	if ttl <= 0 {
		ttl = 30 * time.Second
	}
	return &Table{ttl: ttl, entry: map[netip.Addr]string{}, readTable: readTable}
}

// Mac returns the MAC for an address, or "" if it is unknown.
//
// Always answers immediately from memory. If the data is stale, a refresh
// runs in the background and this query is still answered from the old state
// - a mapping a few seconds out of date is harmless, a blocked DNS answer is
// not.
func (t *Table) Mac(a netip.Addr) string {
	t.mu.RLock()
	mac, ok := t.entry[a.Unmap()]
	old := time.Since(t.fetched) > t.ttl
	t.mu.RUnlock()

	if old {
		go t.Refresh()
	}
	if !ok {
		return ""
	}
	return mac
}

// Refresh reads the table again.
func (t *Table) Refresh() {
	fresh, err := t.readTable()
	if err != nil {
		// A read error is no reason to throw away what we have: it is still
		// better than nothing. Only the timestamp is set, so a fresh attempt
		// does not start on every query.
		t.mu.Lock()
		t.fetched = time.Now()
		t.mu.Unlock()
		return
	}

	t.mu.Lock()
	t.entry = fresh
	t.fetched = time.Now()
	t.mu.Unlock()
}

// Len returns the number of known mappings, for the status display.
func (t *Table) Len() int {
	t.mu.RLock()
	defer t.mu.RUnlock()
	return len(t.entry)
}
