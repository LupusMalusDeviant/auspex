package lists

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"auspex/internal/config"
)

// Managed is a list administered at runtime through the interface — as
// opposed to the lists from the configuration, which belong to the operator
// and are not touched here.
type Managed struct {
	Name    string    `json:"name"`
	URL     string    `json:"url"`
	Allow   bool      `json:"allow"`
	Enabled bool      `json:"enabled"`
	Added   time.Time `json:"added"`
}

// Store holds the managed lists and writes them to disk atomically.
type Store struct {
	path string

	mu    sync.RWMutex
	items map[string]*Managed
}

func OpenStore(dir string) (*Store, error) {
	if dir == "" {
		dir = "var/lists"
	}
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return nil, err
	}
	s := &Store{path: filepath.Join(dir, "managed.json"), items: map[string]*Managed{}}

	data, err := os.ReadFile(s.path)
	if err != nil {
		if os.IsNotExist(err) {
			return s, nil
		}
		return nil, err
	}
	var stored []Managed
	if err := json.Unmarshal(data, &stored); err != nil {
		return nil, fmt.Errorf("%s: %w", s.path, err)
	}
	for i := range stored {
		s.items[stored[i].Name] = &stored[i]
	}
	return s, nil
}

// Add creates a list or updates it.
func (s *Store) Add(m Managed) error {
	m.Name = strings.TrimSpace(m.Name)
	m.URL = strings.TrimSpace(m.URL)
	if m.Name == "" {
		return fmt.Errorf("the name is missing")
	}
	if !strings.HasPrefix(m.URL, "https://") && !strings.HasPrefix(m.URL, "http://") {
		// A path instead of a URL would let the control plane write into the
		// resolver's file system - that belongs in the configuration.
		return fmt.Errorf("the URL has to begin with http:// or https://")
	}

	s.mu.Lock()
	if existing, ok := s.items[m.Name]; ok {
		m.Added = existing.Added
	} else {
		m.Added = time.Now()
	}
	s.items[m.Name] = &m
	s.mu.Unlock()

	return s.save()
}

func (s *Store) Remove(name string) (bool, error) {
	s.mu.Lock()
	_, ok := s.items[name]
	delete(s.items, name)
	s.mu.Unlock()

	if !ok {
		return false, nil
	}
	return true, s.save()
}

// SetEnabled switches a list on or off without losing it.
func (s *Store) SetEnabled(name string, enabled bool) (bool, error) {
	s.mu.Lock()
	item, ok := s.items[name]
	if ok {
		item.Enabled = enabled
	}
	s.mu.Unlock()

	if !ok {
		return false, nil
	}
	return true, s.save()
}

func (s *Store) All() []Managed {
	s.mu.RLock()
	defer s.mu.RUnlock()

	out := make([]Managed, 0, len(s.items))
	for _, m := range s.items {
		out = append(out, *m)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out
}

// AsConfig translates the managed lists into configuration entries, so the
// loader needs no special case.
func (s *Store) AsConfig() []config.List {
	out := []config.List{}
	for _, m := range s.All() {
		enabled := m.Enabled
		out = append(out, config.List{
			Name:    m.Name,
			URL:     m.URL,
			Allow:   m.Allow,
			Enabled: &enabled,
		})
	}
	return out
}

func (s *Store) save() error {
	s.mu.RLock()
	items := make([]Managed, 0, len(s.items))
	for _, m := range s.items {
		items = append(items, *m)
	}
	s.mu.RUnlock()
	sort.Slice(items, func(i, j int) bool { return items[i].Name < items[j].Name })

	data, err := json.MarshalIndent(items, "", "  ")
	if err != nil {
		return err
	}
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err != nil {
		return err
	}
	return os.Rename(tmp, s.path)
}
