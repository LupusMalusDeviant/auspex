// Package querylog keeps the most recent queries in memory and optionally
// records them as JSONL. Every entry carries the rule that triggered it and
// where that rule came from — that is the answer to "why was this blocked?".
package querylog

import (
	"bufio"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"net/netip"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"
)

// Entry is one answered query.
type Entry struct {
	// Seq rises monotonically and is the cursor for the control plane.
	Seq    int64     `json:"seq"`
	Time   time.Time `json:"time"`
	Client string    `json:"client"`
	// ClientName is the device name, where known.
	ClientName string `json:"client_name,omitempty"`
	Profile    string `json:"profile,omitempty"`
	Name       string `json:"name"`
	// Domain is the registrable domain (eTLD+1). The analysis groups by it
	// rather than counting every CDN host name separately.
	Domain string `json:"domain,omitempty"`
	Type   string `json:"type"`
	Action string `json:"action"` // allowed | blocked | rewritten | error
	Source string `json:"source"` // cache | upstream | filter | rewrite | stale
	Rule   string `json:"rule,omitempty"`
	// Cname carries the target that caused the block - otherwise a block on
	// a harmless-looking first-party domain would be inexplicable.
	Cname    string `json:"cname,omitempty"`
	RuleKind string `json:"rule_kind,omitempty"`
	List     string `json:"list,omitempty"`
	Schedule string `json:"schedule,omitempty"`
	Upstream string `json:"upstream,omitempty"`
	Rcode    string `json:"rcode"`
	// Validated is the AD bit of the upstream answer: the signature chain
	// was checked. Only meaningful with dnssec: enforce.
	Validated bool     `json:"validated,omitempty"`
	Answers   []string `json:"answers,omitempty"`
	Millis    float64  `json:"ms"`
	Error     string   `json:"error,omitempty"`
}

type Options struct {
	Enabled   bool
	Size      int
	File      string
	Anonymize bool
}

// Log is a ring buffer plus an optional file transcript.
type Log struct {
	opts Options

	// boot changes on every start. The control plane uses it to tell that it
	// has to reset its cursor.
	boot string

	mu      sync.RWMutex
	entries []Entry
	next    int
	count   int
	seq     int64
	total   int64
	blocked int64

	fileMu sync.Mutex
	file   *os.File
	writer *bufio.Writer
}

func New(opts Options) (*Log, error) {
	if opts.Size <= 0 {
		opts.Size = 10_000
	}
	l := &Log{
		opts:    opts,
		entries: make([]Entry, opts.Size),
		boot:    bootID(),
	}
	if opts.Enabled && opts.File != "" {
		f, err := os.OpenFile(opts.File, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o640)
		if err != nil {
			return nil, err
		}
		l.file, l.writer = f, bufio.NewWriter(f)
	}
	return l, nil
}

// bootID has to be unique even when two instances start within the same
// clock tick — the Windows system clock resolves more coarsely than a
// nanosecond, so a timestamp alone is not enough.
func bootID() string {
	var b [8]byte
	if _, err := rand.Read(b[:]); err != nil {
		return strconv.FormatInt(time.Now().UnixNano(), 36)
	}
	return hex.EncodeToString(b[:])
}

func (l *Log) Add(e Entry) {
	if l == nil || !l.opts.Enabled {
		return
	}
	if l.opts.Anonymize {
		e.Client = anonymize(e.Client)
		// The name identifies the device just as uniquely as the address -
		// leaving it in would defeat the anonymisation.
		e.ClientName = ""
	}

	l.mu.Lock()
	l.seq++
	e.Seq = l.seq
	l.entries[l.next] = e
	l.next = (l.next + 1) % len(l.entries)
	if l.count < len(l.entries) {
		l.count++
	}
	l.total++
	if e.Action == "blocked" {
		l.blocked++
	}
	l.mu.Unlock()

	if l.writer != nil {
		l.fileMu.Lock()
		if data, err := json.Marshal(e); err == nil {
			l.writer.Write(data)
			l.writer.WriteByte('\n')
		}
		l.fileMu.Unlock()
	}
}

