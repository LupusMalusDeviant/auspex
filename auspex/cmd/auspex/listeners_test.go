package main

import (
	"io"
	"log/slog"
	"net"
	"testing"
	"time"

	"github.com/miekg/dns"
)

func quiet() *slog.Logger { return slog.New(slog.NewTextHandler(io.Discard, nil)) }

// fastRetries turns seconds into milliseconds for the duration of one test.
func fastRetries(t *testing.T) {
	t.Helper()
	oldFirst, oldMax := firstRetry, maxRetry
	firstRetry, maxRetry = 20*time.Millisecond, 50*time.Millisecond
	t.Cleanup(func() { firstRetry, maxRetry = oldFirst, oldMax })
}

// waitFor polls until cond holds, or gives up. Polling rather than sleeping a
// fixed time: the point is what happens, not when.
func waitFor(t *testing.T, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatalf("timed out waiting for: %s", what)
}

// The whole reason optional listeners exist. Auspex starts before the tunnel
// interface is there, the address cannot be bound — and the resolver has to
// pick it up on its own once it appears.
//
// Without the retry, "optional" would be strictly worse than the crash it
// replaces: a crash heals itself through the restart policy, a silently
// missing listener does not.
func TestAnOptionalListenerKeepsTryingUntilTheAddressAppears(t *testing.T) {
	fastRetries(t)

	// Occupying the address is the closest stand-in for "the interface is
	// not there yet": the bind fails for a reason outside the listener.
	blocker, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	addr := blocker.LocalAddr().String()

	l := newOptionalListener("udp", addr, dns.HandlerFunc(func(dns.ResponseWriter, *dns.Msg) {}), quiet())
	stop := make(chan struct{})
	done := make(chan struct{})
	go func() { defer close(done); l.serve(stop) }()

	waitFor(t, "the first attempt to fail", func() bool { return l.state().Attempts >= 1 })
	if s := l.state(); s.Up {
		t.Fatal("reports itself up although the address was occupied")
	} else if s.Error == "" {
		t.Error("no reason given for the failure")
	}

	// The address becomes free — as it does when the tunnel comes up.
	blocker.Close()

	waitFor(t, "the listener to come up on its own", func() bool { return l.state().Up })
	if s := l.state(); s.Error != "" {
		t.Errorf("still carries an error after coming up: %s", s.Error)
	}

	close(stop)
	l.shutdown(t.Context())
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("serve did not return after stop — that hangs the shutdown at wg.Wait()")
	}
}

// An address that is there right away must not be treated as a special case.
func TestAnOptionalListenerThatWorksComesUpAtOnce(t *testing.T) {
	fastRetries(t)

	probe, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	addr := probe.LocalAddr().String()
	probe.Close()

	l := newOptionalListener("udp", addr, dns.HandlerFunc(func(dns.ResponseWriter, *dns.Msg) {}), quiet())
	stop := make(chan struct{})
	done := make(chan struct{})
	go func() { defer close(done); l.serve(stop) }()

	waitFor(t, "the listener to come up", func() bool { return l.state().Up })
	if s := l.state(); s.Attempts != 0 {
		t.Errorf("Attempts = %d, expected 0 for an address that was free", s.Attempts)
	}

	close(stop)
	l.shutdown(t.Context())
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("serve did not return after stop")
	}
}

// Shutdown while it is still failing has to end the loop too. Otherwise a
// resolver whose tunnel never comes up would refuse to terminate, and the
// container would need killing instead of stopping.
func TestAFailingOptionalListenerStillStops(t *testing.T) {
	fastRetries(t)

	blocker, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer blocker.Close()

	l := newOptionalListener("udp", blocker.LocalAddr().String(),
		dns.HandlerFunc(func(dns.ResponseWriter, *dns.Msg) {}), quiet())
	stop := make(chan struct{})
	done := make(chan struct{})
	go func() { defer close(done); l.serve(stop) }()

	waitFor(t, "at least two failed attempts", func() bool { return l.state().Attempts >= 2 })

	close(stop)
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("a failing listener does not stop — the process would never terminate")
	}
}

// "Optional" must not turn into "invisible". Whoever asks has to be able to
// tell "up" from "has been trying for two hours".
func TestTheStateIsReadable(t *testing.T) {
	fastRetries(t)

	blocker, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer blocker.Close()
	addr := blocker.LocalAddr().String()

	l := newOptionalListener("udp", addr, dns.HandlerFunc(func(dns.ResponseWriter, *dns.Msg) {}), quiet())
	stop := make(chan struct{})
	go l.serve(stop)
	defer close(stop)

	waitFor(t, "a failure to be recorded", func() bool { return l.state().Attempts >= 1 })

	s := l.state()
	if s.Network != "udp" || s.Address != addr {
		t.Errorf("state does not name the listener: %+v", s)
	}
	if s.Up || s.Error == "" {
		t.Errorf("a failing listener has to say so: %+v", s)
	}
}
