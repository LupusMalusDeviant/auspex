package main

import (
	"context"
	"log/slog"
	"sync"
	"time"

	"github.com/miekg/dns"
)

// How long to wait between two attempts at an optional address. Starts short,
// because the usual case is a tunnel that is three seconds behind the
// container; grows, because an address that is not coming back should not
// produce a line a second for the rest of the day.
//
// Variables rather than constants so the test can turn seconds into
// milliseconds. A test that has to wait two seconds to watch one retry gets
// shortened by whoever is in a hurry, and then it watches nothing.
var (
	firstRetry = 2 * time.Second
	maxRetry   = time.Minute
)

// optionalListener serves an address that is allowed not to exist yet.
//
// # Why this is not simply "ignore the error"
//
// A listener that does not come up is fatal for a good reason: a resolver
// answering only on TCP looks healthy in the log and in practice answers
// nothing. Making a listener optional therefore has to be the narrower
// statement — "this one address may be late" — and not "failures do not
// matter".
//
// The retry is the part that makes it honest. Without it an optional address
// that lost the race at boot would be gone until somebody restarted the
// container, silently, and that is strictly worse than the crash it replaces:
// a crash heals itself through the restart policy, a silent gap does not.
//
// The state is readable, so the difference between "up" and "has been trying
// for two hours" does not live in the log alone.
type optionalListener struct {
	network string
	addr    string
	handler dns.Handler
	log     *slog.Logger

	mu       sync.Mutex
	current  *dns.Server
	up       bool
	attempts int
	lastErr  error
}

func newOptionalListener(network, addr string, h dns.Handler, log *slog.Logger) *optionalListener {
	return &optionalListener{network: network, addr: addr, handler: h, log: log}
}

// serve blocks until stop is closed. Every attempt gets a server of its own:
// a dns.Server that failed to bind carries the state of that attempt, and
// reusing it is not part of its contract.
func (l *optionalListener) serve(stop <-chan struct{}) {
	wait := firstRetry
	for {
		srv := &dns.Server{Addr: l.addr, Net: l.network, Handler: l.handler}
		srv.NotifyStartedFunc = func() {
			l.mu.Lock()
			first := l.attempts
			l.up, l.lastErr = true, nil
			l.mu.Unlock()
			if first == 0 {
				l.log.Info("optional listener is up", "network", l.network, "address", l.addr)
			} else {
				// Deliberately louder than the ordinary start: somebody who
				// saw the failure should also see that it resolved itself.
				l.log.Info("optional listener is up after all",
					"network", l.network, "address", l.addr, "attempts", first+1)
			}
			wait = firstRetry
		}

		l.mu.Lock()
		l.current = srv
		l.mu.Unlock()

		err := srv.ListenAndServe()

		l.mu.Lock()
		l.up = false
		l.attempts++
		attempt := l.attempts
		l.lastErr = err
		l.mu.Unlock()

		// Shutdown closes the server and returns without an error. Both are
		// reasons to stop, and neither is a fault.
		select {
		case <-stop:
			return
		default:
		}
		if err == nil {
			return
		}

		// The first failure is an error: at that moment nobody knows yet
		// whether the address is late or gone. Afterwards a warning, which
		// the growing wait already rations to about one a minute.
		if attempt == 1 {
			l.log.Error("optional listener did not come up, trying again",
				"network", l.network, "address", l.addr, "error", err, "again_in", wait)
		} else {
			l.log.Warn("optional listener still not up",
				"network", l.network, "address", l.addr,
				"error", err, "attempts", attempt, "again_in", wait)
		}

		select {
		case <-time.After(wait):
		case <-stop:
			return
		}
		if wait *= 2; wait > maxRetry {
			wait = maxRetry
		}
	}
}

func (l *optionalListener) shutdown(ctx context.Context) {
	l.mu.Lock()
	srv := l.current
	l.mu.Unlock()
	if srv != nil {
		_ = srv.ShutdownContext(ctx)
	}
}

// listenerState is what the outside gets to see. "Optional" must not become
// "invisible".
type listenerState struct {
	Network  string `json:"network"`
	Address  string `json:"address"`
	Up       bool   `json:"up"`
	Attempts int    `json:"attempts"`
	Error    string `json:"error,omitempty"`
}

func (l *optionalListener) state() listenerState {
	l.mu.Lock()
	defer l.mu.Unlock()
	s := listenerState{Network: l.network, Address: l.addr, Up: l.up, Attempts: l.attempts}
	if l.lastErr != nil && !l.up {
		s.Error = l.lastErr.Error()
	}
	return s
}
