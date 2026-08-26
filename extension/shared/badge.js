// Sets a marker on the dashboard page so the interface knows whether the
// extension is installed in *this* browser.
//
// The server cannot answer that: it only sees that a token was used at some
// point — from which browser, on which machine, it does not know. Open the
// dashboard on a machine without the extension and it would otherwise say
// "installed", and you would never find the setup.
//
// Runs exclusively on the address configured in the settings — the background
// service registers the script for exactly that one. On every other page
// nothing happens, and the extension is not detectable there either.

const api = typeof browser !== "undefined" ? browser : chrome;

// document_start: the attribute is set before the page runs its own code.
// Otherwise there would be a window in which the interface is already asking
// and the answer is not there yet.
document.documentElement.dataset.auspexExtension =
  api.runtime.getManifest().version;
