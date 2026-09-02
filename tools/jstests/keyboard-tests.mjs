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
  //
  // Two visibility answers, and they are deliberately NOT derived from each other.
  // `offsetParent` is what the trap used to filter on, and it is null for a rendered
  // element that is itself position:fixed — Toolbar's alertdialog. `getClientRects()`
  // is what the trap filters on now, and it is empty only for something with no layout
  // box. `fixed: true` models the first case: rendered, boxes present, offsetParent null.
  // Before this option existed the harness answered `offsetParent: {}` for every node,
  // so it could not express the one dialog that was actually escaping the trap. A double
  // that hands the code its own answer cannot fail for the thing it is meant to check.
  const node = (tagName, attrs = {}, opts = {}) => {
    const n = {
      tagName,
      isContentEditable: false,
      offsetParent: (opts.hidden || opts.fixed) ? null : {},
      getClientRects: () => (opts.hidden ? [] : [{ width: 1, height: 1 }]),
      parent: opts.parent ?? null,
      isConnected: opts.detached ? false : true,
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
  //
  // `:not([tabindex="-1"])` is honoured on EVERY clause, not only the [tabindex] one. The
  // browser honours a negative tabindex on any element, and the harness used to ignore the
  // exclusion on tag clauses ("ignore the rest") — which is why the unconditional `summary`
  // clause looked correct here while ObjectTreeModal's roved-out <summary role="treeitem">
  // was escaping the trap backwards in a real browser.
  const matchesSelector = (sel, n) =>
    sel.split(',').map(x => x.trim()).some(part => {
      const ti = n.getAttribute('tabindex');
      if (part.includes(':not([tabindex="-1"])') && ti === '-1') return false;
      if (part.startsWith('[tabindex]')) return ti !== null;
      const tag = part.match(/^[a-zA-Z]+/);
      if (!tag) return false;
      if (tag[0].toLowerCase() !== n.tagName.toLowerCase()) return false;
      // `:not([disabled])` — honour the disabled exclusion; attribute parts are ignored.
      if (part.includes(':not([disabled])') && n.hasAttribute('disabled')) return false;
      return true;
    });

  // A dialog element with a candidate child list, wired the way the trap reads it. What
  // actually becomes a tab stop is decided by the selector the trap passes in.
  //
  // `opts.name` is the dialog's data-modal-name — the ModalName it published, which is how
  // the trap maps the top of the modal stack to an element. Defaults to the role so the
  // single-dialog tests need not care.
  const dialog = (role, focusables, opts = {}) => {
    const attrs = { role, 'data-modal-name': opts.name ?? role };
    if (opts.labelledBy) attrs['aria-labelledby'] = opts.labelledBy;
    const d = node('DIV', attrs, opts);
    for (const f of focusables) f.parent = d;
    d.querySelectorAll = (sel) => focusables.filter(f => matchesSelector(sel, f));
    d.contains = (el) => el === d || focusables.includes(el) ||
                         (el && el.parent ? d.contains(el.parent) : false);
    return d;
  };

  // Put dialogs in the document so the trap's querySelectorAll finds them, and push the
  // modal stack in MOUNT order — which is both DOM order and open order here, so a test
  // that wants the two to disagree calls api.setModalStack itself afterwards.
  const mountDialogs = (...ds) => {
    sandbox.document.querySelectorAll = (sel) =>
      sel.includes('role=') ? ds.filter(d => sel.includes(`[role="${d.getAttribute('role')}"]`)) : [];
    sandbox.window.accessibleTrader.setModalStack(ds.map(d => d.getAttribute('data-modal-name')));
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

// ── The three escapes the 2026-09-02 review demonstrated ────────────────────

test('a dialog that is itself position:fixed (offsetParent null) is still seen by the trap', () => {
  // THE headline of the review, and the reason "trapped at last" was false. Toolbar's
  // destructive "strip your indicators and drawings" alertdialog carries position:fixed on
  // the element ITSELF, and CSSOM-View defines offsetParent as null for such an element.
  // The trap filtered dialogs on `offsetParent !== null`, so the widened selector found the
  // alertdialog and the very next line threw it away — Chromium 140 saw zero dialogs and
  // Tab walked out of an unanswered destructive prompt. The test above this section passed
  // throughout because every node here answered `offsetParent: {}`.
  const h = makeHarness();
  const first = h.node('BUTTON', {}, { focusable: true });
  const last  = h.node('BUTTON', {}, { focusable: true });
  const heading = h.node('H3', { tabindex: '-1' });
  const d = h.dialog('alertdialog', [first, last], { fixed: true });
  heading.parent = d;
  h.mountDialogs(d);

  h.setActive(heading);
  assert.equal(h.press('Tab', heading, { shift: true }), true,
    'a position:fixed alertdialog is invisible to the trap — its offsetParent is null, and ' +
    'Shift+Tab from its heading walks out onto the Load button that raised it');
  assert.equal(h.doc.activeElement, last);

  h.setActive(last);
  assert.equal(h.press('Tab', last), true, 'Tab from the last button must wrap, not escape');
  assert.equal(h.doc.activeElement, first);
});

test('a <summary> roved to tabindex="-1" is NOT a tab stop, so the tree dialog does not leak backwards', () => {
  // The regression the `summary` fix opened. ObjectTreeModal's pane headers are
  // <summary role="treeitem"> under a roving tabindex; after one ArrowDown, treeKeyboard.js
  // has set the summary to -1 and the series div to 0. On a hosted build nothing focusable
  // precedes the tree, so the browser's backward order from the series div leaves the
  // dialog. The trap's selector said `summary` with no exclusion, listed the roved-out
  // summary as index 0 and the series div as index 1, and let the keystroke through.
  const h = makeHarness();
  const heading = h.node('H2', { tabindex: '-1' });
  const pane    = h.node('SUMMARY', { role: 'treeitem', tabindex: '-1' });
  const series  = h.node('DIV',     { role: 'treeitem', tabindex: '0' });
  const close   = h.node('BUTTON');
  const d = h.dialog('dialog', [pane, series, close]);
  heading.parent = d;
  h.mountDialogs(d);
  h.setActive(series);

  assert.equal(h.press('Tab', series, { shift: true }), true,
    'Shift+Tab from the first REAL tab stop was let through — the roved-out summary is being ' +
    'counted as a stop ahead of it, and the browser walks backward out of the dialog');
  assert.equal(h.doc.activeElement, close,
    'Shift+Tab from the first real stop must wrap to the last control');
});

test('with two dialogs open, the trap keeps focus in the one on TOP OF THE STACK, not the last in the DOM', () => {
  // Stacked dialogs, demonstrated rather than inferred. HelpModal is rendered before nineteen
  // other modals in MainLayout and F1 is allowed while a modal is open, so Settings then F1
  // stacks Help on top while Settings stays LAST in the document. The trap took
  // dialogs[length - 1], judged Help's focus to be "outside the modal", and moved it into
  // Settings' search box by its own hand. The fix is the ordered modal stack CommandDispatcher
  // aims Escape by, pushed here by MainLayout and resolved to an element by data-modal-name.
  const h = makeHarness();
  const sSearch = h.node('INPUT', { type: 'search' });
  const sClose  = h.node('BUTTON');
  const settings = h.dialog('dialog', [sSearch, sClose], { name: 'Settings' });
  const hSummary = h.node('SUMMARY');
  const hClose   = h.node('BUTTON');
  const help = h.dialog('dialog', [hSummary, hClose], { name: 'Help' });
  h.mountDialogs(help, settings);   // DOM order: Help first, Settings last
  h.api.setModalStack(['Settings', 'Help']);   // open order: Settings first, Help on top

  h.setActive(hSummary);
  assert.equal(h.press('Tab', hSummary), false,
    'Tab from the first of two stops in Help must be an ordinary move the browser owns — ' +
    'it was claimed, which means the trap thinks focus is outside "the" modal (Settings)');
  assert.equal(h.doc.activeElement, hSummary, 'focus must not have been moved into Settings');

  h.setActive(hClose);
  assert.equal(h.press('Tab', hClose), true);
  assert.equal(h.doc.activeElement, hSummary,
    'Tab from the end of Help must wrap inside HELP, not land in Settings');

  // Focus that has left every dialog is rehomed into the dialog on TOP OF THE STACK — Help —
  // not the DOM-last one. The containment mitigation left this case rehoming into Settings,
  // underneath Help; observed in a real browser as Tab from <body> landing in s-search.
  const outside = h.node('BUTTON');
  h.setActive(outside);
  assert.equal(h.press('Tab', outside), true);
  assert.equal(h.doc.activeElement, hSummary,
    'escaped focus must be rehomed into the top of the stack (Help), not DOM-last (Settings)');
});

test('focus standing in the dialog BENEATH the top one is brought up into the top one', () => {
  // Top of the stack is Help, which is DOM-first; the user is somehow standing in Settings
  // underneath it (a click, a stale focus). aria-modal on Help has already told the screen
  // reader not to describe anything outside Help, so the next Tab must move them into Help —
  // the containment mitigation would have kept them in Settings.
  const h = makeHarness();
  const sSearch = h.node('INPUT', { type: 'search' });
  const sClose  = h.node('BUTTON');
  const settings = h.dialog('dialog', [sSearch, sClose], { name: 'Settings' });
  const hSummary = h.node('SUMMARY');
  const hClose   = h.node('BUTTON');
  const help = h.dialog('dialog', [hSummary, hClose], { name: 'Help' });
  h.mountDialogs(help, settings);
  h.api.setModalStack(['Settings', 'Help']);   // Settings opened first, Help on top

  h.setActive(sClose);
  assert.equal(h.press('Tab', sClose), true);
  assert.equal(h.doc.activeElement, hSummary, 'focus under the top dialog is brought up into it (Tab)');
  h.setActive(sSearch);
  assert.equal(h.press('Tab', sSearch, { shift: true }), true);
  assert.equal(h.doc.activeElement, hClose, 'focus under the top dialog is brought up into it (Shift+Tab)');
});

test('a stack entry with no dialog element (a role=menu) falls through to the entry beneath', () => {
  // The context menus push onto the stack under names no dialog wears. With Help (DOM-first)
  // under such an entry, the trap must resolve Help — not DOM-last Settings, not nothing.
  const h = makeHarness();
  const sSearch = h.node('INPUT', { type: 'search' });
  const sClose  = h.node('BUTTON');
  const settings = h.dialog('dialog', [sSearch, sClose], { name: 'Settings' });
  const hSummary = h.node('SUMMARY');
  const hClose   = h.node('BUTTON');
  const help = h.dialog('dialog', [hSummary, hClose], { name: 'Help' });
  h.mountDialogs(help, settings);
  h.api.setModalStack(['Settings', 'Help', 'ChartContextMenu']);   // the menu is not a dialog

  const outside = h.node('BUTTON');
  h.setActive(outside);
  assert.equal(h.press('Tab', outside), true);
  assert.equal(h.doc.activeElement, hSummary, 'Help is the nearest entry below the menu that has a dialog');
});

test('a return target OUTSIDE the dialog now on top is not used — the heading is', () => {
  // A opened from the chart; B opened over A; A closed out of order (a parent closing under
  // its child). A's recorded return target is the chart control, which is BEHIND B's
  // aria-modal — putting the user there would be the original defect by another route.
  const h = makeHarness();
  const chart = h.node('DIV', { tabindex: '0' });
  const aClose = h.node('BUTTON');
  const a = h.dialog('dialog', [aClose], { name: 'A' });
  const bHeading = h.node('H2', { id: 'b-title', tabindex: '-1' });
  const bClose = h.node('BUTTON');
  const b = h.dialog('dialog', [bClose], { name: 'B', labelledBy: 'b-title' });
  h.doc.getElementById = (id) => (id === 'b-title' ? bHeading : null);
  h.setActive(chart);
  h.mountDialogs(a);            // [A], return target: chart
  h.setActive(aClose);
  h.mountDialogs(a, b);         // [A, B], return target: aClose
  h.setActive(null);            // focus dropped (the closing render took it) — not in B

  h.api.setModalStack(['B']);   // A closed underneath B
  assert.equal(h.doc.activeElement, bHeading,
    'the chart is outside B, so B\'s heading is where the user goes — never behind an aria-modal');
});

test('closing a dialog UNDERNEATH the top one leaves focus where it is, in the top one', () => {
  // A parent closing under its child, or a late close event: the user is in B, A goes away
  // beneath them. Yanking them to B's heading would be a focus bug of this file's own making.
  const h = makeHarness();
  const aClose = h.node('BUTTON');
  const a = h.dialog('dialog', [aClose], { name: 'A' });
  const bHeading = h.node('H2', { id: 'b-title', tabindex: '-1' });
  const bInput = h.node('INPUT');
  const b = h.dialog('dialog', [bInput], { name: 'B', labelledBy: 'b-title' });
  h.doc.getElementById = (id) => (id === 'b-title' ? bHeading : null);
  h.mountDialogs(a);
  h.setActive(aClose);
  h.mountDialogs(a, b);
  h.setActive(bInput);

  h.api.setModalStack(['B']);   // A closed underneath B
  assert.equal(h.doc.activeElement, bInput, 'the user was already in the top dialog; nothing should move');
});

test('a disabled opener is not a place to return to — the heading is', () => {
  const h = makeHarness();
  const sHeading = h.node('H2', { id: 's-title', tabindex: '-1' });
  const opener   = h.node('BUTTON', { disabled: '' });   // disabled itself after opening
  const sClose   = h.node('BUTTON');
  const settings = h.dialog('dialog', [opener, sClose], { name: 'Settings', labelledBy: 's-title' });
  const editor   = h.dialog('dialog', [h.node('BUTTON')], { name: 'ThemeEditor' });
  h.doc.getElementById = (id) => (id === 's-title' ? sHeading : null);
  h.mountDialogs(settings);
  h.setActive(opener);
  h.mountDialogs(settings, editor);
  h.setActive(null);

  h.api.setModalStack(['Settings']);
  assert.equal(h.doc.activeElement, sHeading, 'a disabled control cannot take focus; the heading can');
});

test('a re-open that moves an entry to the top keeps the return targets it already knew', () => {
  // F1 with Help already open UNDER Settings: the C# stack moves Help to the top and pushes
  // [Settings, Help]; this side saw [Help, Settings]. That is a reorder — the rebuild path —
  // and Settings' return target must survive it, because Settings will still close later.
  const h = makeHarness();
  const chart    = h.node('DIV', { tabindex: '0' });
  const hSummary = h.node('SUMMARY');
  const help     = h.dialog('dialog', [hSummary], { name: 'Help' });
  const opener   = h.node('BUTTON');
  const sClose   = h.node('BUTTON');
  const settings = h.dialog('dialog', [opener, sClose], { name: 'Settings' });
  h.setActive(chart);
  h.mountDialogs(help);                 // [Help], Help's return target: chart
  h.setActive(hSummary);
  h.mountDialogs(help, settings);       // [Help, Settings], Settings' return target: hSummary
  h.setActive(sClose);

  h.api.setModalStack(['Settings', 'Help']);   // F1 again: Help moved to the top (a reorder)
  assert.deepEqual(h.api._modalStack.map(e => e.name), ['Settings', 'Help']);
  assert.equal(h.api._modalStack[0].returnTo, hSummary, 'Settings still knows where it came from');
  assert.equal(h.api._modalStack[1].returnTo, chart, 'and so does Help');
});

test('a return target that has left the document is not used — the heading is', () => {
  const h = makeHarness();
  const sHeading = h.node('H2', { id: 's-title', tabindex: '-1' });
  const opener   = h.node('BUTTON', {}, { detached: true });   // re-rendered away meanwhile
  const sClose   = h.node('BUTTON');
  const settings = h.dialog('dialog', [opener, sClose], { name: 'Settings', labelledBy: 's-title' });
  const editor   = h.dialog('dialog', [h.node('BUTTON')], { name: 'ThemeEditor' });
  h.doc.getElementById = (id) => (id === 's-title' ? sHeading : null);
  h.mountDialogs(settings);
  h.setActive(opener);
  h.mountDialogs(settings, editor);
  h.setActive(null);

  h.api.setModalStack(['Settings']);
  assert.equal(h.doc.activeElement, sHeading, 'a detached opener cannot take focus; the heading can');
});

test('closing a stacked dialog returns focus to the control that opened it', () => {
  // Settings' "New theme" button opens the theme editor. When the editor closes, Settings is
  // still open and the user should be back on that button — not on <body>, which is where the
  // closing render used to leave them (the dispatcher only sends focus to the chart when the
  // LAST modal closes, and nothing else moved it).
  const h = makeHarness();
  const newTheme = h.node('BUTTON');
  const sClose   = h.node('BUTTON');
  const settings = h.dialog('dialog', [newTheme, sClose], { name: 'Settings' });
  const tInput   = h.node('INPUT');
  const tClose   = h.node('BUTTON');
  const editor   = h.dialog('dialog', [tInput, tClose], { name: 'ThemeEditor' });
  h.mountDialogs(settings);                 // stack: [Settings]
  h.setActive(newTheme);                    // the user is on the button that opens the editor
  h.mountDialogs(settings, editor);         // stack: [Settings, ThemeEditor] — push records newTheme
  h.setActive(tClose);                      // the user worked in the editor

  h.api.setModalStack(['Settings']);        // the editor closed; Settings remains
  assert.equal(h.doc.activeElement, newTheme, 'focus must return to the control that opened the closed dialog');
  assert.equal(h.api._openModalCount, 1);
});

test('when the opener is no longer rendered, focus returns to the top dialog\'s heading', () => {
  const h = makeHarness();
  const heading  = h.node('H2', { id: 's-title', tabindex: '-1' });
  const opener   = h.node('BUTTON', {}, { hidden: true });   // e.g. on a tab panel now hidden
  const sClose   = h.node('BUTTON');
  const settings = h.dialog('dialog', [opener, sClose], { name: 'Settings', labelledBy: 's-title' });
  const editor   = h.dialog('dialog', [h.node('BUTTON')], { name: 'ThemeEditor' });
  h.doc.getElementById = (id) => (id === 's-title' ? heading : null);
  h.mountDialogs(settings);
  h.setActive(opener);
  h.mountDialogs(settings, editor);
  h.setActive(null);

  h.api.setModalStack(['Settings']);
  assert.equal(h.doc.activeElement, heading, 'a hidden opener is not a place to put the user; the heading is');
});

test('the LAST close moves nothing here — the chart focus request is the dispatcher\'s', () => {
  const h = makeHarness();
  const sClose   = h.node('BUTTON');
  const settings = h.dialog('dialog', [sClose], { name: 'Settings' });
  const outside  = h.node('BUTTON');
  h.setActive(outside);
  h.mountDialogs(settings);                 // records outside as the return target
  h.setActive(sClose);

  h.api.setModalStack([]);
  assert.equal(h.doc.activeElement, sClose, 'no dialog remains, so nothing is refocused from here');
  assert.equal(h.api._openModalCount, 0);
  assert.equal(h.api._modalStack.length, 0);
});

test('an out-of-step stack is rebuilt from the names rather than thrown away', () => {
  const h = makeHarness();
  const sSearch  = h.node('INPUT');
  const settings = h.dialog('dialog', [sSearch], { name: 'Settings' });
  const hSummary = h.node('SUMMARY');
  const help     = h.dialog('dialog', [hSummary], { name: 'Help' });
  h.mountDialogs(help, settings);
  h.api.setModalStack(['Wallet']);                       // something this side never saw
  h.api.setModalStack(['Settings', 'Help', 'Wallet']);   // two pushes at once — cannot diff
  assert.deepEqual(h.api._modalStack.map(e => e.name), ['Settings', 'Help', 'Wallet']);
  assert.equal(h.api._openModalCount, 3);

  // Wallet has no element; Help is the nearest entry with one, so the trap still works.
  const outside = h.node('BUTTON');
  h.setActive(outside);
  assert.equal(h.press('Tab', outside), true);
  assert.equal(h.doc.activeElement, hSummary);
});

// ── Scroll keys inside a dialog (2026-09-01 accessibility audit) ────────────

test('scroll keys are released while a modal is open, so a tall dialog can be read', () => {
  // CommandDispatcher's allowedWhileModalOpen list is Escape plus F1-F4 and nothing
  // else, so preventDefault here bought nothing and cost the ability to READ a dialog.
  // HelpModal has two focusable elements with ~400 lines of guide between them: the
  // keyboard reference could not be scrolled by keyboard.
  const h = makeHarness();
  h.api.setModalStack(['Help']);
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
  h.api.setModalStack(['Help']);
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
