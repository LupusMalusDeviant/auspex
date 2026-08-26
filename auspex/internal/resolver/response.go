package resolver

import (
	"net"
	"net/netip"
	"strings"
	"time"

	"github.com/miekg/dns"

	"auspex/internal/config"
	"auspex/internal/rules"
)

// blockResponse builds the answer to a blocked query.
//
// The choice of mode is not cosmetic:
//   - nxdomain: clients cache negatively and ask less often. The cleanest
//     variant, but it can confuse apps that read NXDOMAIN as a network fault.
//   - zeroip:   the connection attempt runs into nothing. Some apps then run
//     into long timeouts instead of giving up straight away.
//   - refused:  the most honest answer ("I refuse"), but many clients then
//     try the next DNS server.
func (r *Resolver) blockResponse(req *dns.Msg, q dns.Question) *dns.Msg {
	reply := new(dns.Msg)
	reply.SetReply(req)
	reply.Authoritative = true
	ttl := uint32(r.cfg.Filter.BlockTTL.D().Seconds())
	if ttl == 0 {
		ttl = 10
	}

	switch r.cfg.Filter.BlockMode {
	case "refused":
		reply.Rcode = dns.RcodeRefused
		return reply

	case "nxdomain":
		reply.Rcode = dns.RcodeNameError
		// SOA in the authority section: without it the client does not cache
		// the NXDOMAIN and asks again every second.
		reply.Ns = []dns.RR{syntheticSOA(q.Name, ttl)}
		return reply

	case "zeroip", "custom":
		v4, v6 := r.cfg.Filter.BlockIPv4, r.cfg.Filter.BlockIPv6
		if r.cfg.Filter.BlockMode == "zeroip" {
			v4, v6 = "0.0.0.0", "::"
		}
		hdr := dns.RR_Header{Name: q.Name, Class: dns.ClassINET, Ttl: ttl}
		switch q.Qtype {
		case dns.TypeA:
			if ip := net.ParseIP(v4); ip != nil && ip.To4() != nil {
				hdr.Rrtype = dns.TypeA
				reply.Answer = []dns.RR{&dns.A{Hdr: hdr, A: ip.To4()}}
				return reply
			}
		case dns.TypeAAAA:
			if ip := net.ParseIP(v6); ip != nil {
				hdr.Rrtype = dns.TypeAAAA
				reply.Answer = []dns.RR{&dns.AAAA{Hdr: hdr, AAAA: ip.To16()}}
				return reply
			}
		}
		// Other types: NODATA rather than an invented answer.
		reply.Ns = []dns.RR{syntheticSOA(q.Name, ttl)}
		return reply
	}

	reply.Rcode = dns.RcodeNameError
	reply.Ns = []dns.RR{syntheticSOA(q.Name, ttl)}
	return reply
}

// rewriteResponse answers internal names from the configuration.
func (r *Resolver) rewriteResponse(req *dns.Msg, q dns.Question, rw config.Rewrite) *dns.Msg {
	reply := new(dns.Msg)
	reply.SetReply(req)
	reply.Authoritative = true

	ttl := uint32(rw.TTL.D().Seconds())
	if ttl == 0 {
		ttl = 300
	}
	hdr := dns.RR_Header{Name: q.Name, Class: dns.ClassINET, Ttl: ttl}

	switch {
	case rw.CNAME != "":
		hdr.Rrtype = dns.TypeCNAME
		reply.Answer = append(reply.Answer, &dns.CNAME{Hdr: hdr, Target: dns.Fqdn(rw.CNAME)})
	case q.Qtype == dns.TypeA && rw.A != "":
		if ip, err := netip.ParseAddr(rw.A); err == nil && ip.Is4() {
			hdr.Rrtype = dns.TypeA
			reply.Answer = append(reply.Answer, &dns.A{Hdr: hdr, A: net.IP(ip.AsSlice())})
		}
	case q.Qtype == dns.TypeAAAA && rw.AAAA != "":
		if ip, err := netip.ParseAddr(rw.AAAA); err == nil && ip.Is6() {
			hdr.Rrtype = dns.TypeAAAA
			reply.Answer = append(reply.Answer, &dns.AAAA{Hdr: hdr, AAAA: net.IP(ip.AsSlice())})
		}
	}
	if len(reply.Answer) == 0 {
		reply.Ns = []dns.RR{syntheticSOA(q.Name, ttl)}
	}
	return reply
}

// syntheticSOA supplies an SOA for negative answers. The name points at the
// parent zone so clients cache negatively in the correct way.
func syntheticSOA(name string, ttl uint32) *dns.SOA {
	zone := name
	if i := strings.IndexByte(name, '.'); i >= 0 && i+1 < len(name) {
		zone = name[i+1:]
	}
	if zone == "" {
		zone = "."
	}
	return &dns.SOA{
		Hdr:     dns.RR_Header{Name: zone, Rrtype: dns.TypeSOA, Class: dns.ClassINET, Ttl: ttl},
		Ns:      "auspex.invalid.",
		Mbox:    "hostmaster.auspex.invalid.",
		Serial:  uint32(time.Now().Unix() / 3600),
		Refresh: 3600,
		Retry:   600,
		Expire:  86400,
		Minttl:  ttl,
	}
}

// Explanation answers "why was this blocked?" without a real query.
type Explanation struct {
	Name     string `json:"name"`
	Client   string `json:"client,omitempty"`
	Profile  string `json:"profile,omitempty"`
	Blocked  bool   `json:"blocked"`
	Action   string `json:"action"`
	Rule     string `json:"rule,omitempty"`
	RuleKind string `json:"rule_kind,omitempty"`
	List     string `json:"list,omitempty"`
	Line     int    `json:"line,omitempty"`
	Schedule string `json:"schedule,omitempty"`
	Reason   string `json:"reason"`
}

// Explain simulates the filter decision for a name.
func (r *Resolver) Explain(name, client string) Explanation {
	name = strings.TrimSuffix(strings.ToLower(strings.TrimSpace(name)), ".")
	exp := Explanation{Name: name, Client: client}

	var profile *Profile
	if client != "" {
		if addr, err := netip.ParseAddr(client); err == nil {
			profile = r.profileFor(addr)
		}
	}
	if profile != nil {
		exp.Profile = profile.Name
	}

	decision, schedule := r.decide(name, profile, time.Now())
	exp.Schedule = schedule
	exp.Action = decision.Action.String()
	exp.Blocked = decision.Blocked()

	if decision.Rule != nil {
		exp.Rule = decision.Rule.Text()
		exp.RuleKind = decision.Rule.KindString()
		exp.List = decision.Rule.List
		exp.Line = decision.Rule.Line
	}

	switch {
	case decision.Blocked() && decision.Rule != nil && decision.Rule.List == LearnListName:
		exp.Reason = "deny-by-default: not in the learn store of profile " + exp.Profile
	case profile != nil && !profile.Filtering:
		exp.Reason = "filtering is switched off for profile " + profile.Name
	case decision.Blocked() && schedule != "":
		exp.Reason = "blocked by time window " + schedule
	case decision.Blocked():
		exp.Reason = "blocked by a rule from list " + decision.Rule.List
	case decision.Rule != nil && decision.Action == rules.ActionNone:
		exp.Reason = "explicitly allowed by an exception in " + decision.Rule.List
	default:
		exp.Reason = "no rule applies"
	}
	return exp
}
