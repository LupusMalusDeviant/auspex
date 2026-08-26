// Auspex is a filtering DNS resolver: blocks by rule list, caches with
// correct TTLs and talks to its upstreams over DoH/DoT.
package main

import (
	"context"
	"crypto/tls"
	"errors"
	"flag"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"

	"github.com/miekg/dns"

	"auspex/internal/api"
	"auspex/internal/cache"
	"auspex/internal/clients"
	"auspex/internal/config"
	"auspex/internal/doh"
	"auspex/internal/learn"
	"auspex/internal/lists"
	"auspex/internal/names"
	"auspex/internal/neigh"
	"auspex/internal/querylog"
	"auspex/internal/resolver"
	"auspex/internal/upstream"
)

var version = "0.10.0"

func main() {
	var (
		configPath  = flag.String("config", "config.yaml", "path to the configuration")
		explain     = flag.String("explain", "", "check a domain and exit (do not start the server)")
		exportLearn = flag.String("learn-export", "", "print a profile's allowlist and exit")
		showVer     = flag.Bool("version", false, "print the version and exit")
		verbose     = flag.Bool("v", false, "debug logging")
	)
	flag.Parse()

	if *showVer {
		fmt.Println("auspex", version)
		return
	}

	level := slog.LevelInfo
	if *verbose {
		level = slog.LevelDebug
	}
	log := slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: level}))

	if err := run(*configPath, *explain, *exportLearn, log); err != nil {
		log.Error("could not start", "error", err)
		os.Exit(1)
	}
}

