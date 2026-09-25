// Zero-dependency tests for the WebHost's reconnect.js: what a screen reader user hears while
// the circuit is down.
//
// Run:  node tools/jstests/reconnect-tests.mjs
//
// The browser suite drops a real circuit and counts what the assertive node is given over
// 2.5 s. This file drives the minutes a browser test cannot wait out: the framework rewrites
// the attempt counter once a second for as long as it retries (about seven minutes with the
// back-off), and until 2026-09-24 every rewrite re-announced the whole sentence.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const source = readFileSync(join(root, 'AccessibleTrader.WebHost', 'wwwroot', 'js', 'reconnect.js'), 'utf8');

function makeHarness() {
  let now = 1_000_000;
  const els = {};
  const el = (id) => (els[id] = { id, textContent: '', classes: new Set(),
    classList: { contains: (c) => els[id].classes.has(c) } });
  el('components-reconnect-modal'); el('components-reconnect-current-attempt');
  el('components-reconnect-max-retries'); el('reconnect-status'); el('reconnect-progress');
  // Every write to a speaking node, in order, including the empty "clear" of a re-announce.
  const log = [];
  for (const id of ['reconnect-status', 'reconnect-progress']) {
    let v = '';
    Object.defineProperty(els[id], 'textContent', { get: () => v, set: (x) => { v = x; log.push([id, x]); } });
  }
  let observerFn = null;
  const sandbox = {
    window: { requestAnimationFrame: (cb) => cb() },
    document: { readyState: 'complete', getElementById: (id) => els[id] || null, addEventListener() {} },
    MutationObserver: class { constructor(fn) { observerFn = fn; } observe() {} },
    Date: { now: () => now },
    setTimeout: (cb) => cb(),
  };
  vm.createContext(sandbox);
  vm.runInContext(source, sandbox);

  const modal = els['components-reconnect-modal'];
  const setState = (s) => { modal.classes = new Set(['components-reconnect-' + s]); observerFn(); };
  // One framework tick: the counter's text is rewritten (same or new number) and it may re-add
  // the -retrying class; the observer fires either way.
  const tick = (attempt, max = '30') => {
    els['components-reconnect-current-attempt'].textContent = String(attempt);
    els['components-reconnect-max-retries'].textContent = max;
    modal.classes.add('components-reconnect-retrying');
    observerFn();
  };
  const advance = (ms) => { now += ms; };
  const spoken = (id) => log.filter(([n, x]) => n === id && x).map(([, x]) => x);
  return { setState, tick, advance, spoken, log };
}

let passed = 0, failed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log('  ok   ' + name); }
  catch (e) { failed++; console.log('  FAIL ' + name + '\n       ' + e.message); }
}

console.log('reconnect.js');

test('a drop is announced once, however many times the countdown ticks', () => {
  const h = makeHarness();
  h.setState('show');
  for (let s = 0; s < 30; s++) { h.tick(1 + Math.floor(s / 5)); h.advance(1000); }
  assert.equal(h.spoken('reconnect-status').length, 1, h.spoken('reconnect-status').join(' | '));
  assert.match(h.spoken('reconnect-status')[0], /Reconnecting/);
});

test('progress is polite, names the attempt, and comes at most once a minute', () => {
  const h = makeHarness();
  h.setState('show');
  // Seven minutes of retrying, a new attempt every 15 s, the counter rewritten every second.
  let attempt = 1;
  for (let s = 0; s < 420; s++) {
    if (s % 15 === 0) attempt++;
    h.tick(attempt);
    h.advance(1000);
  }
  const progress = h.spoken('reconnect-progress');
  assert.ok(progress.length >= 5 && progress.length <= 7, `${progress.length} progress announcements in 7 minutes`);
  assert.match(progress[0], /Still reconnecting\. Attempt \d+ of 30\./);
  assert.equal(h.spoken('reconnect-status').length, 1, 'progress must not go to the assertive node');
});

test('no progress without a new attempt number, even after a minute', () => {
  const h = makeHarness();
  h.setState('show');
  for (let s = 0; s < 120; s++) { h.tick(3); h.advance(1000); }
  assert.deepEqual(h.spoken('reconnect-progress'), []);
});

test('every state change is announced, each with its own sentence', () => {
  const h = makeHarness();
  for (const [state, re] of [['show', /Reconnecting/], ['failed', /Retry now/], ['show', /Reconnecting/],
                             ['hide', /Reconnected/], ['rejected', /session ended/],
                             ['resume-failed', /session ended/]]) {
    h.setState(state);
    const said = h.spoken('reconnect-status');
    assert.match(said[said.length - 1], re, state);
  }
  assert.equal(h.spoken('reconnect-status').length, 6);
});

test('the same sentence twice in a row is still a change the screen reader hears', () => {
  const h = makeHarness();
  h.setState('rejected');
  h.setState('resume-failed');   // same sentence as rejected
  const writes = h.log.filter(([n]) => n === 'reconnect-status').map(([, x]) => x);
  assert.equal(writes.at(-2), '', 'cleared first');
  assert.match(writes.at(-1), /session ended/);
});

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
