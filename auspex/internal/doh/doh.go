// Package doh answers DNS over HTTPS (RFC 8484).
//
// With it a device runs through the filter outside the home network too,
// with no VPN. The server either speaks TLS itself or plain HTTP — then a
// reverse proxy belongs in front, terminating TLS.
package doh

import (
	"encoding/base64"
	"io"
	"net"
	"net/http"
	"net/netip"
	"strings"
	"sync/atomic"

	"github.com/miekg/dns"
)

// maxBody caps a request. DNS messages are small; anything larger is either
// broken or an attempt to consume memory.
const maxBody = dns.MaxMsgSize

type Options struct {
	// Path is the endpoint, conventionally /dns-query.
	Path string
	// TrustedProxies are networks whose X-Forwarded-For is believed.
	// Without this list every request behind a reverse proxy would arrive
	// carrying the proxy's address — and client profiles, learn mode and
	// per-device analysis would be worthless, because there would only be
	// one client left.
	TrustedProxies []netip.Prefix
}

type Handler struct {
	dnsHandler dns.Handler
	opts       Options
	queries    atomic.Int64
	errors     atomic.Int64
}

func NewHandler(h dns.Handler, opts Options) *Handler {
	if opts.Path == "" {
		opts.Path = "/dns-query"
	}
	return &Handler{dnsHandler: h, opts: opts}
}

// Path is the endpoint being served.
func (h *Handler) Path() string { return h.opts.Path }

func (h *Handler) Queries() int64 { return h.queries.Load() }
func (h *Handler) Errors() int64  { return h.errors.Load() }

func (h *Handler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	if r.URL.Path != h.opts.Path {
		http.NotFound(w, r)
		return
	}

	raw, err := h.readQuery(r)
	if err != nil {
		h.errors.Add(1)
		http.Error(w, err.Error(), http.StatusBadRequest)
		return
	}

	req := new(dns.Msg)
	if err := req.Unpack(raw); err != nil {
		h.errors.Add(1)
		http.Error(w, "unlesbare DNS-Nachricht", http.StatusBadRequest)
		return
	}
	h.queries.Add(1)

	// TCPAddr, not UDPAddr: over HTTP there is no 512-byte limit, and the
	// answer must not be truncated.
	writer := &captureWriter{remote: &net.TCPAddr{IP: h.clientIP(r), Port: 0}}
	h.dnsHandler.ServeDNS(writer, req)

	if writer.msg == nil {
		h.errors.Add(1)
		http.Error(w, "no answer", http.StatusInternalServerError)
		return
	}
	packed, err := writer.msg.Pack()
	if err != nil {
		h.errors.Add(1)
		http.Error(w, "answer cannot be encoded", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/dns-message")
	// No caching by proxies: the TTL sits inside the answer, and an HTTP
	// cache would not count it down.
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(packed)
}

// readQuery takes the DNS message from the POST body or ?dns=… (RFC 8484).
func (h *Handler) readQuery(r *http.Request) ([]byte, error) {
	switch r.Method {
	case http.MethodPost:
		if ct := r.Header.Get("Content-Type"); ct != "" &&
			!strings.HasPrefix(ct, "application/dns-message") {
			return nil, errBadContentType
		}
		return io.ReadAll(io.LimitReader(r.Body, maxBody))

	case http.MethodGet:
		q := r.URL.Query().Get("dns")
		if q == "" {
			return nil, errMissingParam
		}
		// RFC 8484 prescribes base64url without padding; some clients pad
		// anyway.
		return base64.RawURLEncoding.DecodeString(strings.TrimRight(q, "="))

	default:
		return nil, errMethod
	}
}

// clientIP works out the requester's real address.
func (h *Handler) clientIP(r *http.Request) net.IP {
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		host = r.RemoteAddr
	}
	direct, err := netip.ParseAddr(host)
	if err != nil {
		return net.IPv4zero
	}

	// Believe X-Forwarded-For only from proxies we trust - otherwise any
	// client can invent an origin for itself and hit somebody else's
	// profile or learn store with it.
	if h.trusted(direct) {
		if fwd := r.Header.Get("X-Forwarded-For"); fwd != "" {
			// The first entry is the original client.
			first := strings.TrimSpace(strings.Split(fwd, ",")[0])
			if addr, err := netip.ParseAddr(first); err == nil {
				return net.IP(addr.AsSlice())
			}
		}
	}
	return net.IP(direct.AsSlice())
}

func (h *Handler) trusted(addr netip.Addr) bool {
	for _, prefix := range h.opts.TrustedProxies {
		if prefix.Contains(addr) {
			return true
		}
	}
	return false
}

// captureWriter takes the resolver's answer instead of writing it to a
// socket.
type captureWriter struct {
	remote net.Addr
	msg    *dns.Msg
}

func (c *captureWriter) LocalAddr() net.Addr  { return &net.TCPAddr{IP: net.IPv4zero} }
func (c *captureWriter) RemoteAddr() net.Addr { return c.remote }
func (c *captureWriter) WriteMsg(m *dns.Msg) error {
	c.msg = m
	return nil
}
func (c *captureWriter) Write([]byte) (int, error) { return 0, nil }
func (c *captureWriter) Close() error              { return nil }
func (c *captureWriter) TsigStatus() error         { return nil }
func (c *captureWriter) TsigTimersOnly(bool)       {}
func (c *captureWriter) Hijack()                   {}

type dohError string

func (e dohError) Error() string { return string(e) }

const (
	errBadContentType = dohError("Content-Type has to be application/dns-message")
	errMissingParam   = dohError("the dns parameter is missing")
	errMethod         = dohError("GET and POST only")
)
