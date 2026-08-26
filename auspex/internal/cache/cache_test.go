package cache

import (
	"testing"
	"time"

	"github.com/miekg/dns"
)

func msgWithA(name string, ttl uint32) *dns.Msg {
	m := new(dns.Msg)
	m.SetQuestion(dns.Fqdn(name), dns.TypeA)
	m.Response = true
	m.Answer = []dns.RR{&dns.A{
		Hdr: dns.RR_Header{Name: dns.Fqdn(name), Rrtype: dns.TypeA, Class: dns.ClassINET, Ttl: ttl},
		A:   []byte{93, 184, 216, 34},
	}}
	return m
}

func TestGetCountsDownTTL(t *testing.T) {
	c := New(Options{MaxEntries: 10, MinTTL: 0, MaxTTL: time.Hour})
	c.Set("k", msgWithA("example.com", 300))

	// Bring the expiry forward artificially rather than waiting 60 seconds.
	c.mu.Lock()
	e := c.items["k"].Value.(*entry)
	e.stored = e.stored.Add(-60 * time.Second)
	e.expires = e.expires.Add(-60 * time.Second)
	c.mu.Unlock()

	got, meta := c.Get("k")
	if !meta.Hit {
		t.Fatal("a match was expected")
	}
	if ttl := got.Answer[0].Header().Ttl; ttl > 241 || ttl < 239 {
		t.Errorf("TTL = %d, expected ~240 (the remaining time, not the original value)", ttl)
	}
}

func TestExpiredWithoutStaleIsMiss(t *testing.T) {
	c := New(Options{MaxEntries: 10, ServeStale: 0})
	c.Set("k", msgWithA("example.com", 1))

	c.mu.Lock()
	c.items["k"].Value.(*entry).expires = time.Now().Add(-time.Second)
	c.mu.Unlock()

	if got, meta := c.Get("k"); got != nil || meta.Hit {
		t.Error("an expired entry without serve_stale has to be a miss")
	}
}

func TestServeStale(t *testing.T) {
	c := New(Options{MaxEntries: 10, ServeStale: time.Hour})
	c.Set("k", msgWithA("example.com", 1))

	c.mu.Lock()
	c.items["k"].Value.(*entry).expires = time.Now().Add(-time.Minute)
	c.mu.Unlock()

	got, meta := c.Get("k")
	if got == nil || !meta.Stale {
		t.Fatal("an expired entry should be served as stale")
	}
	if got.Answer[0].Header().Ttl != 1 {
		t.Errorf("stale TTL = %d, expected 1", got.Answer[0].Header().Ttl)
	}
}

func TestNegativeUsesSOAMinTTL(t *testing.T) {
	c := New(Options{MaxEntries: 10, NegativeTTL: time.Hour})
	m := new(dns.Msg)
	m.SetQuestion("nx.example.", dns.TypeA)
	m.Response = true
	m.Rcode = dns.RcodeNameError
	m.Ns = []dns.RR{&dns.SOA{
		Hdr:    dns.RR_Header{Name: "example.", Rrtype: dns.TypeSOA, Class: dns.ClassINET, Ttl: 3600},
		Minttl: 60,
	}}

	if ttl := c.ttlFor(m); ttl != time.Minute {
		t.Errorf("negative TTL = %v, expected 1m (the SOA minimum beats negative_ttl)", ttl)
	}
}

func TestServfailIsNotCached(t *testing.T) {
	c := New(Options{MaxEntries: 10})
	m := new(dns.Msg)
	m.SetQuestion("example.com.", dns.TypeA)
	m.Rcode = dns.RcodeServerFailure
	c.Set("k", m)

	if got, _ := c.Get("k"); got != nil {
		t.Error("SERVFAIL must not be cached")
	}
}

func TestLRUEviction(t *testing.T) {
	c := New(Options{MaxEntries: 2})
	c.Set("a", msgWithA("a.example", 300))
	c.Set("b", msgWithA("b.example", 300))
	c.Get("a") // a is touched freshly
	c.Set("c", msgWithA("c.example", 300))

	if got, _ := c.Get("b"); got != nil {
		t.Error("b was unused longest and should have been evicted")
	}
	if got, _ := c.Get("a"); got == nil {
		t.Error("a was just used and should still be there")
	}
}

func TestKeySeparatesDNSSEC(t *testing.T) {
	q := dns.Question{Name: "example.com.", Qtype: dns.TypeA, Qclass: dns.ClassINET}
	if Key(q, false) == Key(q, true) {
		t.Error("the DO bit has to change the cache key")
	}
}

// Whoever allows a domain and reloads the page would otherwise keep getting
// the cached NXDOMAIN - the exception would be set but only take effect once
// the negative TTL expired. From the user's point of view it would simply be
// broken.
func TestForgetOnlyDropsTheNameMeant(t *testing.T) {
	c := New(Options{MaxEntries: 100, MaxTTL: time.Hour})

	set := func(name string, typ uint16) {
		c.Set(Key(dns.Question{Name: name, Qtype: typ, Qclass: dns.ClassINET}, false),
			msgWithA(name, 300))
	}

	set("blocked.example.", dns.TypeA)
	set("blocked.example.", dns.TypeAAAA)
	set("other.example.", dns.TypeA)

	dropped := c.Forget("blocked.example")
	if dropped != 2 {
		t.Errorf("expected 2 removed entries (A and AAAA), got %d", dropped)
	}

	if m, _ := c.Get(Key(dns.Question{Name: "blocked.example.", Qtype: dns.TypeA, Qclass: dns.ClassINET}, false)); m != nil {
		t.Error("the released name is still in the store")
	}
	if m, _ := c.Get(Key(dns.Question{Name: "other.example.", Qtype: dns.TypeA, Qclass: dns.ClassINET}, false)); m == nil {
		t.Error("an unrelated name was removed along with it")
	}
}

func TestForgetTakesTheNameWithAndWithoutADot(t *testing.T) {
	// From the interface comes "example.com", in memory sits "example.com." -
	// both have to hit the same thing.
	for _, written := range []string{"example.com", "example.com.", "EXAMPLE.COM"} {
		c := New(Options{MaxEntries: 10, MaxTTL: time.Hour})
		c.Set(Key(dns.Question{Name: "example.com.", Qtype: dns.TypeA, Qclass: dns.ClassINET}, false),
			msgWithA("example.com.", 300))

		if dropped := c.Forget(written); dropped != 1 {
			t.Errorf("%q: expected 1, got %d", written, dropped)
		}
	}
}

func TestForgetOnAnUnknownNameIsHarmless(t *testing.T) {
	c := New(Options{MaxEntries: 10, MaxTTL: time.Hour})
	if dropped := c.Forget("gibtsnicht.example"); dropped != 0 {
		t.Errorf("expected 0, got %d", dropped)
	}
}
