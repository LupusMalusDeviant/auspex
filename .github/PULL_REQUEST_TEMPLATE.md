# What and why

<!-- What changes, and what prompted it. For a bug: what was the cause, not
     only the symptom. -->

Fixes #

## How it was checked

<!-- Not "the tests pass" — that is the floor. What did you actually see?
     Numbers before and after, an output, a screenshot. -->

## Before sending

- [ ] `go test ./...` and `dotnet test` are green
- [ ] New tests where the behaviour changes — and their comment says **why**
      it could go wrong, not what is being checked
- [ ] Where a line looks like a detour, the reason stands next to it
- [ ] Limits and half-measures are named rather than left unsaid
- [ ] Names in the code are English; German only where the codemap says it
      stays (stored settings keys, Fritz!Box vocabulary)
- [ ] No real MAC addresses, device names or credentials in the diff

## Affects operations

<!-- Keep what applies, delete the rest -->

- [ ] Database change (migration included)
- [ ] Configuration changes (`config.example.yaml` updated)
- [ ] Behaviour changes for existing installations — described in what way
- [ ] None of the above
