package learn

import (
	"fmt"
	"log/slog"
	"os"
	"sort"
	"sync"
)

// Manager holds the stores of every learning or enforcing profile.
type Manager struct {
	dir string
	log *slog.Logger

	mu     sync.RWMutex
	stores map[string]*Store
}

func NewManager(dir string, log *slog.Logger) (*Manager, error) {
	if dir == "" {
		dir = "var/learn"
	}
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return nil, fmt.Errorf("Lernverzeichnis: %w", err)
	}
	return &Manager{dir: dir, log: log, stores: map[string]*Store{}}, nil
}

// Store returns a profile's store, creating it if needed.
func (m *Manager) Store(profile string, granularity Granularity, maxEntries int) (*Store, error) {
	m.mu.Lock()
	defer m.mu.Unlock()

	if s, ok := m.stores[profile]; ok {
		return s, nil
	}
	s, err := Open(m.dir, profile, granularity, maxEntries)
	if err != nil {
		return nil, err
	}
	m.stores[profile] = s
	return s, nil
}

func (m *Manager) Get(profile string) (*Store, bool) {
	m.mu.RLock()
	defer m.mu.RUnlock()
	s, ok := m.stores[profile]
	return s, ok
}

func (m *Manager) All() []*Store {
	m.mu.RLock()
	defer m.mu.RUnlock()

	out := make([]*Store, 0, len(m.stores))
	for _, s := range m.stores {
		out = append(out, s)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].profile < out[j].profile })
	return out
}

// SaveAll writes every changed store to disk.
func (m *Manager) SaveAll() {
	for _, s := range m.All() {
		if err := s.Save(); err != nil {
			m.log.Error("the learned state could not be saved",
				"profile", s.profile, "error", err)
		}
	}
}
