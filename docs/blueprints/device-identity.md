# Device identity

## Purpose

Turns a source address into a device that stays the same device. A query log
full of `192.168.1.43` is a lookup exercise; one that says "living room TV" is
readable. More importantly, an address is not an identity — DHCP hands it out
again, and Windows and Android rotate their IPv6 privacy addresses daily. The
MAC is what stays.

## Files

| Path | Role |
|------|------|
| `auspex/internal/neigh/neigh.go` | The kernel's neighbour table: address → MAC |
| `auspex/internal/neigh/netlink_linux.go` | Reads the netlink attributes by hand |
| `auspex/internal/neigh/netlink_other.go` | The same interface on other platforms, answering "not available" |
| `auspex/internal/names/names.go` | Name resolution: static mapping, reverse lookup, the router's list |
| `auspex/internal/names/devices.go` | Reads the device list the control plane writes |
| `auspex/internal/clients/store.go` | Device profiles: match by address, network or MAC |

## Dependencies

### Internal

- **[Resolver pipeline](./resolver-pipeline.md)** — asks for the name on every
  query and for the profile in `policy.go`.
- **[Router connection](./router-connection.md)** — the control plane writes
  the router's device list to `auspex-shared/devices.json`; this side reads it.

### External

None beyond the standard library. Netlink is spoken directly.

## Public interface

```go
func (n *Table) Lookup(ip netip.Addr) (net.HardwareAddr, bool)
func (r *Resolver) Name(ip netip.Addr) string     // never blocks
func (s *Store) Match(ip netip.Addr, mac net.HardwareAddr) (*Client, bool)
```

## Data flow

1. `Name()` **always answers immediately from memory** and at most kicks a
   lookup off in the background. Name resolution must never sit in the query
   path — a slow router would otherwise slow down DNS.
2. Three sources, in order: the static mapping in the configuration, the
   device list from the router, and a reverse lookup (a Fritz!Box answers PTR
   for its DHCP clients and thereby delivers exactly the names from the home
   network menu).
3. The neighbour table maps address → MAC, which is what makes a profile
   survive a DHCP renewal and a rotating IPv6 address.
4. If anonymisation is on in the query log, the name drops away with the
   address — it identifies the device just as uniquely.

### Why netlink by hand

`netlink_linux.go` parses the attributes itself instead of using
`syscall.ParseNetlinkRouteAttr`. Not for its own sake: that function does not
know `RTM_NEWNEIGH` and silently returns nothing. Whoever does not know that
goes looking for a permissions problem — which is exactly what happened once.

## Open questions

- 11 of the 48 MACs on the test network are randomised. That is stable while
  the device knows the network, but a "forget network" produces a new one. A
  device register would have to be able to merge "known device, new MAC" in
  one click — point 2 in [`open-points.md`](../open-points.md).
