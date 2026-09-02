// Zero-dependency tests for the keyboard.js keydown trap.
//
// Run:  node tools/jstests/keyboard-tests.mjs
//
// Same approach as gesture-tests.mjs: load wwwroot/js/keyboard.js into a vm
// sandbox with a fake window/document, register the KEYBOARD handler, fire
// synthetic keydown events at it, and assert on two things — whether the event
// was preventDefault()ed, and what reached the .NET bridge.
//
// This exists because of the Space bug. `' '` is in trappedKeys (it plays the
// chart), the form-control exclusion covered only INPUT/TEXTAREA/SELECT, and
// nothing in the C# suite can observe a JS preventDefault — so e.preventDefault()
// cancelled the activation click on every button in the application and no test
// could see it. Enter still worked, and screen readers in browse mode synthesize
// a click rather than a keypress, which is why it survived for so long.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const source = readFileSync(
  join(root, 'AccessibleTrader.BlazorClient.Components', 'wwwroot', 'js', 'keyboard.js'), 'utf8');

// ── Sandbox scaffolding ─────────────────────────────────────────────────────

function makeHarness() {
  const calls = [];
  const dotnet = {
    invokeMethodAsync: (method, ...args) => { calls.push([method, ...args]); return Promise.resolve(); },
  };

  const windowListeners = {};
  const sandbox = {
    window: { addEventListener: (type, fn) => { (windowListeners[type] ??= []).push(fn); } },
    document: {
      getElementById: () => null,
      querySelectorAll: () => [],
      addEventListener: () => {},
      activeElement: null,
    },
    navigator: { userAgent: 'test' },
    console, Math,
    Date: { now: () => 100_000 },
    setTimeout: () => 0,
    clearTimeout: () => {},
    requestAnimationFrame: (cb) => { cb(); return 0; },
    cancelAnimationFrame: () => {},
  };
  sandbox.window.document = sandbox.document;
  sandbox.globalThis = sandbox;

  vm.createContext(sandbox);
  vm.runInContext(source, sandbox, { filename: 'keyboard.js' });
  sandbox.window.accessibleTrader.registerKeyboardHandler(dotnet);

  // A minimal DOM node. `attrs` become getAttribute/hasAttribute answers.
  // `parent` links a node to an ancestor chain so closest() can walk it; `focusable`
  // puts the node in the dialog's tab-stop list. Both default off, so every existing
  // test keeps the node it had.
  const node = (tagName, attrs = {}, opts = {}) => {
    const n = {
      tagName,
      isContentEditable: false,
      offsetParent: opts.hidden ? null : {},
      parent: opts.parent ?? null,
      focusable: !!opts.focusable,
      focused: false,
      getAttribute: (k) => (k in attrs ? attrs[k] : null),
      hasAttribute: (k) => k in attrs,
      focus() { sandbox.document.activeElement = this; this.focused = true; },
    };
    // Minimal closest(): matches this node or an ancestor against a comma-separated
    // list of [role="x"] selectors, which is all ARROW_WIDGET_SELECTOR contains.
    n.closest = (selector) => {
      const roles = [...selector.matchAll(/\[role="([^"]+)"\]/g)].map(m => m[1]);
      for (let cur = n; cur; cur = cur.parent)
        if (roles.includes(cur.getAttribute('role'))) return cur;
      return null;
    };
    return n;
  };

  // Does `node` match one of the comma-separated simple selectors in `sel`?
  //
  // Only tag names and [tabindex] forms, which is all focusableSelector contains — but it
  // is applied for real. The previous harness returned the candidate list VERBATIM from
  // querySelectorAll, so the focusable selector was mocked past and never under test. That
  // is precisely how `summary` shipped missing: every test here was green while the browser
  // saw 19 tab stops that this file's selector did not, hit the `idx === -1` snap-back, and
  // pinned focus on one control in both directions. A harness that hands the code under test
  // its own answer cannot fail for the thing it is meant to be checking.
  const matchesSelector = (sel, n) =>
    sel.split(',').map(x => x.trim()).some(part => {
      if (part.startsWith('[tabindex]')) {
        const ti = n.getAttribute('tabindex');
        return ti !== null && !part.includes(':not([tabindex="-1"])') ? true
             : ti !== null && ti !== '-1';
      }
      const tag = part.match(/^[a-zA-Z]+/);
      if (!tag) return false;
      if (tag[0].toLowerCase() !== n.tagName.toLowerCase()) return false;
      // `:not([disabled])` and friends — honour the disabled exclusion, ignore the rest.
      if (part.includes(':not([disabled])') && n.hasAttribute('disabled')) return false;
      return true;
    });

  // A dialog element with a candidate child list, wired the way the trap reads it. What
  // actually becomes a tab stop is decided by the selector the trap passes in.
  const dialog = (role, focusables) => {
    const d = node('DIV', { role });
    for (const f of focusables) f.parent = d;
    d.querySelectorAll = (sel) => focusables.filter(f => matchesSelector(sel, f));
    d.contains = (el) => el === d || focusables.includes(el) ||
                         (el && el.parent ? d.contains(el.parent) : false);
    return d;
  };

  // Put dialogs in the document so the trap's querySelectorAll finds them.
  const mountDialogs = (...ds) => {
    sandbox.document.querySelectorAll = (sel) =>
      sel.includes('role=') ? ds.filter(d => sel.includes(`[role="${d.getAttribute('role')}"]`)) : [];
    sandbox.window.accessibleTrader.setModalOpen(true);
  };

  const setActive = (el) => { sandbox.document.activeElement = el; };

  // Fire a keydown and report whether the default action survived.
  const press = (key, target, mods = {}) => {
    let defaultPrevented = false;
    const ev = {
      key,
      shiftKey: !!mods.shift, ctrlKey: !!mods.ctrl, altKey: !!mods.alt,
      target: target ?? node('DIV'),
      preventDefault: () => { defaultPrevented = true; },
      stopImmediatePropagation: () => {},
    };
    for (const fn of windowListeners.keydown ?? []) fn(ev);
    return defaultPrevented;
  };

  return { calls, press, node, dialog, mountDialogs, setActive,
           doc: sandbox.document, api: sandbox.window.accessibleTrader };
}

const results = [];
function test(name, fn) {
  try { fn(); results.push([name, null]); }
  catch (e) { results.push([name, e]); }
}
const keysSent = (calls) => calls.filter(c => c[0] === 'OnKeyDown').map(c => c[1]);

// ── The Space bug ───────────────────────────────────────────────────────────

test('Space on a button is left alone so the activation click survives', () => {
  const h = makeHarness();
  const prevented = h.press(' ', h.node('BUTTON'));

  assert.equal(prevented, false, 'preventDefault on a button cancels its activation');
  assert.deepEqual(keysSent(h.calls), [], 'the chart-play shortcut must not also fire');
});

test('Space on a <summary> disclosure is left alone', () => {
  const h = makeHarness();
  assert.equal(h.press(' ', h.node('SUMMARY')), false);
});

test('Space on role=button / checkbox / switch / tab / option / treeitem is left alone', () => {
  for (const role of ['button', 'checkbox', 'switch', 'tab', 'option', 'treeitem',
                      'radio', 'menuitem', 'menuitemcheckbox', 'menuitemradio']) {
    const h = makeHarness();
    assert.equal(h.press(' ', h.node('DIV', { role })), false, `role=${role} was swallowed`);
  }
});

test('Space still plays the chart when focus is not on something activatable', () => {
  const h = makeHarness();
  const prevented = h.press(' ', h.node('DIV'));

  assert.equal(prevented, true, 'the shortcut still claims the key elsewhere');
  assert.deepEqual(keysSent(h.calls), ['SPACE']);
});

test('Space on a DISABLED button still plays the chart — it activates nothing', () => {
  const h = makeHarness();
  assert.equal(h.press(' ', h.node('BUTTON', { disabled: '' })), false,
    'a native disabled button is still a BUTTON; leave its key alone');

  const h2 = makeHarness();
  assert.equal(h2.press(' ', h2.node('DIV', { role: 'button', disabled: '' })), true,
    'a disabled ARIA widget activates nothing, so the shortcut may have the key');
  assert.deepEqual(keysSent(h2.calls), ['SPACE']);
});

test('Space on a link is NOT excluded — browsers scroll, they do not activate', () => {
  // Deliberate deviation from the audit note, which suggested adding A to the
  // exclusion set. Space never activates a link, so excluding one would lose the
  // chart-play shortcut and hand the key to page scrolling instead.
  const h = makeHarness();
  assert.equal(h.press(' ', h.node('A', { href: '#x' })), true);
  assert.deepEqual(keysSent(h.calls), ['SPACE']);
});

// ── The modified variants stay global ───────────────────────────────────────

test('Ctrl+Space (PlayPause) still fires with a button focused', () => {
  const h = makeHarness();
  assert.equal(h.press(' ', h.node('BUTTON'), { ctrl: true }), true);
  assert.deepEqual(keysSent(h.calls), ['SPACE']);
});

test('Shift+Space (PlaySeries) still fires with a button focused', () => {
  // Shift+Space is not an activation combination in any browser, so there is
  // nothing to protect and the chart command keeps working from anywhere.
  const h = makeHarness();
  assert.equal(h.press(' ', h.node('BUTTON'), { shift: true }), true);
  assert.deepEqual(keysSent(h.calls), ['SPACE']);
});

// ── Regression guards on the rest of the trap ───────────────────────────────

test('Enter is not trapped at all (which is why the bug hid)', () => {
  const h = makeHarness();
  assert.equal(h.press('Enter', h.node('BUTTON')), false);
  assert.deepEqual(keysSent(h.calls), []);
});

test('typing a space in a text input is untouched', () => {
  const h = makeHarness();
  assert.equal(h.press(' ', h.node('INPUT')), false);
  assert.deepEqual(keysSent(h.calls), []);
});

test('arrow keys still reach the dispatcher from a button', () => {
  const h = makeHarness();
  assert.equal(h.press('ArrowLeft', h.node('BUTTON')), true);
  assert.deepEqual(keysSent(h.calls), ['LEFT']);
});

test('Escape still reaches the dispatcher from inside a form control', () => {
  const h = makeHarness();
  assert.equal(h.press('Escape', h.node('SELECT')), true);
  assert.deepEqual(keysSent(h.calls), ['ESCAPE']);
});

test('single-letter chart commands stay gated on chart focus', () => {
  const h = makeHarness();
  assert.equal(h.press('h', h.node('DIV')), false, 'chart not focused — h must stay typable');

  h.api.setChartFocused(true);
  assert.equal(h.press('h', h.node('DIV')), true);
  assert.deepEqual(keysSent(h.calls), ['H']);
});

// ── The Tab trap (2026-09-01 accessibility audit) ───────────────────────────

test('Shift+Tab from the opening heading is trapped inside the dialog', () => {
  // THE defect. ModalBase focuses the <h2 tabindex="-1"> on open, and the trap's
  // focusableSelector ends with [tabindex]:not([tabindex="-1"]) — so the heading is
  // deliberately NOT a tab stop. The old branch logic tested `active === first` and
  // `active === last`; the heading is neither, and modal.contains(active) is true, so
  // no branch fired at all. preventDefault never ran and the browser's own sequential
  // navigation took over — backward from the first element of a dialog means backward
  // OUT of it. Every one of the 25 dialogs leaked on the very first Shift+Tab.
  const h = makeHarness();
  const first = h.node('BUTTON', {}, { focusable: true });
  const last  = h.node('BUTTON', {}, { focusable: true });
  const heading = h.node('H2', { tabindex: '-1' });
  const d = h.dialog('dialog', [first, last]);
  heading.parent = d;
  h.mountDialogs(d);
  h.setActive(heading);

  assert.equal(h.press('Tab', heading, { shift: true }), true,
    'Shift+Tab from the heading was not trapped — focus escapes the dialog');
  assert.equal(h.doc.activeElement, last,
    'Shift+Tab from the heading must wrap to the LAST focusable, not the first');
});

test('Tab from the opening heading goes forward to the first control', () => {
  const h = makeHarness();
  const first = h.node('BUTTON', {}, { focusable: true });
  const last  = h.node('BUTTON', {}, { focusable: true });
  const heading = h.node('H2', { tabindex: '-1' });
  const d = h.dialog('dialog', [first, last]);
  heading.parent = d;
  h.mountDialogs(d);
  h.setActive(heading);

  assert.equal(h.press('Tab', heading), true);
  assert.equal(h.doc.activeElement, first);
});

test('Tab wraps at the end and Shift+Tab wraps at the start', () => {
  // The behaviour the old logic DID get right. Pinned so the rewrite cannot lose it.
  const h = makeHarness();
  const first = h.node('BUTTON', {}, { focusable: true });
  const last  = h.node('BUTTON', {}, { focusable: true });
  const d = h.dialog('dialog', [first, last]);
  h.mountDialogs(d);

  h.setActive(last);
  assert.equal(h.press('Tab', last), true);
  assert.equal(h.doc.activeElement, first, 'Tab at the end must wrap to the start');

  h.setActive(first);
  assert.equal(h.press('Tab', first, { shift: true }), true);
  assert.equal(h.doc.activeElement, last, 'Shift+Tab at the start must wrap to the end');
});

test('Tab in the middle of the list is left alone', () => {
  // Anti-vacuity: a trap that claims EVERY Tab is a trap that pins focus.
  const h = makeHarness();
  const a = h.node('BUTTON', {}, { focusable: true });
  const b = h.node('BUTTON', {}, { focusable: true });
  const c = h.node('BUTTON', {}, { focusable: true });
  const d = h.dialog('dialog', [a, b, c]);
  h.mountDialogs(d);
  h.setActive(b);

  assert.equal(h.press('Tab', b), false,
    'a Tab from the middle of the list must reach the browser untouched');
});

test('the trap sees role=alertdialog, not just role=dialog', () => {
  // ModalContractScanTests was widened to {dialog, alertdialog} on 2026-08-29 and this
  // selector was not widened with it, so the destructive "strip your indicators and
  // drawings" confirmation armed the counter and then fell through
  // `if (dialogs.length === 0) return`. A guard written in C# does not protect the JS.
  const h = makeHarness();
  const first = h.node('BUTTON', {}, { focusable: true });
  const last  = h.node('BUTTON', {}, { focusable: true });
  const heading = h.node('H2', { tabindex: '-1' });
  const d = h.dialog('alertdialog', [first, last]);
  heading.parent = d;
  h.mountDialogs(d);
  h.setActive(heading);

  assert.equal(h.press('Tab', heading, { shift: true }), true,
    'an alertdialog is invisible to the trap — Tab walks out of a destructive prompt');
  assert.equal(h.doc.activeElement, last);
});

test('focus that has escaped the dialog is rehomed to the correct end', () => {
  const h = makeHarness();
  const first = h.node('BUTTON', {}, { focusable: true });
  const last  = h.node('BUTTON', {}, { focusable: true });
  const outside = h.node('BUTTON');
  const d = h.dialog('dialog', [first, last]);
  h.mountDialogs(d);

  h.setActive(outside);
  assert.equal(h.press('Tab', outside), true);
  assert.equal(h.doc.activeElement, first, 'forward rehome goes to the first control');

  h.setActive(outside);
  assert.equal(h.press('Tab', outside, { shift: true }), true);
  assert.equal(h.doc.activeElement, last, 'backward rehome goes to the last control');
});

// ── Scroll keys inside a dialog (2026-09-01 accessibility audit) ────────────

test('scroll keys are released while a modal is open, so a tall dialog can be read', () => {
  // CommandDispatcher's allowedWhileModalOpen list is Escape plus F1-F4 and nothing
  // else, so preventDefault here bought nothing and cost the ability to READ a dialog.
  // HelpModal has two focusable elements with ~400 lines of guide between them: the
  // keyboard reference could not be scrolled by keyboard.
  const h = makeHarness();
  h.api.setModalOpen(true);
  for (const key of ['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp', 'Home', 'End']) {
    assert.equal(h.press(key, h.node('H2', { tabindex: '-1' })), false,
      `${key} was swallowed inside a dialog, so the content cannot be scrolled`);
  }
  assert.deepEqual(keysSent(h.calls), [], 'and no chart command fires either');
});

test('a <summary> disclosure is a tab stop, so Tab is not pinned on a details-built dialog', () => {
  // THE REGRESSION, and it reached main. HelpModal is built from 37 <details> blocks;
  // <summary> is focusable by default, so the browser's tab order had ~19 stops while
  // focusableSelector knew about 2. Every Tab landed on a summary, which is inside the
  // dialog but absent from the trap's list, so the `idx === -1` branch treated it as an
  // escape and snapped focus back to `first`. Focus bounced on one control forever, in
  // BOTH directions — and the release that freed the scroll keys so the keyboard
  // reference could be read is the one that made it unreadable by Tab.
  const h = makeHarness();
  const close   = h.node('BUTTON');
  const summary = h.node('SUMMARY');
  const d = h.dialog('dialog', [close, summary]);
  h.mountDialogs(d);
  h.setActive(close);

  // From the first of two stops, a forward Tab is an ordinary move the browser owns.
  assert.equal(h.press('Tab', close), false,
    'Tab from the first of two real tab stops was trapped — the summary is not in the ' +
    'trap\'s focusable list, so it snapped focus back and pinned the dialog');
});

test('an element inside the dialog that is genuinely NOT a tab stop still seeds the trap', () => {
  // The other half, so the fix above cannot be "delete the idx === -1 branch". The opening
  // <h2 tabindex="-1"> is inside the dialog and is deliberately not a tab stop; a Tab from
  // it must still be claimed and seeded, or the browser walks out of the dialog.
  const h = makeHarness();
  const only = h.node('BUTTON');
  const heading = h.node('H2', { tabindex: '-1' });
  const d = h.dialog('dialog', [only]);
  heading.parent = d;
  h.mountDialogs(d);
  h.setActive(heading);

  assert.equal(h.press('Tab', heading), true,
    'Tab from the non-focusable heading must be trapped and seeded');
  assert.equal(h.doc.activeElement, only);
});

test('scroll keys are still trapped inside a dialog composite widget', () => {
  // None of the three NavigateTablistAsync callers calls preventDefault — they rely on
  // this file for it. Releasing the key here would move the tab AND scroll the dialog.
  const h = makeHarness();
  h.api.setModalOpen(true);
  const tablist = h.node('DIV', { role: 'tablist' });
  const tab = h.node('BUTTON', { role: 'tab' }, { parent: tablist });

  assert.equal(h.press('ArrowRight', tab), true,
    'a tablist consumes arrows itself and needs the browser scroll suppressed');
});

test('scroll keys still drive the chart when no modal is open', () => {
  const h = makeHarness();
  h.api.setChartFocused(true);
  assert.equal(h.press('ArrowRight', h.node('DIV')), true);
  assert.deepEqual(keysSent(h.calls), ['RIGHT'], 'chart navigation must be untouched');
});

// ── Report ──────────────────────────────────────────────────────────────────

let failed = 0;
for (const [name, err] of results) {
  if (err) { failed++; console.error(`FAIL  ${name}\n      ${err.message}`); }
  else console.log(`ok    ${name}`);
}
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
