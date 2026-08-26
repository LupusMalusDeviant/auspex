// Package api is the control-plane interface: JSON over HTTP, meant as the
// data source for the .NET dashboard.
package api

import (
	"context"
	"crypto/subtle"
	"encoding/json"
	"log/slog"
	"net/http"
	"strconv"
	"time"

	"auspex/internal/clients"
	"auspex/internal/config"
	"auspex/internal/learn"
	"auspex/internal/lists"
	"auspex/internal/resolver"
	"auspex/internal/services"
	"net/netip"
	"strings"
)

// ReloadFunc rebuilds the rule set. force=true bypasses the disk cache.
type ReloadFunc func(ctx context.Context, force bool) error

// DohStats returns the counters of the DoH endpoint. Malformed requests
// never reach the resolver and would otherwise be invisible.
type DohStats interface {
	Queries() int64
	Errors() int64
}

type Server struct {
	cfg     config.API
	res     *resolver.Resolver
	reload  ReloadFunc
	log     *slog.Logger
	version string
	doh     DohStats
	lists   *lists.Store
	clients *clients.Store
	httpSrv *http.Server
}

func New(cfg config.API, res *resolver.Resolver, reload ReloadFunc, version string, log *slog.Logger,
	dohStats DohStats, listStore *lists.Store, clientStore *clients.Store) *Server {
	return &Server{
		cfg: cfg, res: res, reload: reload, log: log,
		version: version, doh: dohStats, lists: listStore, clients: clientStore,
	}
}

func (s *Server) Start() error {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /api/v1/status", s.handleStatus)
	mux.HandleFunc("GET /api/v1/querylog", s.handleQueryLog)
	mux.HandleFunc("GET /api/v1/querylog/stream", s.handleQueryLogStream)
	mux.HandleFunc("GET /api/v1/explain", s.handleExplain)
	mux.HandleFunc("GET /api/v1/who", s.handleWho)
	mux.HandleFunc("GET /api/v1/upstreams", s.handleUpstreams)
	mux.HandleFunc("GET /api/v1/services", s.handleServices)
	mux.HandleFunc("GET /api/v1/safesearch", s.handleSafeSearch)
	mux.HandleFunc("GET /api/v1/clients", s.handleClients)
	mux.HandleFunc("POST /api/v1/clients", s.handleClientPut)
	mux.HandleFunc("DELETE /api/v1/clients/{name}", s.handleClientRemove)
	mux.HandleFunc("GET /api/v1/lists", s.handleLists)
	mux.HandleFunc("POST /api/v1/lists", s.handleListAdd)
	mux.HandleFunc("POST /api/v1/lists/{name}/enabled", s.handleListEnabled)
	mux.HandleFunc("DELETE /api/v1/lists/{name}", s.handleListRemove)
	mux.HandleFunc("POST /api/v1/reload", s.handleReload)
	mux.HandleFunc("POST /api/v1/cache/forget", s.handleForget)
	mux.HandleFunc("POST /api/v1/cache/purge", s.handlePurge)
	mux.HandleFunc("POST /api/v1/cache/warm", s.handleWarm)
	mux.HandleFunc("GET /api/v1/learn", s.handleLearnOverview)
	mux.HandleFunc("GET /api/v1/learn/{profile}", s.handleLearnEntries)
	mux.HandleFunc("GET /api/v1/learn/{profile}/allowlist", s.handleLearnAllowlist)
	mux.HandleFunc("POST /api/v1/learn/{profile}/import", s.handleLearnImport)
	mux.HandleFunc("POST /api/v1/learn/{profile}/reset", s.handleLearnReset)
	mux.HandleFunc("POST /api/v1/learn/{profile}/forget", s.handleLearnForget)
	mux.HandleFunc("GET /metrics", s.handleMetrics)
	mux.HandleFunc("GET /healthz", s.handleHealth)

	s.httpSrv = &http.Server{
		Addr:              s.cfg.Listen,
		Handler:           s.withAuth(mux),
		ReadHeaderTimeout: 10 * time.Second,
	}
	s.log.Info("control API is listening", "address", s.cfg.Listen, "token", s.cfg.Token != "")
	go func() {
		if err := s.httpSrv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			s.log.Error("control API stopped", "error", err)
		}
	}()
	return nil
}

func (s *Server) Shutdown(ctx context.Context) error {
	if s.httpSrv == nil {
		return nil
	}
	return s.httpSrv.Shutdown(ctx)
}

