// Package learn is learn mode: watch what a device actually asks for, build
// an allowlist from it and then shut everything else down.
//
// The process has three states (policy per client profile):
//
//	open     Normal operation, only block lists apply.
//	learn    Everything is resolved, every allowed name goes into the store.
//	enforce  Deny by default: only what was learned or explicitly allowed lives.
//
// Important: only what the normal filter let through is learned. Otherwise
// the tracker that happened to be asked for during the learn window ends up
// in the allowlist and is exempt from then on.
package learn

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"sync"
	"time"
)

// Granularity decides how generously a learned observation applies.
type Granularity string

const (
	// GranularityDomain allows the whole registrable domain.
	// Needed for anything on a CDN: cdn-3f8a.vendor.example is
	// cdn-91cc.vendor.example tomorrow.
	GranularityDomain Granularity = "domain"
	// GranularityExact allows exactly the observed name. Stricter, but
	// permanently broken where host names change.
	GranularityExact Granularity = "exact"
)

type Entry struct {
	Name   string    `json:"name"`
	Domain string    `json:"domain"`
	Count  int64     `json:"count"`
	First  time.Time `json:"first"`
	Last   time.Time `json:"last"`
	Types  []string  `json:"types"`
}

type Stats struct {
	Profile     string    `json:"profile"`
	Policy      string    `json:"policy"`
	Granularity string    `json:"granularity"`
	Names       int       `json:"names"`
	Domains     int       `json:"domains"`
	Created     time.Time `json:"created"`
	LastNew     time.Time `json:"last_new"`
	// Overflow means: the limit is reached and nothing more is learned.
	// Usually a device generating random names — or DNS tunnelling.
	Overflow bool `json:"overflow"`
	// QuietFor is the time since the last *new* domain. That is the signal
	// that a learn window has run long enough.
	QuietForSec float64 `json:"quiet_for_sec"`
}

type persisted struct {
	Profile string            `json:"profile"`
	Created time.Time         `json:"created"`
	Updated time.Time         `json:"updated"`
	LastNew time.Time         `json:"last_new"`
	Entries map[string]*Entry `json:"entries"`
}

// Store holds one profile's observations.
type Store struct {
	profile     string
	path        string
	granularity Granularity
	maxEntries  int

	mu       sync.RWMutex
	entries  map[string]*Entry
	domains  map[string]int
	created  time.Time
	lastNew  time.Time
	dirty    bool
	overflow bool
}

// Open loads an existing store or creates a new one.
func Open(dir, profile string, granularity Granularity, maxEntries int) (*Store, error) {
	if granularity != GranularityExact {
		granularity = GranularityDomain
	}
	if maxEntries <= 0 {
		maxEntries = 5000
	}
	s := &Store{
		profile:     profile,
		path:        filepath.Join(dir, sanitize(profile)+".json"),
		granularity: granularity,
		maxEntries:  maxEntries,
		entries:     map[string]*Entry{},
		domains:     map[string]int{},
		created:     time.Now(),
	}
	data, err := os.ReadFile(s.path)
	if err != nil {
		if os.IsNotExist(err) {
			return s, nil
		}
		return nil, err
	}
	var p persisted
	if err := json.Unmarshal(data, &p); err != nil {
		return nil, fmt.Errorf("%s: %w", s.path, err)
	}
	if !p.Created.IsZero() {
		s.created = p.Created
	}
	s.lastNew = p.LastNew
	for name, e := range p.Entries {
		s.entries[name] = e
		s.domains[e.Domain]++
	}
	return s, nil
}

// Record books an observed query.
func (s *Store) Record(name, qtype string) {
	name = normalize(name)
	if name == "" || isReverseZone(name) {
		return
	}
	now := time.Now()

	s.mu.Lock()
	defer s.mu.Unlock()

	if e, ok := s.entries[name]; ok {
		e.Count++
		e.Last = now
		if !contains(e.Types, qtype) {
			e.Types = append(e.Types, qtype)
			sort.Strings(e.Types)
		}
		s.dirty = true
		return
	}
	if len(s.entries) >= s.maxEntries {
		s.overflow = true
		return
	}
	domain := registrableDomain(name)
	s.entries[name] = &Entry{
		Name:   name,
		Domain: domain,
		Count:  1,
		First:  now,
		Last:   now,
		Types:  []string{qtype},
	}
	s.domains[domain]++
	s.lastNew = now
	s.dirty = true
}

