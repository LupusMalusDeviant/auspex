import { settings, save, me } from "./auspex.js";
import { t, loadLanguage, label } from "./texts.js";

const $ = (id) => document.getElementById(id);

(async () => {
  // Language first, then labels - otherwise the page would stand there in
  // German for a moment and then jump.
  await loadLanguage();
  label();

  const e = await settings();
  $("base").value = e.base;
  $("token").value = e.token;
})();

$("save").addEventListener("click", async () => {
  const status = $("status");
  status.textContent = t.checking;
  status.style.color = "var(--muted)";

  await save($("base").value, $("token").value);

  // Try it straight away instead of only saving: wrong details otherwise
  // only show up when you need the window - and what stands there then is a
  // message with no connection to what was typed here.
  const e = await me();
  if (!e.ok) {
    status.textContent = e.error === "setup" ? t.bothFields : e.error;
    status.style.color = "var(--block)";
    return;
  }
  if (!e.data.known) {
    status.textContent = e.data.hint;
    status.style.color = "var(--block)";
    return;
  }
  status.textContent = t.connectedAs(e.data.device);
  status.style.color = "var(--ok)";
});
