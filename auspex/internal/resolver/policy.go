package resolver

import (
	"fmt"
	"net/netip"
	"strconv"
	"strings"
	"time"

	"auspex/internal/config"
	"auspex/internal/learn"
	"auspex/internal/rules"
	"auspex/internal/services"
)

// Policy is a profile's learn state.
type Policy string

const (
	PolicyOpen    Policy = "open"
	PolicyLearn   Policy = "learn"
	PolicyEnforce Policy = "enforce"
	// PolicyQuarantine blocks everything for this client, whatever it has
	// learned. Meant to be switched on for a while and switched off again —
	// the answer to a finding, not a way of running a device.
	//
	// Deliberately not "enforce with an empty store": a device that has
	// learned something would keep exactly that, which is the opposite of
	// quarantine. And deliberately not "filtering off in reverse", because
	// that would be invisible in the query log.
	PolicyQuarantine Policy = "quarantine"
)

// Profile is the rule set for a group of clients.
type Profile struct {
	Name     string
	prefixes []netip.Prefix
	// macs binds the profile to devices rather than addresses - the only
	// route that holds under changing IPv6 addresses.
	macs      map[string]bool
	Filtering bool
	Overlay   *rules.Engine // this client's own block/allow rules
	Schedules []Schedule
	// SafeSearch are the providers this profile is redirected for, all day.
	SafeSearch []string
	Policy     Policy
	// Learn is the observation store; only set for learn/enforce.
	Learn *learn.Store
}

// Schedule is a time window with rules of its own — quiet nights, focus
// hours, children's hours. Exactly what plain block lists cannot do.
type Schedule struct {
	Name   string
	days   [7]bool
	from   int // minutes since midnight
	to     int
	Engine *rules.Engine
	// SafeSearch applies while the window is open, on top of the profile's
	// own providers.
	SafeSearch []string
}

// Active checks whether the window is open at time t.
// from > to means a window across midnight (22:00–06:00).
func (s Schedule) Active(t time.Time) bool {
	if !s.days[int(t.Weekday())] {
		return false
	}
	minutes := t.Hour()*60 + t.Minute()
	if s.from <= s.to {
		return minutes >= s.from && minutes < s.to
	}
	return minutes >= s.from || minutes < s.to
}

// safeSearchTarget returns the host this name has to be redirected to, or "".
//
// The profile's providers apply all day; an open time window adds its own on
// top rather than replacing them. Additive, because the other direction reads
// wrong: a window called "homework" that switches Google's filter *off*
// between four and six is not what anybody who wrote it down meant.
func (p *Profile) safeSearchTarget(name string, now time.Time) string {
	if p == nil {
		return ""
	}
	keys := p.SafeSearch
	for _, s := range p.Schedules {
		if len(s.SafeSearch) > 0 && s.Active(now) {
			// Copy on first use: appending to p.SafeSearch directly would
			// write into the profile's own slice whenever it has spare
			// capacity, and the window would stay open after it closed.
			joined := make([]string, 0, len(keys)+len(s.SafeSearch))
			joined = append(joined, keys...)
			keys = append(joined, s.SafeSearch...)
		}
	}
	return services.SafeSearchTarget(name, keys)
}

func compileProfiles(clients []config.Client, cfg config.Learning, mgr *learn.Manager) ([]Profile, error) {
	profiles := make([]Profile, 0, len(clients))
	for _, c := range clients {
		policy := Policy(c.Policy)
		if policy == "" {
			policy = PolicyOpen
		}
		p := Profile{
			Name:       c.Name,
			Filtering:  c.FilteringEnabled(),
			Policy:     policy,
			SafeSearch: c.SafeSearch,
		}

		// Quarantine deliberately gets no learn store: it blocks regardless of
		// what was learned, and creating one would make a profile that cannot
		// start on an installation without a learning directory.
		if policy != PolicyOpen && policy != PolicyQuarantine {
			if mgr == nil {
				return nil, fmt.Errorf("client %q: policy %q without a learning store", c.Name, policy)
			}
			store, err := mgr.Store(c.Name, learn.Granularity(cfg.Granularity), cfg.MaxEntries)
			if err != nil {
				return nil, fmt.Errorf("Client %q: %w", c.Name, err)
			}
			p.Learn = store
		}
		for _, m := range c.Match {
			prefix, err := parseMatch(m)
			if err != nil {
				return nil, fmt.Errorf("Client %q: %w", c.Name, err)
			}
			p.prefixes = append(p.prefixes, prefix)
		}
		for _, m := range c.Macs {
			mac, err := config.ParseMac(m)
			if err != nil {
				return nil, fmt.Errorf("Client %q: %w", c.Name, err)
			}
			if p.macs == nil {
				p.macs = map[string]bool{}
			}
			p.macs[mac] = true
		}
		// Services become perfectly ordinary block rules - after that there is
		// no special case anywhere else in the system.
		serviceRules, _ := services.Rules(c.BlockServices)
		block := append(append([]string{}, c.BlockRules...), serviceRules...)
		if len(block) > 0 || len(c.AllowRules) > 0 {
			p.Overlay = rules.NewFromRules("client:"+c.Name, block, c.AllowRules)
		}
		for _, s := range c.Schedules {
			sched, err := compileSchedule(c.Name, s)
			if err != nil {
				return nil, fmt.Errorf("Client %q: %w", c.Name, err)
			}
			p.Schedules = append(p.Schedules, sched)
		}
		profiles = append(profiles, p)
	}
	return profiles, nil
}

