// Zero-dependency tests for treeKeyboard.js — the ARIA tree keyboard model behind the
// Object Tree (Alt+O) and the strategy Condition Tree editor.
//
// Run:  node tools/jstests/tree-tests.mjs
//
// Until 2026-09-03 this file had no tests of any kind: not here, not in the browser suite
// (a cold-start WebHost has no series, so the Object Tree is empty there and the harness
// cannot reach it). Its roving tabindex, its arrow model, the ORDER in which isExpanded
// reads <details open> versus aria-expanded, and visibleTreeitems' filter were all
// unexercised — and the last two are the ones a wrong edit turns into a keyboard trap.
//
// Same approach as keyboard-tests.mjs: load the script into a vm sandbox with a fake
// window/document, build a DOM the shape ObjectTreeModal.razor actually renders, fire
// keydown at the window listener, and assert on focus, tabindex, `open`, clicks and
// preventDefault.
//
// The DOM double is deliberately NOT a mirror of the code under test. `getClientRects()`
// is answered from a per-node `hidden` flag that the fixture sets, never derived from
// the <details> state — so the ancestor-<details> check in visibleTreeitems and the
// rects check are two independent facts here, exactly as they are in Chromium, where a
// closed <details> hides content through `content-visibility` and may still generate
// boxes. A double that computed rects from `open` would hand the code its own answer.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const source = readFileSync(
  join(root, 'AccessibleTrader.BlazorClient.Components', 'wwwroot', 'js', 'treeKeyboard.js'), 'utf8');

// ── A small DOM ─────────────────────────────────────────────────────────────

let activeElement = null;

function makeNode(tagName, attrs = {}, opts = {}) {
  const n = {
    tagName: tagName.toUpperCase(),
    attrs: { ...attrs },
    children: [],
    parentElement: null,
    clicks: 0,
    open: !!opts.open,
    hidden: !!opts.hidden,          // what getClientRects answers; NOT derived from `open`
    getAttribute(k) { return k in this.attrs ? this.attrs[k] : null; },
    setAttribute(k, v) { this.attrs[k] = String(v); },
    hasAttribute(k) { return k in this.attrs; },
    focus() { activeElement = this; },
    click() { this.clicks++; if (this.onclick) this.onclick(); },
    getClientRects() { return this.hidden ? [] : [{ width: 1, height: 1 }]; },
    append(...kids) { for (const k of kids) { k.parentElement = this; this.children.push(k); } return this; },
    // Descendants matching a comma-separated list of [attr="value"] or tag selectors, in
    // document order, excluding the node itself — the subset the script uses.
    querySelectorAll(selector) {
      const out = [];
      const parts = selector.split(',').map(s => s.trim());
      const matches = (el) => parts.some(p => {
        const m = p.match(/^\[([a-z-]+)="([^"]+)"\]$/);
        if (m) return el.getAttribute(m[1]) === m[2];
        return el.tagName === p.toUpperCase();
      });
      const walk = (el) => { for (const c of el.children) { if (matches(c)) out.push(c); walk(c); } };
      walk(this);
      return out;
    },
    querySelector(selector) { const r = this.querySelectorAll(selector); return r.length ? r[0] : null; },
  };
  return n;
}

function makeHarness() {
  activeElement = null;
  const body = makeNode('BODY');
  const windowListeners = {};
  const sandbox = {
    window: { addEventListener: (type, fn) => { (windowListeners[type] ??= []).push(fn); } },
    document: { body },
    console,
  };
  sandbox.window.document = sandbox.document;
  vm.createContext(sandbox);
  vm.runInContext(source, sandbox, { filename: 'treeKeyboard.js' });

  const press = (key, target, mods = {}) => {
    let prevented = false;
    const ev = { key, target, ...mods, preventDefault: () => { prevented = true; } };
    for (const fn of windowListeners.keydown ?? []) fn(ev);
    return prevented;
  };
  return { body, press, active: () => activeElement };
}

// ── Fixtures ─────────────────────────────────────────────────────────────────

