# Learning mode

## Purpose

Deny-by-default for devices you do not trust, without having to know in
advance what they need. A profile runs `open`, then `learn` while the device
shows what it talks to, then `enforce`, after which exactly that and nothing
else resolves. It is the one capability in Auspex that neither Pi-hole nor
AdGuard Home has.

## Files

| Path | Role |
|------|------|
| `auspex/internal/learn/store.go` | What a profile has seen: names, first contact, counts, the overflow flag |
| `auspex/internal/learn/manager.go` | One store per learning profile, persistence, the save interval |
| `auspex/internal/learn/helpers.go` | Generalising a name to its registrable domain, and the allowlist export |

## Dependencies

### Internal

- **[Resolver pipeline](./resolver-pipeline.md)** — records into the store and
  enforces against it.
- **[Rules and lists](./rules-and-lists.md)** — the exported allowlist becomes
  ordinary allow rules.
- **[Control API](./control-api.md)** — reading, exporting, resetting,
  forgetting a single name.

### External

- `golang.org/x/net/publicsuffix` — so that `foo.co.uk` does not let a whole
  country TLD through.

## Public interface

```go
func (m *Manager) Store(profile string) (*Store, bool)
func (s *Store) Record(name string) 
func (s *Store) Allows(name string, g Granularity) bool
func (s *Store) Allowlist(g Granularity) []string   // domain | exact
func (s *Store) Reset()
func (s *Store) Forget(name string) bool
```

CLI: `auspex -config config.yaml -learn-export <profile>`

## Data flow

1. A query from a `learn` profile is resolved normally.
2. **Only what the filter let through gets recorded.** Otherwise a tracker
   that happened to be asked for during the learning window would wander into
   the allowlist permanently, and the whole exercise would be back to front.
3. Reverse lookups (`in-addr.arpa`, `ip6.arpa`) are neither recorded nor
   blocked. They do not belong to the question "which services does this
   device talk to", and blocking them turns every diagnosis into guesswork.
4. The name is generalised to its registrable domain unless granularity is
   `exact`. `cdn-3f8a.vendor.example` is `cdn-91cc.vendor.example` tomorrow;
   without generalising, the allowlist would be broken on day two.
5. `max_entries` caps the store. A device generating random names — broken, or
   tunnelling, must not be able to flood it. On reaching the cap the store sets
   `overflow` rather than being silently incomplete.
6. In `enforce` the check runs against the store; anything not in it gets
   NXDOMAIN.

The dashboard shows per profile how long it has been since a new domain
appeared. That is the most usable signal that a learning window ran long
enough. That works better than a fixed duration, because the answer depends on the device.

## Open questions

- A second instance breaks `enforce`: each learns for itself, and a device
  would be let through or blocked depending on which instance it asked. Either
  only one instance enforces, or the stores get aligned through the backup.
  See [`product.md`](../product.md#resilience).