// withAuth requires a bearer token as soon as one is configured.
// /healthz stays open so container health checks keep working.
func (s *Server) withAuth(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if s.cfg.Token == "" || r.URL.Path == "/healthz" {
			next.ServeHTTP(w, r)
			return
		}
		got := r.Header.Get("Authorization")
		want := "Bearer " + s.cfg.Token
		if subtle.ConstantTimeCompare([]byte(got), []byte(want)) != 1 {
			http.Error(w, "unauthorized", http.StatusUnauthorized)
			return
		}
		next.ServeHTTP(w, r)
	})
}

type statusResponse struct {
	Version   string  `json:"version"`
	UptimeSec float64 `json:"uptime_sec"`
	Resolver  any     `json:"resolver"`
	Cache     any     `json:"cache"`
	Rules     any     `json:"rules"`
	QueryLog  any     `json:"querylog"`
	Upstreams any     `json:"upstreams"`
	Learning  any     `json:"learning"`
	Doh       any     `json:"doh,omitempty"`
}

func (s *Server) handleStatus(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, statusResponse{
		Version:   s.version,
		UptimeSec: s.res.Uptime().Seconds(),
		Resolver:  s.res.Stats(),
		Cache:     s.res.Cache().Stats(),
		Rules:     s.res.Engine().Stats(),
		QueryLog:  s.res.QueryLog().Summary(),
		Upstreams: s.res.Pool().Health(),
		Learning:  s.res.LearnStats(),
		Doh:       s.dohStats(),
	})
}

// handleHealth answers the question that matters: can this resolver serve a
// query right now?
//
// "The process is alive" is not enough for that - a hung handler would pass
// it while nobody on the network gets an answer any more. The self-check
// therefore touches every part that has a lock of its own.
func (s *Server) handleHealth(w http.ResponseWriter, r *http.Request) {
	ctx, cancel := context.WithTimeout(r.Context(), 3*time.Second)
	defer cancel()

	done := make(chan error, 1)
	go func() { done <- s.res.SelfCheck() }()

	select {
	case err := <-done:
		if err != nil {
			http.Error(w, err.Error(), http.StatusServiceUnavailable)
			return
		}
		w.WriteHeader(http.StatusOK)
		w.Write([]byte("ok"))
	case <-ctx.Done():
		http.Error(w, "the self-check is hanging", http.StatusServiceUnavailable)
	}
}

func (s *Server) dohStats() any {
	if s.doh == nil {
		return nil
	}
	return map[string]int64{"queries": s.doh.Queries(), "errors": s.doh.Errors()}
}

func (s *Server) handleQueryLog(w http.ResponseWriter, r *http.Request) {
	limit := 100
	if v := r.URL.Query().Get("limit"); v != "" {
		if n, err := strconv.Atoi(v); err == nil && n > 0 {
			limit = n
		}
	}
	writeJSON(w, http.StatusOK, s.res.QueryLog().Recent(limit))
}

// handleQueryLogStream is the cursor query for the control plane:
// everything after ?since=N, oldest first.
func (s *Server) handleQueryLogStream(w http.ResponseWriter, r *http.Request) {
	var since int64
	if v := r.URL.Query().Get("since"); v != "" {
		if n, err := strconv.ParseInt(v, 10, 64); err == nil && n >= 0 {
			since = n
		}
	}
	limit := 1000
	if v := r.URL.Query().Get("limit"); v != "" {
		if n, err := strconv.Atoi(v); err == nil && n > 0 {
			limit = n
		}
	}
	writeJSON(w, http.StatusOK, s.res.QueryLog().Since(since, limit))
}

// handleExplain is the "why was this blocked?" query.
func (s *Server) handleExplain(w http.ResponseWriter, r *http.Request) {
	domain := r.URL.Query().Get("domain")
	if domain == "" {
		http.Error(w, "the domain parameter is missing", http.StatusBadRequest)
		return
	}
	writeJSON(w, http.StatusOK, s.res.Explain(domain, r.URL.Query().Get("client")))
}

// handleServices returns the service catalogue so the interface can offer
// checkboxes instead of demanding domains.
func (s *Server) handleServices(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, services.All())
}

// handleSafeSearch returns the SafeSearch catalogue, for the same reason:
// the interface offers what exists instead of asking somebody to type a
// provider key that has to match exactly.
func (s *Server) handleSafeSearch(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, services.SafeSearchProviders())
}

// handleClients shows the managed device profiles. Profiles from the
// configuration file do not appear here - they belong to the operator.
func (s *Server) handleClients(w http.ResponseWriter, _ *http.Request) {
	if s.clients == nil {
		writeJSON(w, http.StatusOK, []any{})
		return
	}
	writeJSON(w, http.StatusOK, s.clients.All())
}

