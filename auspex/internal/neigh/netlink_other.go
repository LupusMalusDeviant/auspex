//go:build !linux

package neigh

import (
	"errors"
	"net/netip"
)

// On other systems there is no neighbour table over netlink. Auspex runs on
// Linux in production; that it compiles elsewhere is useful anyway - the
// tests run on the development machine.
func readTable() (map[netip.Addr]string, error) {
	return nil, errors.New("the neighbour table is only available on Linux")
}

func Available() (bool, string) {
	return false, "the neighbour table is only available on Linux"
}
