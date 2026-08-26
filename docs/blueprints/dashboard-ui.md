# Dashboard and appearance

## Purpose

The surface everything else is read through. Blazor Server, one circuit per
tab, which shapes the design: whatever should keep working when the
connection to the server drops is deliberately not Blazor.

## Files

| Path | Role |
|------|------|
| `control/Auspex.Control/Components/App.razor` | The document, and the cold-start appearance inline in the head |
| `control/Auspex.Control/Components/Layout/MainLayout.razor` | Three tabs, the appearance panel, the language links |
| `control/Auspex.Control/Components/Layout/BareLayout.razor` | Without navigation, for the sign-in |
| `control/Auspex.Control/Components/Layout/ReconnectModal.razor` | Blazor's reconnect dialog, in our own words |
| `control/Auspex.Control/Components/Pages/*.razor` | One file per page |
| `control/Auspex.Control/Components/RouterGate.razor` | Guard in front of the router pages |
| `control/Auspex.Control/Components/AnalyticsNav.razor` | The analysis tabs |
| `control/Auspex.Control/wwwroot/appearance.js` | Theme, accent, density, font size — without Blazor |
| `control/Auspex.Control/wwwroot/ruler.js` | Keyboard operation for the query log |
| `control/Auspex.Control/wwwroot/extension.js` | Detects whether the extension is installed in *this* browser |
| `control/Auspex.Control/wwwroot/app.css` | The whole stylesheet |
| `control/Auspex.Control/Services/AppearanceStore.cs` | The appearance, stored server-side |

## Dependencies

### Internal

- **[Localization](./localization.md)** — every string on every page.
- Everything else — the pages are the read side of the other features.

### External

- Blazor Server. No component library, no CSS framework.

## Public interface

Page routes: `/`, `/querylog`, `/devices`, `/dossier`, `/programs`, `/findings`,
`/lists`, `/learn`, `/analytics`, `/impact`, `/settings`, `/backup`,
`/explain`, `/login`, `/router`, `/router/{devices,wlan,mappings,ipv4,log,catalog}`.

JS interop: `appearance.js` and `ruler.js` attach themselves; `extension.js`
exports `version()`, `browser()`, `base()` and `copy(text)`.

## Data flow

Three decisions that shape the whole surface:

1. **The appearance is not Blazor.** It has to take effect before the first
   paint, or the wrong theme flashes up while loading, so the part that sets
   the values exists twice: inline in the `<head>` for the cold start, and in
   `appearance.js` for operating it. It also has to keep working when the
   circuit is gone.

   Blazor replaces the attributes on `<html>` on a page change and wipes
   `style` and `data-theme` with them. Rather than listening for a Blazor
   event, and hooking into its internals would be fragile, so a `MutationObserver` watches the
   root and restores. That heals itself whoever touches the attributes.

2. **The appearance lives server-side** in `darstellung.json`, not only in
   `localStorage`: localStorage is bound to the origin, and the extension has
   a different one. The keys inside that file are German and stay that way,
   they are stored data, and renaming them would orphan every existing
   setting.

3. **The keyboard operation triggers the buttons that are in the row anyway.**
   j/k move, f allows, b blocks, w asks why, p creates a profile. There is
   therefore only ever one route into an action, and the keyboard cannot do
   something different from the mouse. The buttons are found through
   `data-action` rather than by their caption, because the caption changes with the
   language, and `f` would have grasped at nothing in English.

### Structure of the navigation

Three tabs, for watching, stepping in and tending the installation, rather than twelve
equal-looking entries, built from a radio group so the browser enforces
"only ever one" itself and expanding still works without a circuit.

"Why?" deliberately is not a menu item. It is not a section you visit but a
question about one particular decision, and as a menu item it forced you to
type out the name you already had in front of you. It is a button in the log
row instead; the page still exists at `/explain`.

## Open questions

- CSS class names are English since 0.9.0, but the `data-*` attribute *values*
  that select an appearance (`fassung`, `akzent`, `hell`, `dunkel`, `kompakt`
  …) are the stored vocabulary and stay German. The line is in
  [`codemap.md`](../codemap.md#naming-and-where-it-stops).