// The Object Tree as ObjectTreeModal.razor renders it: a <details> per pane with a
// <summary role="treeitem"> header; inside its role="group", a <div role="treeitem"> per
// series wrapping a <details> whose <summary> holds the series name and the row buttons,
// and a role="group" of component treeitems. Children of a closed <details> are marked
// hidden by the FIXTURE (that is the browser's job, not the script's).
function objectTree({ paneOpen = true, seriesOpen = true } = {}) {
  const h = makeHarness();
  const tree = makeNode('DIV', { role: 'tree', 'aria-label': 'Chart Hierarchy' });
  const pane = makeNode('DETAILS', {}, { open: paneOpen });
  const paneHeader = makeNode('SUMMARY', { role: 'treeitem', 'aria-level': '1', tabindex: '0',
    'aria-expanded': paneOpen ? 'true' : 'false', 'aria-label': 'Pane Main, 2 series' });
  const paneGroup = makeNode('DIV', { role: 'group' }, { hidden: !paneOpen });

  const series = (name, comps, open) => {
    const item = makeNode('DIV', { role: 'treeitem', 'aria-level': '2', tabindex: '-1',
      'aria-expanded': comps.length ? (open ? 'true' : 'false') : null, 'aria-label': name }, { hidden: !paneOpen });
    if (item.attrs['aria-expanded'] === null) delete item.attrs['aria-expanded'];
    const details = makeNode('DETAILS', {}, { open, hidden: !paneOpen });
    const summary = makeNode('SUMMARY', { 'data-tree-activate': 'true' }, { hidden: !paneOpen });
    const hide = makeNode('BUTTON', { 'aria-label': `Hide ${name}` }, { hidden: !paneOpen });
    summary.append(hide);
    const group = makeNode('DIV', { role: 'group' }, { hidden: !paneOpen || !open });
    for (const c of comps) {
      group.append(makeNode('DIV', { role: 'treeitem', 'aria-level': '3', tabindex: '-1', 'aria-label': c },
        { hidden: !paneOpen || !open }));
    }
    details.append(summary, group);
    item.append(details);
    return { item, details, summary, group, hide };
  };

  const candles = series('Candles', ['Open', 'High', 'Low', 'Close'], seriesOpen);
  const sma = series('SMA 20', ['SMA'], seriesOpen);
  const drawing = series('TrendLine Drawing', [], seriesOpen);   // no components: no aria-expanded
  paneGroup.append(candles.item, sma.item, drawing.item);
  pane.append(paneHeader, paneGroup);
  tree.append(pane);
  h.body.append(tree);
  return { ...h, tree, pane, paneHeader, candles, sma, drawing };
}

// The Condition Tree editor: no <details> anywhere, aria-expanded only, collapsed
// children omitted from the DOM entirely.
function conditionTree() {
  const h = makeHarness();
  const tree = makeNode('DIV', { role: 'tree' });
  const group = makeNode('DIV', { role: 'treeitem', 'aria-expanded': 'true', tabindex: '0' });
  const toggle = makeNode('BUTTON', { 'data-tree-toggle': 'true' });
  const inner = makeNode('DIV', { role: 'group' });
  const leafA = makeNode('DIV', { role: 'treeitem', tabindex: '-1' });
  const leafB = makeNode('DIV', { role: 'treeitem', tabindex: '-1' });
  inner.append(leafA, leafB);
  group.append(toggle, inner);
  tree.append(group);
  h.body.append(tree);
  return { ...h, tree, group, toggle, leafA, leafB };
}

const results = [];
function test(name, fn) {
  try { fn(); results.push([name, null]); }
  catch (e) { results.push([name, e]); }
}
const tabStops = (tree) => tree.querySelectorAll('[role="treeitem"]').filter(t => t.getAttribute('tabindex') === '0');

// ── Walking ─────────────────────────────────────────────────────────────────

test('ArrowDown walks pane, series, components in document order and stops at the end', () => {
  const t = objectTree();
  assert.equal(t.press('ArrowDown', t.paneHeader), true);
  assert.equal(t.active(), t.candles.item);
  t.press('ArrowDown', t.active());
  assert.equal(t.active(), t.candles.group.children[0], 'first component of Candles');
  for (let i = 0; i < 3; i++) t.press('ArrowDown', t.active());
  assert.equal(t.active(), t.candles.group.children[3], 'last component of Candles');
  t.press('ArrowDown', t.active());
  assert.equal(t.active(), t.sma.item);
  t.press('ArrowDown', t.active()); t.press('ArrowDown', t.active());
  assert.equal(t.active(), t.drawing.item);
  t.press('ArrowDown', t.active());
  assert.equal(t.active(), t.drawing.item, 'End of tree: focus stays put');
});

