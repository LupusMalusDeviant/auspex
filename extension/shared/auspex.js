// The way in to Auspex.
//
// The extension never says which device it is talking about — that follows
// from the address the request comes from. So nobody can set an exception
// for someone else's device through it, not even by accident.

import { t, code } from "./texts.js";

const api = typeof browser !== "undefined" ? browser : chrome;

export async function settings() {
  // The old key names are read as a fallback. Up to 0.9.0 they were called
  // basis and zeichen; whoever loads the new build over an old one would
  // otherwise face an empty settings page and have to enter a token that is
  // shown exactly once and that they no longer have.
  //
  // Read only, never written: save() below writes the new names and clears
  // the old ones, so the fallback goes away by itself on the first save.
  const s = await api.storage.local.get(["base", "token", "basis", "zeichen"]);
  return {
    base: s.base ?? s.basis ?? "",
    token: s.token ?? s.zeichen ?? "",
  };
}

export async function save(base, token) {
  // Without a trailing slash, so the paths below come out right.
  await api.storage.local.set({
    base: (base ?? "").trim().replace(/\/+$/, ""),
    token: (token ?? "").trim(),
  });
  // Only after the write has gone through: a cleanup that runs first and
  // then fails would take the very values it was tidying up with it.
  try {
    await api.storage.local.remove(["basis", "zeichen"]);
  } catch (e) {
    /* nothing there, or the browser will not let it go - harmless either way */
  }
}

export async function call(path, options = {}) {
  const { base, token } = await settings();
  if (!base || !token) {
    return { ok: false, error: "setup" };
  }

  let answer;
  try {
    answer = await fetch(base + path, {
      ...options,
      headers: {
        "Content-Type": "application/json",
        Authorization: "Bearer " + token,
        // So the server's messages come back in the language the window
        // next to it is showing. A cookie will not do here: different
        // origin, and the sign-in runs on the token.
        "X-Auspex-Language": code,
        ...(options.headers ?? {}),
      },
      // The dashboard's session has no business here: the sign-in runs
      // on the token, and a cookie sent along would only obscure what
      // actually does the checking.
      credentials: "omit",
    });
  } catch (e) {
    return { ok: false, error: t.unreachable(e.message) };
  }

  if (answer.status === 401) {
    return { ok: false, error: t.tokenInvalid };
  }

  let data = null;
  try {
    data = await answer.json();
  } catch {
    return { ok: false, error: t.unreadable };
  }

  if (!answer.ok) {
    return { ok: false, error: data?.error ?? t.errorWithCode(answer.status) };
  }
  return { ok: true, data };
}

export const me = () => call("/api/ext/me");

export const blocked = (minutes = 30) =>
  call("/api/ext/blocked?minutes=" + minutes);

export const allow = (domain, minutes) =>
  call("/api/ext/allow", {
    method: "POST",
    body: JSON.stringify({ domain, minutes: minutes ?? null }),
  });

export const revoke = (domain) =>
  call("/api/ext/revoke", {
    method: "POST",
    body: JSON.stringify({ domain, minutes: null }),
  });
