// Zero-dependency tests for canvasRegion.js: the chart rect the desktop head uses to place the
// native Skia canvas over the page.
//
// Run:  node tools/jstests/canvas-region-tests.mjs
//
// Written because the A2k campaign (2026-09-24) found this file had no test anywhere: its three
// mutants (the bottom inset computed from the wrong edge, scroll no longer re-reporting, a
// negative top reported as-is) survived the node suites, the C# scans and the full browser
// suite. The browser suite runs the WebHost, where nothing listens to this report; only the
// Windows/macOS head does, and that head is never run by CI.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const source = readFileSync(
  join(root, 'AccessibleTrader.BlazorClient.Components', 'wwwroot', 'js', 'canvasRegion.js'), 'utf8');

function makeHarness({ top = 120, bottom = 700, innerHeight = 800 } = {}) {
  const reports = [];
  const listeners = {};
  let frame = null;
  let rect = { top, bottom };
  const el = { getBoundingClientRect: () => rect };
  const window = {
    innerHeight,
    addEventListener: (t, fn, opts) => { (listeners[t] ??= []).push({ fn, opts }); },
    removeEventListener: (t, fn) => { listeners[t] = (listeners[t] ?? []).filter(l => l.fn !== fn); },
  };
  const sandbox = {
    window,
    document: { getElementById: (id) => (id === 'chart-interact-zone' ? el : null) },
    requestAnimationFrame: (cb) => { frame = cb; return 1; },
  };
  vm.createContext(sandbox);
  vm.runInContext(source, sandbox);
  const ref = { invokeMethodAsync: (m, ...a) => { reports.push([m, ...a]); return Promise.resolve(); } };
  const flush = () => { const f = frame; frame = null; if (f) f(); };
  const fire = (t) => { for (const l of listeners[t] ?? []) l.fn(); };
  return { api: window.canvasRegion, ref, reports, flush, fire, listeners, window,
           setRect: (r) => { rect = r; } };
}

let passed = 0, failed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log('  ok   ' + name); }
  catch (e) { failed++; console.log('  FAIL ' + name + '\n       ' + e.message); }
}

console.log('canvasRegion.js');

test('reports the top inset and the gap BELOW the chart, once the layout settles', () => {
  const h = makeHarness({ top: 120, bottom: 700, innerHeight: 800 });
  h.api.start(h.ref);
  h.flush();
  assert.deepEqual(h.reports, [['OnCanvasRegionChanged', 120, 100]]);
});

test('a chart scrolled above the viewport reports a top of 0, not a negative inset', () => {
  const h = makeHarness({ top: -40, bottom: 500, innerHeight: 800 });
  h.api.start(h.ref);
  h.flush();
  assert.deepEqual(h.reports[0], ['OnCanvasRegionChanged', 0, 300]);
});

test('scrolling re-reports, in the capture phase, passively', () => {
  // The rect is a VIEWPORT rect: it moves when an ancestor scrolls, and scroll does not bubble.
  const h = makeHarness();
  h.api.start(h.ref);
  h.flush();
  const scroll = h.listeners.scroll ?? [];
  assert.equal(scroll.length, 1, 'no scroll listener: the native canvas stays where it was painted');
  assert.equal(scroll[0].opts.capture, true);
  assert.equal(scroll[0].opts.passive, true);
  h.setRect({ top: 60, bottom: 640 });
  h.fire('scroll');
  h.flush();
  assert.deepEqual(h.reports.at(-1), ['OnCanvasRegionChanged', 60, 160]);
});

test('a burst of resizes in one frame is one report', () => {
  const h = makeHarness();
  h.api.start(h.ref);
  h.flush();
  h.fire('resize'); h.fire('resize'); h.fire('resize');
  h.flush();
  assert.equal(h.reports.length, 2);
});

test('start is idempotent and stop removes the listeners', () => {
  const h = makeHarness();
  h.api.start(h.ref);
  h.api.start(h.ref);
  assert.equal(h.listeners.resize.length, 1);
  h.api.stop();
  assert.equal(h.listeners.resize.length, 0);
  assert.equal(h.listeners.scroll.length, 0);
});

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
