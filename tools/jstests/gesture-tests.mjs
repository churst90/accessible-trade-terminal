// Zero-dependency tests for the keyboard.js mouse/touch gesture engine
// (Phase C second pass — closes the "JS test infra" gap without adding npm).
//
// Run:  node tools/jstests/gesture-tests.mjs
//
// Loads wwwroot/js/keyboard.js into a vm sandbox with fake window/document/
// timers, registers the mouse handler on a fake element, replays synthetic
// event sequences, and asserts on the .NET bridge calls the engine emits
// (invokeMethodAsync on a fake dotnetHelper). The .NET side of every gesture
// is covered by the xunit suite; these tests pin the JS half of the contract.

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
  const calls = [];               // every invokeMethodAsync: [method, ...args]
  const dotnet = {
    invokeMethodAsync: (method, ...args) => {
      calls.push([method, ...args]);
      return Promise.resolve();
    },
  };

  const listeners = {};           // element listeners
  const el = {
    id: 'chart-interact-zone',
    addEventListener: (type, fn) => { (listeners[type] ??= []).push(fn); },
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 500 }),
    focus: () => {},
  };
  const windowListeners = {};
  let now = 100_000;              // controllable clock for double-tap timing
  const timers = [];              // [{id, cb, due}]
  let nextTimer = 1;

  const sandbox = {
    window: {
      addEventListener: (type, fn) => { (windowListeners[type] ??= []).push(fn); },
    },
    document: {
      getElementById: (id) => (id === el.id ? el : null),
      addEventListener: () => {},
      activeElement: null,
    },
    navigator: { userAgent: 'test' },
    console,
    Math,
    Date: { now: () => now },
    setTimeout: (cb, ms) => { const id = nextTimer++; timers.push({ id, cb, due: now + ms }); return id; },
    clearTimeout: (id) => { const i = timers.findIndex(t => t.id === id); if (i >= 0) timers.splice(i, 1); },
    requestAnimationFrame: (cb) => { cb(); return 0; },
    cancelAnimationFrame: () => {},
  };
  sandbox.window.document = sandbox.document;
  sandbox.globalThis = sandbox;

  vm.createContext(sandbox);
  vm.runInContext(source, sandbox, { filename: 'keyboard.js' });
  sandbox.window.accessibleTrader.registerMouseHandler(dotnet, el.id);

  const fire = (type, ev) => {
    ev.preventDefault ??= () => {};
    for (const fn of listeners[type] ?? []) fn(ev);
  };
  const advance = (ms) => {
    now += ms;
    for (const t of [...timers].sort((a, b) => a.due - b.due)) {
      if (t.due <= now) {
        const i = timers.indexOf(t);
        if (i >= 0) timers.splice(i, 1);
        t.cb();
      }
    }
  };
  const touch = (x, y) => ({ clientX: x, clientY: y });

  return { calls, fire, advance, touch, tick: (ms) => { now += ms; }, api: sandbox.window.accessibleTrader };
}

const results = [];
function test(name, fn) {
  try { fn(); results.push([name, null]); }
  catch (e) { results.push([name, e]); }
}
const ofMethod = (calls, m) => calls.filter(c => c[0] === m);

// ── Touch gestures ──────────────────────────────────────────────────────────

test('tap emits MouseDown then MouseUp at the rest position (click-select)', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(300, 200)] });
  h.tick(80);
  h.fire('touchend', { touches: [], changedTouches: [h.touch(300, 200)] });

  const mouse = ofMethod(h.calls, 'OnMouseEvent');
  assert.equal(mouse.length, 2);
  assert.deepEqual(mouse[0].slice(1), [300, 200, 'MouseDown', 1000, 500]);
  assert.deepEqual(mouse[1].slice(1), [300, 200, 'MouseUp', 1000, 500]);
});

