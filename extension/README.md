# The Auspex extension

Exceptions for the device it runs on — without the detour through the admin
interface.

## What it can do that the dashboard cannot

The browser knows what broke on **this** page. Through
`webRequest.onErrorOccurred` the extension sees exactly the requests that
failed with `ERR_NAME_NOT_RESOLVED` — that is, the names Auspex blocked while
you were on the page.

In the query log the same thing would sit somewhere between the queries of
thirty other devices. This turns "something is broken" into a button.

Allowing works for 15 minutes, an hour, or for good. Temporary is the
default: the usual case is a one-off, and permanent exceptions otherwise
pile up until nobody knows why a line is in there any more.

## Building and loading

    ./build.sh

**Chrome/Edge** — `chrome://extensions`, developer mode on, "Load unpacked",
folder `dist/chrome`.

**Firefox** — `about:debugging#/runtime/this-firefox`, "Load Temporary
Add-on", file `dist/firefox/manifest.json`. Firefox asks for the host
permission separately; without it the extension sees no failed requests.

You do not need the repository for this: the dashboard packs the same
archive under **Settings → Browser extension**, built from the sources in
the image rather than from a checked-in zip.

## Setting it up

In the dashboard under **Settings → Browser extension**, issue a token. It is
shown exactly once. Then enter it in the extension's settings, together with
the dashboard's address.

Why a token of its own and not the dashboard's session: the session cookie
is set to `SameSite=Lax`, and a request from an extension counts to the
browser as a foreign context — it simply would not travel with it. A token
of its own can also be withdrawn on its own without throwing anybody out of
the dashboard.

## Which device is meant

Not what the extension says, but the address it asks from. The resolver
resolves it through the neighbour table to a MAC and from there to the
device name. So the extension cannot change somebody else's device, not even
by accident.

The exception lands in the device profile, bound to the **MAC** — not to the
address. Under IPv6 an address binding would be worthless from tomorrow,
because Windows and Android rotate their temporary addresses daily. And
silently so: nothing would have broken, the exception would merely have
stopped applying.

## Redirects: usually more than one click

A name can be allowed and still not resolve. `analytics.tiktok.com`, for
instance, points via CNAME at a chain:

    analytics.tiktok.com
      → analytics.tiktok.com.ttdns2.com
      → analytics.tiktok.com.edgekey.net
      → analytics.tiktok.com.bytewlb.akadns.net
      → e35058.api15.akamaiedge.net

If any link is on a list, the cloaking check bites — which is precisely the
function it was built for. So after every exception Auspex names the next
blocked link, and the extension offers a button for it. Two to four clicks
and the chain is open.

That is deliberate and not awkwardness: whoever opens a cloaking chain
should see how long it is.

## Three caches

When an exception does not take effect straight away, it is nearly always
one of these:

- **Auspex** — is cleared for that name specifically on every rule change,
  so nobody has to think about it.
- **The operating system** — Windows remembers NXDOMAIN as well.
  `Clear-DnsClientCache` or `ipconfig /flushdns`.
- **The browser** — has one of its own; a hard reload with Ctrl+Shift+R
  usually does it.

## Layout

    shared/     all the code, identical for both browsers
    chrome/        the manifest only (service_worker)
    firefox/       the manifest only (background.scripts)
    build.sh       assembles both into dist/

Two manifests, one core. Maintaining the same files twice would be the sure
way to let them drift apart.
