// Package cache is a TTL-correct DNS cache with LRU eviction, prefetch for
// hot entries and optional serve-stale.
package cache

import (
	"container/list"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/miekg/dns"
)

type Options struct {
	MaxEntries        int
	MinTTL            time.Duration
	MaxTTL            time.Duration
	NegativeTTL       time.Duration
	Prefetch          bool
	PrefetchThreshold float64
	PrefetchMinHits   int
	ServeStale        time.Duration
}

type entry struct {
	key      string
	msg      *dns.Msg
	stored   time.Time
	expires  time.Time
	ttl      time.Duration
	hits     int
	fetching bool
}

// Meta describes how an answer came about.
type Meta struct {
	Hit      bool
	Stale    bool
	Prefetch bool // caller should renew the entry in the background
	Age      time.Duration
}

type Stats struct {
	Entries   int   `json:"entries"`
	Hits      int64 `json:"hits"`
	Misses    int64 `json:"misses"`
	StaleHits int64 `json:"stale_hits"`
	Evictions int64 `json:"evictions"`
}

type Cache struct {
	opts Options

	mu    sync.Mutex
	items map[string]*list.Element
	lru   *list.List
	stats Stats
}

func New(opts Options) *Cache {
	if opts.MaxEntries <= 0 {
		opts.MaxEntries = 10_000
	}
	if opts.PrefetchThreshold <= 0 || opts.PrefetchThreshold >= 1 {
		opts.PrefetchThreshold = 0.15
	}
	return &Cache{opts: opts, items: map[string]*list.Element{}, lru: list.New()}
}

// Key identifies a question. The DO bit belongs in it, otherwise a DNSSEC
// client gets served somebody else's unsigned answer.
func Key(q dns.Question, dnssecOK bool) string {
	var b strings.Builder
	b.Grow(len(q.Name) + 12)
	b.WriteString(strings.ToLower(q.Name))
	b.WriteByte('|')
	b.WriteString(strconv.Itoa(int(q.Qtype)))
	b.WriteByte('|')
	b.WriteString(strconv.Itoa(int(q.Qclass)))
	if dnssecOK {
		b.WriteString("|do")
	}
	return b.String()
}

// Get returns a copy with TTLs counted down.
func (c *Cache) Get(key string) (*dns.Msg, Meta) {
	c.mu.Lock()
	defer c.mu.Unlock()

	el, ok := c.items[key]
	if !ok {
		c.stats.Misses++
		return nil, Meta{}
	}
	e := el.Value.(*entry)
	now := time.Now()
	age := now.Sub(e.stored)

	if now.After(e.expires) {
		// Expired: either serve stale or throw it away.
		if c.opts.ServeStale > 0 && now.Sub(e.expires) <= c.opts.ServeStale {
			e.hits++
			c.stats.StaleHits++
			c.lru.MoveToFront(el)
			return copyWithTTL(e.msg, 1), Meta{Hit: true, Stale: true, Age: age, Prefetch: c.markFetching(e)}
		}
		c.removeElement(el)
		c.stats.Misses++
		return nil, Meta{}
	}

	e.hits++
	c.stats.Hits++
	c.lru.MoveToFront(el)

	remaining := e.expires.Sub(now)
	meta := Meta{Hit: true, Age: age}
	if c.opts.Prefetch && e.hits >= c.opts.PrefetchMinHits &&
		float64(remaining) < float64(e.ttl)*c.opts.PrefetchThreshold {
		meta.Prefetch = c.markFetching(e)
	}
	return copyWithTTL(e.msg, uint32(remaining.Seconds())), meta
}

// markFetching stops ten parallel queries kicking off ten prefetches.
func (c *Cache) markFetching(e *entry) bool {
	if e.fetching {
		return false
	}
	e.fetching = true
	return true
}

// Set stores an answer. Answers that cannot be cached are discarded.
func (c *Cache) Set(key string, msg *dns.Msg) {
	if msg == nil || msg.Truncated {
		return
	}
	switch msg.Rcode {
	case dns.RcodeSuccess, dns.RcodeNameError:
	default:
		return // we do not cache SERVFAIL and friends
	}

	ttl := c.ttlFor(msg)
	if ttl <= 0 {
		return
	}

	c.mu.Lock()
	defer c.mu.Unlock()

	now := time.Now()
	stored := msg.Copy()
	stored.Id = 0

	if el, ok := c.items[key]; ok {
		e := el.Value.(*entry)
		e.msg, e.stored, e.expires, e.ttl, e.fetching = stored, now, now.Add(ttl), ttl, false
		c.lru.MoveToFront(el)
		return
	}
	e := &entry{key: key, msg: stored, stored: now, expires: now.Add(ttl), ttl: ttl}
	c.items[key] = c.lru.PushFront(e)

	for c.lru.Len() > c.opts.MaxEntries {
		if back := c.lru.Back(); back != nil {
			c.removeElement(back)
			c.stats.Evictions++
		}
	}
}

