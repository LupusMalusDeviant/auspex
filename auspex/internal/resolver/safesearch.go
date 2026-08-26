package resolver

import (
	"context"
	"errors"

	"github.com/miekg/dns"

	"auspex/internal/cache"
)

var errNoSafeSearchAnswer = errors.New("no answer from the safe-search target")

// safeSearchTypes are the query types a redirect applies to.
//
// Deliberately not every type. Sending an MX or NS query for google.com to
// forcesafesearch.google.com would answer a question nobody asked, and the
// answer would be wrong. A, AAAA and HTTPS are what a browser uses to reach
// the search page — everything else passes through untouched.
//
// HTTPS (type 65) matters more than it looks: it is where Encrypted Client
// Hello and alternative addresses live. Left alone, a browser could use it to
// reach the unfiltered host after the A record was redirected.
func safeSearchApplies(qtype uint16) bool {
	switch qtype {
	case dns.TypeA, dns.TypeAAAA, dns.TypeHTTPS:
		return true
	}
	return false
}

// safeSearchResponse answers with the CNAME to the provider's filtered host,
// and with the records behind it.
//
// The CNAME on its own would be formally correct and practically useless: a
// stub resolver asks for A and expects the chain to have been followed for
// it. It does not chase the CNAME itself, so what arrives at the browser is a
// name with no address, and the search page simply does not load. Hence the
// second query.
//
// Its answer goes into the cache under the *target's* own key, not the
// client's. Six devices redirected to the same provider then share one
// upstream query, and the entry is the same one an unfiltered device would
// produce if it asked for that host directly.
func (r *Resolver) safeSearchResponse(req *dns.Msg, q dns.Question, target string) (*dns.Msg, string, error) {
	reply := new(dns.Msg)
	reply.SetReply(req)

	const cnameTTL = 300
	reply.Answer = append(reply.Answer, &dns.CNAME{
		Hdr: dns.RR_Header{
			Name: q.Name, Rrtype: dns.TypeCNAME, Class: dns.ClassINET, Ttl: cnameTTL,
		},
		Target: dns.Fqdn(target),
	})

	inner := new(dns.Msg)
	inner.SetQuestion(dns.Fqdn(target), q.Qtype)
	inner.RecursionDesired = true

	// Without DO: the client is not being handed the provider's signatures,
	// it is being handed an answer to a question it did not ask. Asking for
	// them would only make the packet bigger.
	key := cache.Key(inner.Question[0], false)
	if r.cfg.Cache.Enabled {
		if cached, _ := r.cache.Get(key); cached != nil {
			appendTarget(reply, cached)
			return reply, "cache", nil
		}
	}

	ctx, cancel := context.WithTimeout(context.Background(), r.cfg.Upstream.Timeout.D())
	defer cancel()

	resp, via, err := r.pool.Exchange(ctx, inner)
	if err != nil {
		return nil, "", err
	}
	// An answer that is not there is not a CNAME into nothing: that would
	// leave the client with a name and no address and no reason why, which
	// looks exactly like a broken filter.
	if resp == nil {
		return nil, "", errNoSafeSearchAnswer
	}
	if r.cfg.Cache.Enabled {
		r.cache.Set(key, resp)
	}
	appendTarget(reply, resp)
	return reply, via, nil
}

// appendTarget hangs the target's records off the CNAME already in the reply.
func appendTarget(reply, resp *dns.Msg) {
	if resp == nil {
		return
	}
	// A target that does not resolve is not turned into an invented answer:
	// the rcode travels, and the client sees the truth. The CNAME stays in
	// place, so it is visible in the query log where the query went.
	if resp.Rcode != dns.RcodeSuccess {
		reply.Rcode = resp.Rcode
		reply.Ns = resp.Ns
		return
	}
	for _, rr := range resp.Answer {
		reply.Answer = append(reply.Answer, rr)
	}
	if len(resp.Answer) == 0 {
		// NODATA at the target — an HTTPS query for a host that has no
		// HTTPS record is the normal case, and exactly the intended
		// outcome: no Encrypted Client Hello, no alternative address.
		reply.Ns = resp.Ns
	}
}