// Recent returns the most recent entries, newest first.
func (l *Log) Recent(limit int) []Entry {
	l.mu.RLock()
	defer l.mu.RUnlock()
	if limit <= 0 || limit > l.count {
		limit = l.count
	}
	out := make([]Entry, 0, limit)
	for i := 0; i < limit; i++ {
		idx := (l.next - 1 - i + len(l.entries)*2) % len(l.entries)
		out = append(out, l.entries[idx])
	}
	return out
}

// Batch is a cursor query for the control plane.
type Batch struct {
	Boot    string  `json:"boot"`
	Next    int64   `json:"next"`
	Entries []Entry `json:"entries"`
	// Lost counts entries that went missing between since and the oldest
	// entry still present — the ring buffer overflowed because the collector
	// was too slow. Better reported than silently dropped.
	Lost int64 `json:"lost"`
}

// Since returns every entry after the cursor, oldest first.
func (l *Log) Since(since int64, limit int) Batch {
	l.mu.RLock()
	defer l.mu.RUnlock()

	if limit <= 0 {
		limit = 1000
	}
	batch := Batch{Boot: l.boot, Next: since, Entries: []Entry{}}
	if l.count == 0 {
		return batch
	}

	size := len(l.entries)
	oldest := ((l.next-l.count)%size + size) % size
	if first := l.entries[oldest].Seq; since > 0 && first > since+1 {
		batch.Lost = first - since - 1
	}

	for i := 0; i < l.count && len(batch.Entries) < limit; i++ {
		e := l.entries[(oldest+i)%size]
		if e.Seq <= since {
			continue
		}
		batch.Entries = append(batch.Entries, e)
	}
	if n := len(batch.Entries); n > 0 {
		batch.Next = batch.Entries[n-1].Seq
	}
	return batch
}

type Summary struct {
	Total   int64 `json:"total"`
	Blocked int64 `json:"blocked"`
	Buffer  int   `json:"buffer"`
}

func (l *Log) Summary() Summary {
	l.mu.RLock()
	defer l.mu.RUnlock()
	return Summary{Total: l.total, Blocked: l.blocked, Buffer: l.count}
}

// Flush schreibt die Dateimitschrift raus (Aufrufer: Ticker + Shutdown).
func (l *Log) Flush() {
	if l == nil || l.writer == nil {
		return
	}
	l.fileMu.Lock()
	l.writer.Flush()
	l.fileMu.Unlock()
}

func (l *Log) Close() error {
	if l == nil || l.file == nil {
		return nil
	}
	l.Flush()
	return l.file.Close()
}

// anonymize truncates IPv4 to /24 and IPv6 to /48.
func anonymize(client string) string {
	host := client
	if h, _, err := splitHostPort(client); err == nil {
		host = h
	}
	addr, err := netip.ParseAddr(host)
	if err != nil {
		return client
	}
	if addr.Is4() {
		b := addr.As4()
		b[3] = 0
		return netip.AddrFrom4(b).String() + "/24"
	}
	b := addr.As16()
	for i := 6; i < 16; i++ {
		b[i] = 0
	}
	return netip.AddrFrom16(b).String() + "/48"
}

func splitHostPort(s string) (string, string, error) {
	i := strings.LastIndexByte(s, ':')
	if i < 0 {
		return s, "", errNoPort
	}
	if strings.Contains(s[:i], ":") && !strings.HasPrefix(s, "[") {
		return s, "", errNoPort // bare IPv6 without a port
	}
	return strings.Trim(s[:i], "[]"), s[i+1:], nil
}

var errNoPort = errNoPortType{}

type errNoPortType struct{}

func (errNoPortType) Error() string { return "no port" }