// ttlFor bestimmt die Lebensdauer: kleinste RR-TTL, geklammert auf
// [MinTTL, MaxTTL]; bei NXDOMAIN/NODATA laut RFC 2308 die SOA-Minimum-TTL.
func (c *Cache) ttlFor(msg *dns.Msg) time.Duration {
	negative := msg.Rcode == dns.RcodeNameError || len(msg.Answer) == 0
	if negative {
		ttl := c.opts.NegativeTTL
		for _, rr := range msg.Ns {
			if soa, ok := rr.(*dns.SOA); ok {
				soaTTL := time.Duration(min(soa.Minttl, soa.Hdr.Ttl)) * time.Second
				if soaTTL > 0 {
					ttl = soaTTL
				}
				break
			}
		}
		if c.opts.MaxTTL > 0 && ttl > c.opts.MaxTTL {
			ttl = c.opts.MaxTTL
		}
		return ttl
	}

	smallest := ^uint32(0)
	for _, section := range [][]dns.RR{msg.Answer, msg.Ns} {
		for _, rr := range section {
			if rr.Header().Rrtype == dns.TypeOPT {
				continue
			}
			if t := rr.Header().Ttl; t < smallest {
				smallest = t
			}
		}
	}
	if smallest == ^uint32(0) {
		return 0
	}
	ttl := time.Duration(smallest) * time.Second
	if c.opts.MinTTL > 0 && ttl < c.opts.MinTTL {
		ttl = c.opts.MinTTL
	}
	if c.opts.MaxTTL > 0 && ttl > c.opts.MaxTTL {
		ttl = c.opts.MaxTTL
	}
	return ttl
}

// ClearFetching releases an entry again after a prefetch attempt.
func (c *Cache) ClearFetching(key string) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if el, ok := c.items[key]; ok {
		el.Value.(*entry).fetching = false
	}
}

func (c *Cache) removeElement(el *list.Element) {
	c.lru.Remove(el)
	delete(c.items, el.Value.(*entry).key)
}

func (c *Cache) Purge() {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.items = map[string]*list.Element{}
	c.lru.Init()
}

// Forget throws away every entry for a name, regardless of type and DNSSEC
// flag.
//
// Needed as soon as a rule changes: whoever allows a domain and reloads the
// page would otherwise keep getting the cached NXDOMAIN - the exception
// would be set but only take effect once the negative TTL expired. From the
// user's point of view it would be broken.
//
// Only this one name, not the whole cache: after a longer run that holds
// thousands of answers, and throwing them all away because somebody set one
// exception would be paying dearly.
func (c *Cache) Forget(name string) int {
	gesucht := strings.ToLower(name)
	if !strings.HasSuffix(gesucht, ".") {
		gesucht += "."
	}
	prefix := gesucht + "|"

	c.mu.Lock()
	defer c.mu.Unlock()

	dropped := 0
	for key, el := range c.items {
		if strings.HasPrefix(key, prefix) {
			c.removeElement(el)
			dropped++
		}
	}
	return dropped
}

func (c *Cache) Stats() Stats {
	c.mu.Lock()
	defer c.mu.Unlock()
	s := c.stats
	s.Entries = c.lru.Len()
	return s
}

// copyWithTTL returns a copy whose TTLs show the remaining lifetime.
// Without it a client would see a 300s TTL that has 4s left in the cache.
func copyWithTTL(msg *dns.Msg, ttl uint32) *dns.Msg {
	out := msg.Copy()
	if ttl < 1 {
		ttl = 1
	}
	for _, section := range [][]dns.RR{out.Answer, out.Ns, out.Extra} {
		for _, rr := range section {
			if rr.Header().Rrtype == dns.TypeOPT {
				continue
			}
			rr.Header().Ttl = ttl
		}
	}
	return out
}
