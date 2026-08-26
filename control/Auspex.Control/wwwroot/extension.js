// Detects whether the browser extension is installed in *this* browser.
//
// The question cannot be answered on the server. All that is known there is
// that a token was used at some point — from which device and which browser
// is written nowhere. Whoever opens the dashboard on a second machine would
// otherwise be shown "set up" and would never find the setup.
//
// The extension sets an attribute on the <html> element when this page loads
// (see extension/shared/badge.js). Nothing more is needed.

export function version() {
  return document.documentElement.dataset.auspexExtension ?? null;
}

/**
 * Which browser this is — for the right instructions.
 *
 * Deliberately rough. The point is not to identify the browser exactly but
 * whether "chrome://extensions" or "about:debugging" is the right address.
 * Everything Chromium-like shares the first route.
 */
export function browser() {
  const ua = navigator.userAgent;

  // Order matters: Edge and Opera carry "Chrome" in their user agent,
  // while Chrome carries neither "Edg" nor "OPR".
  if (/Firefox\/|FxiOS/.test(ua)) return "firefox";
  if (/Edg\//.test(ua)) return "edge";
  if (/OPR\//.test(ua)) return "opera";
  if (/Chrome\/|Chromium\//.test(ua)) return "chrome";
  if (/Safari\//.test(ua)) return "safari";
  return "unknown";
}

/** The address the page is currently running under — for the setup. */
export function base() {
  return window.location.origin;
}

/** Text to the clipboard, so the address does not have to be typed out. */
export async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    // Without HTTPS some browsers refuse the clipboard. No reason for an
    // error message — the text to select stands right next to it.
    return false;
  }
}
