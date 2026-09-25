// Zero-dependency tests for the WebHost's boot.js: the circuit start and its reconnection
// back-off.
//
// Run:  node tools/jstests/boot-tests.mjs
//
// The browser suite measures the behaviour end to end (ReconnectAndHealthBrowserTests drops a
// real circuit and counts the retries). This file pins the POLICY across all thirty attempts,
// which a 2.5-second browser window cannot reach: the cap, the jitter bounds, the end of the
// schedule, and that no attempt after the first is ever immediate.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const source = readFileSync(
  join(root, 'AccessibleTrader.WebHost', 'wwwroot', 'js', 'boot.js'), 'utf8');

function load(withBlazor = true) {
  const starts = [];
  const window = {};
  const sandbox = { window, Math };
  if (withBlazor) sandbox.Blazor = { start: (opts) => { starts.push(opts); } };
  vm.createContext(sandbox);
  vm.runInContext(source, sandbox);
  return { boot: window.terminalBoot, starts };
}

let passed = 0, failed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log('  ok   ' + name); }
  catch (e) { failed++; console.log('  FAIL ' + name + '\n       ' + e.message); }
}

console.log('boot.js');

test('the first retry is immediate: a network blip is not made to wait', () => {
  const { boot } = load();
  assert.equal(boot.retryDelay(0, 0.5), 0);
});

test('no retry after the first is immediate (the framework default makes ten)', () => {
  const { boot } = load();
  for (let n = 1; n < boot.MAX_RETRIES; n++)
    for (const r of [0, 0.5, 0.999999])
      assert.ok(boot.retryDelay(n, r) >= 800, `attempt ${n} at random ${r}: ${boot.retryDelay(n, r)} ms`);
});

test('the un-jittered delays double from one second to the 15 s cap', () => {
  const { boot } = load();
  const mid = [1, 2, 3, 4, 5, 6, 12].map(n => boot.retryDelay(n, 0.5));
  assert.deepEqual(mid, [1000, 2000, 4000, 8000, 15000, 15000, 15000]);
});

test('jitter stays within ±20% and actually varies', () => {
  const { boot } = load();
  for (let n = 1; n < boot.MAX_RETRIES; n++) {
    const base = boot.retryDelay(n, 0.5);
    const lo = boot.retryDelay(n, 0), hi = boot.retryDelay(n, 0.999999);
    assert.ok(lo >= Math.floor(base * 0.8) && hi <= Math.ceil(base * 1.2), `attempt ${n}: ${lo}..${hi} around ${base}`);
    assert.ok(hi > lo, `attempt ${n}: no spread (${lo}..${hi}), so every tab retries on the same beat`);
  }
});

test('the schedule ends after MAX_RETRIES, so the overlay can offer Retry now', () => {
  const { boot } = load();
  assert.equal(boot.MAX_RETRIES, 30);
  assert.equal(boot.retryDelay(boot.MAX_RETRIES, 0.5), null);
  assert.notEqual(boot.retryDelay(boot.MAX_RETRIES - 1, 0.5), null);
});

test('thirty attempts span several minutes, not seconds', () => {
  const { boot } = load();
  let total = 0;
  for (let n = 0; n < boot.MAX_RETRIES; n++) total += boot.retryDelay(n, 0.5);
  assert.ok(total >= 5 * 60_000 && total <= 10 * 60_000, `${total} ms`);
});

test('Blazor.start is called once, with the policy under circuit.reconnectionOptions', () => {
  const { boot, starts } = load();
  assert.equal(starts.length, 1);
  const opts = starts[0].circuit && starts[0].circuit.reconnectionOptions;
  assert.ok(opts, 'blazor.web.js reads reconnection options from circuit.reconnectionOptions only');
  assert.equal(opts.maxRetries, boot.MAX_RETRIES);
  assert.equal(opts.retryIntervalMilliseconds(0), 0);
  assert.ok(opts.retryIntervalMilliseconds(3) >= 3200);
  boot.start();
  assert.equal(starts.length, 1, 'a second start() would make blazor.web.js throw "already started"');
  assert.equal(boot.started, true);
});

test('a missing framework script does not throw', () => {
  const { boot } = load(false);
  assert.equal(boot.started, false);
});

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
