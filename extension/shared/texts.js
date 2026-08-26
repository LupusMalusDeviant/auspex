// The extension's display text.
//
// The same rule as in the dashboard, only smaller: the text lives together
// per language, and the language itself comes from the dashboard. Letting it
// be chosen a second time here would be one switch too many - whoever sets
// the interface to English means the window next to it too.
//
// No translation tooling, no library: 40 strings in a window 380 pixels wide
// do not justify a dependency, and everything here is loaded from the
// extension's own package anyway.

const api = typeof browser !== "undefined" ? browser : chrome;
const STORE = "auspex-language";

const DE = {
  loading: "wird geladen …",
  onThisPage: "Auf dieser Seite gescheitert",
  nothingFailed: "Auf dieser Seite ist nichts an der Namensauflösung gescheitert.",
  runningNow: "Läuft gerade",
  recentlyBlocked: "Zuletzt geblockt",
  thisDevice: "(dieses Gerät)",
  settings: "Einstellungen",
  dashboard: "Dashboard",

  notSetUp: "noch nicht eingerichtet",
  setupMissing:
    "Adresse des Dashboards und Zeichen fehlen — unten unter Einstellungen eintragen.",
  notConnected: "nicht verbunden",
  deviceUnknown: "Gerät nicht erkannt",
  nothingBlocked: "In der letzten halben Stunde nichts geblockt.",

  failed: (n, kind) => `${n}× gescheitert · ${kind}`,
  request: "Anfrage",
  timesBlocked: (n) => `${n}× geblockt`,
  left: (rest) => "noch " + rest,
  profile: (name) => ` · Profil ${name}`,
  allowToo: (host) => `${host} auch freigeben`,

  fifteenMin: "15 min",
  oneHour: "1 h",
  forGood: "dauerhaft",
  extend: "verlängern",
  blockNow: "jetzt sperren",

  // Errors from the API client
  setup: "setup",
  unreachable: (reason) => "nicht erreichbar: " + reason,
  tokenInvalid: "Das Zeichen gilt nicht mehr.",
  unreadable: "unverständliche Antwort",
  errorWithCode: (code) => "Fehler " + code,

  // Settings page
  settingsTitle: "Auspex — Einstellungen",
  settingsExplanation:
    "Beides steht im Dashboard unter „Einstellungen“. Das Zeichen wird dort " +
    "einmal erzeugt und danach nicht wieder angezeigt.",
  dashboardAddress: "Adresse des Dashboards",
  tokenLabel: "Zeichen",
  saveAndCheck: "Speichern und prüfen",
  checking: "wird geprüft …",
  bothFields: "Beide Felder ausfüllen.",
  connectedAs: (device) => "Verbunden — erkannt als " + device,
};

const EN = {
  loading: "loading …",
  onThisPage: "Failed on this page",
  nothingFailed: "Nothing on this page failed at name resolution.",
  runningNow: "Running now",
  recentlyBlocked: "Recently blocked",
  thisDevice: "(this device)",
  settings: "Settings",
  dashboard: "Dashboard",

  notSetUp: "not set up yet",
  setupMissing:
    "The dashboard address and a token are missing — enter them under Settings below.",
  notConnected: "not connected",
  deviceUnknown: "Device not recognised",
  nothingBlocked: "Nothing blocked in the last half hour.",

  failed: (n, kind) => `failed ${n}× · ${kind}`,
  request: "request",
  timesBlocked: (n) => `blocked ${n}×`,
  left: (rest) => rest + " left",
  profile: (name) => ` · profile ${name}`,
  allowToo: (host) => `Allow ${host} too`,

  fifteenMin: "15 min",
  oneHour: "1 h",
  forGood: "for good",
  extend: "extend",
  blockNow: "block now",

  setup: "setup",
  unreachable: (reason) => "unreachable: " + reason,
  tokenInvalid: "That token is no longer valid.",
  unreadable: "unreadable answer",
  errorWithCode: (code) => "Error " + code,

  settingsTitle: "Auspex — Settings",
  settingsExplanation:
    "Both are in the dashboard under “Settings”. The token is issued there " +
    "once and never shown again.",
  dashboardAddress: "Dashboard address",
  tokenLabel: "Token",
  saveAndCheck: "Save and check",
  checking: "checking …",
  bothFields: "Fill in both fields.",
  connectedAs: (device) => "Connected — recognised as " + device,
};

const TABLE = { de: DE, en: EN };

/**
 * The strings for the currently remembered language.
 *
 * Synchronous, because every label needs them and an await per word would
 * make the window unreadable. Filled once at startup by loadLanguage(); until
 * then German applies, the original.
 */
export let t = DE;

/** The code of the currently remembered language — goes out as a header. */
export let code = "de";

function use(k) {
  code = TABLE[k] ? k : "de";
  t = TABLE[code];
  document.documentElement.lang = code;
}

/** Reads the last remembered language from local storage. */
export async function loadLanguage() {
  try {
    const { [STORE]: remembered } = await api.storage.local.get([STORE]);
    use(remembered ?? "de");
  } catch (e) {
    use("de");
  }
  return code;
}

/** Takes over the language the dashboard reports. */
export async function rememberLanguage(k) {
  if (!TABLE[k] || k === code) {
    return;
  }
  use(k);
  try {
    await api.storage.local.set({ [STORE]: k });
  } catch (e) {
    /* cannot be stored: then it applies to this window only */
  }
}

/**
 * Fills every element carrying data-t from the table.
 *
 * That keeps the labelling visible in the HTML - data-t names the key - and
 * saves hunting for it in JavaScript.
 */
export function label(root = document) {
  for (const el of root.querySelectorAll("[data-t]")) {
    const value = t[el.dataset.t];
    if (typeof value === "string") {
      el.textContent = value;
    }
  }
  for (const el of root.querySelectorAll("[data-t-placeholder]")) {
    const value = t[el.dataset.tPlaceholder];
    if (typeof value === "string") {
      el.placeholder = value;
    }
  }
}