// Allows decides in enforce mode whether a name gets through.
func (s *Store) Allows(name string) bool {
	name = normalize(name)
	if name == "" {
		return false
	}
	// Reverse lookups are not part of the question "which services does this
	// device talk to" and would break diagnostics for no reason.
	if isReverseZone(name) {
		return true
	}

	s.mu.RLock()
	defer s.mu.RUnlock()

	if _, ok := s.entries[name]; ok {
		return true
	}
	if s.granularity == GranularityExact {
		return false
	}
	_, ok := s.domains[registrableDomain(name)]
	return ok
}

func (s *Store) Entries() []Entry {
	s.mu.RLock()
	defer s.mu.RUnlock()

	out := make([]Entry, 0, len(s.entries))
	for _, e := range s.entries {
		out = append(out, *e)
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].Count != out[j].Count {
			return out[i].Count > out[j].Count
		}
		return out[i].Name < out[j].Name
	})
	return out
}

// Allowlist produces the rules that go into the configuration once learning
// is done — in the same format the parser understands anyway.
func (s *Store) Allowlist(granularity Granularity) []string {
	s.mu.RLock()
	defer s.mu.RUnlock()

	seen := map[string]bool{}
	if granularity == GranularityExact {
		for name := range s.entries {
			seen[name] = true
		}
	} else {
		for domain := range s.domains {
			seen[domain] = true
		}
	}
	out := make([]string, 0, len(seen))
	for name := range seen {
		out = append(out, "@@||"+name+"^")
	}
	sort.Strings(out)
	return out
}

func (s *Store) Stats(policy string) Stats {
	s.mu.RLock()
	defer s.mu.RUnlock()

	st := Stats{
		Profile:     s.profile,
		Policy:      policy,
		Granularity: string(s.granularity),
		Names:       len(s.entries),
		Domains:     len(s.domains),
		Created:     s.created,
		LastNew:     s.lastNew,
		Overflow:    s.overflow,
	}
	if !s.lastNew.IsZero() {
		st.QuietForSec = time.Since(s.lastNew).Seconds()
	}
	return st
}

func (s *Store) Reset() {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.entries = map[string]*Entry{}
	s.domains = map[string]int{}
	s.created = time.Now()
	s.lastNew = time.Time{}
	s.overflow = false
	s.dirty = true
}

// Forget removes a single name — for the case where something came along
// during learning that should not stay.
func (s *Store) Forget(name string) bool {
	name = normalize(name)
	s.mu.Lock()
	defer s.mu.Unlock()

	e, ok := s.entries[name]
	if !ok {
		return false
	}
	delete(s.entries, name)
	if s.domains[e.Domain] <= 1 {
		delete(s.domains, e.Domain)
	} else {
		s.domains[e.Domain]--
	}
	s.dirty = true
	return true
}

// Import adds observations from a backup without losing what is there:
// counters are added, timestamps pulled out to the extremes. Merge rather
// than replace — restoring a backup should not delete what has been learned
// since.
func (s *Store) Import(entries []Entry) int {
	now := time.Now()

	s.mu.Lock()
	defer s.mu.Unlock()

	taken := 0
	for _, e := range entries {
		name := normalize(e.Name)
		if name == "" || isReverseZone(name) {
			continue
		}
		if e.Domain == "" {
			e.Domain = registrableDomain(name)
		}

		existing, ok := s.entries[name]
		if !ok {
			if len(s.entries) >= s.maxEntries {
				s.overflow = true
				break
			}
			cp := e
			cp.Name = name
			if cp.First.IsZero() {
				cp.First = now
			}
			if cp.Last.IsZero() {
				cp.Last = now
			}
			s.entries[name] = &cp
			s.domains[cp.Domain]++
			taken++
			continue
		}

		existing.Count += e.Count
		if e.First.Before(existing.First) && !e.First.IsZero() {
			existing.First = e.First
		}
		if e.Last.After(existing.Last) {
			existing.Last = e.Last
		}
		for _, t := range e.Types {
			if !contains(existing.Types, t) {
				existing.Types = append(existing.Types, t)
			}
		}
		sort.Strings(existing.Types)
		taken++
	}

	if taken > 0 {
		s.dirty = true
	}
	return taken
}

// Save writes atomically, but only if something changed.
func (s *Store) Save() error {
	s.mu.Lock()
	if !s.dirty {
		s.mu.Unlock()
		return nil
	}
	p := persisted{
		Profile: s.profile,
		Created: s.created,
		Updated: time.Now(),
		LastNew: s.lastNew,
		Entries: make(map[string]*Entry, len(s.entries)),
	}
	for name, e := range s.entries {
		cp := *e
		p.Entries[name] = &cp
	}
	s.dirty = false
	s.mu.Unlock()

	data, err := json.MarshalIndent(p, "", "  ")
	if err != nil {
		return err
	}
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o640); err != nil {
		return err
	}
	return os.Rename(tmp, s.path)
}
