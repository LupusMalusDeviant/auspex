// Package upstream talks to the resolvers in front — in plaintext, over DoT
// or over DoH — and keeps broken targets at arm's length.
package upstream

import (
	"bytes"
	"context"
	"crypto/tls"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"strings"
	"sync/atomic"
	"time"

	"github.com/miekg/dns"
)

// Upstream is a single target.
type Upstream interface {
	Exchange(ctx context.Context, msg *dns.Msg) (*dns.Msg, error)
	Addr() string
	Proto() string
}

// Parse builds a target from the configuration line.
//
//	1.1.1.1                      -> udp://1.1.1.1:53
//	udp://9.9.9.9:53             -> plaintext UDP (falls back to TCP on truncation)
//	tcp://9.9.9.9:53             -> plaintext TCP
//	tls://one.one.one.one:853    -> DoT
//	https://dns.quad9.net/dns-query -> DoH
func Parse(raw string, timeout time.Duration, bootstrap *net.Resolver) (Upstream, error) {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return nil, fmt.Errorf("leerer Upstream")
	}
	if !strings.Contains(raw, "://") {
		raw = "udp://" + raw
	}
	u, err := url.Parse(raw)
	if err != nil {
		return nil, fmt.Errorf("Upstream %q: %w", raw, err)
	}

	dialer := &net.Dialer{Timeout: timeout, Resolver: bootstrap}

	switch u.Scheme {
	case "udp", "tcp":
		addr := withDefaultPort(u.Host, "53")
		return &classic{addr: addr, scheme: u.Scheme, timeout: timeout, dialer: dialer}, nil

	case "tls":
		addr := withDefaultPort(u.Host, "853")
		host, _, _ := net.SplitHostPort(addr)
		return &classic{
			addr: addr, scheme: "tls", timeout: timeout, dialer: dialer,
			tlsConfig: &tls.Config{ServerName: host, MinVersion: tls.VersionTLS12},
		}, nil

	case "https":
		transport := &http.Transport{
			DialContext:         dialer.DialContext,
			ForceAttemptHTTP2:   true,
			MaxIdleConnsPerHost: 4,
			IdleConnTimeout:     90 * time.Second,
			TLSClientConfig:     &tls.Config{MinVersion: tls.VersionTLS12},
		}
		return &doh{
			endpoint: u.String(),
			client:   &http.Client{Transport: transport, Timeout: timeout},
		}, nil
	}
	return nil, fmt.Errorf("upstream %q: scheme %q is not supported", raw, u.Scheme)
}

func withDefaultPort(host, port string) string {
	if _, _, err := net.SplitHostPort(host); err != nil {
		return net.JoinHostPort(host, port)
	}
	return host
}

// classic covers UDP, TCP and DoT — all three are DNS over a socket.
type classic struct {
	addr      string
	scheme    string
	timeout   time.Duration
	dialer    *net.Dialer
	tlsConfig *tls.Config
}

func (c *classic) Addr() string  { return c.scheme + "://" + c.addr }
func (c *classic) Proto() string { return c.scheme }

func (c *classic) Exchange(ctx context.Context, msg *dns.Msg) (*dns.Msg, error) {
	client := &dns.Client{
		Net:       map[string]string{"udp": "udp", "tcp": "tcp", "tls": "tcp-tls"}[c.scheme],
		Timeout:   c.timeout,
		Dialer:    c.dialer,
		TLSConfig: c.tlsConfig,
	}
	resp, _, err := client.ExchangeContext(ctx, msg, c.addr)
	if err != nil {
		return nil, err
	}
	// Truncated UDP answer: the same question again over TCP.
	if resp.Truncated && c.scheme == "udp" {
		tcpClient := &dns.Client{Net: "tcp", Timeout: c.timeout, Dialer: c.dialer}
		if retry, _, rerr := tcpClient.ExchangeContext(ctx, msg, c.addr); rerr == nil {
			return retry, nil
		}
	}
	return resp, nil
}

// doh implementiert RFC 8484 (POST application/dns-message).
type doh struct {
	endpoint string
	client   *http.Client
}

func (d *doh) Addr() string  { return d.endpoint }
func (d *doh) Proto() string { return "https" }

func (d *doh) Exchange(ctx context.Context, msg *dns.Msg) (*dns.Msg, error) {
	// RFC 8484 recommends ID 0 — otherwise every request is made artificially
	// unique for caches along the way.
	out := msg.Copy()
	out.Id = 0
	packed, err := out.Pack()
	if err != nil {
		return nil, err
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, d.endpoint, bytes.NewReader(packed))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/dns-message")
	req.Header.Set("Accept", "application/dns-message")

	resp, err := d.client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		io.Copy(io.Discard, io.LimitReader(resp.Body, 4096))
		return nil, fmt.Errorf("DoH %s: HTTP %d", d.endpoint, resp.StatusCode)
	}
	body, err := io.ReadAll(io.LimitReader(resp.Body, dns.MaxMsgSize))
	if err != nil {
		return nil, err
	}
	answer := new(dns.Msg)
	if err := answer.Unpack(body); err != nil {
		return nil, fmt.Errorf("DoH %s: unreadable answer: %w", d.endpoint, err)
	}
	answer.Id = msg.Id
	return answer, nil
}

// Bootstrap builds the resolver that resolves the host names of the DoT/DoH
// targets. Without it Auspex would ask the system when resolving
// dns.quad9.net — and after setup that points at Auspex itself.
func Bootstrap(servers []string, timeout time.Duration) *net.Resolver {
	if len(servers) == 0 {
		return nil
	}
	addrs := make([]string, 0, len(servers))
	for _, s := range servers {
		addrs = append(addrs, withDefaultPort(strings.TrimSpace(s), "53"))
	}
	var counter uint32
	return &net.Resolver{
		PreferGo: true,
		Dial: func(ctx context.Context, network, _ string) (net.Conn, error) {
			d := net.Dialer{Timeout: timeout}
			start := int(atomic.AddUint32(&counter, 1))
			var lastErr error
			for i := range addrs {
				conn, err := d.DialContext(ctx, network, addrs[(start+i)%len(addrs)])
				if err == nil {
					return conn, nil
				}
				lastErr = err
			}
			return nil, lastErr
		},
	}
}
