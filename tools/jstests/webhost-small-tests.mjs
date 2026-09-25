// Zero-dependency tests for the WebHost's three small scripts: webSpeech.js (the browser voice),
// webPush.js (alert notifications enrolment) and service-worker.js (showing them).
//
// Run:  node tools/jstests/webhost-small-tests.mjs
//
// Written because the A2k campaign (2026-09-24) found none of the three had a test anywhere. An
// interrupting announcement that no longer interrupts is the one that matters most: an order fill
// queued behind the navigation chatter already speaking is heard seconds late.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const read = (...p) => readFileSync(join(root, 'AccessibleTrader.WebHost', 'wwwroot', ...p), 'utf8');

let passed = 0, failed = 0;
async function test(name, fn) {
  try { await fn(); passed++; console.log('  ok   ' + name); }
  catch (e) { failed++; console.log('  FAIL ' + name + '\n       ' + e.message); }
}

// ── webSpeech.js ─────────────────────────────────────────────────────────────

function speechHarness() {
  const log = [];
  const store = {};
  const window = {
    speechSynthesis: { cancel: () => log.push(['cancel']), speak: (u) => log.push(['speak', u.text]) },
    localStorage: { getItem: (k) => (k in store ? store[k] : null), setItem: (k, v) => { store[k] = String(v); } },
  };
  const sandbox = { window, SpeechSynthesisUtterance: class { constructor(t) { this.text = t; } } };
  vm.createContext(sandbox);
  vm.runInContext(read('js', 'webSpeech.js'), sandbox);
  return { at: window.accessibleTrader, log };
}

console.log('webSpeech.js');

await test('an interrupting announcement cancels what is speaking, then speaks', () => {
  const h = speechHarness();
  h.at.speak('Order filled', true);
  assert.deepEqual(h.log, [['cancel'], ['speak', 'Order filled']]);
});

await test('a queued announcement does not cut off the one speaking', () => {
  const h = speechHarness();
  h.at.speak('Bar 12', false);
  assert.deepEqual(h.log, [['speak', 'Bar 12']]);
});

await test('the speech-output choice reads back what was saved', () => {
  const h = speechHarness();
  assert.equal(h.at.getSpeechOutputMode(), '');
  h.at.setSpeechOutputMode('sr');
  assert.equal(h.at.getSpeechOutputMode(), 'sr', 'saved under one key and read from another');
});

// ── webPush.js ───────────────────────────────────────────────────────────────

console.log('webPush.js');

await test('enable sends the subscription keys under their own names', async () => {
  const posted = [];
  const subscription = { endpoint: 'https://push.example/abc',
    toJSON: () => ({ keys: { p256dh: 'P256-KEY', auth: 'AUTH-KEY' } }) };
  const registration = { pushManager: { subscribe: async () => subscription } };
  const window = { PushManager: function () {}, Notification: { requestPermission: async () => 'granted' } };
  const sandbox = {
    window, console, atob: (b) => Buffer.from(b, 'base64').toString('binary'),
    navigator: { serviceWorker: { register: async () => registration, ready: Promise.resolve() } },
    Notification: window.Notification,
    fetch: async (url, opts) => {
      if (url === 'push/vapid-public-key') return { ok: true, text: async () => 'AAAA' };
      posted.push([url, JSON.parse(opts.body)]);
      return { ok: true };
    },
  };
  vm.createContext(sandbox);
  vm.runInContext(read('js', 'webPush.js'), sandbox);
  const result = await window.accessibleTraderPush.enable();
  assert.equal(result, 'enabled');
  assert.deepEqual(posted, [['push/subscribe',
    { endpoint: 'https://push.example/abc', p256dh: 'P256-KEY', auth: 'AUTH-KEY' }]]);
});

// ── service-worker.js ────────────────────────────────────────────────────────

console.log('service-worker.js');

await test('each different alert gets its own notification; a repeat of one replaces itself', async () => {
  const shown = [];
  const listeners = {};
  const self = {
    addEventListener: (t, fn) => { listeners[t] = fn; },
    registration: { showNotification: async (title, opts) => shown.push([title, opts.body, opts.tag]) },
  };
  const sandbox = { self, clients: {} };
  vm.createContext(sandbox);
  vm.runInContext(read('service-worker.js'), sandbox);
  const push = async (body) => {
    let p;
    listeners.push({ data: { json: () => ({ title: 'Alert', body }) }, waitUntil: (x) => { p = x; } });
    await p;
  };
  await push('BTC crossed 70,000');
  await push('ETH crossed 4,000');
  await push('BTC crossed 70,000');
  const tags = shown.map(s => s[2]);
  assert.notEqual(tags[0], tags[1], 'two different alerts share a tag, so the second silently replaces the first');
  assert.equal(tags[0], tags[2], 'a repeat of the same alert should replace, not stack');
});

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
