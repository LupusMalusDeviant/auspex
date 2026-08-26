// Package lists fetches filter lists from disk or the network and compiles
// them into a rule engine.
package lists

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"os"
	"path/filepath"
	"time"

	"auspex/internal/config"
	"auspex/internal/rules"
	"auspex/internal/services"
)

// maxListSize caps a download. HaGeZi Pro sits at ~10 MB; 128 MB is
// generous and still stops a broken mirror filling memory.
const maxListSize = 128 << 20

type Loader struct {
	store    *Store
	cacheDir string
	interval time.Duration
	client   *http.Client
	log      *slog.Logger
}

func NewLoader(cfg config.Filter, store *Store, log *slog.Logger) *Loader {
	dir := cfg.CacheDir
	if dir == "" {
		dir = "var/lists"
	}
	return &Loader{
		store:    store,
		cacheDir: dir,
		interval: cfg.UpdateInterval.D(),
		client:   &http.Client{Timeout: 2 * time.Minute},
		log:      log,
	}
}

// Build compiles every list plus the rules from the configuration.
// force=true bypasses the disk cache and downloads again.
func (l *Loader) Build(ctx context.Context, cfg config.Filter, force bool) (*rules.Engine, error) {
	if err := os.MkdirAll(l.cacheDir, 0o755); err != nil {
		return nil, fmt.Errorf("Listenverzeichnis: %w", err)
	}
	builder := rules.NewBuilder()

	// Lists from the configuration belong to the operator, managed lists to
	// the interface. On a name clash the configuration wins - otherwise a
	// click in the browser could override a line in the file.
	fromConfig := map[string]bool{}
	for _, list := range cfg.Lists {
		fromConfig[list.Name] = true
	}
	all := append([]config.List{}, cfg.Lists...)
	if l.store != nil {
		for _, managed := range l.store.AsConfig() {
			if fromConfig[managed.Name] {
				l.log.Warn("managed list skipped, the name is already in the configuration",
					"list", managed.Name)
				continue
			}
			all = append(all, managed)
		}
	}

	for _, list := range all {
		if !list.IsEnabled() {
			continue
		}
		content, err := l.fetch(ctx, list, force)
		if err != nil {
			// A broken list must not take the resolver down with it.
			l.log.Warn("list skipped", "list", list.Name, "error", err)
			continue
		}
		st := builder.AddLines(list.Name, content, list.Allow)
		l.log.Info("list loaded",
			"list", list.Name, "rules", st.Rules, "skipped", st.Skipped, "duplikate", st.Duplicates)
	}

	// Services from the catalogue become perfectly ordinary rules - after
	// that there is no special case anywhere else in the system.
	if serviceRules, _ := services.Rules(cfg.BlockServices); len(serviceRules) > 0 {
		builder.AddRules("config:dienste", serviceRules, false)
		l.log.Info("services blocked", "services", cfg.BlockServices, "rules", len(serviceRules))
	}

	builder.AddRules("config:block", cfg.BlockRules, false)
	builder.AddRules("config:allow", cfg.AllowRules, true)

	engine := builder.Build()
	stats := engine.Stats()
	l.log.Info("rule set built",
		"block", stats.BlockRules, "allow", stats.AllowRules, "conflicts", len(stats.Conflicts))
	if len(stats.Conflicts) > 0 {
		l.log.Warn("the pattern is on both the block and the allow list (allow wins)",
			"count", len(stats.Conflicts), "examples", firstN(stats.Conflicts, 5))
	}
	return engine, nil
}

func (l *Loader) fetch(ctx context.Context, list config.List, force bool) (string, error) {
	if list.Path != "" {
		data, err := os.ReadFile(list.Path)
		return string(data), err
	}
	if list.URL == "" {
		return "", fmt.Errorf("neither url nor path is set")
	}

	cachePath := filepath.Join(l.cacheDir, cacheName(list))
	if !force {
		if info, err := os.Stat(cachePath); err == nil {
			if l.interval <= 0 || time.Since(info.ModTime()) < l.interval {
				data, err := os.ReadFile(cachePath)
				if err == nil {
					return string(data), nil
				}
			}
		}
	}

	data, err := l.download(ctx, list.URL)
	if err != nil {
		// Network gone: better the old list than none at all.
		if cached, rerr := os.ReadFile(cachePath); rerr == nil {
			l.log.Warn("download failed, using the cache", "list", list.Name, "error", err)
			return string(cached), nil
		}
		return "", err
	}
	// Replace atomically, so an abort leaves no half-written list.
	tmp := cachePath + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err == nil {
		_ = os.Rename(tmp, cachePath)
	}
	return string(data), nil
}

func (l *Loader) download(ctx context.Context, url string) ([]byte, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}
	// Only the name of the software, no identifier for the operator. Whoever
	// fetches lists sends this value to somebody else's servers - every
	// time, for every list. A personal handle in there would be a trace you
	// leave without noticing. List maintainers like to see a contact
	// address; anyone wanting to leave one puts their own repository here.
	req.Header.Set("User-Agent", "auspex/0.1")

	resp, err := l.client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("HTTP %d", resp.StatusCode)
	}
	return io.ReadAll(io.LimitReader(resp.Body, maxListSize))
}

func cacheName(list config.List) string {
	sum := sha256.Sum256([]byte(list.URL))
	return fmt.Sprintf("%s-%s.txt", sanitize(list.Name), hex.EncodeToString(sum[:6]))
}

func sanitize(name string) string {
	out := make([]rune, 0, len(name))
	for _, r := range name {
		switch {
		case r >= 'a' && r <= 'z', r >= 'A' && r <= 'Z', r >= '0' && r <= '9', r == '-', r == '_':
			out = append(out, r)
		default:
			out = append(out, '-')
		}
	}
	if len(out) == 0 {
		return "list"
	}
	return string(out)
}

func firstN(s []string, n int) []string {
	if len(s) < n {
		return s
	}
	return s[:n]
}
