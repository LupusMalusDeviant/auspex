//go:build linux

package neigh

import (
	"fmt"
	"net/netip"
	"os"
	"syscall"
	"unsafe"

	"golang.org/x/sys/unix"
)

// read fetches the neighbour table over netlink.
//
// Without an extra dependency: the request is a single RTM_GETNEIGH, and the
// answer consists of records of fixed shape. Pulling in a library for that
// would mean carrying a whole toolkit, and its upkeep, for a hundred lines.
//
// Read across both families: IPv4 in case a device only speaks over it, IPv6
// for the actual purpose.
func readTable() (map[netip.Addr]string, error) {
	out := map[netip.Addr]string{}
	for _, family := range []uint8{unix.AF_INET, unix.AF_INET6} {
		if err := readFamily(family, out); err != nil {
			return nil, err
		}
	}
	return out, nil
}

func readFamily(family uint8, out map[netip.Addr]string) error {
	fd, err := unix.Socket(unix.AF_NETLINK, unix.SOCK_RAW|unix.SOCK_CLOEXEC, unix.NETLINK_ROUTE)
	if err != nil {
		return fmt.Errorf("netlink-socket: %w", err)
	}
	defer unix.Close(fd)

	if err := unix.Bind(fd, &unix.SockaddrNetlink{Family: unix.AF_NETLINK}); err != nil {
		return fmt.Errorf("netlink-bind: %w", err)
	}

	// Request: give me every neighbour of this family.
	const headerLen = unix.SizeofNlMsghdr + unix.SizeofNdMsg
	query := make([]byte, headerLen)
	header := (*unix.NlMsghdr)(unsafe.Pointer(&query[0]))
	header.Len = headerLen
	header.Type = unix.RTM_GETNEIGH
	header.Flags = unix.NLM_F_REQUEST | unix.NLM_F_DUMP
	header.Seq = 1
	nd := (*unix.NdMsg)(unsafe.Pointer(&query[unix.SizeofNlMsghdr]))
	nd.Family = family

	if err := unix.Sendto(fd, query, 0, &unix.SockaddrNetlink{Family: unix.AF_NETLINK}); err != nil {
		return fmt.Errorf("netlink-senden: %w", err)
	}

	puffer := make([]byte, 64*1024)
	for {
		n, _, err := unix.Recvfrom(fd, puffer, 0)
		if err != nil {
			if err == syscall.EINTR {
				continue
			}
			return fmt.Errorf("netlink-lesen: %w", err)
		}

		nachrichten, err := syscall.ParseNetlinkMessage(puffer[:n])
		if err != nil {
			return fmt.Errorf("netlink-zerlegen: %w", err)
		}

		for _, m := range nachrichten {
			switch m.Header.Type {
			case uint16(unix.NLMSG_DONE):
				return nil
			case uint16(unix.NLMSG_ERROR):
				return fmt.Errorf("netlink reports an error")
			case unix.RTM_NEWNEIGH:
				take(m, out)
			}
		}
	}
}

func take(m syscall.NetlinkMessage, out map[netip.Addr]string) {
	if len(m.Data) < unix.SizeofNdMsg {
		return
	}
	nd := (*unix.NdMsg)(unsafe.Pointer(&m.Data[0]))

	// Only usable states. FAILED and INCOMPLETE stand for neighbours that
	// are not answering right now - their MAC is either empty or stale.
	if nd.State&(unix.NUD_FAILED|unix.NUD_INCOMPLETE|unix.NUD_NOARP) != 0 {
		return
	}

	// Parse the attributes by hand.
	//
	// syscall.ParseNetlinkRouteAttr only knows link, address and route
	// messages and silently returns EINVAL for RTM_NEWNEIGH. That is exactly
	// what the first attempt foundered on: the table was readable, the
	// kernel answered, and still nothing arrived.
	var address netip.Addr
	var mac string
	for rest := m.Data[unix.SizeofNdMsg:]; len(rest) >= unix.SizeofRtAttr; {
		attr := (*unix.RtAttr)(unsafe.Pointer(&rest[0]))
		if int(attr.Len) < unix.SizeofRtAttr || int(attr.Len) > len(rest) {
			break
		}
		value := rest[unix.SizeofRtAttr:attr.Len]

		switch attr.Type {
		case unix.NDA_DST:
			if x, ok := netip.AddrFromSlice(value); ok {
				address = x.Unmap()
			}
		case unix.NDA_LLADDR:
			if len(value) == 6 {
				mac = fmt.Sprintf("%02x:%02x:%02x:%02x:%02x:%02x",
					value[0], value[1], value[2], value[3], value[4], value[5])
			}
		}

		// Attributes are aligned to four bytes.
		step := (int(attr.Len) + 3) &^ 3
		if step >= len(rest) {
			break
		}
		rest = rest[step:]
	}

	// Link-local addresses (fe80::) belong to the device too, but they do
	// not appear as the sender of a DNS query. They would only bloat the
	// table.
	if address.IsValid() && mac != "" && !address.IsLinkLocalUnicast() {
		out[address] = mac
	}
}

// Available says whether the table is readable at all.
//
// In a container with its own network namespace what stands there is that
// namespace's table - which is close to nothing. Auspex deliberately runs in
// the host's network; whoever changes that should find the reason in the log
// rather than wondering why device names are missing.
func Available() (bool, string) {
	if _, err := os.Stat("/proc/net/route"); err != nil {
		return false, "no access to the host's network tables"
	}
	if _, err := readTable(); err != nil {
		return false, err.Error()
	}
	return true, ""
}
