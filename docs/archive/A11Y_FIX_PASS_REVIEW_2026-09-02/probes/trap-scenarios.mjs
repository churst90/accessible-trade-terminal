// Scratch reproduction: loads the REAL keyboard.js the way tools/jstests/keyboard-tests.mjs does
// and drives the Tab trap through the review scenarios. Review only; not a test file.
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const root = '/home/cody/external-rescue/Github/accessible-trade-terminal';
const source = readFileSync(
  root + '/AccessibleTrader.BlazorClient.Components/wwwroot/js/keyboard.js', 'utf8');

function makeHarness() {
  const calls = [];
  const dotnet = { invokeMethodAsync: (m, ...a) => { calls.push([m, ...a]); return Promise.resolve(); } };
  const windowListeners = {};
  const sandbox = {
    window: { addEventListener: (t, fn) => { (windowListeners[t] ??= []).push(fn); } },
    document: { getElementById: () => null, querySelectorAll: () => [], addEventListener: () => {}, activeElement: null },
    navigator: { userAgent: 'test' }, console, Math,
    Date: { now: () => 100000 }, setTimeout: () => 0, clearTimeout: () => {},
    requestAnimationFrame: (cb) => { cb(); return 0; }, cancelAnimationFrame: () => {},
  };
  sandbox.window.document = sandbox.document;
  sandbox.globalThis = sandbox;
  vm.createContext(sandbox);
  vm.runInContext(source, sandbox, { filename: 'keyboard.js' });
  sandbox.window.accessibleTrader.registerKeyboardHandler(dotnet);

  const node = (tagName, attrs = {}, opts = {}) => {
    const n = {
      tagName, name: opts.name ?? tagName.toLowerCase(),
      isContentEditable: false,
      // opts.fixed models CSSOM: offsetParent is null for position:fixed (Chromium).
      offsetParent: (opts.hidden || opts.fixed) ? null : {},
      parent: opts.parent ?? null,
      getAttribute: (k) => (k in attrs ? attrs[k] : null),
      hasAttribute: (k) => k in attrs,
      setAttribute: (k, v) => { attrs[k] = v; },
      focus() { sandbox.document.activeElement = this; },
    };
    n.closest = (selector) => {
      const roles = [...selector.matchAll(/\[role="([^"]+)"\]/g)].map(m => m[1]);
      for (let cur = n; cur; cur = cur.parent) if (roles.includes(cur.getAttribute('role'))) return cur;
      return null;
    };
    return n;
  };

  // Same selector semantics as the jstests harness (tag + [tabindex] parts, applied for real).
  const matchesSelector = (sel, n) =>
    sel.split(',').map(x => x.trim()).some(part => {
      if (part.startsWith('[tabindex]')) {
        const ti = n.getAttribute('tabindex');
        return ti !== null && !part.includes(':not([tabindex="-1"])') ? true : ti !== null && ti !== '-1';
      }
      if (part.startsWith('[contenteditable]')) return n.hasAttribute('contenteditable');
      const tag = part.match(/^[a-zA-Z]+/);
      if (!tag) return false;
      if (tag[0].toLowerCase() !== n.tagName.toLowerCase()) return false;
      if (part.includes(':not([disabled])') && n.hasAttribute('disabled')) return false;
      if (part.includes('[href]') && !n.hasAttribute('href')) return false;
      if (part.includes('[controls]') && !n.hasAttribute('controls')) return false;
      return true;
    });

  // A dialog whose descendants (in DOM order) are `children`. Nested dialogs are supported by
  // giving a child dialog `parent` = outer dialog; `contains` walks parents.
  const dialog = (role, children, opts = {}) => {
    const d = node('DIV', { role }, opts);
    d.name = opts.name ?? role;
    for (const c of children) if (!c.parent) c.parent = d;
    d.descendants = children;
    d.querySelectorAll = (sel) => children.filter(c => matchesSelector(sel, c));
    d.contains = (el) => { for (let cur = el; cur; cur = cur.parent) if (cur === d) return true; return false; };
    return d;
  };

  // `ds` in DOCUMENT order (MainLayout render order).
  const mountDialogs = (...ds) => {
    sandbox.document.querySelectorAll = (sel) =>
      sel.includes('role=') ? ds.filter(d => sel.includes(`[role="${d.getAttribute('role')}"]`)) : [];
  };
  const setModalOpen = (b) => sandbox.window.accessibleTrader.setModalOpen(b);
  const setActive = (el) => { sandbox.document.activeElement = el; };
  const press = (key, target, mods = {}) => {
    let prevented = false;
    const ev = { key, shiftKey: !!mods.shift, ctrlKey: !!mods.ctrl, altKey: !!mods.alt,
      target: target ?? node('BODY'), preventDefault: () => { prevented = true; }, stopImmediatePropagation: () => {} };
    for (const fn of windowListeners.keydown ?? []) fn(ev);
    return prevented;
  };
  return { calls, node, dialog, mountDialogs, setModalOpen, setActive, press, doc: sandbox.document };
}

const out = [];
const log = (s) => { out.push(s); console.log(s); };
const nameOf = (el) => el ? el.name : 'null';
function scenario(title, fn) { log(`\n## ${title}`); fn(); }

// ---------------------------------------------------------------------------------------------
scenario('Q1a: focus on <h2 tabindex=-1> (idx -1, inside)', () => {
  const h = makeHarness();
  const h2 = h.node('H2', { tabindex: '-1' }, { name: 'h2' });
  const a = h.node('BUTTON', {}, { name: 'first-btn' });
  const b = h.node('BUTTON', {}, { name: 'last-btn' });
  const d = h.dialog('dialog', [h2, a, b]);
  h.mountDialogs(d); h.setModalOpen(true); h.setActive(h2);
  log(`Tab       prevented=${h.press('Tab', h2)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(h2);
  log(`Shift+Tab prevented=${h.press('Tab', h2, { shift: true })} -> ${nameOf(h.doc.activeElement)}`);
});

scenario('Q1b: focus on a <summary> in the middle of the list', () => {
  const h = makeHarness();
  const close = h.node('BUTTON', {}, { name: 'close' });
  const s1 = h.node('SUMMARY', {}, { name: 'summary1' });
  const s2 = h.node('SUMMARY', {}, { name: 'summary2' });
  const d = h.dialog('dialog', [close, s1, s2]);
  h.mountDialogs(d); h.setModalOpen(true); h.setActive(s1);
  log(`Tab       prevented=${h.press('Tab', s1)} (browser owns; next is summary2)`);
  h.setActive(s2);
  log(`Tab@last  prevented=${h.press('Tab', s2)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(s1);
  log(`Shift+Tab prevented=${h.press('Tab', s1, { shift: true })} (browser owns; prev is close)`);
});

scenario('Q1c: focus on a DISABLED control inside (became disabled while focused)', () => {
  const h = makeHarness();
  const a = h.node('BUTTON', {}, { name: 'first-btn' });
  const dis = h.node('BUTTON', { disabled: '' }, { name: 'disabled-submit' });
  const b = h.node('BUTTON', {}, { name: 'last-btn' });
  const d = h.dialog('dialog', [a, dis, b]);
  h.mountDialogs(d); h.setModalOpen(true); h.setActive(dis);
  log(`Tab       prevented=${h.press('Tab', dis)} -> ${nameOf(h.doc.activeElement)} (contained; goes to FIRST, not next)`);
  h.setActive(dis);
  log(`Shift+Tab prevented=${h.press('Tab', dis, { shift: true })} -> ${nameOf(h.doc.activeElement)} (contained; goes to LAST, not prev)`);
});

scenario('Q1d: focus on a non-heading tabindex=-1 element inside (roving treeitem div)', () => {
  const h = makeHarness();
  const a = h.node('BUTTON', {}, { name: 'first-btn' });
  const item = h.node('DIV', { role: 'treeitem', tabindex: '-1' }, { name: 'treeitem-minus1' });
  const b = h.node('BUTTON', {}, { name: 'last-btn' });
  const d = h.dialog('dialog', [a, item, b]);
  h.mountDialogs(d); h.setModalOpen(true); h.setActive(item);
  log(`Tab       prevented=${h.press('Tab', item)} -> ${nameOf(h.doc.activeElement)} (contained; jumps to FIRST instead of last-btn)`);
  h.setActive(item);
  log(`Shift+Tab prevented=${h.press('Tab', item, { shift: true })} -> ${nameOf(h.doc.activeElement)} (contained; jumps to LAST instead of first-btn)`);
});

scenario('Q1e: focus OUTSIDE the dialog (body / background control)', () => {
  const h = makeHarness();
  const a = h.node('BUTTON', {}, { name: 'first-btn' });
  const b = h.node('BUTTON', {}, { name: 'last-btn' });
  const d = h.dialog('dialog', [a, b]);
  const body = h.node('BODY', {}, { name: 'body' });
  const bg = h.node('BUTTON', {}, { name: 'toolbar-load' });
  h.mountDialogs(d); h.setModalOpen(true);
  h.setActive(body); log(`body  Tab       prevented=${h.press('Tab', body)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(body); log(`body  Shift+Tab prevented=${h.press('Tab', body, { shift: true })} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(bg);   log(`bgbtn Tab       prevented=${h.press('Tab', bg)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(null); log(`null  Tab       prevented=${h.press('Tab', body)} -> ${nameOf(h.doc.activeElement)}`);
});

scenario('Q1f: dialog with ZERO focusable elements', () => {
  const h = makeHarness();
  const h2 = h.node('H2', { tabindex: '-1' }, { name: 'h2' });
  const d = h.dialog('dialog', [h2]);
  h.mountDialogs(d); h.setModalOpen(true); h.setActive(h2);
  log(`Tab       prevented=${h.press('Tab', h2)}  <-- false means the browser walks OUT`);
  log(`Shift+Tab prevented=${h.press('Tab', h2, { shift: true })}  <-- false means the browser walks OUT`);
});

scenario('Q1g: dialog with exactly ONE focusable', () => {
  const h = makeHarness();
  const h2 = h.node('H2', { tabindex: '-1' }, { name: 'h2' });
  const only = h.node('BUTTON', {}, { name: 'only' });
  const d = h.dialog('dialog', [h2, only]);
  h.mountDialogs(d); h.setModalOpen(true);
  h.setActive(h2);   log(`h2   Tab       prevented=${h.press('Tab', h2)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(only); log(`only Tab       prevented=${h.press('Tab', only)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(only); log(`only Shift+Tab prevented=${h.press('Tab', only, { shift: true })} -> ${nameOf(h.doc.activeElement)}`);
});

// ---------------------------------------------------------------------------------------------
scenario('Q1h: ObjectTreeModal — pane <summary role=treeitem> roved to tabindex=-1, focus on the roved series div', () => {
  // Markup: ObjectTreeModal.razor:46-49 <summary role="treeitem" tabindex="0|-1">;
  //         :58-62 <div role="treeitem" tabindex="-1">; treeKeyboard.js:111-122 roves tabindex.
  // Hosted/demo build: Demo.AllowStrategies=false, so no Manage Strategies button precedes the tree.
  const h = makeHarness();
  const h2 = h.node('H2', { tabindex: '-1' }, { name: 'h2' });
  const pane = h.node('SUMMARY', { role: 'treeitem', tabindex: '0' }, { name: 'pane1-summary' });
  const series = h.node('DIV', { role: 'treeitem', tabindex: '-1' }, { name: 'series-node' });
  const close = h.node('BUTTON', {}, { name: 'close' });
  const d = h.dialog('dialog', [h2, pane, series, close]);
  h.mountDialogs(d); h.setModalOpen(true);
  h.setActive(h2);
  log(`open: Tab from h2 prevented=${h.press('Tab', h2)} -> ${nameOf(h.doc.activeElement)}`);
  // ArrowDown in the tree: treeKeyboard.focusTreeitem sets all treeitems -1, target 0, focus().
  pane.setAttribute('tabindex', '-1'); series.setAttribute('tabindex', '0'); series.focus();
  log(`after ArrowDown: pane1-summary tabindex=${pane.getAttribute('tabindex')}, series-node tabindex=${series.getAttribute('tabindex')}`);
  const p = h.press('Tab', series, { shift: true });
  log(`Shift+Tab from series-node prevented=${p} -> ${nameOf(h.doc.activeElement)}`);
  log(`  browser sequential-backward from series-node skips pane1-summary (tabindex=-1) and h2 (-1): ` +
      (p ? 'trap owned it' : 'NOTHING inside precedes it -> focus leaves the dialog'));
});

scenario('Q1i: Toolbar alertdialog is position:fixed -> offsetParent null in Chromium', () => {
  // Toolbar.razor:430-433: <div role="alertdialog" ... style="position:fixed; ...">
  // keyboard.js:139: .filter(el => el.offsetParent !== null)
  const h = makeHarness();
  const h3 = h.node('H3', { tabindex: '-1' }, { name: 'switchWarnTitle' });
  const c1 = h.node('BUTTON', {}, { name: 'continue-strip' });
  const c3 = h.node('BUTTON', {}, { name: 'cancel' });
  const d = h.dialog('alertdialog', [h3, c1, c3], { fixed: true });
  h.mountDialogs(d); h.setModalOpen(true); h.setActive(h3);
  log(`Tab       prevented=${h.press('Tab', h3)}  <-- false: dialogs[] is empty after the offsetParent filter`);
  log(`Shift+Tab prevented=${h.press('Tab', h3, { shift: true })}`);
  const h2 = makeHarness();
  const d2 = h2.dialog('alertdialog', [h3, c1, c3], { fixed: false });
  h2.mountDialogs(d2); h2.setModalOpen(true); h2.setActive(h3);
  log(`control (offsetParent non-null): Shift+Tab prevented=${h2.press('Tab', h3, { shift: true })} -> ${nameOf(h2.doc.activeElement)}`);
});

// ---------------------------------------------------------------------------------------------
scenario('Q2a: STACKED — Help (DOM 105) opened over Settings (DOM 113): selector picks dialogs[last] = Settings', () => {
  const h = makeHarness();
  const sH2 = h.node('H2', { tabindex: '-1' }, { name: 'settings-h2' });
  const sSearch = h.node('INPUT', { type: 'search' }, { name: 'settings-search' });
  const sClose = h.node('BUTTON', {}, { name: 'settings-close' });
  const settings = h.dialog('dialog', [sH2, sSearch, sClose], { name: 'Settings' });
  const hH2 = h.node('H2', { tabindex: '-1' }, { name: 'help-h2' });
  const hSum = h.node('SUMMARY', {}, { name: 'help-summary1' });
  const hClose = h.node('BUTTON', {}, { name: 'help-close' });
  const help = h.dialog('dialog', [hH2, hSum, hClose], { name: 'Help' });
  h.mountDialogs(help, settings);            // document order: Help before Settings
  h.setModalOpen(true); h.setModalOpen(true); // both open; Help is the TOP (opened last)
  h.setActive(hH2);
  log(`Help h2:      Tab       prevented=${h.press('Tab', hH2)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(hH2);
  log(`Help h2:      Shift+Tab prevented=${h.press('Tab', hH2, { shift: true })} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(hSum);
  log(`Help summary: Tab       prevented=${h.press('Tab', hSum)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(hClose);
  log(`Help close:   Tab       prevented=${h.press('Tab', hClose)} -> ${nameOf(h.doc.activeElement)}`);
});

scenario('Q2b: STACKED — ThemeEditor (DOM 148) opened over Settings (DOM 113): top is later in DOM', () => {
  const h = makeHarness();
  const sH2 = h.node('H2', { tabindex: '-1' }, { name: 'settings-h2' });
  const sSearch = h.node('INPUT', { type: 'search' }, { name: 'settings-search' });
  const sClose = h.node('BUTTON', {}, { name: 'settings-close' });
  const settings = h.dialog('dialog', [sH2, sSearch, sClose], { name: 'Settings' });
  const tH2 = h.node('H2', { tabindex: '-1' }, { name: 'theme-h2' });
  const tA = h.node('BUTTON', {}, { name: 'theme-first' });
  const tClose = h.node('BUTTON', {}, { name: 'theme-close' });
  const theme = h.dialog('dialog', [tH2, tA, tClose], { name: 'ThemeEditor' });
  h.mountDialogs(settings, theme);
  h.setModalOpen(true); h.setModalOpen(true);
  h.setActive(tH2);   log(`Theme h2:    Shift+Tab prevented=${h.press('Tab', tH2, { shift: true })} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(tClose); log(`Theme close: Tab       prevented=${h.press('Tab', tClose)} -> ${nameOf(h.doc.activeElement)}`);
  h.setActive(tA);    log(`Theme first: Shift+Tab prevented=${h.press('Tab', tA, { shift: true })} -> ${nameOf(h.doc.activeElement)}`);
});

scenario('Q2c: context menu only (role=menu) with _openModalCount>0', () => {
  const h = makeHarness();
  const mi = h.node('BUTTON', { role: 'menuitem' }, { name: 'menuitem1' });
  const menu = h.dialog('menu', [mi], { name: 'ChartContextMenu' });
  h.mountDialogs(menu); h.setModalOpen(true); h.setActive(mi);
  log(`Tab       prevented=${h.press('Tab', mi)}  (menu is not in the dialog-family selector)`);
});

// ---------------------------------------------------------------------------------------------
scenario('Scroll-key release scoping', () => {
  const h = makeHarness();
  h.setModalOpen(true);
  const btn = h.node('BUTTON', {}, { name: 'btn' });
  log(`ArrowDown on button in modal:  prevented=${h.press('ArrowDown', btn)} calls=${h.calls.length}`);
  log(`Shift+ArrowDown on button:     prevented=${h.press('ArrowDown', btn, { shift: true })} calls=${h.calls.length}`);
  log(`Ctrl+ArrowDown on button:      prevented=${h.press('ArrowDown', btn, { ctrl: true })} calls=${h.calls.length}`);
  const lb = h.node('UL', { role: 'listbox' }); const opt = h.node('LI', { role: 'option', tabindex: '0' }, { parent: lb });
  log(`ArrowDown on listbox option:   prevented=${h.press('ArrowDown', opt)} calls=${h.calls.length}`);
  const rg = h.node('DIV', { role: 'radiogroup' }); const r = h.node('INPUT', { type: 'radio' }, { parent: rg });
  log(`ArrowDown on radio in group:   prevented=${h.press('ArrowDown', r)} (form-control gate returns first)`);
  const combo = h.node('INPUT', { role: 'combobox' });
  log(`ArrowDown on combobox input:   prevented=${h.press('ArrowDown', combo)}`);
  const h0 = makeHarness();
  log(`no modal, ArrowDown on button: prevented=${h0.press('ArrowDown', h0.node('BUTTON'))} calls=${h0.calls.length}`);
});

import { writeFileSync } from 'node:fs';
writeFileSync('./trap-scenarios.out', out.join('\n') + '\n');
