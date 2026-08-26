# Localization

## Purpose

Every sentence the interface shows, in German and English, in a form where a
missing translation is a compile error rather than a blank space. It also owns
display time: which clock time an event carries, and whether a finding counts
as happening at night.

## Files

| Path | Role |
|------|------|
| `control/…/Services/Localization/Strings.cs` | The abstract base, the culture mapping, `Strings.Current` |
| `control/…/Services/Localization/*.cs` | One file per area: shell, overview, findings, router, settings, … each with its abstract members and both implementations |
| `control/…/Services/Localization/FindingTexts.cs` | Builds a finding's sentence from its stored measurements |
| `control/…/Services/Localization/FindingValues.cs` | Those measurements — the JSON shape stored per finding |
| `control/…/Services/Localization/DisplayTime.cs` | The process-wide display zone and the conversion into it |
| `control/…/Services/Localization/Format.cs` | Dates, times and numbers, and the `AsUtc` overloads |
| `control/…/Services/Localization/LanguageHeader.cs` | `X-Auspex-Language` from the extension |
| `control/…/Services/Localization/ReturnPath.cs` | Whether a return address points at this installation |

## Dependencies

### Internal

Used by everything with a surface: the pages, the detectors' display, the
router connection, the extension API.

### External

- `Microsoft.AspNetCore.Localization` — the request culture from the cookie.

## Public interface

```csharp
public abstract partial class Strings { … }        // one abstract member per sentence
public static Strings Current { get; }             // per request
public static string? CultureToCode(string code);
public static IReadOnlyList<Language> Languages { get; }

static DateTime DisplayTime.ToDisplay(DateTime utc);
static bool DisplayTime.Knows(string ianaId);
static IReadOnlyList<ZoneChoice> DisplayTime.Selectable { get; }
```

`/language/{code}?back=/…` sets the cookie and redirects.

## Data flow

1. The culture hangs off the **request**, not the process — otherwise two
   people with different settings would fight over one variable.
2. `Strings.Current` resolves to `StringsDe` or `StringsEn`. Both derive from
   the same abstract class, so **adding a sentence in one language and not the
   other does not compile**. A dictionary of keys would have fallen over
   silently: `IStringLocalizer` returns the key name, and the page would read
   `Strom_Zusammenfassung`.
3. Methods take named parameters rather than `{0}` and `{1}`. A swapped pair
   of positional arguments produces a grammatically flawless, wrong sentence.
4. `Accept-Language` is deliberately **not** the default. The header travels
   with every browser unasked, and a browser that happens to be English should
   not switch the installation over. Only an explicit choice counts: the
   cookie for the interface, the header for the extension.
5. A test — `LanguageTests` — walks every string member of both classes and
   fails if an English field still holds a German sentence. Umlauts and eszett
   catch most of it; a word list catches the rest. There is one documented
   exception: the Fritz!Box menu path, because a German box has no item called
   "FRITZ!Box users".

### Display time, and the trap under it

SQLite returns `DateTime` with `Kind.Unspecified`, which is a time without an origin.
Both routes from there to a display go silently wrong: `ToLocalTime()` does
nothing at all on `Unspecified`, and the implicit conversion to
`DateTimeOffset` staples the local offset onto the UTC value. 17:53 UTC
becomes "17:53+02:00", a moment that never existed.

So `Format` has explicit `DateTime` overloads and `Strings.AsUtc()` takes the
field name at its word, so the overload has to beat the implicit conversion:
there is a test for that, and it is honest about being blind on a machine
running in UTC, which is why a second test takes the zone into its own hands.

The chosen zone is stored as an IANA id, never a Windows one: the choice is
read later by a Linux container, where "W. Europe Standard Time" would be
worthless.

## Open questions

- Findings from before the split still carry their stored German sentence and
  will until retention clears them.
- The resolver has no localization layer. Its list descriptions are German
  fallbacks that the control plane translates by name; an unknown list shows
  its German description in the English interface.