func run(configPath, explain, exportLearn string, log *slog.Logger) error {
	cfg, err := config.Load(configPath)
	if err != nil {
		if !errors.Is(err, os.ErrNotExist) {
			return err
		}
		log.Warn("no configuration found, using defaults", "path", configPath)
		cfg = config.Default()
	}

	listStore, err := lists.OpenStore(cfg.Filter.CacheDir)
	if err != nil {
		return fmt.Errorf("Listenverwaltung: %w", err)
	}
	clientStore, err := clients.Open(cfg.Filter.CacheDir)
	if err != nil {
		return fmt.Errorf("device management: %w", err)
	}
	loader := lists.NewLoader(cfg.Filter, listStore, log)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
	engine, err := loader.Build(ctx, cfg.Filter, false)
	cancel()
	if err != nil {
		return err
	}

	bootstrap := upstream.Bootstrap(cfg.Upstream.Bootstrap, cfg.Upstream.Timeout.D())
	targets := make([]upstream.Upstream, 0, len(cfg.Upstream.Servers))
	for _, raw := range cfg.Upstream.Servers {
		u, err := upstream.Parse(raw, cfg.Upstream.Timeout.D(), bootstrap)
		if err != nil {
			return err
		}
		targets = append(targets, u)
		log.Info("upstream configured", "target", u.Addr(), "protocol", u.Proto())
	}
	pool := upstream.NewPool(targets, upstream.PoolOptions{
		Strategy:         cfg.Upstream.Strategy,
		FailureThreshold: cfg.Upstream.FailureThreshold,
		FailureCooldown:  cfg.Upstream.FailureCooldown.D(),
		Log:              log,
	})

	dnsCache := cache.New(cache.Options{
		MaxEntries:        cfg.Cache.MaxEntries,
		MinTTL:            cfg.Cache.MinTTL.D(),
		MaxTTL:            cfg.Cache.MaxTTL.D(),
		NegativeTTL:       cfg.Cache.NegativeTTL.D(),
		Prefetch:          cfg.Cache.Prefetch,
		PrefetchThreshold: cfg.Cache.PrefetchThreshold,
		PrefetchMinHits:   cfg.Cache.PrefetchMinHits,
		ServeStale:        cfg.Cache.ServeStale.D(),
	})

	qlog, err := querylog.New(querylog.Options{
		Enabled:   cfg.QueryLog.Enabled,
		Size:      cfg.QueryLog.Size,
		File:      cfg.QueryLog.File,
		Anonymize: cfg.QueryLog.AnonymizeClients,
	})
	if err != nil {
		return fmt.Errorf("Query-Log: %w", err)
	}
	defer qlog.Close()

	learnMgr, err := learn.NewManager(cfg.Learning.Dir, log)
	if err != nil {
		return err
	}

	// The profiles from the file are kept unchanged - on reload the managed
	// part is mixed back in fresh.
	configClients := append([]config.Client{}, cfg.Clients...)

	// The route via the neighbour table: address -> MAC -> name.
	//
	// Without it devices stay nameless under IPv6. Windows and Android
	// rotate their temporary addresses daily, and the router answers no
	// reverse lookup for them - it does not know these addresses, the
	// devices make them up themselves and tell nobody.
	var neighbours *neigh.Table
	var deviceNames *names.DeviceNames
	if cfg.Hosts.Neighbors {
		if ok, reason := neigh.Available(); ok {
			neighbours = neigh.New(30 * time.Second)
			neighbours.Refresh()
			deviceNames = names.NewDeviceNames(cfg.Hosts.DeviceNamePath, time.Minute)
			log.Info("device identity through the neighbour table",
				"neighbours", neighbours.Len(), "names", deviceNames.Len(),
				"list", cfg.Hosts.DeviceNamePath)
		} else {
			// Usually: the container runs in a network namespace of its own.
			// Then what stands there is that namespace's table, which is
			// close to nothing.
			log.Warn("the neighbour table cannot be read - devices stay nameless under IPv6",
				"reason", reason)
		}
	}

	hostNames, err := names.New(names.Options{
		Static:      cfg.Hosts.Static,
		Resolve:     cfg.Hosts.Resolve,
		Via:         cfg.Hosts.Via,
		ReverseVia:  cfg.Hosts.ReverseVia,
		TTL:         cfg.Hosts.TTL.D(),
		NegativeTTL: cfg.Hosts.NegativeTTL.D(),
		Timeout:     cfg.Hosts.Timeout.D(),
		Neighbors:   neighbours,
		DeviceNames: deviceNames,
	})
	if err != nil {
		return fmt.Errorf("hosts: %w", err)
	}
	if cfg.Hosts.Resolve {
		log.Info("device names are being resolved", "via", cfg.Hosts.Via, "fest", len(cfg.Hosts.Static))
	}

	// Managed profiles right at startup, so they survive a restart.
	cfg.Clients = clientStore.Merge(cfg.Clients)

	res, err := resolver.New(cfg, engine, dnsCache, pool, qlog, learnMgr, hostNames)
	if err != nil {
		return err
	}
	for _, st := range res.LearnStats() {
		log.Info("learn mode active",
			"profile", st.Profile, "policy", st.Policy,
			"names", st.Names, "domains", st.Domains)
	}

	// -explain: ask once why a domain is being blocked.
	if explain != "" {
		exp := res.Explain(explain, "")
		fmt.Printf("%-12s %s\n", "Domain:", exp.Name)
		fmt.Printf("%-12s %v\n", "Blocked:", exp.Blocked)
		if exp.Rule != "" {
			fmt.Printf("%-12s %s (%s)\n", "Rule:", exp.Rule, exp.RuleKind)
			fmt.Printf("%-12s %s:%d\n", "Origin:", exp.List, exp.Line)
		}
		fmt.Printf("%-12s %s\n", "Reason:", exp.Reason)
		return nil
	}

	// -learn-export: print the learned allowlist, ready to paste into the
	// configuration.
	if exportLearn != "" {
		store, _, ok := res.LearnStore(exportLearn)
		if !ok {
			return fmt.Errorf("profile %q is not learning (set policy: learn or enforce)", exportLearn)
		}
		st := store.Stats("")
		fmt.Println("# Allowlist for profile " + exportLearn)
		fmt.Println(fmt.Sprintf("# %d names, %d domains, learned since %s",
			st.Names, st.Domains, st.Created.Format("2006-01-02 15:04")))
		if st.Overflow {
			fmt.Println("# WARNING: limit reached - the store is incomplete")
		}
		for _, rule := range store.Allowlist(learn.Granularity(cfg.Learning.Granularity)) {
			fmt.Println("    - \"" + rule + "\"")
		}
		return nil
	}

	reload := func(ctx context.Context, force bool) error {
		fresh, err := loader.Build(ctx, cfg.Filter, force)
		if err != nil {
			return err
		}
		res.SetEngine(fresh)
		// Profiles belong in there too: otherwise a mapping changed in the
		// browser only takes effect after a restart.
		return res.SetClients(clientStore.Merge(configClients))
	}

	var servers []*dns.Server
	var optionalListeners []*optionalListener
	var wg sync.WaitGroup

	// Declared up here rather than next to the maintenance goroutine: the
	// optional listeners retry in the background and have to learn about
	// shutdown, otherwise they would keep the process alive at wg.Wait().
	stop := make(chan struct{})

	// A listener that does not come up is fatal: a resolver answering only
	// on TCP looks healthy in the log and in practice answers not a single
	// query. Addresses explicitly marked optional are the exception — and
	// they retry instead of shrugging, see listeners.go for why that
	// distinction is the whole point.
	// Large enough for every listener: a blocked sender would hang on
	// wg.Wait() at shutdown.
	fatal := make(chan error,
		len(cfg.Listen.UDP)+len(cfg.Listen.TCP)+len(cfg.Listen.TLS)+len(cfg.Listen.HTTPS)+1)
	startServer := func(network string, a config.Address) {
		if a.Addr == "" {
			return
		}
		if a.Optional {
			l := newOptionalListener(network, a.Addr, res, log)
			optionalListeners = append(optionalListeners, l)
			wg.Add(1)
			go func() {
				defer wg.Done()
				l.serve(stop)
			}()
			return
		}
		srv := &dns.Server{Addr: a.Addr, Net: network, Handler: res}
		servers = append(servers, srv)
		wg.Add(1)
		go func() {
			defer wg.Done()
			log.Info("DNS is listening", "network", network, "address", a.Addr)
			if err := srv.ListenAndServe(); err != nil {
				fatal <- fmt.Errorf("%s listener on %s: %w", network, a.Addr, err)
			}
		}()
	}
	for _, addr := range cfg.Listen.UDP {
		startServer("udp", addr)
	}
	for _, addr := range cfg.Listen.TCP {
		startServer("tcp", addr)
	}

	// Encrypted for clients: DoT and DoH. With those a device runs through
	// the filter outside the home network too, with no VPN.
	var tlsConfig *tls.Config
	if cfg.Listen.CertFile != "" && cfg.Listen.KeyFile != "" {
		cert, err := tls.LoadX509KeyPair(cfg.Listen.CertFile, cfg.Listen.KeyFile)
		if err != nil {
			return fmt.Errorf("Zertifikat: %w", err)
		}
		tlsConfig = &tls.Config{Certificates: []tls.Certificate{cert}, MinVersion: tls.VersionTLS12}
	}

	for _, a := range cfg.Listen.TLS {
		srv := &dns.Server{Addr: a.Addr, Net: "tcp-tls", TLSConfig: tlsConfig, Handler: res}
		servers = append(servers, srv)
		wg.Add(1)
		go func(addr string) {
			defer wg.Done()
			log.Info("DNS-over-TLS is listening", "address", addr)
			if err := srv.ListenAndServe(); err != nil {
				fatal <- fmt.Errorf("DoT listener on %s: %w", addr, err)
			}
		}(a.Addr)
	}

	dohHandler := doh.NewHandler(res, doh.Options{
		Path:           cfg.Listen.DoHPath,
		TrustedProxies: cfg.Listen.TrustedPrefixes(),
	})
	var dohServers []*http.Server
	for _, a := range cfg.Listen.HTTPS {
		srv := &http.Server{
			Addr:              a.Addr,
			Handler:           dohHandler,
			TLSConfig:         tlsConfig,
			ReadHeaderTimeout: 10 * time.Second,
		}
		dohServers = append(dohServers, srv)
		wg.Add(1)
		go func(addr string) {
			defer wg.Done()
			var err error
			if tlsConfig != nil {
				log.Info("DNS-over-HTTPS is listening", "address", addr, "path", dohHandler.Path())
				err = srv.ListenAndServeTLS("", "")
			} else {
				// Without a certificate, plaintext only: then a reverse proxy
				// belongs in front, terminating TLS.
				log.Warn("DNS-over-HTTPS is listening in the clear - run it behind a reverse proxy only",
					"address", addr, "path", dohHandler.Path())
				err = srv.ListenAndServe()
			}
			if err != nil && err != http.ErrServerClosed {
				fatal <- fmt.Errorf("DoH listener on %s: %w", addr, err)
			}
		}(a.Addr)
	}

	var apiSrv *api.Server
	if cfg.API.Enabled {
		apiSrv = api.New(cfg.API, res, reload, version, log, dohHandler, listStore, clientStore)
		if err := apiSrv.Start(); err != nil {
			return err
		}
	}

	// Refresh lists on a schedule; Flush keeps the JSONL current.
	go maintenance(cfg, reload, qlog, learnMgr, log, stop)

	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt, syscall.SIGTERM, syscall.SIGHUP)

	var runErr error
