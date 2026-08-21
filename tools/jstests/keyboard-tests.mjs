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
  const node = (tagName, attrs = {}) => ({
    tagName,
    isContentEditable: false,
    getAttribute: (n) => (n in attrs ? attrs[n] : null),
    hasAttribute: (n) => n in attrs,
  });

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

  return { calls, press, node, api: sandbox.window.accessibleTrader };
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

// ── Report ──────────────────────────────────────────────────────────────────

let failed = 0;
for (const [name, err] of results) {
  if (err) { failed++; console.error(`FAIL  ${name}\n      ${err.message}`); }
  else console.log(`ok    ${name}`);
}
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
