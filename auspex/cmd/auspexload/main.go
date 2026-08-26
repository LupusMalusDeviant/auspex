// auspexload measures a resolver's throughput and response times.
//
// Deliberately separate from auspexdig: one answers "what does the resolver
// say about this domain", the other "how much can it take".
package main

import (
	"flag"
	"fmt"
	"math/rand"
	"os"
	"sort"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/miekg/dns"
)

func main() {
	server := flag.String("server", "127.0.0.1:53", "Zielserver")
	count := flag.Int("n", 5000, "number of queries")
	parallel := flag.Int("c", 50, "concurrent queries")
	pattern := flag.String("names", "", "Domains, kommagetrennt")
	zufall := flag.Bool("random", false, "a fresh name per query (bypasses the cache)")
	suffix := flag.String("suffix", "loadtest.invalid", "domain for random names")
	flag.Parse()

	var names []string
	if *pattern != "" {
		names = strings.Split(*pattern, ",")
	}
	if len(names) == 0 && !*zufall {
		fmt.Fprintln(os.Stderr, "give either -names or -random")
		os.Exit(2)
	}

	var (
		wg          sync.WaitGroup
		erledigt    atomic.Int64
		failures    atomic.Int64
		nx          atomic.Int64
		durationsMu sync.Mutex
		durations   []time.Duration
		jobs        = make(chan int, *parallel*2)
	)

	seed := time.Now().UnixNano()

	start := time.Now()
	for i := 0; i < *parallel; i++ {
		wg.Add(1)
		go func(worker int) {
			defer wg.Done()
			// One client per worker: a shared connection would be the
			// bottleneck, not the resolver.
			client := &dns.Client{Net: "udp", Timeout: 5 * time.Second}
			// Seed from the clock, not hard-wired: otherwise every run
			// produces the same names, the second run measures the cache
			// instead of the upstream - and comparing two settings really
			// compares cold against warm.
			source := rand.New(rand.NewSource(seed + int64(worker)*7919))
			lokal := make([]time.Duration, 0, 256)

			for range jobs {
				var name string
				if *zufall {
					name = fmt.Sprintf("n%d-%d.%s", worker, source.Int63(), *suffix)
				} else {
					name = strings.TrimSpace(names[source.Intn(len(names))])
				}

				msg := new(dns.Msg)
				msg.SetQuestion(dns.Fqdn(name), dns.TypeA)

				begin := time.Now()
				resp, _, err := client.Exchange(msg, *server)
				elapsed := time.Since(begin)

				erledigt.Add(1)
				if err != nil {
					failures.Add(1)
					continue
				}
				if resp.Rcode == dns.RcodeNameError {
					nx.Add(1)
				}
				lokal = append(lokal, elapsed)
			}

			durationsMu.Lock()
			durations = append(durations, lokal...)
			durationsMu.Unlock()
		}(i)
	}

	for i := 0; i < *count; i++ {
		jobs <- i
	}
	close(jobs)
	wg.Wait()

	total := time.Since(start)
	sort.Slice(durations, func(i, j int) bool { return durations[i] < durations[j] })

	fmt.Printf("  Queries:       %d in %.2fs\n", erledigt.Load(), total.Seconds())
	fmt.Printf("  Throughput:    %.0f queries/s\n", float64(erledigt.Load())/total.Seconds())
	fmt.Printf("  Errors:        %d\n", failures.Load())
	fmt.Printf("  NXDOMAIN:      %d\n", nx.Load())
	if len(durations) > 0 {
		fmt.Printf("  Median:        %s\n", ms(durations[len(durations)/2]))
		fmt.Printf("  95. Perzentil: %s\n", ms(durations[len(durations)*95/100]))
		fmt.Printf("  99. Perzentil: %s\n", ms(durations[len(durations)*99/100]))
		fmt.Printf("  langsamste:    %s\n", ms(durations[len(durations)-1]))
	}
}

func ms(d time.Duration) string {
	return fmt.Sprintf("%.2f ms", float64(d.Microseconds())/1000)
}
