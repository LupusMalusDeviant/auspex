// Package clients manages device profiles created through the interface — as
// opposed to those from the configuration file, which belong to the operator
// and are not touched here.
package clients

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"

	"auspex/internal/config"
)

// Store holds the managed profiles and writes them to disk atomically.
type Store struct {
	path string

	mu    sync.RWMutex
	items map[string]config.Client
}

func Open(dir string) (*Store, error) {
	if dir == "" {
		dir = "var"
	}
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return nil, err
	}
	s := &Store{path: filepath.Join(dir, "clients.json"), items: map[string]config.Client{}}

	data, err := os.ReadFile(s.path)
	if err != nil {
		if os.IsNotExist(err) {
			return s, nil
		}
		return nil, err
	}
	var stored []config.Client
	if err := json.Unmarshal(data, &stored); err != nil {
		return nil, fmt.Errorf("%s: %w", s.path, err)
	}
	for _, c := range stored {
		s.items[c.Name] = c
	}
	return s, nil
}

// Put creates a profile or replaces it.
func (s *Store) Put(c config.Client) error {
	c.Name = strings.TrimSpace(c.Name)
	if c.Name == "" {
		return fmt.Errorf("the name is missing")
	}
	if len(c.Match) == 0 && len(c.Macs) == 0 {
		// One of the two it has to be - with no mapping the profile never
		// applies, and otherwise nobody notices.
		return fmt.Errorf("at least one address, network or MAC is required")
	}
	// The same check as at startup: a typo should be noticed here and not
	// later, as a profile that silently fails to apply.
	if err := (config.Config{Clients: []config.Client{c}}).ValidateClient(c); err != nil {
		return err
	}

	s.mu.Lock()
	s.items[c.Name] = c
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

func (s *Store) All() []config.Client {
	s.mu.RLock()
	defer s.mu.RUnlock()

	out := make([]config.Client, 0, len(s.items))
	for _, c := range s.items {
		out = append(out, c)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out
}

// Merge appends the managed profiles behind those from the configuration.
// On a name clash the configuration wins — otherwise a click in the browser
// could override a line in the file.
func (s *Store) Merge(fromConfig []config.Client) []config.Client {
	belegt := map[string]bool{}
	for _, c := range fromConfig {
		belegt[c.Name] = true
	}

	out := append([]config.Client{}, fromConfig...)
	for _, c := range s.All() {
		if belegt[c.Name] {
			continue
		}
		out = append(out, c)
	}
	return out
}

func (s *Store) save() error {
	s.mu.RLock()
	items := make([]config.Client, 0, len(s.items))
	for _, c := range s.items {
		items = append(items, c)
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
