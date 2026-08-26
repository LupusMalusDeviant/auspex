import { me, blocked, allow, revoke, settings } from "./auspex.js";
import { applyAppearance } from "./appearance.js";
import { t, loadLanguage, label } from "./texts.js";

const api = typeof browser !== "undefined" ? browser : chrome;

const $ = (id) => document.getElementById(id);
let tabId = null;
let deviceName = "";

function notice(text, kind = "") {
  const k = $("notice");
  k.textContent = text;
  k.className = "notice " + kind;
  k.hidden = !text;
}

function entry(name, below, buttons) {
  const d = document.createElement("div");
  d.className = "entry";

  const n = document.createElement("div");
  n.className = "name";
  n.textContent = name;
  d.append(n);

  const z = document.createElement("div");
  z.className = "row";
  if (below) {
    const i = document.createElement("span");
    i.className = "info";
    i.textContent = below;
    z.append(i);
  }
  for (const k of buttons) {
    z.append(k);
  }
  d.append(z);
  return d;
}

function button(text, cls, onClick) {
  const b = document.createElement("button");
  b.textContent = text;
  if (cls) b.className = cls;
  b.addEventListener("click", async () => {
    // Double clicks would write two rules and make the resolver rebuild
    // twice.
    b.disabled = true;
    try {
      await onClick();
    } finally {
      b.disabled = false;
    }
  });
  return b;
}

async function allowHost(host, minutes, afterwards) {
  const e = await allow(host, minutes);
  notice(e.ok ? e.data.report : e.error, e.ok ? "ok" : "error");
  if (!e.ok) {
    return;
  }

  // A name can be allowed and still fail to resolve: if it points via a
  // redirect at something that is blocked as well, the cloaking check bites.
  // Auspex then names the target — and rather than making you type it out,
  // here is the button for it.
  if (e.data.forwarded) {
    const k = button(t.allowToo(e.data.forwarded), "primary", () =>
      allowHost(e.data.forwarded, minutes)
    );
    document.getElementById("notice").append(document.createElement("br"), k);
  }

  await api.runtime.sendMessage({ kind: "forget", tabId, host });
  await afterwards?.();
  await load();
}

function remainingTime(seconds) {
  if (seconds >= 3600) return Math.round(seconds / 3600) + " h";
  if (seconds >= 60) return Math.round(seconds / 60) + " min";
  return seconds + " s";
}

async function showPage() {
  const list = $("pageList");
  list.replaceChildren();

  const failures = await api.runtime.sendMessage({ kind: "failed", tabId });

  $("pageEmpty").hidden = (failures ?? []).length > 0;

  for (const g of failures ?? []) {
    list.append(
      entry(
        g.host,
        t.failed(g.count, g.kind ?? t.request),
        [
          button(t.fifteenMin, "primary", () => allowHost(g.host, 15)),
          button(t.oneHour, "", () => allowHost(g.host, 60)),
          button(t.forGood, "primary", () => allowHost(g.host, null)),
        ]
      )
    );
  }
}

function showRunning(exceptions) {
  const area = $("running");
  const list = $("runningList");
  list.replaceChildren();

  if (!exceptions?.length) {
    area.hidden = true;
    return;
  }
  area.hidden = false;

  for (const a of exceptions) {
    const rest = document.createElement("span");
    rest.className = "remaining";
    rest.textContent = t.left(remainingTime(a.remainingSeconds));

    const d = entry(a.domain, null, [
      button(t.extend, "", () => allowHost(a.domain, 60)),
      button(t.blockNow, "blocks", async () => {
        const e = await revoke(a.domain);
        notice(e.ok ? e.data.report : e.error, e.ok ? "ok" : "error");
        await load();
      }),
    ]);
    d.querySelector(".row").prepend(rest);
    list.append(d);
  }
}

async function showRecent() {
  const list = $("recentList");
  list.replaceChildren();

  const g = await blocked(30);
  if (!g.ok) {
    list.append(Object.assign(document.createElement("p"), {
      className: "empty",
      textContent: g.error,
    }));
    return;
  }

  const hits = g.data.hits ?? [];
  if (hits.length === 0) {
    list.append(Object.assign(document.createElement("p"), {
      className: "empty",
      textContent: t.nothingBlocked,
    }));
    return;
  }

  // hit and not t: t is the string table.
  for (const hit of hits.slice(0, 8)) {
    list.append(
      entry(hit.name, t.timesBlocked(hit.count), [
        button(t.fifteenMin, "primary", () => allowHost(hit.name, 15)),
        button(t.forGood, "primary", () => allowHost(hit.name, null)),
      ])
    );
  }
}

async function load() {
  const e = await me();

  if (!e.ok) {
    if (e.error === "setup") {
      $("device").textContent = t.notSetUp;
      notice(t.setupMissing);
    } else {
      $("device").textContent = t.notConnected;
      notice(e.error, "error");
    }
    return;
  }

  if (!e.data.known) {
    $("device").textContent = t.deviceUnknown;
    notice(e.data.hint, "error");
    return;
  }

  deviceName = e.data.device;
  $("device").textContent = deviceName + (e.data.profile ? t.profile(e.data.profile) : "");

  showRunning(e.data.exceptions);
  await showPage();
  await showRecent();
}

$("toSettings").addEventListener("click", (ev) => {
  ev.preventDefault();
  api.runtime.openOptionsPage();
});

$("toDashboard").addEventListener("click", async (ev) => {
  ev.preventDefault();
  const { base } = await settings();
  if (base) {
    api.tabs.create({ url: base });
  }
});

(async () => {
  // Language from local storage first, then labels: otherwise the window
  // would stand there in German for a moment and jump. Whatever the
  // dashboard reports, applyAppearance() catches up right after.
  await loadLanguage();
  label();

  // Then the looks: the window is there the instant you click, and it
  // should not flash grey first and then change colour.
  applyAppearance().then(label);

  const [tab] = await api.tabs.query({ active: true, currentWindow: true });
  tabId = tab?.id ?? -1;
  await load();
})();
