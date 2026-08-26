# The Auspex extension

The extension lets you create exceptions for the device it is running on,
without going through the admin interface.

## What it can do that the dashboard cannot

The browser knows what failed on the page you are looking at. Through
`webRequest.onErrorOccurred`, the extension sees exactly which requests failed
with `ERR_NAME_NOT_RESOLVED`, which are the names Auspex blocked while you
were on that page.

In the query log the same information would be buried among the queries of
thirty other devices. The extension turns "something on this page is broken"
into a button.

An exception can be granted for 15 minutes, for an hour, or permanently.
Temporary is the default, because most cases are one-offs, and permanent
exceptions otherwise accumulate until nobody remembers why a given entry is
there.

## Building and loading

    ./build.sh

**Chrome and Edge:** open `chrome://extensions`, switch on developer mode,
choose "Load unpacked" and select the folder `dist/chrome`.

**Firefox:** open `about:debugging#/runtime/this-firefox`, choose "Load
Temporary Add-on" and select `dist/firefox/manifest.json`. Firefox asks for
the host permission separately; without it the extension sees no failed
requests.

You do not need a copy of the repository for this. The dashboard builds the
same archive under **Settings → Browser extension**, from the sources inside
the image rather than from a zip file kept in version control.

## Setting it up

In the dashboard under **Settings → Browser extension**, issue a token. It is
shown exactly once. Then enter it in the extension's settings, together with
the dashboard's address.

The extension uses its own token rather than the dashboard session for two
reasons. The session cookie is set to `SameSite=Lax`, and the browser treats a
request from an extension as a foreign context, so the cookie would not be
sent at all. A separate token can also be revoked on its own, without signing
anybody out of the dashboard.

## Which device is meant

The device is determined by the address the request comes from, not by
anything the extension claims. The resolver looks that address up in the
neighbour table to get a MAC address, and from there the device name. The
extension therefore cannot change another device's rules, even by mistake.

The exception is stored in the device profile and bound to the MAC address
rather than to the IP address. An IP binding would stop working the next day
under IPv6, because Windows and Android rotate their temporary addresses
daily. It would also fail silently: nothing would break, the exception would
simply stop applying.

## Redirects: usually more than one click

A name can be allowed and still not resolve. `analytics.tiktok.com`, for
instance, points via CNAME at a chain:

    analytics.tiktok.com
      → analytics.tiktok.com.ttdns2.com
      → analytics.tiktok.com.edgekey.net
      → analytics.tiktok.com.bytewlb.akadns.net
      → e35058.api15.akamaiedge.net

If any link in the chain is on a list, the cloaking check takes effect, which is precisely the
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