test('drag emits MouseDown at start, MouseMoves, MouseUp at release', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(300, 200)] });
  h.fire('touchmove', { touches: [h.touch(340, 200)] });   // past 10px slop
  h.fire('touchmove', { touches: [h.touch(420, 200)] });
  h.fire('touchend', { touches: [], changedTouches: [h.touch(420, 200)] });

  const mouse = ofMethod(h.calls, 'OnMouseEvent');
  assert.equal(mouse[0][3], 'MouseDown');
  assert.deepEqual(mouse[0].slice(1, 3), [300, 200]);       // down at the START point
  assert.ok(mouse.some(c => c[3] === 'MouseMove'));
  assert.equal(mouse.at(-1)[3], 'MouseUp');
  assert.deepEqual(mouse.at(-1).slice(1, 3), [420, 200]);
});

test('micro-jitter below the slop stays a tap, not a drag', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(300, 200)] });
  h.fire('touchmove', { touches: [h.touch(304, 203)] });    // < 10px
  h.fire('touchend', { touches: [], changedTouches: [h.touch(304, 203)] });

  const types = ofMethod(h.calls, 'OnMouseEvent').map(c => c[3]);
  assert.deepEqual(types, ['MouseDown', 'MouseUp']);        // no MouseMove pan
});

test('long-press opens the context menu and eats the touchend', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(300, 200)] });
  h.advance(600);                                           // past 550ms hold
  h.fire('touchend', { touches: [], changedTouches: [h.touch(300, 200)] });

  const ctx = ofMethod(h.calls, 'OnContextMenu');
  assert.equal(ctx.length, 1);
  assert.deepEqual(ctx[0].slice(1), [300, 200, 1000, 500]);
  assert.equal(ofMethod(h.calls, 'OnMouseEvent').length, 0); // no stray click
});

test('double-tap fires OnDoubleClick on the second tap', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(300, 200)] });
  h.fire('touchend', { touches: [], changedTouches: [h.touch(300, 200)] });
  h.tick(150);                                              // within 300ms window
  h.fire('touchstart', { touches: [h.touch(305, 204)] });
  h.fire('touchend', { touches: [], changedTouches: [h.touch(305, 204)] });

  assert.equal(ofMethod(h.calls, 'OnDoubleClick').length, 1);
});

test('two slow taps are two clicks, not a double-tap', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(300, 200)] });
  h.fire('touchend', { touches: [], changedTouches: [h.touch(300, 200)] });
  h.tick(800);                                              // past the window
  h.fire('touchstart', { touches: [h.touch(300, 200)] });
  h.fire('touchend', { touches: [], changedTouches: [h.touch(300, 200)] });

  assert.equal(ofMethod(h.calls, 'OnDoubleClick').length, 0);
  assert.equal(ofMethod(h.calls, 'OnMouseEvent').length, 4); // two down+up pairs
});

test('pinch apart zooms in via OnWheel with a centroid fraction', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(450, 250)] });
  h.fire('touchstart', { touches: [h.touch(450, 250), h.touch(550, 250)] }); // 100px spread
  h.fire('touchmove', { touches: [h.touch(400, 250), h.touch(600, 250)] }); // 200px spread

  const wheel = ofMethod(h.calls, 'OnWheel');
  assert.ok(wheel.length >= 1, 'at least one zoom notch');
  assert.equal(wheel[0][1], 1);                              // spread → zoom in
  assert.ok(Math.abs(wheel[0][2] - 0.5) < 0.05, 'centroid ~mid-chart');
});

test('pinch together zooms out', () => {
  const h = makeHarness();
  h.fire('touchstart', { touches: [h.touch(400, 250)] });
  h.fire('touchstart', { touches: [h.touch(400, 250), h.touch(600, 250)] });
  h.fire('touchmove', { touches: [h.touch(480, 250), h.touch(520, 250)] });

  const wheel = ofMethod(h.calls, 'OnWheel');
  assert.ok(wheel.length >= 1);
  assert.equal(wheel[0][1], -1);
});

// ── Wheel + shift-click (mouse) ─────────────────────────────────────────────