loop:
	for {
		select {
		case err := <-fatal:
			runErr = err
			break loop
		case s := <-sig:
			if s == syscall.SIGHUP {
				log.Info("SIGHUP: reloading the rule set")
				ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
				if err := reload(ctx, true); err != nil {
					log.Error("reload failed", "error", err)
				}
				cancel()
				continue
			}
			log.Info("shutting down", "signal", s.String())
			break loop
		}
	}

	close(stop)
	shutdownCtx, cancelShutdown := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancelShutdown()
	if apiSrv != nil {
		_ = apiSrv.Shutdown(shutdownCtx)
	}
	for _, srv := range servers {
		_ = srv.ShutdownContext(shutdownCtx)
	}
	for _, l := range optionalListeners {
		l.shutdown(shutdownCtx)
	}
	for _, srv := range dohServers {
		_ = srv.Shutdown(shutdownCtx)
	}
	qlog.Flush()
	learnMgr.SaveAll()
	wg.Wait()
	return runErr
}

func maintenance(cfg config.Config, reload api.ReloadFunc, qlog *querylog.Log, learnMgr *learn.Manager, log *slog.Logger, stop <-chan struct{}) {
	interval := cfg.Filter.UpdateInterval.D()
	var updates <-chan time.Time
	if interval > 0 {
		t := time.NewTicker(interval)
		defer t.Stop()
		updates = t.C
	}
	flush := time.NewTicker(5 * time.Second)
	defer flush.Stop()

	saveEvery := cfg.Learning.SaveInterval.D()
	if saveEvery <= 0 {
		saveEvery = 30 * time.Second
	}
	saveLearn := time.NewTicker(saveEvery)
	defer saveLearn.Stop()

	for {
		select {
		case <-stop:
			return
		case <-flush.C:
			qlog.Flush()
		case <-saveLearn.C:
			learnMgr.SaveAll()
		case <-updates:
			log.Info("lists are being updated")
			ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
			if err := reload(ctx, true); err != nil {
				log.Error("list update failed", "error", err)
			}
			cancel()
		}
	}
}
