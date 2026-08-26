// auspexdig is a minimal query client for testing an Auspex server.
package main

import (
	"crypto/tls"
	"flag"
	"fmt"
	"net"
	"os"
	"strings"
	"time"

	"github.com/miekg/dns"
)

func main() {
	server := flag.String("server", "127.0.0.1:53", "Zielserver")
	qtype := flag.String("type", "A", "Record-Typ")
	proto := flag.String("net", "udp", "udp, tcp or tcp-tls (DoT)")
	insecure := flag.Bool("insecure", false, "do not check the certificate (for tests only)")
	flag.Parse()

	if flag.NArg() == 0 {
		fmt.Fprintln(os.Stderr, "Aufruf: auspexdig [-server host:port] [-type A] <domain> [domain ...]")
		os.Exit(2)
	}
	t, ok := dns.StringToType[strings.ToUpper(*qtype)]
	if !ok {
		fmt.Fprintf(os.Stderr, "unknown type %q\n", *qtype)
		os.Exit(2)
	}

	client := &dns.Client{Net: *proto, Timeout: 5 * time.Second}
	if *proto == "tcp-tls" {
		host, _, err := net.SplitHostPort(*server)
		if err != nil {
			host = *server
		}
		client.TLSConfig = &tls.Config{ServerName: host, InsecureSkipVerify: *insecure}
	}
	for _, name := range flag.Args() {
		msg := new(dns.Msg)
		msg.SetQuestion(dns.Fqdn(name), t)
		msg.SetEdns0(4096, false)

		resp, rtt, err := client.Exchange(msg, *server)
		if err != nil {
			fmt.Printf("%-26s ERROR   %v\n", name, err)
			continue
		}
		answers := make([]string, 0, len(resp.Answer))
		for _, rr := range resp.Answer {
			answers = append(answers, strings.Join(strings.Fields(rr.String())[4:], " "))
		}
		out := strings.Join(answers, ", ")
		if out == "" {
			out = "-"
		}
		fmt.Printf("%-26s %-10s %-28s %5.1fms\n", name, dns.RcodeToString[resp.Rcode], out, float64(rtt.Microseconds())/1000)
	}
}
