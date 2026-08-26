package doh

import (
	"bytes"
	"encoding/base64"
	"net"
	"net/http"
	"net/http/httptest"
	"net/netip"
	"testing"

	"github.com/miekg/dns"
)

// echoHandler answers with a fixed address and remembers which client the
// resolver got to see.
type echoHandler struct{ sawClient string }

func (e *echoHandler) ServeDNS(w dns.ResponseWriter, req *dns.Msg) {
	host, _, _ := net.SplitHostPort(w.RemoteAddr().String())
	e.sawClient = host

	reply := new(dns.Msg)
	reply.SetReply(req)
	reply.Answer = []dns.RR{&dns.A{
		Hdr: dns.RR_Header{Name: req.Question[0].Name, Rrtype: dns.TypeA, Class: dns.ClassINET, Ttl: 60},
		A:   net.IPv4(192, 0, 2, 1),
	}}
	_ = w.WriteMsg(reply)
}

func query(t *testing.T, name string) []byte {
	t.Helper()
	m := new(dns.Msg)
	m.SetQuestion(dns.Fqdn(name), dns.TypeA)
	packed, err := m.Pack()
	if err != nil {
		t.Fatal(err)
	}
	return packed
}

func TestPostReturnsAnAnswer(t *testing.T) {
	h := NewHandler(&echoHandler{}, Options{})

	req := httptest.NewRequest(http.MethodPost, "/dns-query", bytes.NewReader(query(t, "example.com")))
	req.Header.Set("Content-Type", "application/dns-message")
	rec := httptest.NewRecorder()

	h.ServeHTTP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("HTTP %d, expected 200", rec.Code)
	}
	if ct := rec.Header().Get("Content-Type"); ct != "application/dns-message" {
		t.Errorf("Content-Type = %q", ct)
	}
	// An HTTP cache would not count the TTL down.
	if cc := rec.Header().Get("Cache-Control"); cc != "no-store" {
		t.Errorf("Cache-Control = %q, expected no-store", cc)
	}

	var resp dns.Msg
	if err := resp.Unpack(rec.Body.Bytes()); err != nil {
		t.Fatalf("unreadable answer: %v", err)
	}
	if len(resp.Answer) != 1 {
		t.Fatalf("answer records = %d", len(resp.Answer))
	}
}

func TestGetWithBase64Url(t *testing.T) {
	h := NewHandler(&echoHandler{}, Options{})
	encoded := base64.RawURLEncoding.EncodeToString(query(t, "example.com"))

	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/dns-query?dns="+encoded, nil))

	if rec.Code != http.StatusOK {
		t.Fatalf("HTTP %d, expected 200", rec.Code)
	}
}

func TestGetAcceptsPaddedEncoding(t *testing.T) {
	h := NewHandler(&echoHandler{}, Options{})
	// RFC 8484 verbietet Auffuellzeichen, manche Clients senden sie trotzdem.
	encoded := base64.URLEncoding.EncodeToString(query(t, "example.com"))

	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/dns-query?dns="+encoded, nil))

	if rec.Code != http.StatusOK {
		t.Fatalf("HTTP %d, expected 200", rec.Code)
	}
}

func TestWrongPathAndMethod(t *testing.T) {
	h := NewHandler(&echoHandler{}, Options{})

	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/something-else", nil))
	if rec.Code != http.StatusNotFound {
		t.Errorf("a foreign path = HTTP %d, expected 404", rec.Code)
	}

	rec = httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodPut, "/dns-query", nil))
	if rec.Code != http.StatusBadRequest {
		t.Errorf("PUT = HTTP %d, expected 400", rec.Code)
	}
}

func TestABrokenMessage(t *testing.T) {
	h := NewHandler(&echoHandler{}, Options{})

	req := httptest.NewRequest(http.MethodPost, "/dns-query", bytes.NewReader([]byte("no DNS")))
	req.Header.Set("Content-Type", "application/dns-message")
	rec := httptest.NewRecorder()

	h.ServeHTTP(rec, req)

	if rec.Code != http.StatusBadRequest {
		t.Errorf("HTTP %d, expected 400", rec.Code)
	}
	if h.Errors() != 1 {
		t.Errorf("error counter = %d, expected 1", h.Errors())
	}
}

// The security-relevant case: without trust nobody may invent somebody
// else's origin and hit their profile with it.
func TestXForwardedForOnlyFromTrustedProxies(t *testing.T) {
	t.Run("ignored without trust", func(t *testing.T) {
		echo := &echoHandler{}
		h := NewHandler(echo, Options{})

		req := httptest.NewRequest(http.MethodPost, "/dns-query", bytes.NewReader(query(t, "example.com")))
		req.Header.Set("Content-Type", "application/dns-message")
		req.Header.Set("X-Forwarded-For", "10.0.0.99")
		req.RemoteAddr = "203.0.113.5:1234"
		h.ServeHTTP(httptest.NewRecorder(), req)

		if echo.sawClient != "203.0.113.5" {
			t.Errorf("client = %q, expected the real address 203.0.113.5", echo.sawClient)
		}
	})

	t.Run("taken from a trusted proxy", func(t *testing.T) {
		echo := &echoHandler{}
		h := NewHandler(echo, Options{
			TrustedProxies: []netip.Prefix{netip.MustParsePrefix("203.0.113.0/24")},
		})

		req := httptest.NewRequest(http.MethodPost, "/dns-query", bytes.NewReader(query(t, "example.com")))
		req.Header.Set("Content-Type", "application/dns-message")
		req.Header.Set("X-Forwarded-For", "10.0.0.99, 203.0.113.5")
		req.RemoteAddr = "203.0.113.5:1234"
		h.ServeHTTP(httptest.NewRecorder(), req)

		if echo.sawClient != "10.0.0.99" {
			t.Errorf("client = %q, expected the original client 10.0.0.99", echo.sawClient)
		}
	})
}
