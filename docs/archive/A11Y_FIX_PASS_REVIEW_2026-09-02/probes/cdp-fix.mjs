import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
const kj = process.argv[2]; const port = process.argv[3] || '9334';
const page = `./cdp-fixpage.${port}.html`;
writeFileSync(page, readFileSync('./cdp-fixpage.html','utf8').replace('KEYBOARD_JS', kj));
const chrome = spawn('/home/cody/.cache/ms-playwright/chromium-1187/chrome-linux/chrome',
  ['--headless=new','--no-sandbox','--disable-gpu','--allow-file-access-from-files',`--remote-debugging-port=${port}`,'about:blank'],{stdio:'ignore'});
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms));
let targets; for (let i=0;i<40;i++){ try{ targets=await (await fetch(`http://127.0.0.1:${port}/json`)).json(); break;}catch{ await sleep(250);} }
const ws=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
await new Promise(r=>ws.onopen=r);
let id=0; const pending={};
ws.onmessage=(m)=>{const d=JSON.parse(m.data); if(d.id&&pending[d.id]){pending[d.id](d); delete pending[d.id];}};
const send=(method,params={})=>new Promise(res=>{const i=++id; pending[i]=res; ws.send(JSON.stringify({id:i,method,params}));});
const ev=async(expr)=>(await send('Runtime.evaluate',{expression:expr,returnByValue:true})).result?.result?.value;
const tab=async(shift)=>{ const mods=shift?8:0; for (const type of ['rawKeyDown','keyUp']) await send('Input.dispatchKeyEvent',{type,key:'Tab',code:'Tab',windowsVirtualKeyCode:9,nativeVirtualKeyCode:9,modifiers:mods}); await sleep(120); };
await send('Page.enable'); await send('Runtime.enable'); await send('Emulation.setFocusEmulationEnabled',{enabled:true});
const load=async()=>{ await send('Page.navigate',{url:`file://${page}`}); await sleep(900); await send('Input.dispatchKeyEvent',{type:'rawKeyDown',key:'Shift',code:'ShiftLeft',windowsVirtualKeyCode:16}); await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Shift',code:'ShiftLeft',windowsVirtualKeyCode:16}); await sleep(100); };
const active=()=>ev('document.activeElement.id');
const focus=(i)=>ev(`document.getElementById('${i}').focus(), document.activeElement.id`);
const seen=()=>ev(`Array.from(document.querySelectorAll('[role="dialog"], [role="alertdialog"]')).filter(el=>el.getClientRects().length>0).map(e=>e.id+'(offsetParent='+(e.offsetParent===null?'null':'set')+')').join(',')`);
await load(); // warm-up: the first navigation after launch drops the first dispatched key
const out=[]; const step=async(label,shift,expect)=>{ await tab(shift); const a=await active(); out.push(`  ${label}: -> ${a}  ${a===expect?'OK':'FAIL (expected '+expect+')'}`); };

await load(); await ev(`window.show(['ad'],1)`);
out.push(`1. Toolbar alertdialog, position:fixed on the element itself: rendered dialogs seen by the trap's predicate = ${await seen()}`);
// NOTE: the very first Tab-family key after focus() into this freshly shown fixed element is
// re-homed by Chromium itself (focusout/focusin with no focus() call) in BOTH versions of
// keyboard.js, trap or no trap; a native Tab is sent first so the record does not rest on it.
await focus('switchWarnTitle'); await tab(false); out.push(`  (priming Tab from heading -> ${await active()})`);
await focus('switchWarnTitle'); await step('Tab from heading (trap seeds first)', false, 'ad-continue');
await step('Shift+Tab from Continue (first button)', true, 'ad-cancel');
await step('Tab from Cancel (last button)', false, 'ad-continue');
await focus('switchWarnTitle'); await step('Shift+Tab from heading', true, 'ad-cancel');
out.push('  keydown log: '+await ev('window.flog.filter(x=>/prevented/.test(x)).join(" | ")'));

await load(); await ev(`window.show(['tree-overlay'],1)`);
out.push(`2. ObjectTree, summary roved to -1, focus on series div`);
await focus('series'); await step('Shift+Tab from series (first real stop)', true, 'tree-close');
await step('Tab from Close (last)', false, 'series');

await load(); await ev(`window.show(['settings-overlay','help-overlay'],2)`);
out.push(`3. stacked: Help (DOM first) over Settings (DOM last)`);
await focus('help-summary'); await step('Tab from Help summary', false, 'help-close');
await step('Tab from Help Close (last in Help)', false, 'help-summary');
await focus('help-h2'); await step('Shift+Tab from Help heading', true, 'help-close');
out.push('  keydown log: '+await ev('window.flog.join(" | ")'));
console.log(out.join('\n'));
ws.close(); chrome.kill();