// parseMatch accepts both 192.168.1.5 and 192.168.1.0/24.
func parseMatch(m string) (netip.Prefix, error) {
	m = strings.TrimSpace(m)
	if strings.Contains(m, "/") {
		p, err := netip.ParsePrefix(m)
		if err != nil {
			return netip.Prefix{}, fmt.Errorf("invalid network %q: %w", m, err)
		}
		return p.Masked(), nil
	}
	addr, err := netip.ParseAddr(m)
	if err != nil {
		return netip.Prefix{}, fmt.Errorf("invalid address %q: %w", m, err)
	}
	return netip.PrefixFrom(addr, addr.BitLen()), nil
}

var weekdayIndex = map[string]int{
	"sun": 0, "mon": 1, "tue": 2, "wed": 3, "thu": 4, "fri": 5, "sat": 6,
	"so": 0, "mo": 1, "di": 2, "mi": 3, "do": 4, "fr": 5, "sa": 6,
}

func compileSchedule(client string, s config.Schedule) (Schedule, error) {
	out := Schedule{Name: s.Name, SafeSearch: s.SafeSearch}
	if out.Name == "" {
		out.Name = "schedule"
	}
	if len(s.Days) == 0 {
		s.Days = []string{"all"}
	}
	for _, d := range s.Days {
		switch strings.ToLower(strings.TrimSpace(d)) {
		case "all", "daily", "täglich":
			for i := range out.days {
				out.days[i] = true
			}
		case "weekdays", "werktags":
			for i := 1; i <= 5; i++ {
				out.days[i] = true
			}
		case "weekend", "wochenende":
			out.days[0], out.days[6] = true, true
		default:
			idx, ok := weekdayIndex[strings.ToLower(strings.TrimSpace(d))[:min(3, len(d))]]
			if !ok {
				return out, fmt.Errorf("schedule %q: unknown day %q", out.Name, d)
			}
			out.days[idx] = true
		}
	}
	var err error
	if out.from, err = parseClock(s.From); err != nil {
		return out, fmt.Errorf("schedule %q: %w", out.Name, err)
	}
	if out.to, err = parseClock(s.To); err != nil {
		return out, fmt.Errorf("schedule %q: %w", out.Name, err)
	}
	serviceRules, _ := services.Rules(s.BlockServices)
	block := append(append([]string{}, s.Block...), serviceRules...)
	out.Engine = rules.NewFromRules("schedule:"+client+"/"+out.Name, block, s.Allow)
	return out, nil
}

func parseClock(s string) (int, error) {
	parts := strings.Split(strings.TrimSpace(s), ":")
	if len(parts) != 2 {
		return 0, fmt.Errorf("time of day %q expects the format HH:MM", s)
	}
	h, err1 := strconv.Atoi(parts[0])
	m, err2 := strconv.Atoi(parts[1])
	if err1 != nil || err2 != nil || h < 0 || h > 23 || m < 0 || m > 59 {
		return 0, fmt.Errorf("invalid time of day %q", s)
	}
	return h*60 + m, nil
}

// rewriteSet maps internal names onto internal addresses.
type rewriteSet struct {
	exact  map[string]config.Rewrite
	suffix map[string]config.Rewrite
}

func compileRewrites(list []config.Rewrite) *rewriteSet {
	rs := &rewriteSet{exact: map[string]config.Rewrite{}, suffix: map[string]config.Rewrite{}}
	for _, r := range list {
		name := strings.ToLower(strings.TrimSuffix(strings.TrimSpace(r.Domain), "."))
		if strings.HasPrefix(name, "*.") {
			rs.suffix[name[2:]] = r
			continue
		}
		rs.exact[name] = r
	}
	return rs
}

func (rs *rewriteSet) lookup(name string) (config.Rewrite, bool) {
	if rs == nil {
		return config.Rewrite{}, false
	}
	name = strings.ToLower(strings.TrimSuffix(name, "."))
	if r, ok := rs.exact[name]; ok {
		return r, true
	}
	rest := name
	for {
		i := strings.IndexByte(rest, '.')
		if i < 0 {
			return config.Rewrite{}, false
		}
		rest = rest[i+1:]
		if r, ok := rs.suffix[rest]; ok {
			return r, true
		}
	}
}

func (r *Resolver) profileFor(addr netip.Addr) *Profile {
	profiles := *r.profiles.Load()

	// Addresses first: whoever binds a profile explicitly to a fixed address
	// means exactly that one.
	for i := range profiles {
		for _, p := range profiles[i].prefixes {
			if p.Contains(addr) {
				return &profiles[i]
			}
		}
	}

	// Then via the device. Look up only once, even when several profiles
	// carry MACs.
	if r.neigh == nil {
		return nil
	}
	mac := r.neigh.Mac(addr)
	if mac == "" {
		return nil
	}
	for i := range profiles {
		if profiles[i].macs[mac] {
			return &profiles[i]
		}
	}
	return nil
}