func (s *Server) handleClientPut(w http.ResponseWriter, r *http.Request) {
	if s.clients == nil {
		http.Error(w, "device management not available", http.StatusServiceUnavailable)
		return
	}
	var body config.Client
	// Reject unknown fields rather than ignoring them silently: a typo in a
	// field name would otherwise produce a profile that exists and does
	// nothing.
	decoder := json.NewDecoder(http.MaxBytesReader(w, r.Body, 64<<10))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": err.Error()})
		return
	}
	if err := s.clients.Put(body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": err.Error()})
		return
	}
	s.reloadAfterChange(w, r)
}

func (s *Server) handleClientRemove(w http.ResponseWriter, r *http.Request) {
	if s.clients == nil {
		http.Error(w, "device management not available", http.StatusServiceUnavailable)
		return
	}
	ok, err := s.clients.Remove(r.PathValue("name"))
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	if !ok {
		http.Error(w, "unknown profile", http.StatusNotFound)
		return
	}
	s.reloadAfterChange(w, r)
}

// handleLists shows configured and managed lists together with the catalogue.
func (s *Server) handleLists(w http.ResponseWriter, _ *http.Request) {
	// The rule counts come from the active rule set, not from storage - so
	// you see what is actually loaded.
	stats := map[string]any{}
	for _, ls := range s.res.Engine().Stats().Lists {
		stats[ls.Name] = ls
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"managed": s.managedLists(),
		"known":   lists.KnownLists(),
		"stats":   stats,
	})
}

func (s *Server) managedLists() any {
	if s.lists == nil {
		return []any{}
	}
	return s.lists.All()
}

func (s *Server) handleListAdd(w http.ResponseWriter, r *http.Request) {
	if s.lists == nil {
		http.Error(w, "list management not available", http.StatusServiceUnavailable)
		return
	}
	var body lists.Managed
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 8<<10)).Decode(&body); err != nil {
		http.Error(w, "unlesbarer Rumpf", http.StatusBadRequest)
		return
	}
	if err := s.lists.Add(body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": err.Error()})
		return
	}
	s.reloadAfterChange(w, r)
}

func (s *Server) handleListEnabled(w http.ResponseWriter, r *http.Request) {
	if s.lists == nil {
		http.Error(w, "list management not available", http.StatusServiceUnavailable)
		return
	}
	enabled := r.URL.Query().Get("value") != "false"
	ok, err := s.lists.SetEnabled(r.PathValue("name"), enabled)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	if !ok {
		http.Error(w, "unknown list", http.StatusNotFound)
		return
	}
	s.reloadAfterChange(w, r)
}

func (s *Server) handleListRemove(w http.ResponseWriter, r *http.Request) {
	if s.lists == nil {
		http.Error(w, "list management not available", http.StatusServiceUnavailable)
		return
	}
	ok, err := s.lists.Remove(r.PathValue("name"))
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	if !ok {
		http.Error(w, "unknown list", http.StatusNotFound)
		return
	}
	s.reloadAfterChange(w, r)
}

// reloadAfterChange rebuilds the rule set. Without force, so the other lists
// are not downloaded again - the loader fetches the new one anyway, because
// it is not in the cache yet.
func (s *Server) reloadAfterChange(w http.ResponseWriter, r *http.Request) {
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Minute)
	defer cancel()

	if err := s.reload(ctx, false); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "rules": s.res.Engine().Stats()})
}

func (s *Server) handleUpstreams(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, s.res.Pool().Health())
}

func (s *Server) handleReload(w http.ResponseWriter, r *http.Request) {
	force := r.URL.Query().Get("force") == "true"
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Minute)
	defer cancel()

	if err := s.reload(ctx, force); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "rules": s.res.Engine().Stats()})
}

// handleWarm pulls names into the cache ahead of time. Answers immediately:
// the warming carries on in the background, and a caller should not have to
// wait minutes just to learn that it has begun.
func (s *Server) handleWarm(w http.ResponseWriter, r *http.Request) {
	var body struct {
		Names []string `json:"names"`
	}
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 1<<20)).Decode(&body); err != nil {
		http.Error(w, "unlesbarer Rumpf", http.StatusBadRequest)
		return
	}
	// A cap against a list that would flood the upstream.
	const atMost = 2000
	if len(body.Names) > atMost {
		body.Names = body.Names[:atMost]
	}

	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Minute)
		defer cancel()
		fetched := s.res.Warm(ctx, body.Names, 8)
		s.log.Info("cache warmed", "requested", len(body.Names), "fetched", fetched)
	}()

	writeJSON(w, http.StatusAccepted, map[string]any{"ok": true, "names": len(body.Names)})
}

