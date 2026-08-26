// Appearance: theme, accent, density, font size.
//
// Deliberately without Blazor. The choice should still work when the
// connection to the server has dropped — and it has to take effect before the
// first paint, or the wrong theme flashes up briefly while loading. The part
// that sets the values therefore exists twice: once inline in the <head> for
// the cold start, once here for operating it.
//
// Everything is set on <html>, so it takes effect without exception.

(function () {
  "use strict";

  var KEY = "auspex-darstellung";

  // The accent is a HUE, not a hex value. Only the number changes;
  // lightness and chroma sit in the stylesheet. Because OKLCH holds
  // perceived lightness fixed, a green accent has the same contrast as a red
  // one — with hex values you would have to measure that for every tone
  // individually and still not quite hit it.
  //
  // Muted tones, no neon: this is a measuring instrument, not a toy.
  var ACCENTS = {
    oxblut:  { h: 15,  c: 0.105 },
    rost:    { h: 45,  c: 0.115 },
    messing: { h: 80,  c: 0.110 },
    moos:    { h: 145, c: 0.095 },
    petrol:  { h: 195, c: 0.095 },
    stahl:   { h: 240, c: 0.090 },
    indigo:  { h: 280, c: 0.100 },
    pflaume: { h: 330, c: 0.100 },
  };

  var DENSITY = { kompakt: 0.85, normal: 1, luftig: 1.18 };
  var FONT = { klein: 0.92, normal: 1, gross: 1.12 };

  function defaults() {
    return { fassung: "system", akzent: "oxblut", dichte: "normal", schrift: "normal" };
  }

  function read() {
    try {
      var raw = localStorage.getItem(KEY);
      if (!raw) {
        return defaults();
      }
      var w = JSON.parse(raw);
      var s = defaults();
      return {
        fassung: w.fassung === "hell" || w.fassung === "dunkel" ? w.fassung : s.fassung,
        akzent: ACCENTS[w.akzent] ? w.akzent : s.akzent,
        dichte: DENSITY[w.dichte] ? w.dichte : s.dichte,
        schrift: FONT[w.schrift] ? w.schrift : s.schrift,
      };
    } catch (e) {
      // Private window, blocked storage or a broken entry: then without
      // memory, but never with half the values.
      return defaults();
    }
  }

  function apply(w) {
    var e = document.documentElement;

    if (w.fassung === "hell") {
      e.dataset.theme = "light";
    } else if (w.fassung === "dunkel") {
      e.dataset.theme = "dark";
    } else {
      // No attribute = "system". The rules in the stylesheet ask for
      // absence, so a later change at the operating system still takes
      // effect.
      delete e.dataset.theme;
    }

    var a = ACCENTS[w.akzent] || ACCENTS.oxblut;
    e.style.setProperty("--accent-h", String(a.h));
    e.style.setProperty("--accent-c", String(a.c));
    e.style.setProperty("--d", String(DENSITY[w.dichte] || 1));
    e.style.setProperty("--font-scale", String(FONT[w.schrift] || 1));

    mark(w);
  }

  function mark(w) {
    var buttons = document.querySelectorAll("[data-sets]");
    for (var i = 0; i < buttons.length; i++) {
      var k = buttons[i];
      var axis = k.dataset.sets;
      var chosen = w[axis] === k.dataset.value;
      k.classList.toggle("chosen", chosen);
      k.setAttribute("aria-pressed", chosen ? "true" : "false");
    }
  }

  function save(w) {
    try {
      localStorage.setItem(KEY, JSON.stringify(w));
    } catch (e) {
      // Not storable - the choice then applies to this session only.
    }

    // And to the server, so it applies on every machine and the browser
    // extension can read it. Deliberately sent afterwards and not waited
    // for: painting happens immediately from local storage, and if the
    // network happens to be unwilling, that is no reason not to change the
    // colour.
    try {
      fetch("/api/appearance", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(w),
      }).catch(function () { /* offline: the local choice still stands */ });
    } catch (e) {
      /* fetch not available */
    }
  }

  // Event delegation rather than individual listeners: Blazor rebuilds the
  // header on every page change, and directly attached listeners would be
  // gone afterwards.
  document.addEventListener("click", function (ev) {
    var button = ev.target.closest ? ev.target.closest("[data-sets]") : null;
    if (!button) {
      return;
    }
    var w = read();
    w[button.dataset.sets] = button.dataset.value;
    save(w);
    apply(w);
  });

  // Set the marking again after every page change: the buttons are then
  // different elements from before.
  var observer = new MutationObserver(function () {
    mark(read());
  });

  // And set the values themselves again.
  //
  // On a page change Blazor replaces the attributes on the <html> element
  // with what the server rendered - and wipes style and data-theme away with
  // them. Measured: before the change it said "--accent-h: 145; ...",
  // afterwards nothing at all, and the accent fell back to the default. The
  // choice was never lost, only its application.
  //
  // Rather than listening for a Blazor event - which hangs off its internals
  // and can be called something else in the next version - the state is
  // watched here and restored. That heals itself, whoever touches the
  // attributes.
  var rootWatch = new MutationObserver(function () {
    var w = read();
    var e = document.documentElement;
    var a = ACCENTS[w.akzent] || ACCENTS.oxblut;
    // Compare first, then write - otherwise our own change triggers the
    // observer again, and that endlessly.
    if (e.style.getPropertyValue("--accent-h").trim() !== String(a.h)) {
      apply(w);
    }
  });

  function start() {
    apply(read());
    if (document.body) {
      observer.observe(document.body, { childList: true, subtree: true });
    }
    rootWatch.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["style", "data-theme"],
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", start);
  } else {
    start();
  }
})();