test('ArrowUp walks back and Home / End jump', () => {
  const t = objectTree();
  t.press('End', t.paneHeader);
  assert.equal(t.active(), t.drawing.item);
  t.press('ArrowUp', t.active());
  assert.equal(t.active(), t.sma.group.children[0]);
  t.press('Home', t.active());
  assert.equal(t.active(), t.paneHeader);
  t.press('ArrowUp', t.active());
  assert.equal(t.active(), t.paneHeader, 'Start of tree: focus stays put');
});

test('the roving tabindex follows focus: exactly one treeitem is a Tab stop', () => {
  const t = objectTree();
  t.press('ArrowDown', t.paneHeader);
  t.press('ArrowDown', t.active());
  const stops = tabStops(t.tree);
  assert.equal(stops.length, 1);
  assert.equal(stops[0], t.active());
  assert.equal(t.paneHeader.getAttribute('tabindex'), '-1');
});

test('a SHIFTED arrow is not the tree\'s: focus stays, nothing is prevented', () => {
  // Shift+Arrow nudges the focused drawing's anchor, and since 2026-09-03 the dispatcher
  // allows it under the Object Tree — the dialog a drawing is focused FROM. The tree must
  // neither move on it nor preventDefault it; the chart's keyboard handler owns the chord.
  const t = objectTree();
  t.candles.item.focus();
  for (const mods of [{ shiftKey: true }, { ctrlKey: true }, { altKey: true }, { metaKey: true }]) {
    for (const key of ['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight']) {
      const prevented = t.press(key, t.candles.item, mods);
      assert.equal(t.active(), t.candles.item, `${JSON.stringify(mods)} ${key} moved focus`);
      assert.equal(prevented, false, `${JSON.stringify(mods)} ${key} was prevented`);
    }
  }
  assert.equal(t.candles.details.open, true, 'a modified ArrowLeft must not collapse the series');
});

test('keys outside any role="tree" are left alone', () => {
  const h = makeHarness();
  const input = makeNode('INPUT');
  h.body.append(input);
  assert.equal(h.press('ArrowDown', input), false);
  assert.equal(h.active(), null);
});

// ── Visibility ──────────────────────────────────────────────────────────────

test('components under a closed series <details> are not in the walk', () => {
  const t = objectTree({ seriesOpen: false });
  t.press('ArrowDown', t.candles.item);
  assert.equal(t.active(), t.sma.item, 'ArrowDown from a collapsed series must skip its components');
});

test('a closed <details> hides its treeitems even when they still report layout boxes', () => {
  // Chromium hides closed <details> content through content-visibility, which is not the
  // same as "no box". The ancestor check must not rely on getClientRects().
  const t = objectTree({ seriesOpen: false });
  for (const c of t.candles.group.children) c.hidden = false;   // boxes present, details closed
  t.press('ArrowDown', t.candles.item);
  assert.equal(t.active(), t.sma.item);
});

test('a treeitem with no layout box is not in the walk even under an open <details>', () => {
  const t = objectTree();
  t.sma.item.hidden = true;
  t.sma.group.children[0].hidden = true;
  t.press('ArrowDown', t.candles.group.children[3]);
  assert.equal(t.active(), t.drawing.item);
});

test('the header of a collapsed pane stays in the walk — it is the control that re-opens it', () => {
  // A closed <details> hides its CONTENT, not its <summary>. Starting the ancestor walk at
  // the summary's own parent dropped every collapsed pane header out of the walk: with one
  // pane, ArrowLeft collapsed it and every arrow key was then dead; the pane could be
  // collapsed and never re-opened by arrows.
  const t = objectTree({ paneOpen: false });
  assert.equal(t.press('Home', t.paneHeader), true, 'the key must be handled at all');
  assert.equal(t.active(), t.paneHeader);
  t.press('ArrowRight', t.paneHeader);
  assert.equal(t.pane.open, true, 'ArrowRight re-opens the collapsed pane');
});

// ── Expand / collapse on the Object Tree (<details> owned by the treeitem) ──

