package api

import (
	"fmt"
	"net/http"
	"strings"
)

// handleMetrics serves the Prometheus text format.
//
// Written by hand rather than with the client library on purpose: it is a
// few dozen numbers out of structures that already exist, and an extra
// dependency with a registry of its own would be out of all proportion.
func (s *Server) handleMetrics(w http.ResponseWriter, _ *http.Request) {
	var b strings.Builder

	res := s.res.Stats()
	counter(&b, "auspex_queries_total", "DNS queries answered.", res.Queries)
	counter(&b, "auspex_blocked_total", "Queries blocked by filter rules.", res.Blocked)
	counter(&b, "auspex_blocked_cname_total", "Blocks the CNAME chain triggered in the first place.", res.BlockedCNAME)
	counter(&b, "auspex_rewritten_total", "Queries answered by rewrites.", res.Rewritten)
	counter(&b, "auspex_cache_hits_total", "Queries answered from the cache.", res.CacheHits)
	counter(&b, "auspex_errors_total", "Queries no upstream could answer.", res.Errors)
	counter(&b, "auspex_prefetches_total", "Cache entries renewed ahead of expiry.", res.Prefetches)
	counter(&b, "auspex_validated_total", "Answers with a verified DNSSEC signature chain.", res.Validated)
	counter(&b, "auspex_learned_total", "Names recorded in learning mode.", res.Learned)

	c := s.res.Cache().Stats()
	gauge(&b, "auspex_cache_entries", "Entries in the DNS cache.", float64(c.Entries))
	counter(&b, "auspex_cache_misses_total", "Cache misses.", c.Misses)
	counter(&b, "auspex_cache_stale_total", "Queries answered from expired entries.", c.StaleHits)
	counter(&b, "auspex_cache_evictions_total", "Entries evicted for want of room.", c.Evictions)

	rules := s.res.Engine().Stats()
	help(&b, "auspex_rules", "Geladene Regeln nach Wirkung.", "gauge")
	line(&b, "auspex_rules", map[string]string{"action": "block"}, float64(rules.BlockRules))
	line(&b, "auspex_rules", map[string]string{"action": "allow"}, float64(rules.AllowRules))

	help(&b, "auspex_list_rules", "Rules loaded per list.", "gauge")
	for _, l := range rules.Lists {
		line(&b, "auspex_list_rules", map[string]string{"list": l.Name}, float64(l.Rules))
	}

	health := s.res.Pool().Health()
	help(&b, "auspex_upstream_queries_total", "Queries per upstream.", "counter")
	for _, u := range health {
		line(&b, "auspex_upstream_queries_total", upstreamLabels(u.Addr, u.Proto), float64(u.Queries))
	}
	help(&b, "auspex_upstream_wins_total", "Answers per upstream that were actually used.", "counter")
	for _, u := range health {
		line(&b, "auspex_upstream_wins_total", upstreamLabels(u.Addr, u.Proto), float64(u.Wins))
	}
	help(&b, "auspex_upstream_errors_total", "Fehlversuche je Upstream.", "counter")
	for _, u := range health {
		line(&b, "auspex_upstream_errors_total", upstreamLabels(u.Addr, u.Proto), float64(u.Errors))
	}
	help(&b, "auspex_upstream_response_ms", "Mean response time per upstream.", "gauge")
	for _, u := range health {
		line(&b, "auspex_upstream_response_ms", upstreamLabels(u.Addr, u.Proto), u.AvgMillis)
	}
	help(&b, "auspex_upstream_benched", "1 while an upstream is being skipped after errors.", "gauge")
	for _, u := range health {
		var benched float64
		if u.Benched {
			benched = 1
		}
		line(&b, "auspex_upstream_benched", upstreamLabels(u.Addr, u.Proto), benched)
	}

	if s.doh != nil {
		counter(&b, "auspex_doh_queries_total", "Queries answered over DNS-over-HTTPS.", s.doh.Queries())
		counter(&b, "auspex_doh_errors_total", "Malformed DoH requests.", s.doh.Errors())
	}

	help(&b, "auspex_learn_names", "Names observed per learning profile.", "gauge")
	for _, l := range s.res.LearnStats() {
		labels := map[string]string{"profile": l.Profile, "policy": l.Policy}
		line(&b, "auspex_learn_names", labels, float64(l.Names))
	}

	gauge(&b, "auspex_uptime_seconds", "The resolver's uptime.", s.res.Uptime().Seconds())

	w.Header().Set("Content-Type", "text/plain; version=0.0.4; charset=utf-8")
	_, _ = w.Write([]byte(b.String()))
}

func upstreamLabels(addr, proto string) map[string]string {
	return map[string]string{"upstream": addr, "proto": proto}
}

func help(b *strings.Builder, name, description, kind string) {
	fmt.Fprintf(b, "# HELP %s %s\n# TYPE %s %s\n", name, description, name, kind)
}

func counter(b *strings.Builder, name, description string, value int64) {
	help(b, name, description, "counter")
	line(b, name, nil, float64(value))
}

func gauge(b *strings.Builder, name, description string, value float64) {
	help(b, name, description, "gauge")
	line(b, name, nil, value)
}

func line(b *strings.Builder, name string, labels map[string]string, value float64) {
	b.WriteString(name)
	if len(labels) > 0 {
		b.WriteByte('{')
		first := true
		// Sorted would be nicer, but Prometheus does not care about order and
		// the label sets are tiny.
		for k, v := range labels {
			if !first {
				b.WriteByte(',')
			}
			first = false
			fmt.Fprintf(b, "%s=%q", k, escapeLabel(v))
		}
		b.WriteByte('}')
	}
	fmt.Fprintf(b, " %g\n", value)
}

// escapeLabel defuses characters that would break the text format - list
// names and upstream addresses come from the configuration.
func escapeLabel(v string) string {
	v = strings.ReplaceAll(v, `\`, `\`)
	v = strings.ReplaceAll(v, `"`, `\"`)
	v = strings.ReplaceAll(v, "\n", `\n`)
	return v
}