func (s *Server) handlePurge(w http.ResponseWriter, _ *http.Request) {
	s.res.Cache().Purge()
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// handleLearnOverview lists every learning or enforcing profile.
func (s *Server) handleLearnOverview(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, s.res.LearnStats())
}

func (s *Server) handleLearnEntries(w http.ResponseWriter, r *http.Request) {
	store, _, ok := s.res.LearnStore(r.PathValue("profile"))
	if !ok {
		http.Error(w, "the profile is not learning", http.StatusNotFound)
		return
	}
	writeJSON(w, http.StatusOK, store.Entries())
}

// handleLearnAllowlist returns the rules that go into the configuration once
// learning is done.
func (s *Server) handleLearnAllowlist(w http.ResponseWriter, r *http.Request) {
	store, _, ok := s.res.LearnStore(r.PathValue("profile"))
	if !ok {
		http.Error(w, "the profile is not learning", http.StatusNotFound)
		return
	}
	granularity := learn.Granularity(r.URL.Query().Get("granularity"))
	if granularity != learn.GranularityExact {
		granularity = learn.GranularityDomain
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"profile":     r.PathValue("profile"),
		"granularity": string(granularity),
		"rules":       store.Allowlist(granularity),
	})
}

// handleLearnImport brings observations from a backup back in.
func (s *Server) handleLearnImport(w http.ResponseWriter, r *http.Request) {
	store, _, ok := s.res.LearnStore(r.PathValue("profile"))
	if !ok {
		http.Error(w, "the profile is not learning", http.StatusNotFound)
		return
	}
	var entries []learn.Entry
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 32<<20)).Decode(&entries); err != nil {
		http.Error(w, "unlesbarer Rumpf", http.StatusBadRequest)
		return
	}
	taken := store.Import(entries)
	if err := store.Save(); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "imported": taken})
}

func (s *Server) handleLearnReset(w http.ResponseWriter, r *http.Request) {
	store, _, ok := s.res.LearnStore(r.PathValue("profile"))
	if !ok {
		http.Error(w, "the profile is not learning", http.StatusNotFound)
		return
	}
	store.Reset()
	if err := store.Save(); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// handleLearnForget removes a single name from what was learned.
func (s *Server) handleLearnForget(w http.ResponseWriter, r *http.Request) {
	store, _, ok := s.res.LearnStore(r.PathValue("profile"))
	if !ok {
		http.Error(w, "the profile is not learning", http.StatusNotFound)
		return
	}
	name := r.URL.Query().Get("name")
	if name == "" {
		http.Error(w, "the name parameter is missing", http.StatusBadRequest)
		return
	}
	removed := store.Forget(name)
	if removed {
		_ = store.Save()
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": removed})
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	enc := json.NewEncoder(w)
	enc.SetIndent("", "  ")
	_ = enc.Encode(body)
}

// handleWho says which device is behind an address.
//
// Needed by the control plane: on a request it only sees the sender's
// address, and under IPv6 that is a different one tomorrow. The resolver has
// the answer anyway - neighbour table and device list - and hands it out
// here, rather than having a second place work the same thing out again.
func (s *Server) handleWho(w http.ResponseWriter, r *http.Request) {
	roh := strings.TrimSpace(r.URL.Query().Get("ip"))
	if roh == "" {
		http.Error(w, "the ip parameter is missing", http.StatusBadRequest)
		return
	}

	addr, err := netip.ParseAddr(roh)
	if err != nil {
		http.Error(w, "not a valid address", http.StatusBadRequest)
		return
	}
	addr = addr.Unmap()

	reply := struct {
		IP      string `json:"ip"`
		Name    string `json:"name,omitempty"`
		Mac     string `json:"mac,omitempty"`
		Profile string `json:"profile,omitempty"`
		Known   bool   `json:"known"`
	}{IP: addr.String()}

	if s.res != nil {
		reply.Name = s.res.NameOf(addr)
		reply.Mac = s.res.MacOf(addr)
		reply.Profile = s.res.ProfileNameOf(addr)
	}
	reply.Known = reply.Name != "" || reply.Mac != ""

	writeJSON(w, http.StatusOK, reply)
}

// handleForget throws away the cached answers for a name. Without it a fresh
// exception only takes effect once the negative TTL expires - and to whoever
// just clicked "allow", that looks like it did not work.
func (s *Server) handleForget(w http.ResponseWriter, r *http.Request) {
	name := strings.TrimSpace(r.URL.Query().Get("name"))
	if name == "" {
		http.Error(w, "the name parameter is missing", http.StatusBadRequest)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"ok":       true,
		"name":     name,
		"entfernt": s.res.Forget(name),
	})
}
