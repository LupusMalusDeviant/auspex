// Keyboard operation for the strip chart.
//
// Whoever works through a list does not want to switch between keyboard and
// mouse. j/k move, f allows, b blocks, w asks why, p creates a profile.
//
// Deliberately without Blazor: the selection is pure display, and a key press
// should still change the row when the connection to the server happens to be
// stuck. What ends up being triggered are the buttons that sit in the row
// anyway - so there is no second route into the same action that could drift
// apart later.

(function () {
  "use strict";

  var ACTIVE = "here";

  function rows() {
    return Array.prototype.slice.call(
      document.querySelectorAll("table.stream tbody tr"));
  }

  function current() {
    return document.querySelector("table.stream tbody tr." + ACTIVE);
  }

  function select(row) {
    if (!row) {
      return;
    }
    var previous = current();
    if (previous) {
      previous.classList.remove(ACTIVE);
    }
    row.classList.add(ACTIVE);
    // Only scroll when the row is not visible anyway - otherwise the page
    // jumps on every key press.
    var r = row.getBoundingClientRect();
    if (r.top < 60 || r.bottom > window.innerHeight - 20) {
      row.scrollIntoView({ block: "center", behavior: "auto" });
    }
  }

  function move(direction) {
    var all = rows();
    if (!all.length) {
      return;
    }
    var now = current();
    var i = now ? all.indexOf(now) : -1;
    var next = i < 0 ? 0 : Math.min(Math.max(i + direction, 0), all.length - 1);
    select(all[next]);
  }

  /**
   * Triggers an action in the current row.
   *
   * What gets clicked is the button that sits in the row anyway - so there is
   * still only ONE route into the action, and the keyboard cannot do
   * something different from the mouse.
   *
   * It is found through data-tat and not through its caption. The text used
   * to be here, and I had noted that as an advantage - until the interface
   * became bilingual: in English the button is called "allow", and the f key
   * grasped at nothing. A caption is for people, an identifier for programs;
   * mixing the two held up exactly as long as there was only one language.
   */
  function trigger(action) {
    var row = current();
    if (!row) {
      return false;
    }
    var button = row.querySelector('.row-actions button[data-action="' + action + '"]');
    if (!button || button.disabled) {
      return false;
    }
    button.click();
    return true;
  }

  function typingAllowed(target) {
    // In an input field "f" means an f, not "allow".
    if (!target) {
      return true;
    }
    var t = target.tagName;
    return !(t === "INPUT" || t === "TEXTAREA" || t === "SELECT" || target.isContentEditable);
  }

  document.addEventListener("keydown", function (ev) {
    if (!document.querySelector("table.stream")) {
      return;
    }
    if (ev.ctrlKey || ev.altKey || ev.metaKey || !typingAllowed(ev.target)) {
      return;
    }

    var handled = true;
    switch (ev.key) {
      case "j": case "ArrowDown": move(1); break;
      case "k": case "ArrowUp":   move(-1); break;
      case "f": handled = trigger("allow"); break;
      case "b": handled = trigger("block"); break;
      case "w": handled = trigger("why"); break;
      case "p": handled = trigger("profile"); break;
      case "Escape": {
        var a = current();
        if (a) { a.classList.remove(ACTIVE); } else { handled = false; }
        break;
      }
      default: handled = false;
    }

    if (handled) {
      ev.preventDefault();
    }
  });

  // The table is rebuilt on every refresh - every two seconds while
  // "live" is running. The selection then travels with the position, not
  // with the element: the row underneath is a different one by now, but the
  // place in the list is the same.
  var marker = -1;
  new MutationObserver(function () {
    if (!document.querySelector("table.stream")) {
      return;
    }
    if (!current() && marker >= 0) {
      var all = rows();
      if (all.length) {
        select(all[Math.min(marker, all.length - 1)]);
      }
    }
    var now = current();
    marker = now ? rows().indexOf(now) : -1;
  }).observe(document.body, { childList: true, subtree: true });
})();