test('plain wheel zooms; shift+wheel pans', () => {
  const h = makeHarness();
  h.fire('wheel', { deltaX: 0, deltaY: -100, shiftKey: false, clientX: 500, clientY: 200 });
  h.fire('wheel', { deltaX: 0, deltaY: 100, shiftKey: true, clientX: 500, clientY: 200 });

  const zoom = ofMethod(h.calls, 'OnWheel');
  const pan = ofMethod(h.calls, 'OnWheelPan');
  assert.equal(zoom.length, 1);
  assert.equal(zoom[0][1], 1);                               // wheel-up → zoom in
  assert.equal(pan.length, 1);
  assert.equal(pan[0][1], 1);                                // scroll down+shift → newer bars
});

test('horizontal trackpad swipe pans without shift', () => {
  const h = makeHarness();
  h.fire('wheel', { deltaX: -50, deltaY: 3, shiftKey: false, clientX: 500, clientY: 200 });
  const pan = ofMethod(h.calls, 'OnWheelPan');
  assert.equal(pan.length, 1);
  assert.equal(pan[0][1], -1);                               // swipe left → older bars
});

test('shift+mouseup is reported as ShiftMouseUp (range measure)', () => {
  const h = makeHarness();
  h.fire('mousedown', { clientX: 300, clientY: 200, shiftKey: true });
  h.fire('mouseup', { clientX: 300, clientY: 200, shiftKey: true });

  const types = ofMethod(h.calls, 'OnMouseEvent').map(c => c[3]);
  assert.ok(types.includes('ShiftMouseUp'));
  assert.ok(!types.includes('MouseUp'));
});

test('explore mode: finger slide emits TouchExplore per move, never pans', () => {
  const h = makeHarness();
  h.api.setTouchExploreMode(true);
  h.fire('touchstart', { touches: [h.touch(100, 100)] });
  h.fire('touchmove', { touches: [h.touch(160, 100)] });
  h.fire('touchmove', { touches: [h.touch(220, 100)] });
  h.fire('touchend', { touches: [], changedTouches: [h.touch(220, 100)] });

  const types = h.calls.filter(c => c[0] === 'OnMouseEvent').map(c => c[3]);
  assert.equal(types.filter(t => t === 'TouchExplore').length >= 2, true,
    'expected TouchExplore reports from start + moves');
  assert.equal(types.includes('MouseDown'), false, 'explore must not start a pan');
  assert.equal(types.at(-1), 'MouseLeave', 'lifting the finger clears the crosshair');
});

test('explore mode off: the same slide is a pan (regression guard)', () => {
  const h = makeHarness();
  h.api.setTouchExploreMode(false);
  h.fire('touchstart', { touches: [h.touch(100, 100)] });
  h.fire('touchmove', { touches: [h.touch(160, 100)] });
  h.fire('touchend', { touches: [], changedTouches: [h.touch(160, 100)] });

  const types = h.calls.filter(c => c[0] === 'OnMouseEvent').map(c => c[3]);
  assert.equal(types.includes('MouseDown'), true);
  assert.equal(types.includes('TouchExplore'), false);
});

test('explore mode: second finger still pinch-zooms', () => {
  const h = makeHarness();
  h.api.setTouchExploreMode(true);
  h.fire('touchstart', { touches: [h.touch(100, 100)] });
  h.fire('touchstart', { touches: [h.touch(100, 100), h.touch(200, 100)] });
  h.fire('touchmove', { touches: [h.touch(60, 100), h.touch(260, 100)] });

  assert.equal(h.calls.some(c => c[0] === 'OnWheel'), true, 'pinch should zoom in explore mode');
});

test('double-click emits OnDoubleClick (jump to live)', () => {
  const h = makeHarness();
  h.fire('dblclick', {});
  assert.equal(ofMethod(h.calls, 'OnDoubleClick').length, 1);
});

// ── Report ──────────────────────────────────────────────────────────────────

let failed = 0;
for (const [name, err] of results) {
  if (err) { failed++; console.error(`FAIL  ${name}\n      ${err.message}`); }
  else console.log(`ok    ${name}`);
}
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