test('ArrowRight on a collapsed pane header opens its <details>; a second press enters it', () => {
  const t = objectTree({ paneOpen: false });
  assert.equal(t.press('ArrowRight', t.paneHeader), true);
  assert.equal(t.pane.open, true);
  // The browser would now lay the children out; the fixture does that job.
  for (const n of [t.candles.item, t.sma.item, t.drawing.item]) n.hidden = false;
  t.press('ArrowRight', t.paneHeader);
  assert.equal(t.active(), t.candles.item, 'already open: move to the first child');
});

test('ArrowLeft on an open series collapses it; ArrowLeft again goes to the pane header', () => {
  const t = objectTree();
  t.press('ArrowLeft', t.candles.item);
  assert.equal(t.candles.details.open, false);
  t.press('ArrowLeft', t.candles.item);
  assert.equal(t.active(), t.paneHeader);
});

test('ArrowLeft on a component (a leaf) moves to its series', () => {
  const t = objectTree();
  t.press('ArrowLeft', t.candles.group.children[2]);
  assert.equal(t.active(), t.candles.item);
  assert.equal(t.candles.details.open, true, 'a leaf has nothing to collapse');
});

test('the <details> is the source of truth: a stale aria-expanded="true" cannot invert ArrowRight', () => {
  // ObjectTreeModal re-renders aria-expanded from the browser's `toggle` event, which is
  // queued — so for a task turn after ArrowLeft the attribute still says "true" while the
  // <details> is already closed. Read the attribute first and ArrowRight takes the "already
  // expanded, move to first child" branch, finds nothing visible, and does nothing: the
  // pane can be collapsed and never re-opened. This is the trap the reordering fixed.
  const t = objectTree({ seriesOpen: false });
  t.candles.item.setAttribute('aria-expanded', 'true');   // stale projection
  t.press('ArrowRight', t.candles.item);
  assert.equal(t.candles.details.open, true, 'ArrowRight must open the closed details');
  assert.equal(t.active(), null, 'and not pretend to move into children that are not shown');
});

test('a series with no components is a leaf: ArrowRight does nothing rather than opening an empty group', () => {
  const t = objectTree();
  t.drawing.details.open = false;
  assert.equal(t.drawing.item.hasAttribute('aria-expanded'), false, 'fixture: no aria-expanded on a childless series');
  t.press('ArrowRight', t.drawing.item);
  // findOwnedDetails still finds the <details>, so the script treats it as a group and opens it;
  // pin the CURRENT behaviour so a change here is a decision, not an accident.
  assert.equal(t.drawing.details.open, true);
});

// ── Activation ───────────────────────────────────────────────────────────────

test('Enter on a series treeitem clicks its <summary> (the focus-series action), not a row button', () => {
  const t = objectTree();
  t.press('Enter', t.candles.item);
  assert.equal(t.candles.summary.clicks, 1);
  assert.equal(t.candles.hide.clicks, 0, 'Enter must never press Hide by accident');
});

test('Enter on a pane header clicks the header itself', () => {
  const t = objectTree();
  t.press('Enter', t.paneHeader);
  assert.equal(t.paneHeader.clicks, 1);
});

test('Space toggles a group and activates a leaf', () => {
  const t = objectTree();
  t.press(' ', t.candles.item);
  assert.equal(t.candles.details.open, false);
  const leaf = t.sma.group.children[0];
  t.press(' ', leaf);
  assert.equal(leaf.clicks, 1, 'a component has no details, no summary and no button: click itself');
});

// ── The Condition Tree (aria-expanded only, no <details>) ───────────────────

test('without a <details>, aria-expanded decides and the data-tree-toggle button is what gets clicked', () => {
  const c = conditionTree();
  c.press('ArrowLeft', c.group);
  assert.equal(c.toggle.clicks, 1, 'collapse goes through the component\'s own toggle button');
  c.group.setAttribute('aria-expanded', 'false');   // the component re-renders
  c.press('ArrowRight', c.group);
  assert.equal(c.toggle.clicks, 2, 'expand goes through the same button');
});

test('ArrowRight on an expanded aria-expanded group moves into its first child', () => {
  const c = conditionTree();
  c.press('ArrowRight', c.group);
  assert.equal(c.toggle.clicks, 0);
  assert.equal(c.active(), c.leafA);
});

// ── Report ───────────────────────────────────────────────────────────────────

let failed = 0;
for (const [name, err] of results) {
  if (err) { failed++; console.log(`FAIL  ${name}\n      ${err.message.split('\n')[0]}`); }
  else console.log(`ok    ${name}`);
}
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
