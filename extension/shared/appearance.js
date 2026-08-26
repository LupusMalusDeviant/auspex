// Takes the appearance from the dashboard.
//
// The choice of theme and accent colour lives on the server, not in the
// browser's localStorage: localStorage is bound to the origin, and an
// extension has a different one from the dashboard. Through the API we get
// the same tone the dashboard is showing.
//
// Hue and chroma come WITH it - the extension deliberately does not keep the
// table of the eight tones itself. Two copies of a colour table drift apart
// the moment a tone is added.
//
// The field names inside the stored object stay as they are: they are the
// shape the dashboard writes into its own settings file, not names in this
// code.

import { call } from "./auspex.js";
import { rememberLanguage } from "./texts.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const STORE = "auspex-appearance";

function apply(d) {
  const w = document.documentElement;
  if (d.fassung === "hell") {
    w.dataset.theme = "light";
  } else if (d.fassung === "dunkel") {
    w.dataset.theme = "dark";
  } else {
    delete w.dataset.theme;
  }
  if (typeof d.h === "number") {
    w.style.setProperty("--accent-h", String(d.h));
  }
  if (typeof d.c === "number") {
    w.style.setProperty("--accent-c", String(d.c));
  }
}

/**
 * Paint from local storage first, then ask the dashboard.
 *
 * An extension window is there the instant you click; waiting for a network
 * answer would mean showing it wrong, or not at all, for a moment. The last
 * known state is almost always the right one.
 */
export async function applyAppearance() {
  try {
    const { [STORE]: remembered } = await api.storage.local.get([STORE]);
    if (remembered) {
      apply(remembered);
    }
  } catch (e) {
    // Nothing stored: then the stylesheet default it is.
  }

  const answer = await call("/api/ext/appearance");
  if (answer.ok && answer.data) {
    apply(answer.data);
    // The language comes from the same answer. Letting it be chosen a
    // second time here would be one switch too many - whoever sets the
    // dashboard to English means this window too.
    await rememberLanguage(answer.data.sprache);
    try {
      await api.storage.local.set({ [STORE]: answer.data });
    } catch (e) {
      /* cannot be stored */
    }
  }
}
