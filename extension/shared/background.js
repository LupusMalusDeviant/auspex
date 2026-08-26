// The extension's background service.
//
// Its one genuinely important job: recording which requests on which tab
// failed at name resolution. That is why this extension is worth more than a
// remote control for the dashboard — the browser knows exactly what broke on
// *this* page, while thirty other devices are writing into the query log at
// the same time.
//
// Runs unchanged in Chrome (Manifest V3) and Firefox: both know
// `browser`/`chrome` with the same calls, and `webRequest.onErrorOccurred` is
// observe-only in both — no blocking permission needed.

const api = typeof browser !== "undefined" ? browser : chrome;

// Failed names per tab, with a timestamp. Deliberately in memory and not on
// disk: after a browser restart the question "what just broke" is moot
// anyway.
const failed = new Map();

// Errors that suggest a blocked name resolution. Chrome and Firefox name
// them differently, and neither reports the same thing consistently for
// NXDOMAIN - hence several.
const NAME_ERRORS = new Set([
  "net::ERR_NAME_NOT_RESOLVED",
  "net::ERR_NAME_RESOLUTION_FAILED",
  "NS_ERROR_UNKNOWN_HOST",
  "net::ERR_ADDRESS_UNREACHABLE",
]);

function hostOf(url) {
  try {
    return new URL(url).hostname.toLowerCase();
  } catch {
    return "";
  }
}

api.webRequest.onErrorOccurred.addListener(
  (details) => {
    if (details.tabId < 0 || !NAME_ERRORS.has(details.error)) {
      return;
    }

    const host = hostOf(details.url);
    if (!host) {
      return;
    }

    const list = failed.get(details.tabId) ?? new Map();
    const soFar = list.get(host) ?? { count: 0, last: 0, kind: details.type };
    soFar.count += 1;
    soFar.last = Date.now();
    list.set(host, soFar);
    failed.set(details.tabId, list);

    // The number on the icon makes it visible that there is something to do -
    // without having to open the window.
    const mark = String(Math.min(list.size, 99));
    api.action.setBadgeText({ tabId: details.tabId, text: mark });
    api.action.setBadgeBackgroundColor({ color: "#d29922" });
  },
  { urls: ["<all_urls>"] }
);

// Clean up when leaving a page: what failed on the old page does not help
// on the new one and would only confuse.
api.webNavigation?.onCommitted.addListener((details) => {
  if (details.frameId === 0) {
    failed.delete(details.tabId);
    api.action.setBadgeText({ tabId: details.tabId, text: "" });
  }
});

api.tabs.onRemoved.addListener((tabId) => failed.delete(tabId));

api.runtime.onMessage.addListener((message, _sender, respond) => {
  if (message?.kind === "failed") {
    const list = failed.get(message.tabId) ?? new Map();
    respond(
      [...list.entries()]
        .map(([host, d]) => ({ host, ...d }))
        .sort((a, b) => b.last - a.last)
    );
    return true;
  }

  if (message?.kind === "forget") {
    const list = failed.get(message.tabId);
    list?.delete(message.host);
    const rest = list?.size ?? 0;
    api.action.setBadgeText({
      tabId: message.tabId,
      text: rest > 0 ? String(rest) : "",
    });
    respond({ ok: true });
    return true;
  }

  return false;
});

// ---------------------------------------------------------------------------
// A marker on the dashboard page
// ---------------------------------------------------------------------------
//
// The dashboard should be able to tell whether the extension is installed in
// *this* browser, and offer setup otherwise. The server cannot answer that —
// it only sees that a token was used at some point, not from which browser.
//
// Registered deliberately for the configured address only, and not fixed in
// the manifest: a script running on every page would make the extension
// detectable by any website at all. In a tool meant to prevent tracking that
// would be the wrong direction.

const BADGE_ID = "auspex-badge";

/** "http://192.168.1.61:5390" becomes "http://192.168.1.61:5390/*". */
function patternOf(base) {
  try {
    const u = new URL(base);
    return `${u.protocol}//${u.host}/*`;
  } catch {
    return null;
  }
}

async function registerBadge() {
  // Firefox before 128 and some environments do not have scripting. The
  // hint in the dashboard is then simply absent - the extension itself
  // works unchanged.
  if (!api.scripting?.registerContentScripts) {
    return;
  }

  // basis as a fallback, as in auspex.js: on an install upgraded from before
  // 0.9.0 the address still sits under the old name, and without it the badge
  // script would not register - the dashboard would then say the extension is
  // not installed while it plainly is.
  const stored = await api.storage.local.get(["base", "basis"]);
  const base = stored.base ?? stored.basis;
  const pattern = base ? patternOf(base) : null;

  // Unregister first: the address may have changed, and a script for the old
  // one should not keep running.
  try {
    await api.scripting.unregisterContentScripts({ ids: [BADGE_ID] });
  } catch {
    // Was not registered. Not an error, just the normal case the first time.
  }

  if (!pattern) {
    return;
  }

  try {
    await api.scripting.registerContentScripts([
      {
        id: BADGE_ID,
        matches: [pattern],
        js: ["badge.js"],
        runAt: "document_start",
        persistAcrossSessions: true,
      },
    ]);
  } catch (error) {
    // An unusable address in the settings must not take the background
    // service down with it - everything else depends on it.
    console.warn("Auspex: could not register the badge script", error);
  }
}

// At startup and whenever the address changes.
registerBadge();
api.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && (changes.base || changes.basis)) {
    registerBadge();
  }
});
