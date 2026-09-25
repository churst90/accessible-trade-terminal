// Zero-dependency tests for the WebHost's audio.js: the browser sink for the sonification PCM
// the server streams over the circuit.
//
// Run:  node tools/jstests/audio-tests.mjs
//
// Written because the A2k campaign (2026-09-24) found this file had no test anywhere. A left/right
// swap survived every suite: on a product whose chart is PANNED by time (older bars left, newer
// right) that is the sound of time running backwards. The scheduling rules (drop while suspended,
// resync on underrun and overrun, declick only at a seam) were equally unguarded.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const source = readFileSync(join(root, 'AccessibleTrader.WebHost', 'wwwroot', 'js', 'audio.js'), 'utf8');

function makeHarness({ state = 'running' } = {}) {
  const started = [];      // [{ when, left, right, gained }]
  let ctx = null;
  class FakeAudioContext {
    constructor() { ctx = this; this.state = state; this.currentTime = 10; this.destination = { dest: true }; this.resumes = 0; }
    resume() { this.resumes++; return Promise.resolve(); }
    createBuffer(ch, frames) {
      const data = [new Float32Array(frames), new Float32Array(frames)];
      return { getChannelData: (i) => data[i] };
    }
    createBufferSource() {
      const src = { buffer: null, target: null,
        connect(t) { this.target = t; },
        start(when) { started.push({ when, left: [...this.buffer.getChannelData(0)], right: [...this.buffer.getChannelData(1)],
                                     gained: this.target && this.target.isGain === true }); } };
      return src;
    }
    createGain() {
      const g = { isGain: true, ramps: [], gain: {
        setValueAtTime: (v, t) => g.ramps.push(['set', v, t]),
        linearRampToValueAtTime: (v, t) => g.ramps.push(['ramp', v, t]) }, connect() {} };
      return g;
    }
  }
  const window = { AudioContext: FakeAudioContext };
  const sandbox = { window, document: { addEventListener() {} }, atob: (b) => Buffer.from(b, 'base64').toString('binary') };
  vm.createContext(sandbox);
  vm.runInContext(source, sandbox);
  // Interleaved stereo float32, little-endian, as the server writes it.
  const chunk = (frames) => {
    const f = new Float32Array(frames.length * 2);
    frames.forEach(([l, r], i) => { f[i * 2] = l; f[i * 2 + 1] = r; });
    return Buffer.from(f.buffer).toString('base64');
  };
  return { api: window.accessibleTrader, started, chunk, ctx: () => ctx };
}

let passed = 0, failed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log('  ok   ' + name); }
  catch (e) { failed++; console.log('  FAIL ' + name + '\n       ' + e.message); }
}

console.log('audio.js');

test('even samples are LEFT and odd samples RIGHT (the chart is panned by time)', () => {
  const h = makeHarness();
  h.api.audioPush(h.chunk([[0.25, -0.5], [0.75, -1]]));
  assert.deepEqual(h.started[0].left, [0.25, 0.75]);
  assert.deepEqual(h.started[0].right, [-0.5, -1]);
});

test('a chunk pushed while the context is suspended is dropped, not queued to burst later', () => {
  const h = makeHarness({ state: 'suspended' });
  h.api.audioPush(h.chunk([[0.1, 0.1]]));
  assert.equal(h.started.length, 0);
  assert.equal(h.ctx().resumes, 1, 'and it asks the context to resume');
});

test('contiguous chunks are butted head to tail with no gain ramp between them', () => {
  const h = makeHarness();
  const frames = Array.from({ length: 441 }, () => [0, 0]);   // 10 ms at 44.1 kHz
  h.api.audioPush(h.chunk(frames));
  h.api.audioPush(h.chunk(frames));
  assert.equal(h.started[0].gained, true, 'the first chunk is a seam (start from silence): declicked');
  assert.equal(h.started[1].gained, false, 'a ramp between contiguous buffers buzzes at the buffer rate');
  assert.ok(Math.abs(h.started[1].when - (h.started[0].when + 0.01)) < 1e-9, 'head to tail');
});

test('a schedule that has run too far ahead snaps back near the present, declicked', () => {
  const h = makeHarness();
  const frames = Array.from({ length: 4410 }, () => [0, 0]);  // 100 ms each
  for (let i = 0; i < 4; i++) h.api.audioPush(h.chunk(frames));  // pushed faster than real time
  const leads = h.started.map(x => +(x.when - h.ctx().currentTime).toFixed(3));
  // 20 ms, 120 ms, then 220 ms would pass MAX_LEAD (200 ms): that one snaps back to 20 ms.
  const snapped = h.started.slice(1).find(x => x.when - h.ctx().currentTime <= 0.05);
  assert.ok(snapped, `the lead only grew: ${leads.join(', ')} s`);
  assert.equal(snapped.gained, true, 'the snap-back is a seam and is declicked');
});

test('a late chunk (underrun) restarts just ahead of the clock, not in the past', () => {
  const h = makeHarness();
  h.api.audioPush(h.chunk([[0, 0]]));
  h.ctx().currentTime += 5;                                     // the queue ran dry
  h.api.audioPush(h.chunk([[0, 0]]));
  const last = h.started.at(-1);
  assert.ok(last.when > h.ctx().currentTime && last.when < h.ctx().currentTime + 0.05);
  assert.equal(last.gained, true);
});

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
